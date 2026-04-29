#Requires -Version 7
<#
.SYNOPSIS
    Downloads M2M-100-1.2B tokenizer files and exports ONNX models.
.DESCRIPTION
    Step 1: Downloads tokenizer files from facebook/m2m100_1.2B via huggingface-cli.
    Step 2: Exports encoder + decoder ONNX models via optimum-cli.
            Requires: pip install "optimum[exporters]"

    LICENSE NOTE
    ------------
    M2M-100 is licensed under MIT — suitable for commercial use.
    (Contrast: NLLB-200 is CC BY-NC 4.0, non-commercial only.)

.EXAMPLE
    # Default: download tokenizer + export float32 ONNX
    .\scripts\download-m2m100.ps1

    # Float16 export (smaller, faster on GPU):
    .\scripts\download-m2m100.ps1 -Dtype fp16

    # Tokenizer only (skip ONNX export):
    .\scripts\download-m2m100.ps1 -SkipExport
#>
param(
    [string]$ModelRepo  = "facebook/m2m100_1.2B",
    [string]$OutputDir  = "./models/m2m100",
    [string]$Dtype      = "fp32",
    [switch]$SkipExport
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not (Get-Command huggingface-cli -ErrorAction SilentlyContinue)) {
    Write-Error "huggingface-cli not found. Install with: pip install 'huggingface_hub[cli]'"
}

# --- Step 1: tokenizer files ---

$tokenizerFiles = @(
    "sentencepiece.bpe.model",
    "tokenizer.json",
    "tokenizer_config.json",
    "special_tokens_map.json"
)

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

foreach ($file in $tokenizerFiles) {
    $dest = Join-Path $OutputDir $file
    if (Test-Path $dest) {
        Write-Host "[skip] $file already exists"
        continue
    }
    Write-Host "[download] $file from $ModelRepo"
    huggingface-cli download $ModelRepo $file --local-dir $OutputDir
}

Write-Host "`nTokenizer files saved to: $OutputDir"

if ($SkipExport) {
    Write-Host "Skipping ONNX export (-SkipExport specified)."
    exit 0
}

# --- Step 2: ONNX export ---

if (-not (Get-Command optimum-cli -ErrorAction SilentlyContinue)) {
    Write-Warning "optimum-cli not found — skipping ONNX export."
    Write-Warning "Install with: pip install 'optimum[exporters]'"
    Write-Warning "Then run: optimum-cli export onnx --model $ModelRepo --task seq2seq-lm --dtype $Dtype $OutputDir"
    exit 0
}

$encoderPath = Join-Path $OutputDir "encoder_model.onnx"
if (Test-Path $encoderPath) {
    Write-Host "[skip] encoder_model.onnx already exists"
} else {
    Write-Host "[export] Exporting M2M-100 to ONNX (dtype=$Dtype) — this may take several minutes..."
    optimum-cli export onnx `
        --model $ModelRepo `
        --task seq2seq-lm `
        --dtype $Dtype `
        $OutputDir
    Write-Host "[done] ONNX export complete."
}

Write-Host "`nDone. Models saved to: $OutputDir"
Write-Host "Set Models__M2M100__Path=$OutputDir in your environment or docker/.env to enable the provider."
