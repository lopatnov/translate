# Deployment

## Contents

- [Option A — Pre-built image (GHCR)](#option-a--pre-built-image-ghcr)
- [Option B — Build from source](#option-b--build-from-source)
- [Environment variables](#environment-variables)
- [Selecting models at runtime](#selecting-models-at-runtime)

---

## Option A — Pre-built image (GHCR)

Published images are available at `ghcr.io/lopatnov/translate`. **Model files are not bundled** — they must be downloaded separately and mounted as a volume.

**Step 1 — install system dependencies:**

Piper TTS requires **espeak-ng** installed on the host or in the container:

```bash
# Debian / Ubuntu / Docker image based on debian
apt-get install -y espeak-ng

# Windows — download MSI from https://github.com/espeak-ng/espeak-ng/releases and add to PATH
# macOS
brew install espeak-ng
```

**Step 2 — download models** (one-time):

```powershell
# Translation model (Apache 2.0)
huggingface-cli download lopatnov/m2m100_418M-onnx `
  --local-dir ./models/translate/m2m100_418M

# Language detection — required for source_language:"auto" and DetectLanguage RPC (CC-BY-SA 3.0)
huggingface-cli download lopatnov/fasttext-language-id lid.176.ftz `
  --local-dir ./models/detect-lang/fasttext-language-id

# Speech-to-text model (MIT)
huggingface-cli download lopatnov/whisper.cpp ggml-small.bin `
  --local-dir ./models/audio-to-text/whisper.cpp

# Text-to-speech voices (MIT) — download the languages you need
huggingface-cli download lopatnov/piper-voices `
  en_US/en_US-joe-medium.onnx en_US/en_US-joe-medium.onnx.json `
  --local-dir ./models/text-to-audio/piper-voices
```

**Step 3 — run with Docker Compose** (recommended):

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
      - Translation__DefaultModel=m2m100_418M
      - Translation__AudioToText=whisper-small
      - Models__m2m100_418M__Path=/app/models/translate/m2m100_418M
      - Models__whisper-small__Path=/app/models/audio-to-text/whisper.cpp/ggml-small.bin
```

```bash
docker compose up
```

**Or run directly with `docker run`** (Step 4 alternative):

```bash
docker run -p 5100:5100 \
  -v ./models:/app/models:ro \
  -e Translation__DefaultModel=m2m100_418M \
  -e Translation__AudioToText=whisper-small \
  -e Models__m2m100_418M__Path=/app/models/translate/m2m100_418M \
  -e Models__whisper-small__Path=/app/models/audio-to-text/whisper.cpp/ggml-small.bin \
  ghcr.io/lopatnov/translate:latest
```

Available tags: `latest`, semver (e.g. `1.2.3`, `1.2`), and short SHA (e.g. `abc1234`).

---

## Option B — Build from source

```bash
docker compose -f docker/docker-compose.yml up --build
```

Builds the image from the local `docker/Dockerfile` and mounts `models/` from the repository root.

```bash
docker build -f docker/Dockerfile -t lopatnov/translate .
```

---

## Environment variables

Override any `appsettings.json` setting via environment variable (double underscore = section nesting).

| Variable | Default | Description |
|---|---|---|
| `Translation__DefaultModel` | `m2m100_418M` | Translation model used when `model` field is empty in the request |
| `Translation__AudioToText` | `whisper-small` | STT model key; set to `""` to disable `TranscribeAudio` |
| `Translation__AutoDetect` | `lid-176-ftz` | Language detection model key |
| `Translation__AllowedModels__0` | `m2m100_418M` | First allowed translation model (array index) |
| `Translation__ModelTtlMinutes` | `30` | Minutes idle before a loaded model is evicted from memory |
| `Translation__TextToAudio__en` | `piper-en-US` | TTS voice key for English (ISO 639-1 code → model key) |
| `Translation__TextToAudio__ru` | `piper-ru-Ruslan` | TTS voice key for Russian |
| `Translation__TextToAudio__uk` | `piper-uk-Oleksa` | TTS voice key for Ukrainian |
| `Models__<key>__Path` | *(see appsettings.json)* | Path override for any model entry |
| `Models__<key>__ExecutionProvider` | `""` (auto) | ONNX provider: `auto`, `cpu`, `directml`, `cuda`; Whisper: also `vulkan`, `coreml` |
| `ASPNETCORE_HTTP_PORTS` | `5100` | gRPC server port |

**Common path overrides:**

| Variable | Docker default |
|---|---|
| `Models__m2m100_418M__Path` | `/app/models/translate/m2m100_418M` |
| `Models__whisper-small__Path` | `/app/models/audio-to-text/whisper.cpp/ggml-small.bin` |
| `Models__whisper-medium__Path` | `/app/models/audio-to-text/whisper.cpp/ggml-medium.bin` |
| `Models__lid-176-ftz__Path` | `/app/models/detect-lang/fasttext-language-id/lid.176.ftz` |
| `Models__piper-en-US__Path` | `/app/models/text-to-audio/piper-voices/en_US/en_US-joe-medium.onnx` |

---

## Selecting models at runtime

Models are identified by name. Switching models requires only a configuration change — no rebuild.

**Switch to a higher-quality translation model:**

```yaml
environment:
  - Translation__DefaultModel=m2m100_1.2B
  - Models__m2m100_1.2B__Path=/app/models/translate/m2m100_1.2B
```

**Switch to Whisper medium for better transcription:**

```yaml
environment:
  - Translation__AudioToText=whisper-medium
  - Models__whisper-medium__Path=/app/models/audio-to-text/whisper.cpp/ggml-medium.bin
```

**Disable STT entirely:**

```yaml
environment:
  - Translation__AudioToText=
```

**Expose multiple translation models** (clients can choose via the `model` field):

```yaml
environment:
  - Translation__DefaultModel=m2m100_418M
  - Translation__AllowedModels__0=m2m100_418M
  - Translation__AllowedModels__1=m2m100_1.2B
  - Models__m2m100_418M__Path=/app/models/translate/m2m100_418M
  - Models__m2m100_1.2B__Path=/app/models/translate/m2m100_1.2B
```

**Enable text-to-speech (English + Ukrainian):**

```yaml
environment:
  - Translation__TextToAudio__en=piper-en-US
  - Translation__TextToAudio__uk=piper-uk-UA
  - Models__piper-en-US__Path=/app/models/text-to-audio/piper-voices/en_US/en_US-joe-medium.onnx
  - Models__piper-uk-UA__Path=/app/models/text-to-audio/piper-voices/uk_UA/uk_UA-ukrainian_tts-medium.onnx
```

**Enable GPU acceleration (DirectML on Windows, CUDA on Linux):**

```yaml
environment:
  # Force DirectML for ONNX models (Windows only):
  - Models__m2m100_418M__ExecutionProvider=directml
  # Force CUDA for ONNX models (Linux with NVIDIA GPU):
  - Models__m2m100_418M__ExecutionProvider=cuda
  # Whisper auto-selects GPU at runtime; force a backend:
  - Models__whisper-small__ExecutionProvider=cuda
```

**Disable TTS entirely:**

```yaml
environment:
  - Translation__TextToAudio=   # empty value clears the map
```
