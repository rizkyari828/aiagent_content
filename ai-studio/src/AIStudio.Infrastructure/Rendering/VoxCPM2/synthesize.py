"""Repository-owned VoxCPM2 text-to-speech launcher.

Reads one constrained JSON request, synthesizes a single Indonesian WAV with the
local VoxCPM2 model, and writes a small JSON result. The model path comes from a
trusted CLI argument supplied by AI Studio configuration; the request carries only
content data (text, approved voice description, inference settings, output path).
No request-supplied Python, model path, executable, or shell fragment is executed.

Usage:
    python synthesize.py --model <MODEL_DIR> --request <request.json> --result <result.json>
"""

from __future__ import annotations

import argparse
import inspect
import json
from pathlib import Path


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="VoxCPM2 text-to-speech launcher.")
    parser.add_argument("--model", required=True, help="Local VoxCPM2 model directory.")
    parser.add_argument("--request", required=True, help="Constrained JSON request.")
    parser.add_argument("--result", required=True, help="JSON result destination.")
    return parser.parse_args()


def write_result(path: str, payload: dict) -> None:
    Path(path).write_text(json.dumps(payload), encoding="utf-8")


def synthesize(model_path: str, request: dict) -> dict:
    import soundfile as sf
    from voxcpm import VoxCPM

    text = str(request["text"])
    voice = str(request["voice"])
    output_path = str(request["outputPath"])
    timesteps = int(request.get("inferenceTimesteps", 20))
    cfg_value = float(request.get("cfgValue", 2.0))
    normalize = bool(request.get("normalize", True))

    model = VoxCPM.from_pretrained(model_path, load_denoiser=False)
    final_text = f"({voice}){text}"

    kwargs = {
        "text": final_text,
        "cfg_value": cfg_value,
        "inference_timesteps": timesteps,
    }

    # The installed package may or may not expose ``normalize``; only pass it when
    # the local API actually supports it (no package upgrade is required).
    supported = inspect.signature(model._generate).parameters
    if "normalize" in supported:
        kwargs["normalize"] = normalize

    wav = model.generate(**kwargs)
    sample_rate = int(model.tts_model.sample_rate)
    sf.write(output_path, wav, sample_rate)

    duration = len(wav) / sample_rate if sample_rate else 0.0
    return {
        "success": True,
        "outputPath": output_path,
        "sampleRate": sample_rate,
        "durationSeconds": duration,
    }


def main() -> int:
    args = parse_args()
    try:
        request = json.loads(Path(args.request).read_text(encoding="utf-8"))
        result = synthesize(args.model, request)
        write_result(args.result, result)
        return 0
    except Exception as error:  # noqa: BLE001 - structured failure only
        write_result(args.result, {"success": False, "error": str(error)})
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
