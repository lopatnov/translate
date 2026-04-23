# Model Setup

Model files are excluded from the repository (`.gitignore`). Place them in `models/nllb/` before running.

## Option A — Export float32 from HuggingFace (recommended)

Requires Python with PyTorch and `transformers`:

```bash
pip install torch transformers
python scripts/export_nllb.py
```

Exports `facebook/nllb-200-distilled-600M` from the HuggingFace cache (downloads ~2.5 GB on first run) into `models/nllb/` as float32 ONNX models.

For the 1.3B variant:

```bash
python scripts/export_nllb.py --model facebook/nllb-200-distilled-1.3B --output ./models/nllb
```

After export, download the tokenizer files:

```powershell
.\scripts\download-models.ps1   # downloads sentencepiece.bpe.model, tokenizer.json, etc.
```

## Option B — Download quantized ONNX (lower quality)

```powershell
.\scripts\download-models.ps1
```

Downloads INT8-quantized encoder + decoder from Hugging Face. Requires authentication (`huggingface-cli login`).

> **Note:** the INT8 quantized model has degraded cross-attention quality and may produce garbled output for some language pairs. Use Option A for production.

## Required files

After running either option above, `models/nllb/` should contain:

```text
models/nllb/
├── encoder_model.onnx
├── encoder_model.onnx.data
├── decoder_model.onnx
├── decoder_model.onnx.data
├── sentencepiece.bpe.model
└── tokenizer.json
```

| File                                  | Description                   |
| ------------------------------------- | ----------------------------- |
| `encoder_model.onnx` (+ `.onnx.data`) | NLLB encoder                  |
| `decoder_model.onnx` (+ `.onnx.data`) | NLLB decoder + LM head        |
| `sentencepiece.bpe.model`             | SentencePiece tokenizer       |
| `tokenizer.json`                      | FLORES-200 language token IDs |

Model weights are CC-BY-NC-4.0 (Meta AI).
