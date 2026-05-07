# Deployment

## Contents

- [Option A — Pre-built image (GHCR)](#option-a--pre-built-image-ghcr)
- [Option B — Build from source](#option-b--build-from-source)
- [Environment variables](#environment-variables)
- [Selecting models at runtime](#selecting-models-at-runtime)

---

## Option A — Pre-built image (GHCR)

Published images are available at `ghcr.io/lopatnov/translate`. **Model files are not bundled** — they must be downloaded separately and mounted as a volume.

**Step 1 — download models** (one-time):

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
```

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
      - Translation__DefaultModel=m2m100_418M
      - Translation__AudioToText=whisper-small
      - Models__m2m100_418M__Path=/app/models/translate/m2m100_418M
      - Models__whisper-small__Path=/app/models/audio-to-text/whisper.cpp/ggml-small.bin
```

```bash
docker compose up
```

**Or run directly with `docker run`:**

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
| `Models__<key>__Path` | *(see appsettings.json)* | Path override for any model entry |
| `ASPNETCORE_HTTP_PORTS` | `5100` | gRPC server port |

**Common path overrides:**

| Variable | Docker default |
|---|---|
| `Models__m2m100_418M__Path` | `/app/models/translate/m2m100_418M` |
| `Models__whisper-small__Path` | `/app/models/audio-to-text/whisper.cpp/ggml-small.bin` |
| `Models__whisper-medium__Path` | `/app/models/audio-to-text/whisper.cpp/ggml-medium.bin` |
| `Models__lid-176-ftz__Path` | `/app/models/detect-lang/fasttext-language-id/lid.176.ftz` |

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
