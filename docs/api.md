# gRPC API Reference

Package: `lopatnov.translate.v1` · Port: `5100`

Proto source: [`src/Lopatnov.Translate.Grpc/Protos/translate.proto`](../src/Lopatnov.Translate.Grpc/Protos/translate.proto)

---

## Contents

- [Language code formats](#language-code-formats)
- [RPCs](#rpcs)
- [TranslateText](#translatetext)
- [TranslateLocalization](#translatelocalization)
- [DetectLanguage](#detectlanguage)
- [TranscribeAudio](#transcribeaudio)
- [GetCapabilities](#getcapabilities)
- [Error codes](#error-codes)

---

## Language code formats

All RPCs that accept or return language codes support a `language_format` field on the request.

| Value | Description | Example |
|---|---|---|
| `"bcp47"` | BCP-47 tags. Default when field is empty or omitted. | `"uk"`, `"zh-Hans"` |
| `"flores200"` | FLORES-200 codes used internally by NLLB and M2M-100 | `"ukr_Cyrl"`, `"zho_Hans"` |
| `"iso639-1"` | ISO 639-1 two-letter codes | `"uk"`, `"de"` |
| `"iso639-2"` | ISO 639-2 three-letter codes (terminological form) | `"ukr"`, `"deu"` |
| `"iso639-3"` | ISO 639-3 three-letter codes | `"ukr"`, `"deu"` |
| `"native"` | No conversion — pass the code through unchanged. | any string |

Unknown or unrecognised codes are returned unchanged regardless of format.

---

## RPCs

| RPC | Description |
|---|---|
| `TranslateText` | Translate a text string |
| `TranslateLocalization` | Translate all strings in a JSON i18n file |
| `DetectLanguage` | Detect the language of a text string |
| `TranscribeAudio` | Transcribe speech from a WAV file |
| `GetCapabilities` | List available models and service capabilities |

---

## TranslateText

Translates a single text string between two languages.

### Request

```protobuf
message TranslateTextRequest {
  string text            = 1;
  string source_language = 2;  // language code (see language_format); "auto" or "" = auto-detect
  string target_language = 3;  // language code (see language_format)
  string model           = 4;  // model key from appsettings.json (e.g. "m2m100_418M"); "" = default
  string context         = 5;  // optional: free-form hint (reserved for LLM-based models)
  string language_format = 6;  // "bcp47" (default) | "flores200" | "native"
}
```

### Response

```protobuf
message TranslateTextResponse {
  string translated_text   = 1;
  string detected_language = 2;  // set when source_language was "auto" or ""; in language_format
  string model_used        = 3;  // key of the model that performed the translation
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

With FLORES-200 codes:

```bash
grpcurl -plaintext \
  -proto src/Lopatnov.Translate.Grpc/Protos/translate.proto \
  -d '{"text": "Привіт, як справи?", "source_language": "ukr_Cyrl", "target_language": "eng_Latn", "language_format": "flores200"}' \
  localhost:5100 lopatnov.translate.v1.TranslateService/TranslateText
```

With auto language detection:

```bash
grpcurl -plaintext \
  -proto src/Lopatnov.Translate.Grpc/Protos/translate.proto \
  -d '{"text": "Привіт, як справи?", "source_language": "auto", "target_language": "en"}' \
  localhost:5100 lopatnov.translate.v1.TranslateService/TranslateText
# → {"translatedText": "Hello, how are you?", "detectedLanguage": "uk", "modelUsed": "m2m100_418M"}
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
  string json                 = 1;
  string source_language      = 2;  // language code (see language_format)
  string target_language      = 3;
  string model                = 4;  // optional; "" = default model
  string existing_translation = 5;  // optional: same-structure JSON with already-translated values; matching keys are reused as-is
  string context              = 6;  // optional: same-structure JSON with context hints per key
  string language_format      = 7;
}
```

### Response

```protobuf
message TranslateLocalizationResponse {
  string json               = 1;  // translated JSON, same structure as input
  int32  strings_translated = 2;  // number of strings newly translated (excludes reused from existing_translation)
}
```

### Examples

Translate a full i18n file:

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

Detects the language of a text string. Returns BCP-47 by default.

Requires `Translation:AutoDetect` to be set in `appsettings.json`.
If not configured, falls back to heuristic detection (Unicode block analysis).

### Request

```protobuf
message DetectLanguageRequest {
  string text            = 1;
  string language_format = 2;  // "bcp47" (default) | "flores200" | "native"
}
```

### Response

```protobuf
message DetectLanguageResponse {
  string language    = 1;  // language code in the requested format
  float  probability = 2;  // confidence score (0.0–1.0); populated if the detector supports it
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

# FLORES-200
grpcurl -plaintext \
  -proto src/Lopatnov.Translate.Grpc/Protos/translate.proto \
  -d '{"text": "Привіт, як справи?", "language_format": "flores200"}' \
  localhost:5100 lopatnov.translate.v1.TranslateService/DetectLanguage
# → {"language": "ukr_Cyrl"}
```

---

## TranscribeAudio

Transcribes speech from a WAV audio file.

Requires `Translation:AudioToText` to be set in `appsettings.json` (see [docs/models.md](models.md#whisper)).
The model is loaded lazily on first request and unloaded after `ModelTtlMinutes` of inactivity.

### Request

```protobuf
message TranscribeAudioRequest {
  bytes  audio_data      = 1;  // WAV file bytes, max 50 MB (any sample rate / channels — resampled automatically to 16 kHz mono)
  string language        = 2;  // BCP-47 language hint (e.g. "en", "ru"); "" or "auto" = Whisper auto-detection
  string audio_format    = 3;  // reserved; pass "" or "wav"
  string language_format = 4;  // format for detected_language in response: "bcp47" (default) | "flores200"
}
```

### Response

```protobuf
message TranscribeAudioResponse {
  repeated TranscriptionSegment segments          = 1;
  string                        detected_language = 2;  // in language_format
  string                        full_text         = 3;  // all segments joined with spaces
}

message TranscriptionSegment {
  string text       = 1;
  float  start_time = 2;  // seconds from start of audio
  float  end_time   = 3;
}
```

### Examples

**bash / macOS / Linux:**

```bash
grpcurl -plaintext \
  -proto src/Lopatnov.Translate.Grpc/Protos/translate.proto \
  -d "{\"audio_data\": \"$(base64 -w0 recording.wav)\", \"language\": \"auto\"}" \
  localhost:5100 lopatnov.translate.v1.TranslateService/TranscribeAudio
```

**PowerShell (Windows):**

```powershell
$audioBase64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes("recording.wav"))
$body = "{`"audio_data`": `"$audioBase64`", `"language`": `"auto`"}"
grpcurl -plaintext `
  -proto src/Lopatnov.Translate.Grpc/Protos/translate.proto `
  -d $body `
  localhost:5100 lopatnov.translate.v1.TranslateService/TranscribeAudio
```

With explicit language and FLORES-200 response:

```bash
grpcurl -plaintext \
  -proto src/Lopatnov.Translate.Grpc/Protos/translate.proto \
  -d "{\"audio_data\": \"$(base64 -w0 recording.wav)\", \"language\": \"uk\", \"language_format\": \"flores200\"}" \
  localhost:5100 lopatnov.translate.v1.TranslateService/TranscribeAudio
# → {"segments":[{"text":"Привіт","startTime":0.0,"endTime":1.2}], "detectedLanguage":"ukr_Cyrl", "fullText":"Привіт"}
```

---

## GetCapabilities

Returns service capabilities and the list of available translation models.

```protobuf
message GetCapabilitiesResponse {
  repeated string available_models = 3;  // translation model keys from AllowedModels
  bool stt_available               = 4;  // true when Translation:AudioToText is configured
}
```

```bash
grpcurl -plaintext \
  -proto src/Lopatnov.Translate.Grpc/Protos/translate.proto \
  -d '{}' \
  localhost:5100 lopatnov.translate.v1.TranslateService/GetCapabilities
# → {"availableModels":["m2m100_418M"], "sttAvailable":true}
```

---

## Error codes

| Scenario | gRPC Status |
|---|---|
| Unknown `model` key | `INVALID_ARGUMENT` |
| `model` not in `AllowedModels` | `PERMISSION_DENIED` |
| `TranscribeAudio` called when `AudioToText` is not configured | `FAILED_PRECONDITION` |
| Invalid JSON in `TranslateLocalization` | `INVALID_ARGUMENT` |
| Model file missing at configured path | `INTERNAL` |
