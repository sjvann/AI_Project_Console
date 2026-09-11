param(
    [Parameter(Mandatory = $true)]
    [string[]]$Files,
    [string]$MetadataPath = "",
    [string]$CorrelationId = "",
    [switch]$Required
)

$ErrorActionPreference = "Stop"
$Root = Resolve-Path (Join-Path $PSScriptRoot "..")
$ToolsDir = Join-Path $Root "artifacts\signing-tools"
$TimestampUrl = "http://timestamp.acs.microsoft.com/"
$DlibPackage = "Microsoft.ArtifactSigning.Client"
$DlibVersion = "1.0.128"
$SdkPackage = "Microsoft.Windows.SDK.BuildTools"

function Write-SignInfo([string]$Message) {
    Write-Host $Message
}

function Get-SigningMetadataObject {
    $path = $MetadataPath
    if (-not $path) { $path = $env:TRUSTED_SIGNING_METADATA }
    if (-not $path) {
        $local = Join-Path $Root "installer\windows\trusted-signing.json"
        if (Test-Path $local) { $path = $local }
    }

    $endpoint = $env:TRUSTED_SIGNING_ENDPOINT
    $account = $env:TRUSTED_SIGNING_ACCOUNT
    $profile = $env:TRUSTED_SIGNING_PROFILE

    if ($path) {
        if (-not (Test-Path $path)) {
            throw "找不到 Trusted Signing metadata：$path"
        }
        $raw = Get-Content -LiteralPath $path -Raw -Encoding UTF8
        $obj = $raw | ConvertFrom-Json
        if (-not $endpoint) { $endpoint = $obj.Endpoint }
        if (-not $account) { $account = $obj.CodeSigningAccountName }
        if (-not $profile) { $profile = $obj.CertificateProfileName }
    }

    if ([string]::IsNullOrWhiteSpace($endpoint) -or
        [string]::IsNullOrWhiteSpace($account) -or
        [string]::IsNullOrWhiteSpace($profile)) {
        return $null
    }

    return [pscustomobject]@{
        Endpoint               = $endpoint.TrimEnd('/') + '/'
        CodeSigningAccountName = $account
        CertificateProfileName = $profile
    }
}

function Test-DotNet8Runtime {
    $list = & dotnet --list-runtimes 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $list) { return $false }
    return [bool]($list | Where-Object { $_ -match '^Microsoft\.NETCore\.App 8\.' })
}

function Find-SignTool {
    $kitRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    if (Test-Path $kitRoot) {
        $found = Get-ChildItem -LiteralPath $kitRoot -Directory -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending |
            ForEach-Object {
                Join-Path $_.FullName "x64\signtool.exe"
            } |
            Where-Object { Test-Path $_ } |
            Select-Object -First 1
        if ($found) { return $found }
    }

    if (Test-Path $ToolsDir) {
        $fromNuget = Get-ChildItem -LiteralPath $ToolsDir -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
            Where-Object { $_.DirectoryName -match '\\x64$' } |
            Select-Object -First 1
        if ($fromNuget) { return $fromNuget.FullName }
    }
    return $null
}

function Find-Dlib {
    $candidates = @(
        (Join-Path $ToolsDir "$DlibPackage\bin\x64\Azure.CodeSigning.Dlib.dll"),
        (Join-Path ${env:ProgramFiles} "Azure Artifact Signing Client Tools\bin\x64\Azure.CodeSigning.Dlib.dll"),
        (Join-Path ${env:ProgramFiles} "Microsoft Azure Artifact Signing Client Tools\bin\x64\Azure.CodeSigning.Dlib.dll")
    )
    foreach ($p in $candidates) {
        if ($p -and (Test-Path $p)) { return $p }
    }
    if (Test-Path $ToolsDir) {
        $fromNuget = Get-ChildItem -LiteralPath $ToolsDir -Recurse -Filter Azure.CodeSigning.Dlib.dll -ErrorAction SilentlyContinue |
            Where-Object { $_.DirectoryName -match '\\x64$' } |
            Select-Object -First 1
        if ($fromNuget) { return $fromNuget.FullName }
    }
    return $null
}

function Get-NugetExe {
    if (-not (Test-Path $ToolsDir)) {
        New-Item -ItemType Directory -Path $ToolsDir -Force | Out-Null
    }
    $nuget = Join-Path $ToolsDir "nuget.exe"
    if (-not (Test-Path $nuget)) {
        Write-SignInfo "Downloading nuget.exe ..."
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri "https://dist.nuget.org/win-x86-commandline/latest/nuget.exe" -OutFile $nuget -UseBasicParsing
    }
    return $nuget
}

function Install-SigningTools {
    $nuget = Get-NugetExe
    $dlibDir = Join-Path $ToolsDir $DlibPackage
    if (-not (Test-Path (Join-Path $dlibDir "bin\x64\Azure.CodeSigning.Dlib.dll"))) {
        Write-SignInfo "Installing $DlibPackage $DlibVersion ..."
        & $nuget install $DlibPackage -Version $DlibVersion -OutputDirectory $ToolsDir -ExcludeVersion -NonInteractive | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "無法安裝 $DlibPackage。請確認網路可用。" }
    }

    if (-not (Find-SignTool)) {
        Write-SignInfo "Installing $SdkPackage (SignTool) ..."
        & $nuget install $SdkPackage -OutputDirectory $ToolsDir -ExcludeVersion -NonInteractive | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "無法安裝 $SdkPackage。也可改裝 Windows SDK。" }
    }
}

function Write-MetadataFile($meta) {
    $dir = Join-Path $ToolsDir "metadata"
    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    $path = Join-Path $dir "current.json"
    $payload = [ordered]@{
        Endpoint               = $meta.Endpoint
        CodeSigningAccountName = $meta.CodeSigningAccountName
        CertificateProfileName = $meta.CertificateProfileName
    }
    if ($CorrelationId) {
        $payload.CorrelationId = $CorrelationId
    }
    ($payload | ConvertTo-Json) | Set-Content -LiteralPath $path -Encoding UTF8
    return $path
}

function Skip-OrThrow([string]$Message) {
    if ($Required) { throw $Message }
    Write-Warning $Message
    return $false
}

$meta = Get-SigningMetadataObject
if (-not $meta) {
    if (-not (Skip-OrThrow "未設定 Azure Artifact Signing（舊稱 Trusted Signing）。安裝包將保持未簽署，SmartScreen 可能顯示「不明的發行者」。見 docs/maintainer/code-signing.md")) {
        return
    }
}

$missing = @($Files | Where-Object { -not (Test-Path $_) })
if ($missing.Count -gt 0) {
    throw "找不到要簽署的檔案：$($missing -join ', ')"
}

if (-not (Test-DotNet8Runtime)) {
    if (-not (Skip-OrThrow "Artifact Signing 的 SignTool dlib 需要 .NET 8 Runtime（x64）。請安裝 https://dotnet.microsoft.com/download/dotnet/8.0 後再打包。")) {
        return
    }
}

Install-SigningTools
$signTool = Find-SignTool
$dlib = Find-Dlib
if (-not $signTool) { throw "找不到 signtool.exe。請安裝 Windows SDK，或讓腳本下載 Microsoft.Windows.SDK.BuildTools。" }
if (-not $dlib) { throw "找不到 Azure.CodeSigning.Dlib.dll。請檢查 artifacts/signing-tools。" }

$dmdf = Write-MetadataFile $meta
Write-SignInfo "Signing with Artifact Signing account '$($meta.CodeSigningAccountName)' profile '$($meta.CertificateProfileName)'"
Write-SignInfo "Endpoint $($meta.Endpoint)"

foreach ($file in $Files) {
    $full = (Resolve-Path $file).Path
    Write-SignInfo "  $full"
    & $signTool sign /fd SHA256 /tr $TimestampUrl /td SHA256 /d "AI_Project Console" /du "https://github.com/sjvann/AI_Project_Console" /dlib $dlib /dmdf $dmdf $full
    if ($LASTEXITCODE -ne 0) {
        throw "簽署失敗：$full（exit $LASTEXITCODE）。請確認已 az login，且帳號有 Artifact Signing Certificate Profile Signer 角色。"
    }
    $sig = Get-AuthenticodeSignature -LiteralPath $full
    if ($sig.Status -ne "Valid") {
        throw "簽署後驗證失敗：$full（Status=$($sig.Status)）"
    }
    $subject = $sig.SignerCertificate.Subject
    Write-SignInfo "  OK  $subject"
}

Write-SignInfo "Signing done."
