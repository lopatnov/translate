#Requires -Version 7
<#
.SYNOPSIS
    Downloads NLLB-200 ONNX models from Hugging Face into ./models/nllb/.
.DESCRIPTION
    Uses huggingface-cli (pip install huggingface_hub[cli]) for auth, resumption,
    and revision pinning. Set HF_TOKEN env var for gated repos.

    MODEL QUALITY NOTE
    ------------------
    The default forkjoin INT8 (IntegerOps) quantized model has degraded cross-attention
    quality: the gap between the correct target-language token and garbage tokens is
    ~0.8 logits instead of the expected 3-5 logits. This causes multi-script garbage
    output (e.g. Sinhala/Telugu characters when requesting English).

    For production use, prefer a float16 or QDQ-quantized export. See the optimum
    export section below.

.EXAMPLE
    # Default: download the forkjoin INT8 quantized model (fast, lower quality)
    .\scripts\download-models.ps1

    # Export float16 with optimum (recommended for production):
    #   pip install "optimum[exporters]"
    #   optimum-cli export onnx `
    #       --model facebook/nllb-200-distilled-600M `
    #       --task seq2seq-lm `
    #       --dtype fp16 `
    #       .\models\nllb\
    # Then update appsettings.json:
    #   "Models": { "Nllb": { "EncoderFile": "encoder_model.onnx",
    #                          "DecoderFile": "decoder_model.onnx" } }
#>
param(
    [string]$ModelRepo = "forkjoin/nllb-200-distilled-1.3B-onnx",
    [string]$OutputDir = "./models/nllb"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not (Get-Command huggingface-cli -ErrorAction SilentlyContinue)) {
    Write-Error "huggingface-cli not found. Install with: pip install 'huggingface_hub[cli]'"
}

$files = @(
    "encoder_model_quantized.onnx",
    "decoder_model_quantized.onnx",
    "sentencepiece.bpe.model",
    "tokenizer.json",
    "tokenizer_config.json",
    "special_tokens_map.json"
)

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

foreach ($file in $files) {
    $dest = Join-Path $OutputDir $file
    if (Test-Path $dest) {
        Write-Host "[skip] $file already exists"
        continue
    }
    Write-Host "[download] $file from $ModelRepo"
    huggingface-cli download $ModelRepo $file --local-dir $OutputDir
}

Write-Host "`nDone. Models saved to: $OutputDir"
Write-Host "NOTE: The INT8 quantized model may produce low-quality translations."
Write-Host "      See script header for instructions on exporting a float16 model."
