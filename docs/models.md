# Models

## Contents

- [Configuration](#configuration)
  - [Models section](#models-section)
  - [Translation section](#translation-section)
- Language Detection
  - [LID-176](#lid-176)
  - [GlotLID v3](#glotlid-v3)
- Translation
  - [NLLB-200 600M distilled](#nllb-200-600m-distilled)
  - [NLLB-200 1.3B](#nllb-200-13b)
  - [NLLB-200 3.3B](#nllb-200-33b)
  - [M2M-100 418M](#m2m-100-418m)
  - [M2M-100 1.2B](#m2m-100-12b)
  - [LibreTranslate](#libretranslate)

---

## Configuration

### Models section

Models are configured by name in `appsettings.json` under `Models`. Each entry has a `Type` discriminator and type-specific properties. The name you choose becomes the value of the `model` field in API requests.

```jsonc
"Models": {
  "<name>": {
    "Type": "<type>",  // required: NLLB | M2M100 | FastText | LibreTranslate
    // ... type-specific properties (see each model below)
  }
}
```

Multiple entries of the same type are allowed — just use different names. If `Type` is missing or unknown, the service will fail to start with a configuration error.

**Properties for NLLB and M2M100:**

| Property | Default | Description |
| --- | --- | --- |
| `Path` | — | Path to the directory with model files |
| `EncoderFile` | `encoder_model.onnx` | Encoder ONNX filename |
| `DecoderFile` | `decoder_model.onnx` | Decoder ONNX filename |
| `TokenizerFile` | `sentencepiece.bpe.model` | SentencePiece tokenizer filename |
| `TokenizerConfigFile` | `""` | Secondary tokenizer config (`tokenizer.json` for NLLB, `added_tokens.json` for M2M100) |
| `MaxTokens` | `512` | Maximum tokens per translation |
| `BeamSize` | `1` | Beam search width (NLLB only; higher = better quality, slower) |
| `VocabFile` | `""` | BPE vocabulary file (M2M100 only: `vocab.json`) |

**Properties for FastText:**

| Property | Default | Description |
| --- | --- | --- |
| `Path` | — | Path to the model file (`.bin` or `.ftz`) |
| `LabelFormat` | `"flores200"` | Format of the model's output labels. Use `"iso639-1"` for LID-176 (outputs `__label__en`), `"flores200"` for GlotLID (outputs `__label__eng_Latn`). Also supports `"iso639-2"`, `"iso639-3"`. |
| `LabelPrefix` | `"__label__"` | Prefix to strip from each label before format conversion. |
| `LabelSuffix` | `""` | Optional suffix to strip from each label. |

**Properties for LibreTranslate:**

| Property | Default | Description |
| --- | --- | --- |
| `BaseUrl` | — | URL of the LibreTranslate instance, e.g. `http://libretranslate:5000` |
| `ApiKey` | `""` | API key, if the instance requires one |

#### Type compatibility

`Type` identifies the tokenizer format, not the exact model. This means fine-tuned or alternative models are supported as long as the tokenizer is compatible:

| Type | Compatible with |
| --- | --- |
| `NLLB` | Any ONNX encoder-decoder model using the NLLB-200 SentencePiece tokenizer with FLORES-200 language tokens |
| `M2M100` | Any ONNX encoder-decoder model using the M2M-100 tokenizer (`vocab.json` + `added_tokens.json`, ISO 639-1 `__lang__` tokens) |
| `FastText` | Any fastText supervised classification model in `.bin` or `.ftz` format |
| `LibreTranslate` | Any LibreTranslate-compatible HTTP API endpoint |

Models **not** compatible without a new type: MarianMT (OPUS-MT), mBART-50, SeamlessM4T — they use different tokenizer formats.

---

### Translation section

Controls routing and lifecycle of loaded models.

```jsonc
"Translation": {
  "DefaultModel": "nllb",      // model used when the request's model field is empty
  "AutoDetect": "langdetect",  // name of a FastText model for language auto-detection
  "AllowedModels": [],         // allowlist of model names; empty = all configured models are allowed
  "ModelTtlMinutes": 30        // minutes of inactivity before a model is unloaded from memory
}
```

| Property | Default | Description |
| --- | --- | --- |
| `DefaultModel` | `""` | Name of the model to use when `model` is not specified in the request. If empty and the request omits `model`, the request fails. |
| `AutoDetect` | `""` | Name of a `FastText` model used for language auto-detection. Required to use `source_language: "auto"` in `TranslateText` or the `DetectLanguage` RPC. If empty or the model file is missing, falls back to heuristic detection. |
| `AllowedModels` | `[]` | Restricts which models clients may request by name. Empty list means all configured translation models are accessible. Useful when you configure multiple models but want to expose only some via the API. |
| `ModelTtlMinutes` | `30` | A loaded model is kept in memory for this many minutes after its last use, then unloaded to free resources. Set to a large value to keep models always loaded. |

---

## Language Detection

Language detection models are used for automatic source language detection (`source_language: "auto"` in `TranslateText`, or the `DetectLanguage` RPC). They are not translation models and cannot be used as `model` in translation requests. Configure the active detector via `Translation:AutoDetect`.

---

### LID-176

**176 languages · fastText binary format (`.ftz`)**

Facebook's compact language identification model. Fast and lightweight (~1 MB). Best choice when you only need common languages and care about startup time.

**License: [CC-BY-SA-3.0](https://creativecommons.org/licenses/by-sa/3.0/)**
Commercial use is allowed. You must credit Facebook Research. If you distribute a modified version of the model itself, it must remain under the same license — this does not apply to using it as a backend service.

**Download**

```bash
huggingface-cli download lopatnov/fasttext-language-id \
  --local-dir ./models/langdetect/lid176
```

HuggingFace repo: [lopatnov/fasttext-language-id](https://huggingface.co/lopatnov/fasttext-language-id)

**appsettings.json**

```jsonc
"Models": {
  "langdetect": {
    "Type": "FastText",
    "Path": "./models/langdetect/lid176/lid.176.ftz",
    "LabelFormat": "iso639-1"   // LID-176 outputs ISO 639-1 codes (en, de, fr, …)
  }
},
"Translation": {
  "AutoDetect": "langdetect"
}
```

---

### GlotLID v3

**1633 language varieties · fastText binary format (`.bin`)**

Covers a much wider range of languages than LID-176, including low-resource and minority languages. Larger model (~1.6 GB).

**License: [Apache 2.0](https://www.apache.org/licenses/LICENSE-2.0)**
Unrestricted commercial use. No attribution required.

**Download**

```bash
huggingface-cli download lopatnov/glotlid \
  --local-dir ./models/langdetect/glotlid
```

HuggingFace repo: [lopatnov/glotlid](https://huggingface.co/lopatnov/glotlid)

**appsettings.json**

```jsonc
"Models": {
  "langdetect": {
    "Type": "FastText",
    "Path": "./models/langdetect/glotlid/model_v3.bin",
    "LabelFormat": "flores200"  // GlotLID outputs FLORES-200 codes (eng_Latn, ukr_Cyrl, …) — this is the default
  }
},
"Translation": {
  "AutoDetect": "langdetect"
}
```

---

## Translation

---

### NLLB-200 (600M distilled)

**200 languages · ONNX**

Meta's No Language Left Behind model, distilled to 600M parameters. Good balance of quality and speed. Recommended starting point.

**License: [CC-BY-NC-4.0](https://creativecommons.org/licenses/by-nc/4.0/)**
⚠️ **Non-commercial use only.** Cannot be used in commercial products or services.

**Download**

```bash
huggingface-cli download lopatnov/nllb-200-distilled-600M-onnx \
  --local-dir ./models/nllb-600m
```

HuggingFace repo: [lopatnov/nllb-200-distilled-600M-onnx](https://huggingface.co/lopatnov/nllb-200-distilled-600M-onnx)

**appsettings.json**

```jsonc
"Models": {
  "nllb": {
    "Type": "NLLB",
    "Path": "./models/nllb-600m",
    "EncoderFile": "encoder_model.onnx",
    "DecoderFile": "decoder_model.onnx",
    "TokenizerFile": "sentencepiece.bpe.model",
    "TokenizerConfigFile": "tokenizer.json",
    "MaxTokens": 512,
    "BeamSize": 1
  }
},
"Translation": {
  "DefaultModel": "nllb"
}
```

---

### NLLB-200 1.3B

**200 languages · ONNX**

Higher quality than the 600M distilled variant at the cost of more memory (~5 GB).

**License: [CC-BY-NC-4.0](https://creativecommons.org/licenses/by-nc/4.0/)**
⚠️ **Non-commercial use only.**

**Download**

```bash
huggingface-cli download lopatnov/nllb-200-1.3B-onnx \
  --local-dir ./models/nllb-1.3b
```

HuggingFace repo: [lopatnov/nllb-200-1.3B-onnx](https://huggingface.co/lopatnov/nllb-200-1.3B-onnx)

**appsettings.json**

Same as 600M distilled — change `Path` to `./models/nllb-1.3b`.

---

### NLLB-200 3.3B

**200 languages · ONNX**

Highest quality NLLB variant. Requires significant RAM/VRAM (~12 GB).

**License: [CC-BY-NC-4.0](https://creativecommons.org/licenses/by-nc/4.0/)**
⚠️ **Non-commercial use only.**

**Download**

```bash
huggingface-cli download lopatnov/nllb-200-3.3B-onnx \
  --local-dir ./models/nllb-3.3b
```

HuggingFace repo: [lopatnov/nllb-200-3.3B-onnx](https://huggingface.co/lopatnov/nllb-200-3.3B-onnx)

**appsettings.json**

Same as 600M distilled — change `Path` to `./models/nllb-3.3b`.

---

### M2M-100 (418M)

**100 languages · ONNX**

Facebook's many-to-many translation model. MIT-licensed — suitable for commercial use. 418M parameter variant, lower memory footprint.

**License: [MIT](https://opensource.org/licenses/MIT)**
✅ Unrestricted commercial use.

**Download**

```bash
huggingface-cli download lopatnov/m2m100_418M-onnx \
  --local-dir ./models/m2m100-418m
```

HuggingFace repo: [lopatnov/m2m100_418M-onnx](https://huggingface.co/lopatnov/m2m100_418M-onnx)

**appsettings.json**

```jsonc
"Models": {
  "m2m100": {
    "Type": "M2M100",
    "Path": "./models/m2m100-418m",
    "EncoderFile": "encoder_model.onnx",
    "DecoderFile": "decoder_model.onnx",
    "TokenizerFile": "sentencepiece.bpe.model",
    "TokenizerConfigFile": "added_tokens.json",
    "VocabFile": "vocab.json",
    "MaxTokens": 512
  }
},
"Translation": {
  "DefaultModel": "m2m100"
}
```

---

### M2M-100 (1.2B)

**100 languages · ONNX**

Higher quality than the 418M variant. Good choice for commercial deployments where translation quality matters.

**License: [MIT](https://opensource.org/licenses/MIT)**
✅ Unrestricted commercial use.

**Download**

```bash
huggingface-cli download lopatnov/m2m100_1.2B-onnx \
  --local-dir ./models/m2m100-1.2b
```

HuggingFace repo: [lopatnov/m2m100_1.2B-onnx](https://huggingface.co/lopatnov/m2m100_1.2B-onnx)

**appsettings.json**

Same as 418M — change `Path` to `./models/m2m100-1.2b`.

---

### LibreTranslate

**Argos Translate backend · HTTP API**

An open-source machine translation server. Runs as a separate Docker container alongside the service. Useful as an additional translation option or fallback.

**License: [AGPL-3.0](https://www.gnu.org/licenses/agpl-3.0.html)** (LibreTranslate server) · Argos Translate language packages are MIT/CC-BY licensed.
⚠️ AGPL requires that the source code of any network-accessible service using LibreTranslate is made publicly available. Evaluate whether this is acceptable for your use case before using in production.

**Setup**

1. In `docker/docker-compose.yml`, uncomment the `libretranslate:` service block, the `depends_on:` section in the `translate:` service, and the `libretranslate-models:` volume at the bottom of the file.

2. Add to `appsettings.json`:

```jsonc
"Models": {
  "libretranslate": {
    "Type": "LibreTranslate",
    "BaseUrl": "http://libretranslate:5000",
    "ApiKey": ""  // set if your LibreTranslate instance requires a key
  }
},
"Translation": {
  "DefaultModel": "libretranslate"
}
```

The `BaseUrl` `http://libretranslate:5000` works when running via Docker Compose. For an external instance, replace with its URL.
