param(
    [string]$Version = "0.4.4",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$Root = Resolve-Path (Join-Path $PSScriptRoot "..")
$PublishDir = Join-Path $Root "dist\$Runtime"
$Iss = Join-Path $Root "installer\windows\setup.iss"
$IsccCandidates = @(
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:LocalAppData "Programs\Inno Setup 6\ISCC.exe")
)
$Iscc = $IsccCandidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $Iscc) {
    throw "找不到 Inno Setup 6（ISCC.exe）。請先安裝 https://jrsoftware.org/isinfo.php"
}

Write-Host "Publishing $Runtime $Configuration v$Version ..."
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
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Get-ChildItem $PublishDir -Recurse -Include *.pdb | Remove-Item -Force -ErrorAction SilentlyContinue

$Zip = Join-Path $Root "dist\AI_Project_Console-$Version-$Runtime.zip"
if (Test-Path $Zip) { Remove-Item $Zip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($PublishDir, $Zip, [System.IO.Compression.CompressionLevel]::Optimal, $false)

Write-Host "Building installer ..."
& $Iscc /Q /DMyAppVersion=$Version /DPublishDir=$PublishDir $Iss
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compile failed" }

Write-Host "Done."
Get-ChildItem (Join-Path $Root "dist") -File | Select-Object Name, @{N="SizeMB";E={[math]::Round($_.Length/1MB,2)}} | Format-Table -AutoSize
