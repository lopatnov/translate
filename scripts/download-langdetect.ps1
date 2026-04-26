#Requires -Version 7
<#
.SYNOPSIS
    Downloads the OpenLID language identification model (MIT license).
.DESCRIPTION
    Downloads the OpenLID compressed fastText model (~1.5 MB, MIT license)
    from HuggingFace. Supports 201 languages.

    Reference: Burchell et al. (2023) "A Overwhelmingly Large Compendium of
    Parallel Corpora". HuggingFace: laurieburchell/open-lid
    License: MIT

    After download, set Models__LangDetect__Path in appsettings.json or
    as an environment variable to the path printed at the end of this script.

.EXAMPLE
    .\scripts\download-langdetect.ps1
    .\scripts\download-langdetect.ps1 -OutputDir ./models/langdetect
#>
param(
    [string]$ModelRepo  = "laurieburchell/open-lid",
    [string]$ModelFile  = "lid201-7.ftz",
    [string]$OutputDir  = "./models/langdetect"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not (Get-Command huggingface-cli -ErrorAction SilentlyContinue)) {
    Write-Error "huggingface-cli not found. Install with: pip install 'huggingface_hub[cli]'"
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$Dest = Join-Path $OutputDir $ModelFile

if (Test-Path $Dest) {
    Write-Host "[skip] $ModelFile already exists at $Dest"
} else {
    Write-Host "[download] $ModelFile from $ModelRepo ..."
    huggingface-cli download $ModelRepo $ModelFile --local-dir $OutputDir
    $size = (Get-Item $Dest).Length
    Write-Host "[done] $ModelFile saved ($([math]::Round($size/1KB, 1)) KB)"
}

Write-Host "`nSet in appsettings.json or environment:"
Write-Host "  Models__LangDetect__Path = $((Resolve-Path $Dest).Path)"
