# gRPC API Reference

Package: `lopatnov.translate.v1` · Port: `5100`

Proto source: [`src/Lopatnov.Translate.Grpc/Protos/translate.proto`](../src/Lopatnov.Translate.Grpc/Protos/translate.proto)

---

## Language code formats

All RPCs that accept or return language codes support a `language_format` field on the request.

| Value         | Description                                          | Example                    |
| ------------- | ---------------------------------------------------- | -------------------------- |
| `"bcp47"`     | BCP-47 tags — default when field is empty or omitted | `"uk"`, `"zh-Hans"`        |
| `"flores200"` | FLORES-200 codes used internally by NLLB and M2M-100 | `"ukr_Cyrl"`, `"zho_Hans"` |
| `"native"`    | No conversion — pass the code through unchanged      | any string                 |

Unknown or unrecognised codes are returned unchanged regardless of format.

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
  string source_language = 2;  // optional: language code (see language_format); or "auto" / "" to detect = default value
  string target_language = 3;  // language code (see language_format)
  string model = 4;            // optional: name of the model entry from config (e.g. "nllb"); empty = default model
  string context = 5;          // optional: free-form hint for the translation (reserved for LLM-based models)
  string language_format = 6;  // optional: "bcp47" (default), "flores200", "native"
}
```

### Response

```protobuf
message TranslateTextResponse {
  string translated_text = 1;
  string detected_language = 2;  // set when source_language was "auto" or empty; format matches request language_format
  string model_used = 3;
}
```

### Examples

BCP-47 (default):

```bash
grpcurl -plaintext \
  -proto src/Lopatnov.Translate.Grpc/Protos/translate.proto \
  -d '{"text": "Привіт, як справи?", "source_language": "uk", "target_language": "en"}' \
  localhost:5100 lopatnov.translate.v1.TranslateService/TranslateText
```

FLORES-200 sample with direct NLLB model compatibility:

```bash
grpcurl -plaintext \
  -proto src/Lopatnov.Translate.Grpc/Protos/translate.proto \
  -d '{"text": "Привіт, як справи?", "source_language": "ukr_Cyrl", "target_language": "eng_Latn", "language_format": "flores200"}' \
  localhost:5100 lopatnov.translate.v1.TranslateService/TranslateText
```

With automatic language detection (detected language returned in BCP-47 by default):

```bash
grpcurl -plaintext \
  -proto src/Lopatnov.Translate.Grpc/Protos/translate.proto \
  -d '{"text": "Привіт, як справи?", "source_language": "auto", "target_language": "en"}' \
  localhost:5100 lopatnov.translate.v1.TranslateService/TranslateText
# → {"translatedText": "...", "detectedLanguage": "uk", "modelUsed": "nllb"}
```

---

## TranslateLocalization

Translates all leaf string values in a JSON localization file, preserving the key structure.
Non-string values (numbers, booleans, null) and blank strings are passed through unchanged.
Supports arbitrary nesting depth and arrays.

> `source_language` must be an explicit language code — `"auto"` is not supported for this RPC.

### Request

```protobuf
message TranslateLocalizationRequest {
  string json = 1;
  string source_language = 2;       // language code (see language_format)
  string target_language = 3;       // language code (see language_format)
  string model = 4;                 // optional: name of the model entry from config; empty = default model
  string existing_translation = 5;  // optional: same-structure JSON with already-translated values; matching keys are reused as-is
  string context = 6;               // optional: same-structure JSON with context hints per key (used by LLM-based models)
  string language_format = 7;       // optional: "bcp47" (default), "flores200", "native"
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

Translate a full i18n file (BCP-47):

```bash
grpcurl -plaintext \
  -proto src/Lopatnov.Translate.Grpc/Protos/translate.proto \
  -d '{"json": "{\"auth\":{\"email\":\"Email\",\"password\":\"Password\",\"signIn\":\"Sign in\"}}", "source_language": "en", "target_language": "uk"}' \
  localhost:5100 lopatnov.translate.v1.TranslateService/TranslateLocalization
```

Incremental translation — reuse existing, translate only new keys:

```bash
grpcurl -plaintext \
  -proto src/Lopatnov.Translate.Grpc/Protos/translate.proto \
  -d '{"json": "{\"auth\":{\"email\":\"Email\",\"password\":\"Password\",\"signIn\":\"Sign in\"}}", "source_language": "en", "target_language": "uk", "existing_translation": "{\"auth\":{\"signIn\":\"Увійти\"}}"}' \
  localhost:5100 lopatnov.translate.v1.TranslateService/TranslateLocalization
```

---

## DetectLanguage

Detects the language of a text string. Requires `Translation:AutoDetect` to be configured in `appsettings.json`.
Returns BCP-47 by default.

### Request

```protobuf
message DetectLanguageRequest {
  string text = 1;
  string language_format = 2;  // optional: "bcp47" (default), "flores200", "native"
}
```

### Response

```protobuf
message DetectLanguageResponse {
  string language = 1;  // language code in the requested format; default BCP-47, e.g. "uk", "de", "zh-Hans"
}
```

### Examples

```bash
# BCP-47 (default)
grpcurl -plaintext \
  -proto src/Lopatnov.Translate.Grpc/Protos/translate.proto \
  -d '{"text": "Привіт, як справи?"}' \
  localhost:5100 lopatnov.translate.v1.TranslateService/DetectLanguage
# → {"language": "uk"}

# FLORES-200 sample
grpcurl -plaintext \
  -proto src/Lopatnov.Translate.Grpc/Protos/translate.proto \
  -d '{"text": "Привіт, як справи?", "language_format": "flores200"}' \
  localhost:5100 lopatnov.translate.v1.TranslateService/DetectLanguage
# → {"language": "ukr_Cyrl"}
```

---

## GetCapabilities

Returns the list of configured translation models.

```protobuf
message GetCapabilitiesResponse {
  repeated string available_models = 3;
}
```

```bash
grpcurl -plaintext \
  -proto src/Lopatnov.Translate.Grpc/Protos/translate.proto \
  -d '{}' \
  localhost:5100 lopatnov.translate.v1.TranslateService/GetCapabilities
```
