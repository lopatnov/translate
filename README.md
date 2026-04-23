[![CI](https://github.com/lopatnov/translate/actions/workflows/ci.yml/badge.svg)](https://github.com/lopatnov/translate/actions/workflows/ci.yml)
[![License: Apache 2.0](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](LICENSE)
[![GitHub issues](https://img.shields.io/github/issues/lopatnov/translate)](https://github.com/lopatnov/translate/issues)
[![Quality Gate Status](https://sonarcloud.io/api/project_badges/measure?project=lopatnov_translate&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=lopatnov_translate)
[![GitHub stars](https://img.shields.io/github/stars/lopatnov/translate?style=social)](https://github.com/lopatnov/translate/stargazers)

# Lopatnov.Translate

> Self-hosted speech and text translation service. **.NET 10 · gRPC · ONNX Runtime · Docker.**

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

## Quick Start

```powershell
# 1. Download models
.\scripts\download-models.ps1

# 2. Run the gRPC server
dotnet run --project src/Lopatnov.Translate.Grpc

# 3. Translate
grpcurl -plaintext -d '{"text":"Hello","source_language":"eng_Latn","target_language":"ukr_Cyrl"}' \
  localhost:5100 lopatnov.translate.v1.TranslateService/TranslateText
```

---

## Documentation

| Doc                                        | Description                                   |
| ------------------------------------------ | --------------------------------------------- |
| [docs/api.md](docs/api.md)                 | gRPC API reference — RPCs, messages, examples |
| [docs/models.md](docs/models.md)           | Model setup — download and export             |
| [docs/deployment.md](docs/deployment.md)   | Docker deployment, environment variables      |
| [docs/development.md](docs/development.md) | Local dev, configuration, testing             |

---

## Project Structure

```
src/
  Lopatnov.Translate.Grpc/           # gRPC server entry point
  Lopatnov.Translate.Core/           # interfaces, DTOs, JSON localization
  Lopatnov.Translate.Nllb/           # NLLB-200 via ONNX Runtime
  Lopatnov.Translate.LibreTranslate/ # HTTP fallback translator
tests/
  Lopatnov.Translate.Grpc.Tests/     # service dispatch tests
  Lopatnov.Translate.Nllb.Tests/     # tokenizer, translator, integration
  Lopatnov.Translate.Core.Tests/     # JSON localization translator
docs/
  api.md          # gRPC API reference
  models.md       # model setup
  deployment.md   # Docker deployment
  development.md  # local dev and testing
models/           # gitignored — populate before running
  nllb/
docker/
  Dockerfile
  docker-compose.yml
scripts/
  download-models.ps1   # tokenizer files + optional quantized ONNX
  export_nllb.py        # float32 ONNX export from HuggingFace (recommended)
```

---

## Contributing

Contributions are welcome. Please read [CONTRIBUTING.md](CONTRIBUTING.md) before opening a pull request.

- Bug reports → [open an issue](https://github.com/lopatnov/translate/issues)
- Found it useful? A [star on GitHub](https://github.com/lopatnov/translate/stargazers) helps others discover the project

---

## License

[Apache 2.0](LICENSE) © 2026 [Oleksandr Lopatnov](https://github.com/lopatnov) · [LinkedIn](https://www.linkedin.com/in/lopatnov/)
