# Changelog

All notable changes to Lopatnov.Translate are documented here.

## [2.0.0] — Unreleased

### Added

**Speech-to-text**

- `TranscribeAudio` RPC — transcribe a WAV audio file to text
- **Whisper** via Whisper.net (whisper.cpp backend) — 99 languages, MIT-licensed
- Any WAV format accepted — resampled automatically to 16 kHz mono before inference
- Lazy model load on first request; TTL eviction after inactivity
- Language auto-detection supported (`language: "auto"`)

**Language detection**

- `DetectLanguage` RPC — detect the language of a text string
- Heuristic detector built-in (22 languages, no model required)
- Optional FastText detector: LID-176 (~1 MB, 176 languages) or GlotLID v3 (~1.6 GB, 1633 languages)

**Translation models**

- **M2M-100 418M** via ONNX Runtime — 100 languages, MIT-licensed (commercial use allowed)
- **M2M-100 1.2B** configuration supported (same tokenizer, larger weights)

**API**

- `language_format` field on all RPCs — choose between BCP-47 (default), FLORES-200, or ISO 639-1/3
- `source_language: "auto"` in `TranslateText` — auto-detects source language before translating
- `GetCapabilities` now reports `stt_available` and `tts_available` flags
- Invalid language codes now return `InvalidArgument` instead of an internal server error

**Infrastructure**

- Named model registry — configure any number of models by name in `appsettings.json`; select per request via the `model` field
- `ModelSessionManager` — lazy model loading with configurable TTL eviction and per-model allowlist
- MCP server client in `clients/translate-mcp/` — integrates the service as an AI tool in Claude and other MCP hosts
- Hadolint Dockerfile linting added to CI
- Trivy dependency vulnerability scanning added to CI
- NuGet `dotnet list package --vulnerable` check added to CI

### Notes

- Model weights are **not included** — download them with `huggingface-cli` (see `docs/models.md`)
- NLLB-200 is licensed CC BY-NC 4.0 (non-commercial use only); M2M-100 and Whisper are MIT
- Text-to-speech (Piper) is planned for a future release

---

## [1.0.0] — 2026-04-24

Initial release — text translation over gRPC.

### Added

**Text translation**

- `TranslateText` RPC — translate a single string between any two supported languages
- `TranslateLocalization` RPC — translate an entire JSON i18n file in one call, preserving key structure, arrays, and nesting; supports incremental translation via `existing_translation`
- `GetCapabilities` RPC — query available models and supported language count
- Optional `context` field on translation RPCs (reserved for future LLM-based providers)

**Translation models**

- **NLLB-200-distilled-600M** via ONNX Runtime — 200 languages, fully offline; CC-BY-NC-4.0
- **LibreTranslate** HTTP client — optional fallback provider, deployable as a Docker Compose sidecar

**Infrastructure**

- Multi-stage Dockerfile (SDK build → ASP.NET runtime image)
- Docker Compose setup with optional LibreTranslate sidecar
- GitHub Actions CI: build, unit tests, SonarCloud analysis, buf lint/breaking check
- Docker image published to GitHub Container Registry (`ghcr.io/lopatnov/translate`)
- Dependabot configured for NuGet, GitHub Actions, and Docker

### Supported languages

Translation: up to 200 languages (NLLB-200).

### Notes

- Model weights are **not included** — download via `huggingface-cli` (see `docs/models.md`)
- NLLB-200 is licensed CC BY-NC 4.0 (non-commercial use only)
