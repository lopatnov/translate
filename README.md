# Lopatnov.Translate

> Self-hosted speech and text translation service. **.NET 10 · gRPC · ONNX Runtime · Docker.**

[![CI](https://github.com/lopatnov/translate/actions/workflows/ci.yml/badge.svg)](https://github.com/lopatnov/translate/actions/workflows/ci.yml)
[![License: Apache 2.0](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](LICENSE)
[![GitHub issues](https://img.shields.io/github/issues/lopatnov/translate)](https://github.com/lopatnov/translate/issues)
[![Quality Gate Status](https://sonarcloud.io/api/project_badges/measure?project=lopatnov_translate&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=lopatnov_translate)
[![GitHub stars](https://img.shields.io/github/stars/lopatnov/translate?style=social)](https://github.com/lopatnov/translate/stargazers)

A self-hosted gRPC service for text translation, language detection, and speech-to-text transcription. All models run locally — no cloud dependencies. Multiple models can be configured by name and selected per request.

---

## Getting Started

### 1. Clone

```bash
git clone https://github.com/lopatnov/translate.git
cd translate
```

### 2. Download models

Download the default translation and STT models:

```powershell
# Speech-to-text (Whisper small, ~500 MB)
.\scripts\download-whisper.ps1 -ModelSize small

# Translation model — see docs/models.md for all options
huggingface-cli download lopatnov/m2m100_418M-onnx --local-dir ./models/translate/m2m100_418M
```

See [docs/models.md](docs/models.md) for all available models and configuration.

### 3. Start

```bash
docker compose -f docker/docker-compose.yml up --build
```

### 4. Translate text

```bash
grpcurl -plaintext \
  -d '{"text":"Hello","source_language":"en","target_language":"uk"}' \
  localhost:5100 lopatnov.translate.v1.TranslateService/TranslateText
```

### 5. Transcribe audio

```bash
# Linux
grpcurl -plaintext \
  -d "{\"audio_data\": \"$(base64 -w0 my-audio.wav)\", \"language\": \"auto\"}" \
  localhost:5100 lopatnov.translate.v1.TranslateService/TranscribeAudio

# macOS (base64 has no -w flag)
grpcurl -plaintext \
  -d "{\"audio_data\": \"$(base64 my-audio.wav | tr -d '\n')\", \"language\": \"auto\"}" \
  localhost:5100 lopatnov.translate.v1.TranslateService/TranscribeAudio
```

The gRPC server runs on port `5100`. See [docs/api.md](docs/api.md) for the full API reference.

---

## Documentation

| Doc | Description |
|---|---|
| [docs/api.md](docs/api.md) | gRPC API reference — RPCs, messages, examples |
| [docs/models.md](docs/models.md) | Model setup — download, configuration, licenses |
| [docs/deployment.md](docs/deployment.md) | Docker deployment |
| [docs/development.md](docs/development.md) | Local dev, build, testing |

---

## Project Structure

```
src/
  Lopatnov.Translate.Grpc/           # gRPC server, DI wiring, model registry
  Lopatnov.Translate.Core/           # interfaces, language detection, JSON localization
  Lopatnov.Translate.Nllb/           # NLLB-200 translator (ONNX Runtime)
  Lopatnov.Translate.M2M100/         # M2M-100 translator (ONNX Runtime)
  Lopatnov.Translate.Whisper/        # Whisper speech-to-text (Whisper.net)
  Lopatnov.Translate.LibreTranslate/ # LibreTranslate HTTP client

tests/
  Lopatnov.Translate.Grpc.Tests/     # service dispatch, model session manager
  Lopatnov.Translate.Core.Tests/     # language detection, JSON localization
  Lopatnov.Translate.Nllb.Tests/     # tokenizer, translator, integration
  Lopatnov.Translate.M2M100.Tests/   # tokenizer, translator, integration
  Lopatnov.Translate.Whisper.Tests/  # audio resampling, recognizer, integration

models/                              # gitignored — populate with download scripts
  translate/                         # M2M-100, NLLB ONNX files
  detect-lang/                       # FastText LID-176, GlotLID
  audio-to-text/                     # Whisper ggml files
  text-to-audio/                     # Piper voice files

scripts/
  download-whisper.ps1               # fetch Whisper ggml model from HuggingFace

docker/
  Dockerfile
  docker-compose.yml
```

---

## Contributing

Contributions are welcome. Please read [CONTRIBUTING.md](CONTRIBUTING.md) before opening a pull request.

- Bug reports → [open an issue](https://github.com/lopatnov/translate/issues)
- Found it useful? A [star on GitHub](https://github.com/lopatnov/translate/stargazers) helps others discover the project

---

## License

[Apache 2.0](LICENSE) © 2026 [Oleksandr Lopatnov](https://github.com/lopatnov) · [LinkedIn](https://www.linkedin.com/in/lopatnov/)
