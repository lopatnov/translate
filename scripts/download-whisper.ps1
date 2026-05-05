<#
.SYNOPSIS
    Downloads a Whisper ggml model from HuggingFace (ggerganov/whisper.cpp).

.PARAMETER ModelSize
    Model size to download: tiny, base, small, medium, large, large-v2, large-v3.
    Defaults to "small".

.EXAMPLE
    .\download-whisper.ps1
    .\download-whisper.ps1 -ModelSize medium

.NOTES
    Requires: huggingface-cli (pip install huggingface_hub)
    Output:   models/audio-to-text/whisper.cpp/ggml-<size>.bin
#>
param(
    [ValidateSet("tiny", "base", "small", "medium", "large", "large-v2", "large-v3")]
    [string]$ModelSize = "small"
)

$ErrorActionPreference = "Stop"

$dest = Join-Path $PSScriptRoot ".." "models" "audio-to-text" "whisper.cpp"
$dest = [System.IO.Path]::GetFullPath($dest)
$file = "ggml-$ModelSize.bin"
$target = Join-Path $dest $file

if (Test-Path $target) {
    Write-Host "[whisper] $file already exists at $dest — skipping download." -ForegroundColor Green
    exit 0
}

if (-not (Get-Command huggingface-cli -ErrorAction SilentlyContinue)) {
    Write-Error "huggingface-cli not found. Install it with: pip install huggingface_hub"
    exit 1
}

New-Item -ItemType Directory -Force -Path $dest | Out-Null

Write-Host "[whisper] Downloading ggml-$ModelSize.bin from ggerganov/whisper.cpp ..." -ForegroundColor Cyan
huggingface-cli download ggerganov/whisper.cpp $file --local-dir $dest

Write-Host "[whisper] Done. Model saved to: $target" -ForegroundColor Green
