# Local Development

## Contents

- [Prerequisites](#prerequisites)
- [Run](#run)
- [Try it with grpcurl](#try-it-with-grpcurl)
- [Configuration](#configuration)
- [Testing](#testing)

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- At least one translation model downloaded — see [docs/models.md](models.md)
- *(Optional)* A Whisper model for STT — see [Whisper](models.md#whisper)

---

## Run

From the repository root:

```bash
dotnet run --project src/Lopatnov.Translate.Grpc
```

The gRPC server starts on **http://localhost:5100** (HTTP/2, no TLS).

---

## Try it with grpcurl

Install [grpcurl](https://github.com/fullstorydev/grpcurl):

```bash
winget install fullstorydev.grpcurl   # Windows
brew install grpcurl                  # macOS
```

**Translate text (bash / macOS / Linux):**

```bash
grpcurl -plaintext -d '{
  "text": "Привіт, як справи?",
  "source_language": "uk",
  "target_language": "en"
}' localhost:5100 lopatnov.translate.v1.TranslateService/TranslateText
```

**Translate text (PowerShell):**

```powershell
$body = '{"text":"Привіт, як справи?","source_language":"uk","target_language":"en"}'
grpcurl -plaintext -d $body localhost:5100 lopatnov.translate.v1.TranslateService/TranslateText
```

**Transcribe audio (PowerShell):**

```powershell
$audioBase64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes("recording.wav"))
$body = "{`"audio_data`": `"$audioBase64`", `"language`": `"auto`"}"
grpcurl -plaintext -d $body localhost:5100 lopatnov.translate.v1.TranslateService/TranscribeAudio
```

Full API examples: [docs/api.md](api.md).

---

## Configuration

Override any `appsettings.json` setting via environment variable (double underscore = section nesting).

**Common overrides for local development:**

| Variable | Default (local) | Description |
|---|---|---|
| `Translation__DefaultModel` | `m2m100_418M` | Default translation model |
| `Translation__AudioToText` | `whisper-small` | STT model key; set to `""` to disable |
| `Translation__AutoDetect` | `lid-176-ftz` | Language detection model key |
| `Translation__ModelTtlMinutes` | `30` | Minutes idle before model is unloaded |
| `Models__m2m100_418M__Path` | `../../models/translate/m2m100_418M` | Path override for M2M-100 418M |
| `Models__whisper-small__Path` | `../../models/audio-to-text/whisper.cpp/ggml-small.bin` | Path override for Whisper small |

All `Models__<key>__Path` variables follow the same pattern. See [docs/models.md](models.md) for all model keys.

---

## Testing

### Unit tests

No model files required. Run from the repository root:

```bash
dotnet test --filter "Category!=Integration"
```

**Coverage:**

| Project | What's tested |
|---|---|
| `Core.Tests` | `LanguageCodeConverter`, `HeuristicLanguageDetector`, `FastTextLanguageDetector` (unit) |
| `Nllb.Tests` | `NllbTokenizer` encode/decode round-trip |
| `M2M100.Tests` | `M2M100Tokenizer` — language token IDs, BPE encode/decode |
| `Whisper.Tests` | `WhisperRecognizer.ResampleToWhisperFormat` (unit), lazy/dispose guards |
| `Grpc.Tests` | `TranslateGrpcService` — model dispatch, allowlist, auto-detect, language format conversion |

### Integration tests

Require model files. Tests skip automatically if the model is not found.

```bash
dotnet test --filter "Category=Integration"
```

**What runs with models present:**

| Project | Models needed | What's tested |
|---|---|---|
| `Core.Tests` | `lid.176.ftz` (FastText), `model_v3.bin` (GlotLID) | Language detection accuracy across 10+ languages |
| `Nllb.Tests` | NLLB-200 600M | Ukrainian→English, Russian→English translation |
| `M2M100.Tests` | M2M-100 418M | Ukrainian→English, Russian→English translation; tokenizer round-trips |
| `Whisper.Tests` | `ggml-small.bin` | Silent audio pipeline (model load, inference, result structure) |

Override model paths with environment variables if your models are in a non-default location:

```bash
Models__whisper-small__Path=/path/to/ggml-small.bin \
  dotnet test --filter "Category=Integration"
```
