param(
    [string]$Version = "0.6.18",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SkipSign,
    [switch]$RequireSign
)

$ErrorActionPreference = "Stop"
$Root = Resolve-Path (Join-Path $PSScriptRoot "..")
$PublishDir = Join-Path $Root "dist\$Runtime"
$Iss = Join-Path $Root "installer\windows\setup.iss"
$SignScript = Join-Path $PSScriptRoot "sign-win.ps1"
$IsccCandidates = @(
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 7\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 7\ISCC.exe"),
    (Join-Path $env:LocalAppData "Programs\Inno Setup 7\ISCC.exe"),
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:LocalAppData "Programs\Inno Setup 6\ISCC.exe")
)

Write-Host "PACK:inno"
$Iscc = $IsccCandidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $Iscc) {
    throw "找不到 Inno Setup（ISCC.exe）。請先安裝 Inno Setup 6 或 7：https://jrsoftware.org/isinfo.php"
}

function Invoke-AuthenticodeSign {
    param(
        [string[]]$Files,
        [string]$CorrelationId
    )
    if ($SkipSign) {
        Write-Host "Skipping Authenticode signing (-SkipSign)."
        return
    }
    if (-not (Test-Path $SignScript)) {
        if ($RequireSign) { throw "找不到 scripts/sign-win.ps1" }
        Write-Warning "找不到 scripts/sign-win.ps1，略過簽署。"
        return
    }
    $signArgs = @{
        Files         = $Files
        CorrelationId = $CorrelationId
    }
    if ($RequireSign) { $signArgs.Required = $true }
    & $SignScript @signArgs
}

Write-Host "PACK:publish"
if (Test-Path $PublishDir) {
    Remove-Item $PublishDir -Recurse -Force
}
dotnet publish (Join-Path $Root "src\AiProject.Console.App\AiProject.Console.App.csproj") `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishReadyToRun=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -o $PublishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失敗" }

Get-ChildItem $PublishDir -Recurse -Include *.pdb | Remove-Item -Force -ErrorAction SilentlyContinue

$AppExe = Join-Path $PublishDir "AI_Project_Console.exe"
if (-not (Test-Path $AppExe)) { throw "publish 後找不到 $AppExe" }
Invoke-AuthenticodeSign -Files @($AppExe) -CorrelationId "AI_Project_Console-$Version-$Runtime-app"

Write-Host "PACK:zip"
$Zip = Join-Path $Root "dist\AI_Project_Console-$Version-$Runtime.zip"
if (Test-Path $Zip) { Remove-Item $Zip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($PublishDir, $Zip, [System.IO.Compression.CompressionLevel]::Optimal, $false)

Write-Host "PACK:installer"
& $Iscc /Q /DMyAppVersion=$Version /DPublishDir=$PublishDir $Iss
if ($LASTEXITCODE -ne 0) { throw "Inno Setup 編譯失敗" }

$Setup = Join-Path $Root "dist\AI_Project_Console-$Version-$Runtime-setup.exe"
if (-not (Test-Path $Setup)) { throw "Inno Setup 後找不到 $Setup" }
Invoke-AuthenticodeSign -Files @($Setup) -CorrelationId "AI_Project_Console-$Version-$Runtime-setup"

Write-Host "PACK:checksum"
function Write-Sha256Sidecar([string]$Path) {
    $hash = (Get-FileHash -Algorithm SHA256 -Path $Path).Hash.ToLowerInvariant()
    $name = Split-Path $Path -Leaf
    Set-Content -Path ($Path + ".sha256") -Value "$hash  $name" -Encoding ascii -NoNewline
    Write-Host "  $($name).sha256"
}
Write-Sha256Sidecar $Setup
Write-Sha256Sidecar $Zip

Write-Host "PACK:done"
Get-ChildItem (Join-Path $Root "dist") -File | Select-Object Name, @{N="SizeMB";E={[math]::Round($_.Length/1MB,2)}} | Format-Table -AutoSize
