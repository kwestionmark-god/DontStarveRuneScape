# Sprite Assets

This directory contains all sprite assets for Don't Starve RuneScape.

## Structure

```
assets/sprites/
├── player/        # Player character sprites (16x16)
│   ├── idle_0.png - idle_3.png  # 4 idle frames
│   └── walk_0.png - walk_3.png  # 4 walk frames
├── trees/         # Tree and bush sprites (32x32 unless noted)
│   ├── oak.png, pine.png, maple.png, spruce.png, willow.png, birch.png
│   ├── elder_wood.png             # Tier 4 tree (32x32)
│   ├── berry_bush.png             # Bush sprite (16x16)
│   ├── berry_bush_depleted.png    # Bare bush (16x16)
│   ├── berry_bush_young.png       # Regrowing bush (16x16)
│   ├── oak_depleted.png, *_depleted.png   # Stump/depleted variants
│   ├── oak_sapling.png, *_sapling.png     # Early regrowth (~0-40%)
│   ├── oak_young.png, *_young.png         # Mid regrowth (~40-80%)
│   ├── stump.png                  # Shared tree-depleted fallback
│   ├── sapling_generic.png        # Shared early-regrowth fallback
│   └── young_generic.png          # Shared mid-regrowth fallback
├── rocks/         # Rock and ore sprites (16x16)
│   ├── iron.png, copper.png, gold.png, stone.png
│   ├── gem.png, rare.png, tin.png
│   ├── void_crystal.png, obsidian.png, mithril.png
│   ├── amber.png, moonstone.png, ghost_iron.png
│   ├── star_metal.png, ancient_rune.png, celestial_crystal.png
│   ├── *_depleted.png             # Rubble/cracked-vein variants
│   └── rubble.png                 # Shared rock-depleted fallback
├── world/         # World resources (16x16)
│   ├── herb.png, grass.png, fiber.png, wheat.png
│   ├── water.png, driftwood.png, shell.png, fish.png
│   ├── salt.png, peat.png, toxic_reed.png
│   ├── sand.png, salt_crystal.png, cactus.png
│   ├── silk_nest.png, dragonbone.png
│   ├── void_essence.png, phoenix_feather.png
│   ├── *_depleted.png             # Cut/desiccated/scoured variants
│   └── depleted_patch.png         # Shared world-depleted fallback
├── monster/       # Monster sprites (16x16)
│   ├── wolf.png, bear.png, goblin.png
│   ├── poison_frog.png, crocodile.png, swamp_drake.png
│   ├── scorpion.png, sand_worm.png, djinn.png
│   ├── stone_golem.png, eagle.png, cave_troll.png
│   ├── boar.png, snake.png, hawk.png
│   ├── crab.png, sea_serpent.png
├── structure/     # Structure sprites (16x16)
│   ├── campfire.png, chest.png, furnace.png
│   ├── stone_wall.png, wooden_gate.png, iron_gate.png
│   └── ... (18 total)
├── npcs/          # NPC sprites (16x16)
│   ├── merchant.png
│   ├── quest_giver.png
│   ├── faction_leader.png
│   └── recruit.png
├── terrain/       # Terrain tile sprites (64x64)
│   ├── forest.png
│   ├── plains.png
│   ├── coastal.png
│   ├── swamp.png
│   ├── mountains.png
│   └── desert.png
└── items/         # Inventory icons
```

## Naming Convention

The renderer derives sprite keys from a strict suffix scheme:

| Suffix | Meaning |
|--------|---------|
| (none) | Mature / full sprite |
| `_depleted` | Fully depleted (stump / rubble / bare patch) |
| `_sapling` | Tree early regrowth (~0–40% progress) |
| `_young` | Tree mid regrowth (~40–80% progress) |

Examples:
- `trees/oak` → `trees/oak_depleted.png`, `trees/oak_sapling.png`, `trees/oak_young.png`
- `rocks/iron` → `rocks/iron_depleted.png`
- `world/grass` → `world/grass_depleted.png`

### Shared fallbacks (always generated)

- `trees/stump.png` — generic tree depleted
- `trees/sapling_generic.png` / `trees/young_generic.png` — shared tree regrowth stages
- `rocks/rubble.png` — generic rock depleted
- `world/depleted_patch.png` — generic plant/soft resource depleted

If a per-type sprite is missing, the renderer falls back through these shared assets before
using the geometric placeholder.

## Generation

All sprites are generated programmatically using `tools/generate_sprites.py` with pygame primitives. This creates consistent pixel-art style sprites using simple geometric shapes (circles, rectangles, polygons).

To regenerate all sprites:
```bash
python tools/generate_sprites.py
```

## Sprite Loading

Sprites are loaded by `SpriteRenderer` and `TileRenderer` using the `sprite_key.png` convention:
- Player: `player/{frame}.png`
- Resources: `{category}/{name}.png` (with optional `_depleted`, `_sapling`, `_young` suffixes)
- Terrain: `terrain/{biome}.png`

The `SpriteRenderer._resolve_resource_sprite` method selects the correct sprite based on the
resource's depletion state and regrowth timer, applying seasonal tint and fog overlays consistently
across all stages.

The renderers cache loaded sprites and fall back to colored placeholder shapes if a sprite is not found.
