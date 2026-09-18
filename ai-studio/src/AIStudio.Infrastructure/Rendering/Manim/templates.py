"""Repository-owned Manim templates for AI Studio.

Only *data* is supplied at render time: kicker, headline, short labels, palette
name, resolved palette colors, and duration. This module is fixed source owned by
the repository. Templates use shapes and ``Text`` (no LaTeX/MathTex), so Manim runs
without a TeX installation.

Visual language is intentionally aligned with ``SceneVisualSvg``: same palettes,
same dark layered background, same left-aligned kicker/headline header with an
accent underline, same accent chips, and the same reserved bottom subtitle zone.
"""

from manim import (
    DOWN,
    FadeIn,
    FadeOut,
    LaggedStart,
    LEFT,
    Line,
    MoveAlongPath,
    ORIGIN,
    RIGHT,
    RoundedRectangle,
    Scene,
    Text,
    UP,
    UL,
    UR,
    VGroup,
    WHITE,
    Write,
    Create,
    Circle,
    Dot,
)

# Typography shares the SVG font so SVG and Manim scenes read as one video.
FONT = "DejaVu Sans"
BLOCK_COLOR = "#ff6b6b"
PANEL_COLOR = "#05090d"
ONLINE_COLOR = "#5ef2a0"

# Fallback palettes mirror SceneVisualSvg.GetColors; the renderer sends the
# authoritative values in params["colors"] so the two never drift.
PALETTES = {
    "Ocean": {"background": "#0b1e2d", "backgroundAlt": "#123a4d", "accent": "#5fd3c4", "text": "#eaf6ff"},
    "Sunset": {"background": "#2a1220", "backgroundAlt": "#4a1f2d", "accent": "#ffb26b", "text": "#fff2e8"},
    "Violet": {"background": "#14122b", "backgroundAlt": "#2a2352", "accent": "#b9a7ff", "text": "#f0edff"},
    "Slate": {"background": "#101418", "backgroundAlt": "#1e2a33", "accent": "#7fb2ff", "text": "#eaf1fb"},
}

# The burned subtitles sit in the bottom ~90px of a 720p frame (~1.0 unit).
SAFE_BOTTOM_Y = -3.0


def _colors(params):
    fallback = PALETTES.get(params.get("palette"), PALETTES["Ocean"])
    provided = params.get("colors") or {}
    return {
        "background": provided.get("background", fallback["background"]),
        "backgroundAlt": provided.get("backgroundAlt", fallback["backgroundAlt"]),
        "accent": provided.get("accent", fallback["accent"]),
        "text": provided.get("text", fallback["text"]),
    }


def _text(value, size, color, weight="NORMAL", opacity=1.0):
    mob = Text(value or "", font=FONT, font_size=size, color=color, weight=weight)
    if opacity != 1.0:
        mob.set_opacity(opacity)
    return mob


def _short(value, limit, fallback):
    value = (value or "").strip()
    return value if 0 < len(value) <= limit else fallback


def _fit(mob, max_width):
    if mob.width > max_width:
        mob.scale_to_fit_width(max_width)
    return mob


def _apply_background(scene, colors):
    """Dark layered treatment matching the SVG gradient + decorative glows."""
    scene.camera.background_color = colors["background"]
    scene.add(
        VGroup(
            Circle(radius=6.4, fill_color=colors["backgroundAlt"], fill_opacity=0.24, stroke_width=0)
            .move_to(RIGHT * 6.6 + UP * 3.6),
            Circle(radius=3.0, fill_color=colors["accent"], fill_opacity=0.06, stroke_width=0)
            .move_to(RIGHT * 4.7 + UP * 2.9),
            Circle(radius=2.4, fill_color=colors["accent"], fill_opacity=0.05, stroke_width=0)
            .move_to(LEFT * 5.4 + DOWN * 3.7),
        )
    )


def _header(params, colors):
    kicker = _text(" ".join((params.get("kicker") or "Video 1").upper()), 20, colors["accent"], "BOLD")
    heading = _fit(_text(params.get("primaryText"), 42, colors["text"], "BOLD"), 11.6)
    underline = Line(ORIGIN, RIGHT * 1.05, color=colors["accent"], stroke_width=5)
    group = VGroup(kicker, heading, underline).arrange(DOWN, aligned_edge=LEFT, buff=0.13)
    group.to_corner(UL, buff=0.45)
    return group


def _chip(label, colors, highlight=False):
    text = _text(label, 20, colors["background"] if highlight else colors["text"], "BOLD")
    box = RoundedRectangle(
        width=text.width + 0.5,
        height=0.56,
        corner_radius=0.28,
        fill_color=colors["accent"],
        fill_opacity=0.9 if highlight else 0.12,
        stroke_color=colors["accent"],
        stroke_opacity=0.5,
        stroke_width=2,
    )
    return VGroup(box, text)


class LocalAiFlow(Scene):
    """Laptop -> data flows to a blocked cloud -> data returns -> LOCAL state."""

    def __init__(self, params, **kwargs):
        super().__init__(**kwargs)
        self.params = params

    def construct(self):
        colors = _colors(self.params)
        duration = float(self.params.get("durationSeconds", 3.5))
        accent = colors["accent"]
        _apply_background(self, colors)

        elapsed = 0.0
        header = _header(self.params, colors)
        self.play(Write(header), run_time=0.45)
        elapsed += 0.45

        # Laptop: large, left side, occupying real frame area.
        screen = RoundedRectangle(
            width=4.7,
            height=3.0,
            corner_radius=0.18,
            stroke_color=accent,
            stroke_width=5,
            fill_color=PANEL_COLOR,
            fill_opacity=0.9,
        )
        screen_bar = RoundedRectangle(
            width=3.9, height=0.34, corner_radius=0.08, fill_color=accent, fill_opacity=0.55, stroke_width=0
        ).move_to(screen.get_center() + UP * 0.98)
        keyboard = RoundedRectangle(
            width=5.6, height=0.42, corner_radius=0.1, fill_color=accent, fill_opacity=0.9, stroke_width=0
        ).next_to(screen, DOWN, buff=0.03)
        laptop = VGroup(screen, screen_bar, keyboard).move_to(LEFT * 3.6 + DOWN * 0.2)
        self.play(FadeIn(laptop, shift=UP * 0.2), run_time=0.35)
        elapsed += 0.35

        # Cloud: large, right side.
        cloud_bumps = VGroup(
            Circle(radius=0.62, fill_color=accent, fill_opacity=0.24, stroke_width=0).move_to(LEFT * 0.78),
            Circle(radius=0.9, fill_color=accent, fill_opacity=0.24, stroke_width=0).move_to(UP * 0.16),
            Circle(radius=0.56, fill_color=accent, fill_opacity=0.24, stroke_width=0).move_to(RIGHT * 0.82 + DOWN * 0.1),
            RoundedRectangle(
                width=2.2, height=0.78, corner_radius=0.36, fill_color=accent, fill_opacity=0.24, stroke_width=0
            ).move_to(DOWN * 0.4),
        )
        cloud = cloud_bumps.copy().move_to(RIGHT * 3.5 + UP * 0.35)
        self.play(FadeIn(cloud, scale=0.8), run_time=0.3)
        elapsed += 0.3

        # Data path + travelling dots.
        path = Line(laptop.get_right(), cloud.get_left(), color=accent, stroke_width=4)
        dots = VGroup(
            *[Dot(radius=0.1, color=accent).move_to(path.point_from_proportion(p)) for p in (0.0, 0.16, 0.32)]
        )
        self.play(Create(path), FadeIn(dots), run_time=0.25)
        self.play(
            LaggedStart(*[MoveAlongPath(dot, path) for dot in dots], lag_ratio=0.12),
            run_time=0.4,
        )
        elapsed += 0.65

        # Cloud connection is blocked: red X stays on the cloud.
        cross = VGroup(
            Line(cloud.get_corner(UL), cloud.get_corner(DOWN + RIGHT), color=BLOCK_COLOR, stroke_width=10),
            Line(cloud.get_corner(UR), cloud.get_corner(DOWN + LEFT), color=BLOCK_COLOR, stroke_width=10),
        )
        self.play(FadeIn(cross, scale=0.6), path.animate.set_color(BLOCK_COLOR), run_time=0.3)
        elapsed += 0.3

        # Data returns to the laptop; the cloud stays blocked.
        self.play(
            LaggedStart(
                *[MoveAlongPath(dot, path.copy().reverse_points()) for dot in reversed(dots)],
                lag_ratio=0.12,
            ),
            run_time=0.4,
        )
        self.play(path.animate.set_color(accent), FadeOut(dots), run_time=0.2)
        elapsed += 0.6

        # Final state: LOCAL / PRIVATE / OFFLINE.
        chips = VGroup(
            _chip("LOCAL", colors, highlight=True),
            _chip("PRIVATE", colors),
            _chip("OFFLINE", colors),
        ).arrange(RIGHT, buff=0.3).move_to(LEFT * 3.6 + DOWN * 2.35)
        self.play(
            LaggedStart(*[FadeIn(chip, shift=UP * 0.18) for chip in chips], lag_ratio=0.16),
            run_time=0.5,
        )
        elapsed += 0.5

        self.wait(max(0.1, duration - elapsed))


class ChatFlow(Scene):
    """User message -> local processing -> assistant reply, inside a chat window."""

    def __init__(self, params, **kwargs):
        super().__init__(**kwargs)
        self.params = params

    def construct(self):
        colors = _colors(self.params)
        duration = float(self.params.get("durationSeconds", 3.5))
        accent = colors["accent"]
        _apply_background(self, colors)

        elapsed = 0.0
        header = _header(self.params, colors)
        self.play(Write(header), run_time=0.45)
        elapsed += 0.45

        # Chat window mirroring the SVG Chat layout.
        window = RoundedRectangle(
            width=10.4,
            height=4.4,
            corner_radius=0.28,
            fill_color=PANEL_COLOR,
            fill_opacity=0.92,
            stroke_color=accent,
            stroke_opacity=0.45,
            stroke_width=3,
        ).move_to(DOWN * 0.25)
        title_bar = RoundedRectangle(
            width=10.4, height=0.6, corner_radius=0.28, fill_color=WHITE, fill_opacity=0.06, stroke_width=0
        ).align_to(window, UP)
        window_title = _text("AI Lokal", 20, colors["text"], "NORMAL", opacity=0.8)
        window_title.next_to(title_bar.get_left(), RIGHT, buff=0.72).align_to(title_bar, UP).shift(DOWN * 0.08)
        traffic = VGroup(
            *[Dot(radius=0.07, color=c).move_to(title_bar.get_left() + RIGHT * (0.36 + 0.26 * i) + DOWN * 0.03)
              for i, c in enumerate(("#ff5f56", "#ffbd2e", "#27c93f"))]
        )
        status_dot = Dot(radius=0.07, color=ONLINE_COLOR)
        status_dot.next_to(title_bar.get_right(), LEFT, buff=0.35).align_to(title_bar, UP).shift(DOWN * 0.3)
        status_label = _text("offline", 16, colors["text"], "NORMAL", opacity=0.7)
        status_label.next_to(status_dot, LEFT, buff=0.12)
        self.play(
            FadeIn(window),
            FadeIn(title_bar),
            FadeIn(window_title),
            FadeIn(traffic),
            FadeIn(status_dot),
            FadeIn(status_label),
            run_time=0.45,
        )
        elapsed += 0.45

        # User message (right aligned, upper body).
        user_text = _short(self.params.get("secondaryText"), 16, "HALO")
        user_bubble = RoundedRectangle(
            width=min(5.6, 0.7 + len(user_text) * 0.28),
            height=0.95,
            corner_radius=0.24,
            fill_color=accent,
            fill_opacity=0.25,
            stroke_color=accent,
            stroke_width=2,
        )
        user_label = _text(user_text, 22, colors["text"], "BOLD").move_to(user_bubble.get_center())
        user_group = VGroup(user_bubble, user_label).move_to(window.get_center() + UP * 0.95 + RIGHT * 2.5)
        self.play(FadeIn(user_group, shift=LEFT * 0.2), run_time=0.4)
        elapsed += 0.4

        # Local processing indicator (left aligned, between the two messages).
        proc_bubble = RoundedRectangle(
            width=1.9, height=0.85, corner_radius=0.24, fill_color=WHITE, fill_opacity=0.08, stroke_color=accent, stroke_width=2
        )
        proc_dots = VGroup(
            *[Dot(radius=0.09, color=accent).move_to(proc_bubble.get_center() + LEFT * 0.4 + RIGHT * 0.4 * i)
              for i in range(3)]
        )
        proc_group = VGroup(proc_bubble, proc_dots).move_to(window.get_center() + UP * 0.1 + LEFT * 3.1)
        self.play(FadeIn(proc_group), run_time=0.2)
        self.wait(0.3)
        self.play(FadeOut(proc_group), run_time=0.15)
        elapsed += 0.65

        # Assistant reply (left aligned, lower body).
        reply_text = _short(self.params.get("tertiaryText"), 18, "SIAP, OFFLINE")
        reply_bubble = RoundedRectangle(
            width=min(6.4, 0.7 + len(reply_text) * 0.28),
            height=1.05,
            corner_radius=0.24,
            fill_color=WHITE,
            fill_opacity=0.1,
            stroke_color=accent,
            stroke_width=2,
        )
        reply_label = _text(reply_text, 22, colors["text"], "BOLD").move_to(reply_bubble.get_center())
        reply_group = VGroup(reply_bubble, reply_label).move_to(window.get_center() + DOWN * 0.95 + LEFT * 2.5)
        self.play(FadeIn(reply_group, shift=RIGHT * 0.2), run_time=0.45)
        elapsed += 0.45

        self.wait(max(0.1, duration - elapsed))
