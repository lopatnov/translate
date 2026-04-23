# gRPC API Reference

Package: `lopatnov.translate.v1` · Port: `5100`

Proto source: [`src/Lopatnov.Translate.Grpc/Protos/translate.proto`](../src/Lopatnov.Translate.Grpc/Protos/translate.proto)

## RPCs

| RPC                     | Phase | Status          |
| ----------------------- | ----- | --------------- |
| `TranslateText`         | 1     | Available       |
| `TranslateLocalization` | 1     | Available       |
| `GetCapabilities`       | 1     | Available       |
| `TranscribeAudio`       | 2     | `UNIMPLEMENTED` |
| `SynthesizeSpeech`      | 3     | `UNIMPLEMENTED` |
| `TranslateAudio`        | 4     | `UNIMPLEMENTED` |

---

## TranslateText

Translates a single text string between two languages.

### Request

```protobuf
message TranslateTextRequest {
  string text = 1;
  string source_language = 2;  // FLORES-200 code, e.g. "ukr_Cyrl"
  string target_language = 3;  // FLORES-200 code, e.g. "eng_Latn"
  string provider = 4;         // "nllb" | "libretranslate" | "" → defaults to "nllb"
  string context = 5;          // optional: free-form hint, e.g. "UI button on login form" (reserved for LLM-based providers)
}
```

### Response

```protobuf
message TranslateTextResponse {
  string translated_text = 1;
  string detected_language = 2;
  string provider_used = 3;
}
```

### Example

```bash
grpcurl -plaintext -d '{
  "text": "Привіт, як справи?",
  "source_language": "ukr_Cyrl",
  "target_language": "eng_Latn"
}' localhost:5100 lopatnov.translate.v1.TranslateService/TranslateText
```

---

## TranslateLocalization

Translates all leaf string values in a JSON localization file, preserving the key structure.
Non-string values (numbers, booleans, null) and blank strings are passed through unchanged.
Supports arbitrary nesting depth (`a.b.c.d`) and arrays.

### Request

```protobuf
message TranslateLocalizationRequest {
  string json = 1;                  // JSON object, e.g. i18n / l10n file content
  string source_language = 2;       // FLORES-200 code, e.g. "eng_Latn"
  string target_language = 3;       // FLORES-200 code, e.g. "ukr_Cyrl"
  string provider = 4;              // "nllb" | "libretranslate" | "" → defaults to "nllb"
  string existing_translation = 5;  // optional: same-structure JSON; matching non-blank values are reused as-is (incremental translation)
  string context = 6;               // optional: same-structure JSON with per-key hints (reserved for LLM-based providers)
}
```

### Response

```protobuf
message TranslateLocalizationResponse {
  string json = 1;              // translated JSON, same structure as input
  int32 strings_translated = 2; // number of strings newly translated (excludes reused from existing_translation)
}
```

### Examples

Translate a full i18n file:

```bash
grpcurl -plaintext -d '{
  "json": "{\"auth\":{\"email\":\"Email\",\"password\":\"Password\",\"signIn\":\"Sign in\"}}",
  "source_language": "eng_Latn",
  "target_language": "ukr_Cyrl"
}' localhost:5100 lopatnov.translate.v1.TranslateService/TranslateLocalization
```

Incremental translation — reuse existing, translate only new keys:

```bash
grpcurl -plaintext -d '{
  "json": "{\"auth\":{\"email\":\"Email\",\"password\":\"Password\",\"signIn\":\"Sign in\"}}",
  "source_language": "eng_Latn",
  "target_language": "ukr_Cyrl",
  "existing_translation": "{\"auth\":{\"signIn\":\"Увійти\"}}"
}' localhost:5100 lopatnov.translate.v1.TranslateService/TranslateLocalization
```

---

## GetCapabilities

Returns supported languages, available providers, and STT/TTS availability.

```bash
grpcurl -plaintext -d '{}' localhost:5100 lopatnov.translate.v1.TranslateService/GetCapabilities
```

---

## Supported Languages

| Code        | Language              |
| ----------- | --------------------- |
| `eng_Latn`  | English               |
| `ukr_Cyrl`  | Ukrainian             |
| `rus_Cyrl`  | Russian               |
| `deu_Latn`  | German                |
| `fra_Latn`  | French                |
| `spa_Latn`  | Spanish               |
| `pol_Latn`  | Polish                |
| `zho_Hans`  | Chinese (Simplified)  |
| `jpn_Jpan`  | Japanese              |
| `arb_Arab`  | Arabic                |

Uses [FLORES-200](https://github.com/facebookresearch/flores) language codes. Full list via `GetCapabilities`.

---

## Providers

| Key              | Backend                   | Default |
| ---------------- | ------------------------- | ------- |
| `nllb`           | NLLB-200 via ONNX Runtime | ✅ yes  |
| `libretranslate` | LibreTranslate HTTP API   | no      |

Pass `provider` in the request to select. Empty string defaults to `"nllb"`.
