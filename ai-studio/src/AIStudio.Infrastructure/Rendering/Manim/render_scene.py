"""Controlled Manim launcher for AI Studio.

Renders a predefined template from structured JSON parameters and copies the
result to the requested output path. Template names are allowlisted; this script
never evaluates input as source.
"""

import argparse
import json
import os
import shutil
import sys
import tempfile

from manim import tempconfig

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import templates  # noqa: E402

TEMPLATES = {
    "local_ai_flow": templates.LocalAiFlow,
    "chat_flow": templates.ChatFlow,
    "process_flow": templates.ProcessFlow,
}


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--template", required=True)
    parser.add_argument("--params", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()

    if args.template not in TEMPLATES:
        print(f"unknown template '{args.template}'", file=sys.stderr)
        return 2

    with open(args.params, "r", encoding="utf-8") as handle:
        params = json.load(handle)

    media_dir = tempfile.mkdtemp(prefix="aistudio-manim-media-")
    try:
        config = {
            "media_dir": media_dir,
            "output_file": "scene",
            "pixel_width": 1280,
            "pixel_height": 720,
            "frame_rate": 30,
            "disable_caching": True,
            "verbosity": "ERROR",
            "progress_bar": "none",
        }
        with tempconfig(config):
            scene = TEMPLATES[args.template](params)
            scene.render()
            rendered = scene.renderer.file_writer.movie_file_path

        output_dir = os.path.dirname(os.path.abspath(args.output))
        os.makedirs(output_dir, exist_ok=True)
        shutil.copyfile(rendered, args.output)
        return 0
    finally:
        shutil.rmtree(media_dir, ignore_errors=True)


if __name__ == "__main__":
    sys.exit(main())
