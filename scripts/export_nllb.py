"""
Export NLLB-200 to ONNX (float32) compatible with Lopatnov.Translate.
Uses TorchScript ONNX path (not dynamo) for reliability.

Usage:
    python scripts/export_nllb.py
    python scripts/export_nllb.py --model facebook/nllb-200-distilled-1.3B --output ./models/nllb-1.3b
"""
import argparse
from pathlib import Path

import torch
from transformers import AutoModelForSeq2SeqLM


class _Encoder(torch.nn.Module):
    def __init__(self, encoder):
        super().__init__()
        self.encoder = encoder

    def forward(self, input_ids, attention_mask):
        return self.encoder(input_ids=input_ids, attention_mask=attention_mask).last_hidden_state


class _Decoder(torch.nn.Module):
    def __init__(self, decoder, lm_head):
        super().__init__()
        self.decoder = decoder
        self.lm_head = lm_head

    def forward(self, input_ids, encoder_hidden_states, encoder_attention_mask):
        out = self.decoder(
            input_ids=input_ids,
            encoder_hidden_states=encoder_hidden_states,
            encoder_attention_mask=encoder_attention_mask,
        )
        return self.lm_head(out.last_hidden_state)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--model", default="facebook/nllb-200-distilled-600M")
    parser.add_argument("--output", default="./models/nllb")
    parser.add_argument("--opset", type=int, default=17)
    args = parser.parse_args()

    out = Path(args.output)
    out.mkdir(parents=True, exist_ok=True)

    print(f"Loading {args.model} ...")
    model = AutoModelForSeq2SeqLM.from_pretrained(args.model)
    model.eval()
    hidden = model.config.d_model

    with torch.no_grad():
        print("Exporting encoder ...")
        torch.onnx.export(
            _Encoder(model.model.encoder),
            args=(torch.zeros(1, 10, dtype=torch.long),
                  torch.ones(1, 10, dtype=torch.long)),
            f=str(out / "encoder_model.onnx"),
            input_names=["input_ids", "attention_mask"],
            output_names=["last_hidden_state"],
            dynamic_axes={
                "input_ids":         {0: "batch_size", 1: "encoder_sequence_length"},
                "attention_mask":    {0: "batch_size", 1: "encoder_sequence_length"},
                "last_hidden_state": {0: "batch_size", 1: "encoder_sequence_length"},
            },
            opset_version=args.opset,
            do_constant_folding=True,
        )
        print("  encoder_model.onnx ✓")

        print("Exporting decoder ...")
        torch.onnx.export(
            _Decoder(model.model.decoder, model.lm_head),
            args=(torch.zeros(1, 2, dtype=torch.long),
                  torch.zeros(1, 10, hidden),
                  torch.ones(1, 10, dtype=torch.long)),
            f=str(out / "decoder_model.onnx"),
            input_names=["input_ids", "encoder_hidden_states", "encoder_attention_mask"],
            output_names=["logits"],
            dynamic_axes={
                "input_ids":              {0: "batch_size", 1: "decoder_sequence_length"},
                "encoder_hidden_states":  {0: "batch_size", 1: "encoder_sequence_length"},
                "encoder_attention_mask": {0: "batch_size", 1: "encoder_sequence_length"},
                "logits":                 {0: "batch_size", 1: "decoder_sequence_length"},
            },
            opset_version=args.opset,
            do_constant_folding=True,
        )
        print("  decoder_model.onnx ✓")

    print(f"\nDone! Files saved to: {out.resolve()}")
    print('\nSet in appsettings.json:')
    print('  "EncoderFile": "encoder_model.onnx"')
    print('  "DecoderFile": "decoder_model.onnx"')


if __name__ == "__main__":
    main()
