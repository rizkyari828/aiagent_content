# 05 Video1 Production Pipeline

> Derived split of **Local AI Ecosystem PRD v0.9.2**. The numbered sections below are preserved from the frozen source PRD; this file does not introduce new product decisions.

---

# 26. Content Production Pipeline

Target workflow:

```text
Topic
  ↓
Idea
  ↓
Research
  ↓
Script
  ↓
Storyboard
  ↓
Scene Manifest
  ↓
Assets
  ↓
Narration
  ↓
Subtitle
  ↓
FFmpeg
  ↓
QA
  ↓
Final Video
  ↓
Human Approval
  ↓
Publish

```

Not every stage requires automation.

---

# 27. Video #1 Minimal Workflow

Video #1 must intentionally contain manual steps.

Recommended:

```text
Topic / Problem selection       Human
Idea support                    AI
Script draft                    AI
Script editing                  Human
Research/fact checking          Human + AI support
Storyboard                      Simple structured draft
Visual collection               Mostly manual
Narration                       Manual or simple local TTS
Composition                     FFmpeg
QA                              Human
Publishing                      Human
Analytics recording             Manual

```

The goal is to validate content.

Not automation coverage.

---

# 28. Video Generation

Heavy generative video is deferred.

Progression:

```text
Manual / sourced visual
        ↓
Generated image if useful
        ↓
Simple motion
        ↓
Image-to-video selectively
        ↓
Generated video only after evidence

```

FFmpeg remains the primary deterministic composition engine.

Remotion and Blender remain deferred unless production evidence requires them.

---
