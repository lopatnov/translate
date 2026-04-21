# Notices for Lopatnov.Translate

Copyright 2026 Oleksandr Lopatnov

Licensed under the Apache License, Version 2.0.
See [LICENSE](LICENSE) for the full license text.

---

## Third-Party Components

This product incorporates third-party software and machine learning models.
Their licenses and attribution notices are reproduced below.

---

### NLLB-200 Distilled 1.3B — ONNX Conversion

**Converted by:** Forkjoin.ai — https://forkjoin.ai  
**Source:** https://huggingface.co/forkjoin/nllb-200-distilled-1.3B-onnx  
**License:** Apache License, Version 2.0

We gratefully acknowledge Forkjoin.ai for making the NLLB-200 model available
in ONNX format, optimised for edge and on-device inference. Their work
significantly reduced the barrier to self-hosted, privacy-respecting translation
and made this project possible.

> Forkjoin.ai runs AI models at the edge — in-browser, on-device, zero cloud
> cost. These converted models power real-time inference, speech recognition,
> and natural language capabilities. All conversions are optimized for edge
> deployment within browser and mobile memory constraints.

---

### NLLB-200 Distilled 1.3B — Original Model Weights

**Author:** Meta AI (Facebook Research)  
**Source:** https://huggingface.co/facebook/nllb-200-distilled-1.3B  
**Research paper:** [No Language Left Behind: Scaling Human-Centered Machine
Translation (2022)](https://arxiv.org/abs/2207.04672)  
**License:** Creative Commons Attribution-NonCommercial 4.0 International
(CC BY-NC 4.0) — https://creativecommons.org/licenses/by-nc/4.0/

The NLLB-200 model is a research artifact from Meta AI's *No Language Left
Behind* initiative, which aimed to deliver high-quality machine translation
to all 200 languages of the world, with a focus on low-resource languages that
are underserved by existing translation systems.

The model weights are **not included in this repository**. They must be
obtained separately in accordance with the CC BY-NC 4.0 license terms.
**Non-commercial use only.**

Citation:
```
@article{nllb2022,
  title     = {No Language Left Behind: Scaling Human-Centered Machine Translation},
  author    = {NLLB Team et al.},
  journal   = {arXiv preprint arXiv:2207.04672},
  year      = {2022},
  url       = {https://arxiv.org/abs/2207.04672}
}
```

---

### Key Runtime Dependencies

| Component | Author | License |
|-----------|--------|---------|
| [Grpc.AspNetCore](https://github.com/grpc/grpc-dotnet) | Google / .NET Foundation | Apache 2.0 |
| [Microsoft.ML.OnnxRuntime](https://github.com/microsoft/onnxruntime) | Microsoft | MIT |
| [Microsoft.ML.Tokenizers](https://github.com/dotnet/machinelearning) | Microsoft | MIT |
| [NAudio](https://github.com/naudio/NAudio) | Mark Heath | MIT |
| [Moq](https://github.com/devlooped/moq) | Devlooped | BSD 3-Clause |

Full dependency list is available in the project files under `src/`.

---

*This NOTICE file is provided in accordance with Section 4(d) of the
Apache License, Version 2.0.*
