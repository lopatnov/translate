# Changelog

All notable changes to Lopatnov.Translate are documented here.

## [1.0.0] — Unreleased

### What's new

**Text translation (Phase 1 complete)**

- `TranslateText` RPC — translate a single string between any two supported languages
- `TranslateLocalization` RPC — translate an entire JSON i18n file in one call, preserving key structure, arrays, and nesting; supports incremental translation (reuse existing translations, only translate new keys)
- `GetCapabilities` RPC — query supported languages, available providers, and STT/TTS availability
- Optional `context` field on both translation RPCs (reserved for future LLM-based providers)

**Providers**

- **NLLB-200-distilled-1.3B** via ONNX Runtime — primary translation engine, runs fully offline
- **LibreTranslate** HTTP client — optional second provider, select via `provider: "libretranslate"` in the request

**Infrastructure**

- Docker Compose setup with LibreTranslate as an optional sidecar service
- Multi-stage Dockerfile (SDK build → ASP.NET runtime)
- GitHub Actions CI: build, unit tests, SonarCloud, buf lint/breaking, Trivy, Snyk, CodeQL
- Docker image published to GitHub Container Registry (`ghcr.io/lopatnov/translate`)
- Dependabot configured for NuGet, GitHub Actions, and Docker

### Supported languages

English, Ukrainian, Russian, German, French, Spanish, Polish, Chinese (Simplified), Japanese, Arabic.

### Notes

- NLLB-200 model weights are **not included** — download via `scripts/download-models.ps1`
- NLLB-200 is licensed CC BY-NC 4.0 (non-commercial use only)
- Speech-to-text (Whisper) and text-to-speech (Piper) are planned for phases 2–3
