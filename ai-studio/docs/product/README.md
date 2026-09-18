# How to use this split PRD

Start with `PRD_INDEX.md`.

- `parts/` is the canonical topical split of every numbered v0.9.2 section.
- `execution-context/` contains short, source-derived contexts for Video #1 implementation tasks.
- `_source/` contains the unchanged original PRD for verification and ambiguity resolution.
- `SECTION_MAP.md` proves where each original top-level section moved.

The split is optimized for agent context efficiency: a coding task should normally load the index plus one narrow part/context file, not the whole PRD.
