# I2V Benchmark Specification (P1.7)

This specification defines the exact `P1.7` benchmark protocol for Home GPU execution comparing **LTX-Video 0.9.x 2B** against **Wan2.2 TI2V-5B** within a constrained 16 GB VRAM budget (NVIDIA RTX 4060 Ti).

---

## 1. Fixed Test Scene Definition

To ensure reproducible comparison between LTX-Video and Wan2.2, all benchmark executions must use the following standardized scene parameters:

- **Scene Description**: A cinematic close-up of a character silhouette raising a hand as a warm golden light grows and sweeps across the fingers and palm.
- **Prompt**: 
  > `Cinematic close-up of a character raising a hand, warm golden light growing and wrapping around fingers, intricate shadow play, high detail, 24fps, photorealistic or high-grade anime aesthetic.`
- **Negative Prompt**: 
  > `deformed, distorted, disfigured, poorly drawn, bad anatomy, wrong anatomy, extra limb, missing limb, floating limbs, disconnected limbs, mutation, mutated, ugly, disgusting, blurry, amputation, watermark, lowres, bad proportions.`
- **Duration**: `4.0 seconds` (yielding exactly `96 frames` at 24 fps).
- **Resolution & Aspect Ratio**: 
  - Target: `1280x720` (16:9 widescreen).
  - Model-native constraints: If the selected model tier or VRAM headroom requires clamping, scale down proportionally to `854x480` or model-supported latent block sizes, maintaining 16:9 aspect ratio.
- **Fixed Random Seed**: `42` (applied to both noise initializer and first-frame conditioning samplers).
- **First Frame Conditioning**: A standardized 1280x720 PNG (`.demo/fixtures/bench_start_frame_42.png`) depicting the initial resting hand pose and lighting setup.

---

## 2. Evaluation Rubric & Metrics

Each engine run is evaluated across six rigorous dimensions:

1. **Face / Hand Morphing & Anatomical Stability (Score 1–5)**
   - *5*: Fingers and hand structure remain anatomically consistent throughout the 4s clip; no extra or disappearing digits, minimal warping.
   - *1*: Severe hand/finger mutation, melting, or chaotic morphing.
2. **Prompt Adherence (Score 1–5)**
   - *5*: Golden light grows organically and illuminates the hand exactly as described; strong shadow play.
   - *1*: Light/shadow dynamics are absent or contradict the prompt.
3. **Temporal Stability / Flicker (Score 1–5)**
   - *5*: Smooth temporal transitions between frames; zero high-frequency jitter or background shimmering.
   - *1*: Pronounced flickering, frame dropping, or strobe-like artifacts.
4. **Peak VRAM Consumption (MB / GB)**
   - Measured via `nvidia-smi` query logging during inference execution. Must not exceed **15.5 GB** on a 16 GB RTX 4060 Ti (leaving headroom for system display managers).
5. **Wall-Clock Latency (Seconds)**
   - Total time elapsed from ComfyUI workflow enqueue (`/prompt`) to video file finalized on disk.
6. **`IMediaInspector` Probe Pass (Boolean)**
   - Automated validation via the backend `IMediaInspector` service: verifies valid video stream headers, container format (`mp4` / `mov`), exact requested dimensions (`1280x720` or model-clamped equivalent), frame rate (`24 fps`), and non-zero audio/video duration bounds.

---

## 3. Artifact Output Directory Convention

All benchmark runs must output their results into the timestamped artifact directory:

```
.demo/artifacts/i2v-bench-<UTC>/
├── ltx2b/
│   ├── generated.mp4
│   ├── frames/
│   └── vram_profile.csv
├── wan22/
│   ├── generated.mp4
│   ├── frames/
│   └── vram_profile.csv
├── metrics.json
└── summary.md
```

### `metrics.json` Schema Structure
```json
{
  "timestamp_utc": "2026-11-13T05:18:42Z",
  "hardware": "NVIDIA RTX 4060 Ti 16GB",
  "test_scene": {
    "prompt": "Cinematic close-up of a character raising a hand...",
    "seed": 42,
    "resolution": "1280x720",
    "fps": 24,
    "duration_seconds": 4.0
  },
  "engines": {
    "ltx2b": {
      "vram_peak_mb": 11450,
      "wall_time_seconds": 78.5,
      "inspector_pass": true,
      "rubric_scores": {
        "anatomy_stability": 4,
        "prompt_adherence": 4,
        "temporal_stability": 4
      }
    },
    "wan22": {
      "vram_peak_mb": 14920,
      "wall_time_seconds": 145.2,
      "inspector_pass": true,
      "rubric_scores": {
        "anatomy_stability": 5,
        "prompt_adherence": 5,
        "temporal_stability": 4
      }
    }
  }
}
```

---

## 4. GO/NO-GO Decision Criteria (P2.3)

Upon completion of benchmark runs on the Home GPU hardware, the results must be evaluated against the following thresholds to determine the primary production engine:

| Criterion | LTX-Video 0.9.x 2B | Wan2.2 TI2V-5B | GO Threshold |
| :--- | :--- | :--- | :--- |
| **Peak VRAM** | $\le 12 \text{ GB}$ | $\le 15.5 \text{ GB}$ | Must fit within 16 GB physical VRAM without OOM or aggressive CPU offloading penalty. |
| **Wall-Clock Latency** | $\le 90 \text{ s}$ per 4s clip | $\le 180 \text{ s}$ per 4s clip | Practical iteration speed for local preview rendering. |
| **Anatomical Stability** | $\ge 3.5 / 5$ | $\ge 4.0 / 5$ | Acceptable visual fidelity for hero character animations without egregious hand morphing. |
| **`IMediaInspector`** | `true` | `true` | Mandatory stream verification pass. |

### Decision Recording
- If **Wan2.2 TI2V-5B** meets VRAM (< 15.5 GB) and inspector criteria with superior prompt adherence, it is selected as the primary engine (`ComfyUiVideoGenerationProvider` configured for Wan2.2 workflows).
- If **Wan2.2 TI2V-5B** exceeds 15.5 GB VRAM or suffers prohibitive generation latency (> 3 min), **LTX-Video 0.9.x 2B** is selected as the primary engine for local Home GPU workloads, with Wan2.2 reserved for cloud/high-end rigs.
- The formal decision, rationale, and metric hashes must be recorded in both `ai-studio/docs/development/DECISIONS.md` and `ai-studio/docs/context/STATE.md` under milestone `P2.3`.
