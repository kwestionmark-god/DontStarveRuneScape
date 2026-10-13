# Firelight glow on tiles — slice 16 design (2026-10-13)

Vertical-building arc NEXT item (firelight glow radius on tiles), carried
from the Hermes-harness memory transfer. Fires read at night only as the
flame itself; the ground around them stays at full darkness. Fires should
pool warm light onto nearby tiles.

## Law
- Glow exists ONLY under darkness: alpha scales with the night-overlay
  darkness factor (day = invisible; cave = its constant overlay factor).
- Glow grows with the fire's STAGE (ember < burning < roaring) and the
  fire's WarmthRadius (the first consumer of the field; gameplay warmth
  is the separate slice-17 item).
- Glow is a soft radial WARM overlay drawn after the darkness overlay,
  centered on the fire's screen position, textured (quadratic falloff,
  same idiom as the soft ground shadow).

## Contracts
- `FireGlowRadiusPx(stage, warmthRadius, zoom)`: embers = 0.5x the warmth
  footprint, burning = 0.8x, roaring = 1.1x; footprint = warmthRadius *
  32 world-px * zoom.
- `FireGlowAlpha(stage, darkness)`: 0 when darkness <= 0.01; stage
  multipliers 0.35 / 0.6 / 0.8 * darkness * 255, clamped.
- Both pure and static on SpriteRenderer (test-pinned).
- Smoketest: night capture (`DSR_TIME_OF_DAY=0.75`) with `campfire:lit:10`
  shows a warm ground pool around the pit; `campfire:lit:1` shows a faint
  one. Vision-verified via the delegated kimi-k3 pass.
