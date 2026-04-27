#Requires -Version 7
<#
.SYNOPSIS
    Downloads the GlotLID v3 language identification model (Apache 2.0).
.DESCRIPTION
    Downloads GlotLID from cis-lmu/glotlid-model via huggingface-cli or hf.
    Supports 1633 language varieties. License: Apache 2.0.

    Reference: Kargaran et al. (2023) "GlotLID: Language Identification for
    Low-Resource Languages". https://huggingface.co/cis-lmu/glotlid-model
#>
param(
    [string]$ModelRepo = "cis-lmu/glotlid-model",
    [string]$OutputDir = "./models/langdetect"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Detect available HuggingFace CLI
$hfCmd = if     (Get-Command huggingface-cli -ErrorAction SilentlyContinue) { "huggingface-cli" }
         elseif (Get-Command hf              -ErrorAction SilentlyContinue) { "hf" }
         else   { Write-Error "HuggingFace CLI not found. Install: pip install 'huggingface_hub[cli]'"; exit 1 }

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

Write-Host "[download] $ModelRepo → $OutputDir  (using $hfCmd)"

if ($hfCmd -eq "huggingface-cli") {
    huggingface-cli download $ModelRepo --local-dir $OutputDir `
        --ignore-patterns "*.md" "*.txt" "*.json"
} else {
    # hf uses --exclude (repeatable) instead of --ignore-patterns
    hf download $ModelRepo --local-dir $OutputDir `
        --exclude "*.md" --exclude "*.txt" --exclude "*.json"
}

# Find the downloaded model file
$model = Get-ChildItem $OutputDir -Filter "*.ftz" -Recurse | Select-Object -First 1
if (-not $model) { $model = Get-ChildItem $OutputDir -Filter "*.bin" -Recurse | Select-Object -First 1 }

if (-not $model) {
    Write-Warning "No .ftz or .bin file found. Files downloaded:"
    Get-ChildItem $OutputDir -Recurse | ForEach-Object { Write-Host "  $($_.FullName)" }
    exit 1
}

$size = [math]::Round($model.Length / 1MB, 1)
Write-Host "[done] $($model.Name)  ($size MB)"
Write-Host ""
Write-Host "Set in appsettings.json or environment:"
Write-Host "  Models__LangDetect__Path = $((Resolve-Path $model.FullName).Path)"
