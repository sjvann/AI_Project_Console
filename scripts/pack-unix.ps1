param(
    [Parameter(Mandatory = $true)][string]$Version,
    [ValidateSet("osx-arm64", "osx-x64", "linux-x64", "linux-arm64")]
    [string]$Runtime = "osx-arm64",
    [string]$Configuration = "Release"
)

# 在 Windows 交叉編譯，只用來確認 Photino 原生庫有進 RID 輸出。
# 這樣產出的 zip 沒有 Unix 執行權限、也不能 ad-hoc 簽署，不得當成 GitHub Release 正式檔。
$ErrorActionPreference = "Stop"
$Root = Resolve-Path (Join-Path $PSScriptRoot "..")
$PublishDir = Join-Path $Root "dist\$Runtime"
$Project = Join-Path $Root "src\AiProject.Console.App\AiProject.Console.App.csproj"

Write-Host "PACK:publish (Windows 交叉編譯 $Runtime；非正式資產)"
if (Test-Path $PublishDir) {
    Remove-Item $PublishDir -Recurse -Force
}
dotnet publish $Project `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishReadyToRun=false `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -o $PublishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失敗" }

$exe = Join-Path $PublishDir "AI_Project_Console"
if (-not (Test-Path $exe)) { throw "publish 後找不到 $exe" }

$nativeName = if ($Runtime.StartsWith("osx-")) { "Photino.Native.dylib" } else { "Photino.Native.so" }
$native = Get-ChildItem $PublishDir -Recurse -Filter $nativeName | Select-Object -First 1
if (-not $native) { throw "publish 後找不到 $nativeName" }
Write-Host "OK native $($native.FullName)"

$stage = Join-Path $Root "dist\stage-$Runtime"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }

if ($Runtime.StartsWith("osx-")) {
    $app = Join-Path $stage "AI_Project_Console.app"
    $macos = Join-Path $app "Contents\MacOS"
    $resources = Join-Path $app "Contents\Resources"
    New-Item -ItemType Directory -Force -Path $macos, $resources | Out-Null
    Copy-Item (Join-Path $PublishDir "*") $macos -Recurse -Force
    $plist = Get-Content (Join-Path $Root "installer\macos\Info.plist") -Raw
    $plist = $plist.Replace("__VERSION__", $Version)
    $iconPng = Join-Path $Root "assets\brand\logo.png"
    if (Test-Path $iconPng) {
        Copy-Item $iconPng (Join-Path $resources "AppIcon.png")
        $plist = $plist.Replace("<string>AppIcon</string>", "<string>AppIcon.png</string>")
    }
    Set-Content -Path (Join-Path $app "Contents\Info.plist") -Value $plist -Encoding utf8
}
else {
    New-Item -ItemType Directory -Force -Path $stage | Out-Null
    Copy-Item (Join-Path $PublishDir "*") $stage -Recurse -Force
    Copy-Item (Join-Path $Root "installer\linux\install.sh") (Join-Path $stage "install.sh")
}

$zip = Join-Path $Root "dist\AI_Project_Console-$Version-$Runtime.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
if ($Runtime.StartsWith("osx-")) {
    [System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
}
else {
    [System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
}

$hash = (Get-FileHash -Algorithm SHA256 -Path $zip).Hash.ToLowerInvariant()
Set-Content -Path ($zip + ".sha256") -Value "$hash  $(Split-Path $zip -Leaf)`n" -Encoding ascii -NoNewline
Write-Warning "此 zip 在 Windows 產出，沒有 Unix 執行權限。正式發行請跑 GitHub Actions「Pack Unix」。"
Write-Host "PACK:done $zip"
Get-Item $zip | Select-Object Name, @{N = "SizeMB"; E = { [math]::Round($_.Length / 1MB, 2) } }
