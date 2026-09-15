#Requires -Version 7
<#
.SYNOPSIS
  打包公司工作區 Docker 產物（tag 前綴 workspace-v*）並寫 checksum。

.PARAMETER Version
  語意版號，例如 0.1.0 → 產物目錄 dist/workspace-v0.1.0/

.PARAMETER SkipBuild
  略過 docker build（僅複製 compose 並對已有映像算 digest／checksum）
#>
param(
    [string]$Version = "0.1.0",
    [string]$ImageName = "aiproject-workspace",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$tag = "workspace-v$Version"
$out = Join-Path $root "dist\$tag"
New-Item -ItemType Directory -Force -Path $out | Out-Null

$fullImage = "${ImageName}:$tag"
if (-not $SkipBuild) {
    Write-Host "docker build $fullImage ..."
    docker build -f (Join-Path $root "deploy\workspace\Dockerfile") -t $fullImage $root
    if ($LASTEXITCODE -ne 0) { throw "docker build failed" }
}

Copy-Item (Join-Path $root "deploy\workspace\docker-compose.yml") (Join-Path $out "docker-compose.yml") -Force
Copy-Item (Join-Path $root "deploy\workspace\.env.example") (Join-Path $out ".env.example") -Force

$digestFile = Join-Path $out "image-digest.txt"
$digest = ""
try {
    $digest = (docker image inspect $fullImage --format "{{index .RepoDigests 0}}").Trim()
    if (-not $digest) {
        $id = (docker image inspect $fullImage --format "{{.Id}}").Trim()
        $digest = $id
    }
}
catch {
    $digest = "(local image; push to registry to get repo digest)"
}
Set-Content -Path $digestFile -Value $digest -Encoding utf8

$manifest = @"
# AI_Project 公司工作區產物 $tag
# 同一映像：Hosting:Mode=SaaS|SelfHosted（見 docs/product/deploy-company.md）
image: $fullImage
digest: $digest
compose: docker-compose.yml
"@
Set-Content -Path (Join-Path $out "MANIFEST.txt") -Value $manifest.Trim() -Encoding utf8

$shaFile = Join-Path $out "SHA256SUMS.txt"
$lines = @()
Get-ChildItem $out -File | Where-Object { $_.Name -ne "SHA256SUMS.txt" } | ForEach-Object {
    $hash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $lines += "$hash  $($_.Name)"
}
Set-Content -Path $shaFile -Value ($lines -join "`n") -Encoding utf8

Write-Host "Wrote $out"
Write-Host "Attach these files to a GitHub Release tag $tag (workspace-v* channel)."
