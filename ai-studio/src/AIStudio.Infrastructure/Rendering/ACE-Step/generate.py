"""Repository-owned ACE-Step text-to-music launcher.

Reads one constrained JSON request, generates a single instrumental WAV with the
local ACE-Step turbo model, and writes a small JSON result. The project root and
model name come from trusted CLI arguments supplied by AI Studio configuration;
the request carries only content data (caption, duration, bpm, seed, output path).
No request-supplied Python, paths, CLI flags, or shell fragment is executed.

Usage:
    python generate.py --project-root <ROOT> --model <MODEL> \
        --request <request.json> --result <result.json>
"""

from __future__ import annotations

import argparse
import json
import os
import shutil
from pathlib import Path


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="ACE-Step text-to-music launcher.")
    parser.add_argument("--project-root", required=True, help="Local ACE-Step root.")
    parser.add_argument("--model", required=True, help="ACE-Step checkpoint name.")
    parser.add_argument("--request", required=True, help="Constrained JSON request.")
    parser.add_argument("--result", required=True, help="JSON result destination.")
    return parser.parse_args()


def write_result(path: str, payload: dict) -> None:
    Path(path).write_text(json.dumps(payload), encoding="utf-8")


def generate(project_root: str, model: str, request: dict) -> dict:
    import soundfile as sf
    from acestep.handler import AceStepHandler
    from acestep.inference import GenerationConfig, GenerationParams, generate_music
    from acestep.llm_inference import LLMHandler

    caption = str(request["caption"])
    duration = float(request["duration"])
    bpm = int(request["bpm"])
    instrumental = bool(request.get("instrumental", True))
    inference_steps = int(request.get("inferenceSteps", 8))
    seed_value = request.get("seed")
    seed = int(seed_value) if seed_value is not None else None
    output_path = str(request["outputPath"])
    work_dir = str(request["workDir"])

    os.makedirs(work_dir, exist_ok=True)

    # The LM is only needed when thinking/caption generation is requested; the
    # proven baseline supplies caption/metadata directly, so it stays unloaded.
    handler = AceStepHandler()
    llm_handler = LLMHandler()

    status, ok = handler.initialize_service(
        project_root=project_root,
        config_path=model,
        device="cuda",
        offload_to_cpu=False,
    )
    if not ok:
        raise RuntimeError(f"ACE-Step initialization failed: {status}")

    params = GenerationParams(
        task_type="text2music",
        caption=caption,
        lyrics="[Instrumental]" if instrumental else "",
        instrumental=instrumental,
        bpm=bpm,
        timesignature="4/4",
        vocal_language="unknown",
        duration=duration,
        inference_steps=inference_steps,
        seed=seed,
        thinking=False,
        enable_normalization=True,
    )

    config = GenerationConfig(
        batch_size=1,
        seeds=[seed] if seed is not None else None,
        use_random_seed=seed is None,
        audio_format="wav",
    )

    result = generate_music(handler, llm_handler, params, config, save_dir=work_dir)
    if not result.success:
        raise RuntimeError("ACE-Step generation failed")

    audio = None
    for entry in result.audios or []:
        candidate = entry.get("path") if isinstance(entry, dict) else None
        if candidate and os.path.exists(candidate):
            audio = candidate
            break

    if audio is None:
        raise RuntimeError("ACE-Step produced no audio file")

    shutil.move(audio, output_path)

    info = sf.info(output_path)
    return {
        "success": True,
        "outputPath": output_path,
        "sampleRate": int(info.samplerate),
        "channels": int(info.channels),
        "durationSeconds": float(info.frames) / float(info.samplerate)
        if info.samplerate
        else 0.0,
    }


def main() -> int:
    args = parse_args()
    try:
        request = json.loads(Path(args.request).read_text(encoding="utf-8"))
        result = generate(args.project_root, args.model, request)
        write_result(args.result, result)
        return 0
    except Exception as error:  # noqa: BLE001 - structured failure only
        write_result(args.result, {"success": False, "error": str(error)})
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
