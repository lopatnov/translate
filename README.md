# Lopatnov.Translate

> Self-hosted text translation service. **.NET 10 · gRPC · ONNX Runtime · Docker.**

[![CI](https://github.com/lopatnov/translate/actions/workflows/ci.yml/badge.svg)](https://github.com/lopatnov/translate/actions/workflows/ci.yml)
[![License: Apache 2.0](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](LICENSE)
[![GitHub issues](https://img.shields.io/github/issues/lopatnov/translate)](https://github.com/lopatnov/translate/issues)
[![Quality Gate Status](https://sonarcloud.io/api/project_badges/measure?project=lopatnov_translate&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=lopatnov_translate)
[![GitHub stars](https://img.shields.io/github/stars/lopatnov/translate?style=social)](https://github.com/lopatnov/translate/stargazers)

Exposes a gRPC API for text translation and language detection. Multiple translation models can be configured by name and selected per request.

---

## Getting Started

### 1. Clone

```bash
git clone https://github.com/lopatnov/translate.git
cd translate
```

### 2. Download models and configure paths

Download one or more translation models from HuggingFace (see [docs/models.md](docs/models.md)), then set the `Path` for each model in `src/Lopatnov.Translate.Grpc/appsettings.json`.

### 3. Start

```bash
cd docker
docker compose up --build
```

### 4. Translate

```bash
grpcurl -plaintext \
  -d '{"text":"Hello","source_language":"eng_Latn","target_language":"ukr_Cyrl"}' \
  localhost:5100 lopatnov.translate.v1.TranslateService/TranslateText
```

The gRPC server runs on port `5100`. See [docs/api.md](docs/api.md) for the full API reference.

---

## Documentation

| Doc                                        | Description                                   |
| ------------------------------------------ | --------------------------------------------- |
| [docs/api.md](docs/api.md)                 | gRPC API reference — RPCs, messages, examples |
| [docs/models.md](docs/models.md)           | Model setup — download, export, configuration |
| [docs/deployment.md](docs/deployment.md)   | Docker deployment                             |
| [docs/development.md](docs/development.md) | Local dev, build, testing                     |

---

## Project Structure

```
src/
  Lopatnov.Translate.Grpc/           # gRPC server, configuration, DI wiring
  Lopatnov.Translate.Core/           # interfaces, language detection, JSON localization
  Lopatnov.Translate.Nllb/           # NLLB-200 translator (ONNX Runtime)
  Lopatnov.Translate.M2M100/         # M2M-100 translator (ONNX Runtime)
  Lopatnov.Translate.LibreTranslate/ # LibreTranslate HTTP client
tests/
  Lopatnov.Translate.Grpc.Tests/     # service dispatch, model session manager
  Lopatnov.Translate.Core.Tests/     # language detection, JSON localization
  Lopatnov.Translate.Nllb.Tests/     # tokenizer, translator, integration
  Lopatnov.Translate.M2M100.Tests/   # tokenizer, translator, integration
docs/
  api.md          # gRPC API reference
  models.md       # model setup
  deployment.md   # Docker deployment
  development.md  # local dev and testing
models/           # gitignored — populate with download/export scripts
  nllb/
  m2m100/
  langdetect/
docker/
  Dockerfile
  docker-compose.yml
scripts/
  download-nllb.ps1        # tokenizer files + optional quantized ONNX
  download-m2m100.ps1      # tokenizer + optional ONNX export
  download-langdetect.ps1  # GlotLID (1633 languages) + optional LID-176
  export_nllb.py           # float32 ONNX export from HuggingFace
```

---

## Contributing

Contributions are welcome. Please read [CONTRIBUTING.md](CONTRIBUTING.md) before opening a pull request.

- Bug reports → [open an issue](https://github.com/lopatnov/translate/issues)
- Found it useful? A [star on GitHub](https://github.com/lopatnov/translate/stargazers) helps others discover the project

---

## License

[Apache 2.0](LICENSE) © 2026 [Oleksandr Lopatnov](https://github.com/lopatnov) · [LinkedIn](https://www.linkedin.com/in/lopatnov/)
