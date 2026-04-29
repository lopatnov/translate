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
Write-Host "Set in appsettings.json under Models:<name>:"
Write-Host "  { ""Model"": ""GlotLID"", ""Path"": ""$((Resolve-Path $model.FullName).Path)"" }"

# -------------------------------------------------------------------------
# Optional: LID-176 (Facebook, CC-BY-SA-3.0, ~917 KB compressed)
# A smaller, faster alternative supporting 176 languages.
# Use Model: "LID-176" in appsettings.json.
# -------------------------------------------------------------------------
$lid176Path = Join-Path $OutputDir "lid.176.ftz"
if (-not (Test-Path $lid176Path)) {
    Write-Host ""
    $downloadLid = Read-Host "Download LID-176 as well? (lid.176.ftz, ~917 KB) [y/N]"
    if ($downloadLid -eq "y" -or $downloadLid -eq "Y") {
        Write-Host "[download] lid.176.ftz → $lid176Path"
        Invoke-WebRequest -Uri "https://dl.fbaipublicfiles.com/fasttext/supervised-models/lid.176.ftz" `
            -OutFile $lid176Path
        Write-Host "[done] lid.176.ftz  ($([math]::Round((Get-Item $lid176Path).Length / 1KB, 0)) KB)"
        Write-Host "  { ""Model"": ""LID-176"", ""Path"": ""$((Resolve-Path $lid176Path).Path)"" }"
    }
}
