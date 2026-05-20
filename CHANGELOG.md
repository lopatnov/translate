# Changelog

All notable changes to Lopatnov.Translate are documented here.

## [2.0.0] — Unreleased

### Added

**Text-to-speech (Piper TTS)**

- `SynthesizeSpeech` RPC — synthesize text to a WAV audio file
- **Piper TTS** via ONNX Runtime — language-specific voices, MIT-licensed
- Phonemization via **espeak-ng** subprocess (must be installed separately)
- Multi-speaker voices supported via the `voice` field (e.g. Ukrainian has 3 speakers: `lada`, `mykyta`, `tetiana`)
- `speed` field — controls speech rate (`0.5`–`2.0`, default `1.0`)
- Lazy model load on first request; TTL eviction after inactivity
- `GetCapabilities.tts_available` flag and `available_voices` list
- `Translation:TextToAudio` config — maps ISO 639-1 language codes to Piper model keys

**Speech-to-speech translation**

- `TranslateAudio` RPC — end-to-end pipeline: Whisper STT → text translation → Piper TTS in one call
- Returns transcription, translated text, and synthesized WAV audio
- `target_voice` field for selecting a speaker in multi-speaker target voices
- Requires both `AudioToText` (Whisper) and `TextToAudio` (Piper) to be configured

**GPU / NPU inference acceleration**

- `ExecutionProvider` field on every model entry in `appsettings.json` — values: `auto`, `cpu`, `directml`, `cuda`
- `auto` (default) probes the best available backend at startup: DirectML on Windows, CUDA on Linux, CPU fallback
- **Whisper.net** auto GPU selection — installs multiple runtime packages simultaneously; `RuntimeOptions.RuntimeLibraryOrder` selects the best available at runtime (Cuda → Cuda12 → Vulkan → CoreML → Cpu)
- Explicit values force the provider with a logged warning + CPU fallback if unavailable
- Intel Arc supported via DirectML (Windows) and Vulkan (Whisper.net)

**Model distribution (Redirect type)**

- New model type `Redirect` — forwards translation requests to another instance of this service over gRPC
- `RedirectUrl` — gRPC endpoint of the remote service (e.g. `http://192.168.1.100:5100`)
- `RedirectName` — model name to request on the remote; defaults to the local key name if omitted
- Cycle detection via `x-redirect-id` header — returns `FAILED_PRECONDITION` if a routing loop is detected

**Model warm-up at startup**

- `Translation:WarmUp` — list of model keys to pre-load at service startup
- Runs warm-up inference concurrently with request serving; each model logs elapsed time
- Eliminates cold-start latency on the first real request
- Failures are logged as warnings — the service stays up even if warm-up fails

**Speech-to-text**

- `TranscribeAudio` RPC — transcribe a WAV audio file to text
- **Whisper** via Whisper.net (whisper.cpp backend) — 99 languages, MIT-licensed
- Any WAV format accepted — resampled automatically to 16 kHz mono before inference
- Lazy model load on first request; TTL eviction after inactivity
- Language auto-detection supported (`language: "auto"`)

**Language detection**

- `DetectLanguage` RPC — detect the language of a text string
- Heuristic detector built-in (22 languages, no model required)
- Optional FastText detector: LID-176 (~1 MB, 176 languages) or GlotLID v3 (~1.6 GB, 1633 language varieties)

**Translation models**

- **M2M-100 418M** via ONNX Runtime — 100 languages, MIT-licensed (commercial use allowed)
- **M2M-100 1.2B** configuration supported (same tokenizer, larger weights)

**API**

- `language_format` field on all RPCs — choose between BCP-47 (default), FLORES-200, ISO 639-1/2/3, or `native` (pass-through)
- `source_language: "auto"` in `TranslateText` — auto-detects source language before translating
- `GetCapabilities` reports `stt_available`, `tts_available`, `available_models`, and `available_voices`
- Invalid language codes now return `InvalidArgument` instead of an internal server error

**Infrastructure**

- Named model registry — configure any number of models by name in `appsettings.json`; select per request via the `model` field
- `ModelSessionManager` — lazy model loading with configurable TTL eviction and per-model allowlist
- MCP server client in `clients/translate-mcp/` — integrates the service as an AI tool in Claude and other MCP hosts; tools: `translate_text`, `translate_localization`, `detect_language`, `transcribe_audio`, `synthesize_speech`, `translate_audio`, `get_capabilities`
- Angular web UI in `clients/translate-angular/` — 7 pages: text translation, language detection, JSON localization, speech-to-text (with browser microphone recording), text-to-speech, speech-to-speech, and live streaming translation with VAD
- Hadolint Dockerfile linting added to CI
- Trivy dependency vulnerability scanning added to CI
- NuGet `dotnet list package --vulnerable` check added to CI
- Playwright end-to-end tests in CI (83 tests across all Angular pages)
- xUnit upgraded to v3 (3.2.2) across all 6 test projects

### Fixed

- **FastText GlotLID accuracy** — `</s>` EOS token embedding was excluded from hidden vector computation, causing systematic misclassification (e.g. Pitcairnese Creole → French, Plains Cree → Spanish); fixed by including the EOS row in the sum
- **ONNX concurrent inference crash** (`0xC0000005`) — `InferenceSession.Run()` is not thread-safe; added `SemaphoreSlim(1,1)` serialization in both `M2M100Translator` and `NllbTranslator`
- **Norwegian Nynorsk (`nno_Latn`) crash** — `ArgumentException` from M2M100 tokenizer when Whisper detects a language not in the M2M100 vocabulary; now returned as `InvalidArgument` gRPC status

### Notes

- Model weights are **not included** — download them with `hf` (see `docs/models.md`)
- NLLB-200 is licensed CC BY-NC 4.0 (non-commercial use only); M2M-100, Whisper, and Piper voices are MIT
- espeak-ng is GPL v3 (called as a subprocess — does not affect this project's license)

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

- Model weights are **not included** — download via `hf` (see `docs/models.md`)
- NLLB-200 is licensed CC BY-NC 4.0 (non-commercial use only)
