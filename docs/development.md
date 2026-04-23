# Local Development

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Model files in `models/nllb/` (see [Model Setup](models.md))

## Run

From the repository root:

```bash
dotnet run --project src/Lopatnov.Translate.Grpc
```

The gRPC server starts on **http://localhost:5100** (HTTP/2, no TLS).

## Try it with grpcurl

Install [grpcurl](https://github.com/fullstorydev/grpcurl):

```bash
winget install fullstorydev.grpcurl   # Windows
brew install grpcurl                  # macOS
```

**bash / macOS / Linux:**

```bash
grpcurl -plaintext -d '{
  "text": "Привіт, як справи?",
  "source_language": "ukr_Cyrl",
  "target_language": "eng_Latn"
}' localhost:5100 lopatnov.translate.v1.TranslateService/TranslateText
```

**PowerShell (Windows):**

```powershell
$body = '{"text":"Привіт, як справи?","source_language":"ukr_Cyrl","target_language":"eng_Latn"}'
grpcurl -plaintext -d $body localhost:5100 lopatnov.translate.v1.TranslateService/TranslateText
```

Full API examples: [docs/api.md](api.md).

## Configuration

Override any setting via environment variable (double underscore = section nesting):

| Variable                  | Default (local dev)     | Description                   |
| ------------------------- | ----------------------- | ----------------------------- |
| `Models__Nllb__Path`      | `../../models/nllb`     | NLLB ONNX files directory     |
| `Models__Nllb__MaxTokens` | `512`                   | Max output tokens per request |
| `LibreTranslate__BaseUrl` | `http://localhost:5000` | LibreTranslate fallback URL   |

The local dev default (`../../models/nllb`) is set in `launchSettings.json` and resolves
to `models/nllb/` at the solution root — relative to the app's content root
(`src/Lopatnov.Translate.Grpc/`).

## Testing

### Unit tests

```bash
dotnet test --filter "Category!=Integration"
```

No model files required. Tests cover:

- `NllbTokenizer` — FLORES-200 token formatting and encode/decode round-trip
- `NllbTranslator` — ONNX session calls verified with mocks
- `TranslateGrpcService` — provider dispatch (keyed DI) and default-to-nllb fallback
- `JsonLocalizationTranslator` — nested JSON traversal, existing_translation reuse, blank string handling

### Integration tests

Require model files in `models/nllb/`.

```bash
dotnet test --filter "Category=Integration"
```

Translates Ukrainian→English and Russian→English reference sentences through the real ONNX models:

- `Привіт, як справи?`
- `Сьогодні гарна погода.`
- `Дякую за вашу допомогу.`
- `Добрый день, чем могу помочь?`
- `Спасибо за внимание.`

Override the model path with `Models__Nllb__Path` if needed.
