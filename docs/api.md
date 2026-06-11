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
- [SynthesizeSpeech](#synthesizespeech)
- [TranslateAudio](#translateaudio)
- [GetCapabilities](#getcapabilities)
- [Error codes](#error-codes)

---

## Language code formats

All RPCs that accept or return language codes support a `language_format` field on the request.

| Value      | Description                                                            | Example             |
| ---------- | ---------------------------------------------------------------------- | ------------------- |
| `"bcp47"`  | BCP-47 tags — the system interchange format. Default when empty.       | `"uk"`, `"zh-Hans"` |
| `"native"` | Model-specific codes, passed through unchanged. Detection results return the detector's raw label (e.g. `"ukr_Cyrl"` from GlotLID, `"en"` from LID-176). | any string |

Any other value (including the formerly supported `"flores200"`) is rejected with
`INVALID_ARGUMENT`.

**How codes flow.** BCP-47 is the only intermediate format: detector output is normalised
to BCP-47 before reaching a translation model, and each model adapter converts BCP-47 to
its own native codes internally (NLLB → FLORES-200, M2M-100 / LibreTranslate → ISO 639-1).
Codes an adapter does not recognise as BCP-47 pass through unchanged, so with `"native"`
you can address any model in its own vocabulary directly. Region-qualified tags collapse
to the primary subtag automatically (`"en-US"` → `"en"`).

---

## RPCs

| RPC                     | Description                                                      |
| ----------------------- | ---------------------------------------------------------------- |
| `TranslateText`         | Translate a text string                                          |
| `TranslateLocalization` | Translate all strings in a JSON i18n file                        |
| `DetectLanguage`        | Detect the language of a text string                             |
| `TranscribeAudio`       | Transcribe speech from a WAV file to text (STT)                  |
| `SynthesizeSpeech`      | Synthesize text to a WAV audio file (TTS)                        |
| `TranslateAudio`        | End-to-end speech-to-speech: STT → translate → TTS in one call  |
| `GetCapabilities`       | List available models, voices, and service capabilities          |

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
  string language_format = 6;  // "bcp47" (default) | "native"
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

With model-native codes (FLORES-200 for NLLB):

```bash
grpcurl -plaintext \
  -proto src/Lopatnov.Translate.Grpc/Protos/translate.proto \
  -d '{"text": "Привіт, як справи?", "source_language": "ukr_Cyrl", "target_language": "eng_Latn", "language_format": "native"}' \
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
  string language_format = 2;  // "bcp47" (default) | "native"
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

# native — the detector's raw label (e.g. GlotLID emits ISO 639-3 + script)
grpcurl -plaintext \
  -proto src/Lopatnov.Translate.Grpc/Protos/translate.proto \
  -d '{"text": "Привіт, як справи?", "language_format": "native"}' \
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
  string language_format = 4;  // format for detected_language in response: "bcp47" (default) | "native"
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

With an explicit language:

```bash
grpcurl -plaintext \
  -proto src/Lopatnov.Translate.Grpc/Protos/translate.proto \
  -d "{\"audio_data\": \"$(base64 -w0 recording.wav)\", \"language\": \"uk\"}" \
  localhost:5100 lopatnov.translate.v1.TranslateService/TranscribeAudio
# → {"segments":[{"text":"Привіт","startTime":0.0,"endTime":1.2}], "detectedLanguage":"uk", "fullText":"Привіт"}
```

---

## SynthesizeSpeech

Synthesizes text to a WAV audio file using Piper TTS.

Requires `Translation:TextToAudio` to be configured in `appsettings.json` for the requested language (see [docs/models.md](models.md#piper-tts)).
The voice model is loaded lazily on first request and unloaded after `ModelTtlMinutes` of inactivity.

espeak-ng must be installed on the server (see [docs/models.md](models.md#piper-tts)).

### Request

```protobuf
message SynthesizeSpeechRequest {
  string text            = 1;
  string language        = 2;  // BCP-47 language code (e.g. "en", "uk"); matched against TextToAudio map
  string voice           = 3;  // optional: speaker name for multi-speaker models (e.g. "mykyta", "lada", "tetiana")
  float  speed           = 4;  // speech rate multiplier; 1.0 = normal, 0.5 = half speed, 2.0 = double speed
  string language_format = 5;  // format for the language field: "bcp47" (default) | "native"
}
```

### Response

```protobuf
message SynthesizeSpeechResponse {
  bytes audio_data  = 1;  // WAV file bytes (16-bit PCM, RIFF header)
  int32 sample_rate = 2;  // sample rate of the output audio, typically 22050 Hz
}
```

### Examples

**bash / Linux:**

```bash
# Synthesize and save to file
grpcurl -plaintext \
  -proto src/Lopatnov.Translate.Grpc/Protos/translate.proto \
  -d '{"text": "Hello, world!", "language": "en"}' \
  localhost:5100 lopatnov.translate.v1.TranslateService/SynthesizeSpeech \
  | jq -r '.audioData' | base64 -d > output.wav
```

**PowerShell (Windows):**

```powershell
$body = '{"text":"Привіт, як справи?","language":"uk","voice":"mykyta","speed":1.0}'
$resp = grpcurl -plaintext `
  -proto src/Lopatnov.Translate.Grpc/Protos/translate.proto `
  -d $body `
  localhost:5100 lopatnov.translate.v1.TranslateService/SynthesizeSpeech `
  | ConvertFrom-Json
[IO.File]::WriteAllBytes("output.wav", [Convert]::FromBase64String($resp.audioData))
```

With custom speed (half speed):

```bash
grpcurl -plaintext \
  -proto src/Lopatnov.Translate.Grpc/Protos/translate.proto \
  -d '{"text": "Хорошо, что ты пришёл!", "language": "ru", "speed": 0.75}' \
  localhost:5100 lopatnov.translate.v1.TranslateService/SynthesizeSpeech \
  | jq -r '.audioData' | base64 -d > output.wav
```

---

## TranslateAudio

End-to-end speech-to-speech translation: transcribes the input audio (Whisper STT), translates the text (default translation model), and synthesizes the result (Piper TTS). Returns all intermediate results along with the translated audio.

Requires both `Translation:AudioToText` (Whisper) and `Translation:TextToAudio` (Piper) to be configured in `appsettings.json`.

### Request

```protobuf
message TranslateAudioRequest {
  bytes  audio_data       = 1;  // WAV file bytes, max 50 MB (resampled automatically to 16 kHz mono for STT)
  string source_language  = 2;  // BCP-47 source language hint; "" or "auto" = Whisper auto-detection
  string target_language  = 3;  // BCP-47 target language; selects the TTS voice via TextToAudio map
  string audio_format     = 4;  // reserved; pass "" or "wav"
  string target_voice     = 5;  // optional: speaker name for multi-speaker target voice
  string language_format  = 6;  // format for source_language/target_language: "bcp47" (default) | "native"
}
```

### Response

```protobuf
message TranslateAudioResponse {
  bytes  translated_audio = 1;  // synthesized WAV (16-bit PCM, RIFF header)
  string transcription    = 2;  // full text transcribed from the input audio
  string translated_text  = 3;  // translated text before TTS synthesis
  int32  sample_rate      = 4;  // sample rate of the output audio, typically 22050 Hz
}
```

### Examples

**bash / Linux:**

```bash
# Translate Ukrainian speech to English
grpcurl -plaintext \
  -proto src/Lopatnov.Translate.Grpc/Protos/translate.proto \
  -d "{\"audio_data\": \"$(base64 -w0 speech-uk.wav)\", \"source_language\": \"uk\", \"target_language\": \"en\"}" \
  localhost:5100 lopatnov.translate.v1.TranslateService/TranslateAudio \
  | tee /tmp/resp.json \
  | jq -r '.translatedAudio' | base64 -d > translated.wav

# Show transcription and translation
cat /tmp/resp.json | jq '{transcription, translatedText}'
```

**PowerShell (Windows):**

```powershell
$audioBase64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes("speech.wav"))
$body = "{`"audio_data`":`"$audioBase64`",`"source_language`":`"uk`",`"target_language`":`"en`"}"
$resp = grpcurl -plaintext `
  -proto src/Lopatnov.Translate.Grpc/Protos/translate.proto `
  -d $body `
  localhost:5100 lopatnov.translate.v1.TranslateService/TranslateAudio `
  | ConvertFrom-Json
Write-Host "Transcription: $($resp.transcription)"
Write-Host "Translation:   $($resp.translatedText)"
[IO.File]::WriteAllBytes("translated.wav", [Convert]::FromBase64String($resp.translatedAudio))
```

With auto language detection and a specific target speaker:

```bash
grpcurl -plaintext \
  -proto src/Lopatnov.Translate.Grpc/Protos/translate.proto \
  -d "{\"audio_data\": \"$(base64 -w0 speech.wav)\", \"source_language\": \"auto\", \"target_language\": \"uk\", \"target_voice\": \"mykyta\"}" \
  localhost:5100 lopatnov.translate.v1.TranslateService/TranslateAudio \
  | jq -r '.translatedAudio' | base64 -d > translated.wav
```

---

## GetCapabilities

Returns service capabilities and the list of available translation models and TTS voices.

```protobuf
message GetCapabilitiesResponse {
  repeated string available_voices = 2;  // TTS language keys from Translation:TextToAudio (e.g. "en", "ru", "uk")
  repeated string available_models = 3;  // translation model keys from AllowedModels
  bool stt_available               = 4;  // true when Translation:AudioToText is configured
  bool tts_available               = 5;  // true when Translation:TextToAudio has at least one entry
}
```

```bash
grpcurl -plaintext \
  -proto src/Lopatnov.Translate.Grpc/Protos/translate.proto \
  -d '{}' \
  localhost:5100 lopatnov.translate.v1.TranslateService/GetCapabilities
# → {"availableVoices":["en","ru","uk"], "availableModels":["m2m100_418M"], "sttAvailable":true, "ttsAvailable":true}
```

---

## Error codes

| Scenario                                                              | gRPC Status           |
| --------------------------------------------------------------------- | --------------------- |
| Unknown `model` key                                                   | `INVALID_ARGUMENT`    |
| `model` not in `AllowedModels`                                        | `PERMISSION_DENIED`   |
| `TranscribeAudio` called when `AudioToText` is not configured         | `FAILED_PRECONDITION` |
| `SynthesizeSpeech` called when no voice is configured for `language`  | `FAILED_PRECONDITION` |
| `TranslateAudio` called when STT or TTS is not configured             | `FAILED_PRECONDITION` |
| Redirect cycle detected (`x-redirect-id` loop)                        | `FAILED_PRECONDITION` |
| Invalid JSON in `TranslateLocalization`                               | `INVALID_ARGUMENT`    |
| Language code not supported by the translation model                  | `INVALID_ARGUMENT`    |
| Model file missing at configured path                                 | `INTERNAL`            |
