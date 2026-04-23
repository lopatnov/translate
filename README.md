# Lopatnov.Translate

[![CI](https://github.com/lopatnov/translate/actions/workflows/ci.yml/badge.svg)](https://github.com/lopatnov/translate/actions/workflows/ci.yml)

Self-hosted speech and text translation service. **.NET 10 · gRPC · ONNX Runtime · Docker.**

Cascade of specialized models: **Whisper** (STT) → **NLLB-200** (text translation) → **Piper** (TTS). Each model is an independent adapter behind an interface.

Used as a backend by [Tereveni](https://github.com/lopatnov/tereveni) (messenger), [Mise](https://github.com/lopatnov/mise) (recipes), and [Pressmark](https://github.com/lopatnov/pressmark) (RSS aggregator).

---

## Status

| Phase | Scope                     | Status      |
| ----- | ------------------------- | ----------- |
| 1     | Text → Text (NLLB-200)    | **Done**    |
| 2     | Speech → Text (Whisper)   | Planned     |
| 3     | Text → Speech (Piper)     | Planned     |
| 4     | Speech → Speech (cascade) | Planned     |
| 5     | Docker, CI/CD             | In Progress |

---

## Models

Model files are excluded from the repository (`.gitignore`). Place them in `models/nllb/` before running.

### Option A — Export float32 from HuggingFace (recommended)

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

### Option B — Download quantized ONNX (lower quality)

```powershell
.\scripts\download-models.ps1
```

Downloads INT8-quantized encoder + decoder from Hugging Face. Requires authentication (`huggingface-cli login`). **Note:** the INT8 quantized model has degraded cross-attention quality and may produce garbled output for some language pairs. Use Option A for production.

### Required files

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

---

## Local Development

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Model files in `models/nllb/` (see [Models](#models) above)

### Run

From the repository root:

```bash
dotnet run --project src/Lopatnov.Translate.Grpc
```

The gRPC server starts on **http://localhost:5100** (HTTP/2, no TLS).

### Call the service (grpcurl)

Install [grpcurl](https://github.com/fullstorydev/grpcurl):

```bash
winget install fullstorydev.grpcurl   # Windows
brew install grpcurl                  # macOS
```

Translate text:

```bash
grpcurl -plaintext -d '{
  "text": "Привіт, як справи?",
  "source_language": "ukr_Cyrl",
  "target_language": "eng_Latn"
}' localhost:5100 lopatnov.translate.v1.TranslateService/TranslateText
```

Query capabilities:

```bash
grpcurl -plaintext -d '{}' localhost:5100 lopatnov.translate.v1.TranslateService/GetCapabilities
```

### Configuration

Override any setting via environment variable (double underscore = section nesting):

| Variable                  | Default (local dev)     | Default (Docker)             | Description                   |
| ------------------------- | ----------------------- | ---------------------------- | ----------------------------- |
| `Models__Nllb__Path`      | `../../models/nllb`     | `/app/models/nllb`           | NLLB ONNX files directory     |
| `Models__Nllb__MaxTokens` | `512`                   | `512`                        | Max output tokens per request |
| `LibreTranslate__BaseUrl` | `http://localhost:5000` | `http://libretranslate:5000` | LibreTranslate fallback URL   |

The local dev default (`../../models/nllb`) is set in `launchSettings.json` and resolves
to `models/nllb/` at the solution root — relative to the app's content root
(`src/Lopatnov.Translate.Grpc/`). The Docker default is set in `docker-compose.yml`.

---

## Deployment

### Option A. Pre-built image (GHCR)

Published images are available at `ghcr.io/lopatnov/translate`. **Model files are not bundled** — they must be downloaded separately and mounted as a volume.

**Step 1 — download models** to a local directory (one-time):

```powershell
.\scripts\download-models.ps1
```

This populates `models/nllb/` under the current directory with the NLLB tokenizer and ONNX weights.

**Step 2 — run with Docker Compose** (recommended):

Create a `docker-compose.yml` next to your `models/` directory:

```yaml
services:
  translate:
    image: ghcr.io/lopatnov/translate:latest
    ports:
      - "5100:5100"
    volumes:
      - ./models:/app/models:ro
    environment:
      - Models__Nllb__Path=/app/models/nllb
```

Then:

```bash
docker compose up
```

**Or run directly with `docker run`:**

```bash
docker run -p 5100:5100 \
  -v ./models:/app/models:ro \
  -e Models__Nllb__Path=/app/models/nllb \
  ghcr.io/lopatnov/translate:latest
```

Available tags: `latest`, semver (e.g. `1.2.3`, `1.2`), and short SHA (e.g. `abc1234`).

---

### Option B. Build from source

```bash
docker compose -f docker/docker-compose.yml up
```

Builds the image from the local `docker/Dockerfile` and mounts `models/` from the repository root. Useful during development.

```bash
docker build -f docker/Dockerfile -t lopatnov/translate .
```

### Environment variables (Docker)

| Variable                | Default in container |
| ----------------------- | -------------------- |
| `Models__Nllb__Path`    | `/app/models/nllb`   |
| `ASPNETCORE_HTTP_PORTS` | `5100`               |

---

## Testing

### Unit tests

```bash
dotnet test --filter "Category!=Integration"
```

Runs all unit tests (no model files required). Tests cover:

- `NllbTokenizer` — FLORES-200 token formatting and encode/decode round-trip
- `NllbTranslator` — ONNX session calls verified with mocks
- `TranslateGrpcService` — provider dispatch (keyed DI) and default-to-nllb fallback

### Integration tests

Require model files in `models/nllb/`.

```bash
dotnet test --filter "Category=Integration"
```

Translates Ukrainian→English and Russian→English reference sentences through the real ONNX models:

- `Привіт, як справи?`
- `Сьогодні гарна погода.`
- `Дякую за вашу допомогу.`
- `Добрый день, чем могу помочь?`
- `Спасибо за внимание.`

The path to models is resolved automatically from the solution root. Override with `Models__Nllb__Path` if needed.

---

## gRPC API

Package: `lopatnov.translate.v1` · Port: `5100`

Proto source: [`src/Lopatnov.Translate.Grpc/Protos/translate.proto`](src/Lopatnov.Translate.Grpc/Protos/translate.proto)

| RPC                | Phase | Status          |
| ------------------ | ----- | --------------- |
| `TranslateText`    | 1     | Available       |
| `GetCapabilities`  | 1     | Available       |
| `TranscribeAudio`  | 2     | `UNIMPLEMENTED` |
| `SynthesizeSpeech` | 3     | `UNIMPLEMENTED` |
| `TranslateAudio`   | 4     | `UNIMPLEMENTED` |

### TranslateText request

```protobuf
message TranslateTextRequest {
  string text = 1;
  string source_language = 2;  // FLORES-200 code, e.g. "ukr_Cyrl"
  string target_language = 3;  // FLORES-200 code, e.g. "eng_Latn"
  string provider = 4;         // "nllb" | "libretranslate" | "" → defaults to "nllb"
}
```

### Supported languages

`eng_Latn` · `ukr_Cyrl` · `rus_Cyrl` · `deu_Latn` · `fra_Latn` · `spa_Latn` · `pol_Latn` · `zho_Hans` · `jpn_Jpan` · `arb_Arab`

Full list via `GetCapabilities`. Uses [FLORES-200](https://github.com/facebookresearch/flores) codes.

---

## Project structure

```
src/
  Lopatnov.Translate.Grpc/           # gRPC server entry point
  Lopatnov.Translate.Core/           # interfaces and DTOs
  Lopatnov.Translate.Nllb/           # NLLB-200 via ONNX Runtime
  Lopatnov.Translate.LibreTranslate/ # HTTP fallback translator
tests/
  Lopatnov.Translate.Grpc.Tests/     # service dispatch tests
  Lopatnov.Translate.Nllb.Tests/     # tokenizer, translator, integration
  Lopatnov.Translate.Core.Tests/
models/                              # gitignored — populate before running
  nllb/
docker/
  Dockerfile
  docker-compose.yml
scripts/
  download-models.ps1    # tokenizer files + optional quantized ONNX
  export_nllb.py         # float32 ONNX export from HuggingFace (recommended)
```

---

## License

Apache 2.0 — see [LICENSE](LICENSE).

Third-party attributions, including the NLLB-200 model weights (CC-BY-NC-4.0, Meta AI),
are documented in [NOTICE.md](NOTICE.md).
