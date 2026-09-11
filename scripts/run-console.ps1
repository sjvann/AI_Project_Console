#Requires -Version 5.1
<#
.SYNOPSIS
  從原始碼啟動控制台。若已有一個在跑（bin DLL 被鎖），略過建置再開一個視窗。
#>
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\AiProject.Console.App\AiProject.Console.App.csproj"
$dll = Join-Path $root "src\AiProject.Console.App\bin\Debug\net10.0\AI_Project_Console.dll"

function Test-OutputLocked([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) {
        return $false
    }
    try {
        $fs = [System.IO.File]::Open($path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
        $fs.Dispose()
        return $false
    }
    catch {
        return $true
    }
}

$passthru = @($args)
if (Test-OutputLocked $dll) {
    Write-Host "已有控制台佔用建置輸出。略過編譯，再開一個視窗（與目前 DLL 相同）。"
    Write-Host "若要載入剛改的程式碼，請先關掉所有控制台再重新 dotnet run。"
    if ($passthru.Count -gt 0) {
        & dotnet run --no-build --project $project -- @passthru
    }
    else {
        & dotnet run --no-build --project $project
    }
    exit $LASTEXITCODE
}

if ($passthru.Count -gt 0) {
    & dotnet run --project $project -- @passthru
}
else {
    & dotnet run --project $project
}
exit $LASTEXITCODE
