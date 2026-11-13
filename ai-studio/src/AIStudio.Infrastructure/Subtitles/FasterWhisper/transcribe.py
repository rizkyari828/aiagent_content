#!/usr/bin/env python3
"""
Repository-owned faster-whisper launcher for AI Studio (PRD §35).

Usage:
    python3 transcribe.py --request <request.json> --result <result.json>
"""

import argparse
import json
import os
import sys
from pathlib import Path


def format_srt_timestamp(seconds: float) -> str:
    total_ms = max(0, int(round(seconds * 1000)))
    hours, rem = divmod(total_ms, 3_600_000)
    minutes, rem = divmod(rem, 60_000)
    secs, ms = divmod(rem, 1000)
    return f"{hours:02d}:{minutes:02d}:{secs:02d},{ms:03d}"


def write_result(result_path: Path, payload: dict) -> None:
    result_path.write_text(json.dumps(payload), encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--request", required=True)
    parser.add_argument("--result", required=True)
    args = parser.parse_args()

    request_path = Path(args.request)
    result_path = Path(args.result)

    try:
        req = json.loads(request_path.read_text(encoding="utf-8"))
        audio_path = req["audioPath"]
        output_path = Path(req["outputPath"])
        model_name = req.get("model", "small")
        device = req.get("device", "auto")
        compute_type = req.get("computeType", "default")
        language = req.get("language") or None

        from faster_whisper import WhisperModel  # type: ignore

        model = WhisperModel(model_name, device=device, compute_type=compute_type)
        segments, info = model.transcribe(audio_path, language=language, vad_filter=True)

        blocks = []
        count = 0
        for segment in segments:
            text = (segment.text or "").strip()
            if not text:
                continue
            count += 1
            start_ts = format_srt_timestamp(float(segment.start))
            end_ts = format_srt_timestamp(float(segment.end))
            blocks.append(f"{count}\n{start_ts} --> {end_ts}\n{text}")

        if count == 0:
            write_result(result_path, {"ok": False, "error": "No speech segments detected."})
            return 2

        output_path.write_text("\n\n".join(blocks) + "\n", encoding="utf-8")
        write_result(
            result_path,
            {
                "ok": True,
                "language": getattr(info, "language", language),
                "segmentCount": count,
            },
        )
        return 0
    except Exception as exc:  # pragma: no cover
        try:
            write_result(result_path, {"ok": False, "error": str(exc)})
        except OSError:
            pass
        print(str(exc), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
