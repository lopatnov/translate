"""
Validate exported NLLB ONNX models against the PyTorch reference.
NLLB encoder format: X [EOS] [src_lang_code]  (lang token is LAST)
Decoder init:        [EOS] [tgt_lang_code]

Run from project root: python scripts/test_onnx.py
"""
import numpy as np
import onnxruntime as ort
import torch
from transformers import AutoModelForSeq2SeqLM, NllbTokenizer

MODEL_DIR = "./models/nllb"
HF_MODEL  = "facebook/nllb-200-distilled-600M"
EOS       = 2
ENG_LATN  = 256047
TEXT      = "Привіт, як справи?"
SRC_LANG  = "ukr_Cyrl"

# ── Step 1: get correct token IDs from the authoritative Python tokenizer ──────
print("Loading HuggingFace tokenizer...")
tok = NllbTokenizer.from_pretrained(HF_MODEL)
tok.src_lang = SRC_LANG

py_ids   = tok(TEXT)["input_ids"]
print(f"Python NllbTokenizer IDs for '{TEXT}': {py_ids}")
print(f"  Format: [src_lang={py_ids[0]}] X [EOS={py_ids[-1]}]")

input_ids      = np.array([py_ids], dtype=np.int64)
attention_mask = np.ones_like(input_ids, dtype=np.int64)

# ── Step 2: ONNX encoder ───────────────────────────────────────────────────────
print("\nLoading ONNX sessions...")
enc = ort.InferenceSession(f"{MODEL_DIR}/encoder_model.onnx")
dec = ort.InferenceSession(f"{MODEL_DIR}/decoder_model.onnx")
print("Sessions loaded.")

print("\nRunning encoder...")
enc_out = enc.run(None, {"input_ids": input_ids, "attention_mask": attention_mask})
hidden  = enc_out[0]
print(f"  shape={hidden.shape}, abs_sum={np.abs(hidden).sum():.1f}, first4={hidden[0,0,:4]}")

# ── Step 3: greedy decode ──────────────────────────────────────────────────────
dec_ids   = np.array([[EOS, ENG_LATN]], dtype=np.int64)
generated = []

print("\nGreedy decoding (30 steps max):")
for step in range(30):
    dec_out = dec.run(None, {
        "input_ids":              dec_ids,
        "encoder_hidden_states":  hidden,
        "encoder_attention_mask": attention_mask,
    })
    logits  = dec_out[0]
    pos     = dec_ids.shape[1] - 1
    top5    = np.argsort(logits[0, pos, :])[-5:][::-1]
    print(f"  step {step}: pos={pos}, top5={top5.tolist()}, "
          f"scores={[round(float(logits[0,pos,t]),2) for t in top5]}")
    next_tok = int(top5[0])
    if next_tok == EOS:
        print("  → EOS, stopping")
        break
    generated.append(next_tok)
    dec_ids = np.concatenate([dec_ids, [[next_tok]]], axis=1)

onnx_text = tok.decode(generated, skip_special_tokens=True)
print(f"\nONNX generated IDs: {generated}")
print(f"ONNX translation:   '{onnx_text}'")

# ── Step 4: cross-attention sanity check ──────────────────────────────────────
print("\n--- Cross-attention sanity check ---")
zeros        = np.zeros_like(hidden)
logits_zero  = dec.run(None, {"input_ids": np.array([[EOS, ENG_LATN]], dtype=np.int64),
                               "encoder_hidden_states": zeros,
                               "encoder_attention_mask": attention_mask})[0]
logits_real  = dec.run(None, {"input_ids": np.array([[EOS, ENG_LATN]], dtype=np.int64),
                               "encoder_hidden_states": hidden,
                               "encoder_attention_mask": attention_mask})[0]
top_zero = int(np.argmax(logits_zero[0, 1, :]))
top_real  = int(np.argmax(logits_real[0, 1, :]))
print(f"  zero encoder → top={top_zero} ({float(logits_zero[0,1,top_zero]):.2f})")
print(f"  real encoder → top={top_real}  ({float(logits_real[0,1,top_real]):.2f})")
print(f"  cross-attn affects output: {top_zero != top_real or abs(float(logits_zero[0,1,top_zero]) - float(logits_real[0,1,top_real])) > 0.5}")

# ── Step 5: PyTorch baseline for comparison ───────────────────────────────────
print("\n--- PyTorch baseline (ground truth) ---")
print("Loading PyTorch model from cache...")
pt_model = AutoModelForSeq2SeqLM.from_pretrained(HF_MODEL)
pt_model.eval()

pt_ids  = torch.tensor([py_ids])
pt_mask = torch.ones_like(pt_ids)

with torch.no_grad():
    result = pt_model.generate(
        input_ids=pt_ids,
        attention_mask=pt_mask,
        forced_bos_token_id=ENG_LATN,
        max_new_tokens=30,
    )

pt_text = tok.batch_decode(result, skip_special_tokens=True)
print(f"PyTorch translation: {pt_text}")
print(f"PyTorch token IDs:   {result[0].tolist()}")

print("\n--- Summary ---")
print(f"  Input:   '{TEXT}' ({SRC_LANG} → eng_Latn)")
print(f"  ONNX:    '{onnx_text}'")
print(f"  PyTorch: '{pt_text[0]}'")
print(f"  Match:   {onnx_text.strip() == pt_text[0].strip()}")
