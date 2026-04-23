# Deployment

## Option A — Pre-built image (GHCR)

Published images are available at `ghcr.io/lopatnov/translate`. **Model files are not bundled** — they must be downloaded separately and mounted as a volume.

**Step 1 — download models** to a local directory (one-time):

```powershell
.\scripts\download-models.ps1
```

This populates `models/nllb/` under the current directory with the NLLB tokenizer and ONNX weights.

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
      - Models__Nllb__Path=/app/models/nllb
```

Then:

```bash
docker compose up
```

**Or run directly with `docker run`:**

```bash
docker run -p 5100:5100 \
  -v ./models:/app/models:ro \
  -e Models__Nllb__Path=/app/models/nllb \
  ghcr.io/lopatnov/translate:latest
```

Available tags: `latest`, semver (e.g. `1.2.3`, `1.2`), and short SHA (e.g. `abc1234`).

---

## Option B — Build from source

```bash
docker compose -f docker/docker-compose.yml up
```

Builds the image from the local `docker/Dockerfile` and mounts `models/` from the repository root. Useful during development.

```bash
docker build -f docker/Dockerfile -t lopatnov/translate .
```

---

## Environment variables

| Variable                  | Default (local dev)     | Default (Docker)             | Description                   |
| ------------------------- | ----------------------- | ---------------------------- | ----------------------------- |
| `Models__Nllb__Path`      | `../../models/nllb`     | `/app/models/nllb`           | NLLB ONNX files directory     |
| `Models__Nllb__MaxTokens` | `512`                   | `512`                        | Max output tokens per request |
| `LibreTranslate__BaseUrl` | `http://localhost:5000` | `http://libretranslate:5000` | LibreTranslate fallback URL   |
| `ASPNETCORE_HTTP_PORTS`   | —                       | `5100`                       | gRPC server port              |

Override any setting via environment variable (double underscore = section nesting).
