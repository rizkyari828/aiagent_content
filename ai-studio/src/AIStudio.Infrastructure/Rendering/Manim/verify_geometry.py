"""Geometry self-check for the repository-owned Manim templates.

Runs with the Manim-enabled interpreter, for example::

    .venv/bin/python3 src/AIStudio.Infrastructure/Rendering/Manim/verify_geometry.py

It guards the ProcessFlow progress bar against the bow-tie/hourglass regression:
a ``RoundedRectangle`` narrower than ``2 * corner_radius`` self-intersects, and a
uniform ``scale_to_fit_width`` (or a width-only stretch of a capsule) deforms the
bar. Every progress state must be a valid, constant-height, left-anchored capsule.

Exits non-zero when any invariant is violated, so it can gate a targeted change.
"""

from __future__ import annotations

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import templates  # noqa: E402


def _crossings(mobject) -> int:
    """Count self-intersections of a closed 2D path (projected to XY)."""
    points = mobject.points[:, :2]

    def cross(o, p, q):
        return (p[0] - o[0]) * (q[1] - o[1]) - (p[1] - o[1]) * (q[0] - o[0])

    def orient(a, b, c):
        value = cross(a, b, c)
        return 0 if abs(value) < 1e-9 else (1 if value > 0 else -1)

    count = 0
    total = len(points)
    for i in range(total):
        a, b = points[i], points[(i + 1) % total]
        for j in range(i + 1, total):
            if abs(i - j) <= 1 or (i == 0 and j == total - 1):
                continue
            c, d = points[j], points[(j + 1) % total]
            if orient(a, b, c) != orient(a, b, d) and orient(c, d, a) != orient(c, d, b):
                count += 1
    return count


def main() -> int:
    colors = templates.PALETTES["Ocean"]
    track = templates.build_progress_track()
    failures: list[str] = []

    if _crossings(track):
        failures.append("progress track self-intersects")

    track_left = track.get_left()[0]
    for value in (0.0, 0.25, 0.75, 1.0):
        fill = templates.progress_fill(colors, value, track)
        if _crossings(fill):
            failures.append(f"fill self-intersects at value {value}")
        if fill.width < 2 * templates.BAR_CORNER_RADIUS - 1e-9:
            failures.append(f"fill narrower than 2*corner_radius at value {value}")
        if abs(fill.height - templates.BAR_HEIGHT) > 1e-6:
            failures.append(f"fill height changed at value {value}")
        if abs(fill.get_left()[0] - track_left) > 1e-6:
            failures.append(f"fill left edge moved at value {value}")
        if abs(fill.get_center()[1] - track.get_center()[1]) > 1e-6:
            failures.append(f"fill not vertically centred on track at value {value}")

    if failures:
        for failure in failures:
            print(f"FAIL: {failure}", file=sys.stderr)
        return 1

    print("OK: process_flow progress bar is a valid, constant-height, left-anchored capsule")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
