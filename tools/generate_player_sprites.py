#!/usr/bin/env python3
"""generate_player_sprites.py — Split the original player sprite into a
static body plus a loose boot, for the physical-stride leg renderer.

The body is byte-identical to the original idle_0.png above the legs
(the cryptid stays intact). Legs/boots are not baked in: the renderer
places each foot procedurally along its stride arc and plants it at the
terrain elevation it lands on, contouring the heightmap even when idle.

Output: src/DontStarveRuneScape/Assets/sprites/player/
    body.png   original sprite with the leg/boot rows removed
    boot.png   4x4 brown boot block

Regenerate after tweaking the source sprite:
    python3 tools/generate_player_sprites.py
"""

import os
from PIL import Image

ROOT = os.path.join(os.path.dirname(__file__), "..", "src", "DontStarveRuneScape", "Assets", "sprites", "player")
SOURCE_IDLE = os.path.join(ROOT, "idle_0.png")
LEG_TOP_ROW = 13          # rows >= this belong to legs/boots in the original
BOOT_COLOR = (139, 90, 43, 255)  # original boot brown


def make_body():
    src = Image.open(SOURCE_IDLE).convert("RGBA")
    body = src.copy()
    px = body.load()
    for y in range(LEG_TOP_ROW, body.height):
        for x in range(body.width):
            px[x, y] = (0, 0, 0, 0)
    body.save(os.path.join(ROOT, "body.png"))


def make_boot():
    boot = Image.new("RGBA", (4, 4), (0, 0, 0, 0))
    px = boot.load()
    for x in range(4):
        for y in range(4):
            px[x, y] = BOOT_COLOR
    boot.save(os.path.join(ROOT, "boot.png"))


def main():
    make_body()
    make_boot()
    print(f"Generated body.png + boot.png in {os.path.abspath(ROOT)}")


if __name__ == "__main__":
    main()
