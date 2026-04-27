# gRPC API Reference

Package: `lopatnov.translate.v1` · Port: `5100`

Proto source: [`src/Lopatnov.Translate.Grpc/Protos/translate.proto`](../src/Lopatnov.Translate.Grpc/Protos/translate.proto)

---

## RPCs

| RPC                     | Description                               |
| ----------------------- | ----------------------------------------- |
| `TranslateText`         | Translate a text string                   |
| `TranslateLocalization` | Translate all strings in a JSON i18n file |
| `DetectLanguage`        | Detect the language of a text string      |
| `GetCapabilities`       | List available models and languages       |

---

## TranslateText

Translates a single text string between two languages.

### Request

```protobuf
message TranslateTextRequest {
  string text = 1;
  string source_language = 2;  // FLORES-200 code, e.g. "ukr_Cyrl"; or "auto" / "" to detect
  string target_language = 3;  // FLORES-200 code, e.g. "eng_Latn"
  string model = 4;            // name of the model entry from config (e.g. "nllb"); empty = default model
  string context = 5;          // optional: free-form hint for the translation (reserved for LLM-based models)
}
```

### Response

```protobuf
message TranslateTextResponse {
  string translated_text = 1;
  string detected_language = 2;  // set when source_language was "auto" or empty
  string model_used = 3;
}
```

### Example

```bash
grpcurl -plaintext \
  -proto src/Lopatnov.Translate.Grpc/Protos/translate.proto \
  -d '{"text": "Привіт, як справи?", "source_language": "ukr_Cyrl", "target_language": "eng_Latn"}' \
  localhost:5100 lopatnov.translate.v1.TranslateService/TranslateText
```

With automatic language detection:

```bash
grpcurl -plaintext \
  -proto src/Lopatnov.Translate.Grpc/Protos/translate.proto \
  -d '{"text": "Привіт, як справи?", "source_language": "auto", "target_language": "eng_Latn"}' \
  localhost:5100 lopatnov.translate.v1.TranslateService/TranslateText
```

---

## TranslateLocalization

Translates all leaf string values in a JSON localization file, preserving the key structure.
Non-string values (numbers, booleans, null) and blank strings are passed through unchanged.
Supports arbitrary nesting depth and arrays.

> `source_language` must be an explicit FLORES-200 code — `"auto"` is not supported for this RPC.

### Request

```protobuf
message TranslateLocalizationRequest {
  string json = 1;
  string source_language = 2;       // FLORES-200 code, e.g. "eng_Latn"
  string target_language = 3;       // FLORES-200 code, e.g. "ukr_Cyrl"
  string model = 4;                 // name of the model entry from config; empty = default model
  string existing_translation = 5;  // optional: same-structure JSON with already-translated values; matching keys are reused as-is
  string context = 6;               // optional: same-structure JSON with context hints per key (used by LLM-based models)
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
grpcurl -plaintext \
  -proto src/Lopatnov.Translate.Grpc/Protos/translate.proto \
  -d '{"json": "{\"auth\":{\"email\":\"Email\",\"password\":\"Password\",\"signIn\":\"Sign in\"}}", "source_language": "eng_Latn", "target_language": "ukr_Cyrl"}' \
  localhost:5100 lopatnov.translate.v1.TranslateService/TranslateLocalization
```

Incremental translation — reuse existing, translate only new keys:

```bash
grpcurl -plaintext \
  -proto src/Lopatnov.Translate.Grpc/Protos/translate.proto \
  -d '{"json": "{\"auth\":{\"email\":\"Email\",\"password\":\"Password\",\"signIn\":\"Sign in\"}}", "source_language": "eng_Latn", "target_language": "ukr_Cyrl", "existing_translation": "{\"auth\":{\"signIn\":\"Увійти\"}}"}' \
  localhost:5100 lopatnov.translate.v1.TranslateService/TranslateLocalization
```

---

## DetectLanguage

Detects the language of a text string. Requires `Translation:AutoDetect` to be configured in `appsettings.json`.

### Request

```protobuf
message DetectLanguageRequest {
  string text = 1;
}
```

### Response

```protobuf
message DetectLanguageResponse {
  string language = 1;  // FLORES-200 code, e.g. "ukr_Cyrl", "eng_Latn"
}
```

### Example

```bash
grpcurl -plaintext \
  -proto src/Lopatnov.Translate.Grpc/Protos/translate.proto \
  -d '{"text": "Guten Morgen"}' \
  localhost:5100 lopatnov.translate.v1.TranslateService/DetectLanguage
```

---

## GetCapabilities

Returns the list of configured translation models and supported languages.

```protobuf
message GetCapabilitiesResponse {
  repeated string supported_languages = 1;
  repeated string available_models = 3;
}
```

```bash
grpcurl -plaintext \
  -proto src/Lopatnov.Translate.Grpc/Protos/translate.proto \
  -d '{}' \
  localhost:5100 lopatnov.translate.v1.TranslateService/GetCapabilities
```

---

## Models

See [models.md](models.md) for configuration and download instructions.

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

Need a language that isn't listed? Feel free to [open an issue](https://github.com/lopatnov/translate/issues).
