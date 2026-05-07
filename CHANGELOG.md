# Changelog

All notable changes to Lopatnov.Translate are documented here.

## [1.0.0] — Unreleased

### What's new

**Text translation**

- `TranslateText` RPC — translate a single string between any two supported languages
- `TranslateLocalization` RPC — translate an entire JSON i18n file in one call, preserving key structure, arrays, and nesting; supports incremental translation (reuse existing translations, only translate new keys)
- `GetCapabilities` RPC — query supported languages, available models, and STT/TTS availability
- Optional `context` field on both translation RPCs (reserved for future LLM-based providers)
- `language_format` field on all RPCs — choose between BCP-47 (default), FLORES-200, or ISO codes

**Translation models**

- **NLLB-200-distilled-600M** via ONNX Runtime — 200 languages, runs fully offline; CC-BY-NC-4.0
- **M2M-100 418M** via ONNX Runtime — 100 languages, MIT-licensed (commercial use allowed)
- **LibreTranslate** HTTP client — optional third provider, select via `model: "libretranslate"` in the request

**Speech-to-text**

- `TranscribeAudio` RPC — transcribe a WAV audio file to text
- **Whisper** via Whisper.net (whisper.cpp backend) — 99 languages, MIT-licensed
- Any WAV format accepted — resampled automatically to 16 kHz mono before inference
- Lazy model load on first request; TTL eviction after inactivity
- Language auto-detection supported (`language: "auto"`)

**Language detection**

- `DetectLanguage` RPC — detect the language of a text string
- Heuristic detector built-in (22 languages, no model required)
- Optional FastText detector: LID-176 (~1 MB) or GlotLID v3 (~1.6 GB, 1633 languages)

**Infrastructure**

- Docker Compose setup with LibreTranslate as an optional sidecar service
- Multi-stage Dockerfile (SDK build → ASP.NET runtime)
- GitHub Actions CI: build, unit tests, SonarCloud, hadolint, buf lint/breaking, and Trivy
- Docker image published to GitHub Container Registry (`ghcr.io/lopatnov/translate`)
- Dependabot configured for NuGet, GitHub Actions, and Docker
- MCP server client in `clients/translate-mcp/` — integrates the service as an AI tool

### Supported languages

Translation: up to 200 languages (NLLB-200) or 100 languages (M2M-100).
Speech-to-text: 99 languages (Whisper).
Language detection: 22 languages built-in; 176 or 1633 with optional FastText model.

### Notes

- Model weights are **not included** — download them with `huggingface-cli` (see `docs/models.md`)
- NLLB-200 is licensed CC BY-NC 4.0 (non-commercial use only); M2M-100 and Whisper are MIT
- Text-to-speech (Piper) is planned for phase 3
