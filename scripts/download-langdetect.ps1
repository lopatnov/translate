#Requires -Version 7
<#
.SYNOPSIS
    Downloads the GlotLID v3 language identification model (Apache 2.0).
.DESCRIPTION
    Downloads the GlotLID model from HuggingFace (cis-lmu/glotlid-model).
    Supports 1633 language varieties. License: Apache 2.0.

    Reference: Kargaran et al. (2023) "GlotLID: Language Identification for
    Low-Resource Languages". https://huggingface.co/cis-lmu/glotlid-model

    After download, verify Models__LangDetect__Path in appsettings.json
    points to the downloaded .bin file (path printed at the end).

.EXAMPLE
    .\scripts\download-langdetect.ps1
    .\scripts\download-langdetect.ps1 -OutputDir ./models/langdetect
#>
param(
    [string]$ModelRepo = "cis-lmu/glotlid-model",
    [string]$OutputDir = "./models/langdetect"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not (Get-Command huggingface-cli -ErrorAction SilentlyContinue)) {
    Write-Error "huggingface-cli not found. Install with: pip install 'huggingface_hub[cli]'"
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

Write-Host "[download] Fetching model files from $ModelRepo ..."
huggingface-cli download $ModelRepo --local-dir $OutputDir --ignore-patterns "*.md" "*.txt"

# Find the downloaded model file (.ftz preferred for size, fallback to .bin)
$model = Get-ChildItem $OutputDir -Filter "*.ftz" | Select-Object -First 1
if (-not $model) {
    $model = Get-ChildItem $OutputDir -Filter "*.bin" | Select-Object -First 1
}

if (-not $model) {
    Write-Warning "No .ftz or .bin file found in $OutputDir. Check what was downloaded:"
    Get-ChildItem $OutputDir | ForEach-Object { Write-Host "  $($_.Name)" }
    exit 1
}

$size = [math]::Round($model.Length / 1MB, 1)
Write-Host "[done] Model: $($model.Name) ($size MB)"
Write-Host "`nSet in appsettings.json or environment:"
Write-Host "  Models__LangDetect__Path = $((Resolve-Path $model.FullName).Path)"
