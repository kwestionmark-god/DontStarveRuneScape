# Animal taming — design

Date: 2026-10-09
Status: Implemented (RED 7615462 / GREEN 2b47599 / wiring e8aed14)
Slice line: feed-to-tame wild animals → follower or colony guard, gated by a new `taming` skill.

## Design law

Any `tamable: true` monster can be fed the right food to become a pet:
either a bonded follower (companion tether) or a colony-assigned guard
that wanders near the colony anchor and attacks hostiles in radius.
Feeding spends the item and rolls a tame chance scaled by the new
top-level `taming` skill; stronger species gate behind higher levels.
Failures are visible, never silent.

## Goal (user-approved, clarify round 2026-10-09)

1. Feed-to-tame: E on a tamable monster with the species-keyed food
   spends the item and rolls a tame chance.
2. Both paths: a tamed animal can follow the player (companion slot,
   existing CompanionBehavior tether) or be assigned to the colony
   (wander near anchor, guard: attacks hostiles in radius).
3. New top-level skill `taming`: XP per attempt (small) and per
   success (large); stronger species carry `tame_level` gates.
4. Colony-assigned animals wander near the colony anchor and attack
   hostiles that come within a guard radius.

## Non-goals

- Breeding, animal aging, pet naming UI.
- Advanced guard orders / pack behaviors; colony production from animals
  (wool/eggs) is a later econ slice.
- Taming the `tamable: false` faction NPCs (goblins etc.) — explicitly
  excluded by the data flags.
- Weaken-first-then-tame; feeding is the whole verb (user vote).

## Ground — verified in the tree

- `MonsterDef.Tamable` + `FactionOwned` exist and monsters.json already
  flags 10 species tamable (wolf, bear, poison_frog, crocodile, eagle,
  boar, others) with `_note` reserving "Phase 0 taming flags"
  (Data/Monster.cs:46-50).
- `CombatSystem.SpawnMonster(def, x, y, biome)` is the public spawn
  seam (CombatSystem.cs:71); `Monster.IsHostile` is readable runtime
  state (CompanionBehavior.cs:129 pattern).
- CompanionBehavior is the follower engine: `Bond(npcId)` single bond,
  `TetherTiles` 14 / `ResumeTiles` 7, `RecruitBehavior == "companion"`
  filter at Register/Tick (CompanionBehavior.cs:57-72).
- InteractSystem.HandleInteract: NPC-panel branch first, then resource
  fallback (Interactions/InteractSystem.cs:24-86) — monsters currently
  have no E-verb at all.
- Skill registration is a literal id list in SkillManager ctor
  (Skills/SkillManager.cs:57-62); SubStatCatalog in the same file is
  single-sourced to the panel and SpendPoint.
- RecruitNpc carries `RecruitBehavior` strings; the dispatch filter
  (`RecruitBehavior is not (...)`) is a 3-part change per the skill.
- Probabilistic rolls in tests need the tri-state hook + seeded Random
  pattern (raid/rare-drop precedent): pure static window math
  (roll, level) + a `string?` roll override + `Random?` seed.

## Design

### Data

- monsters.json: each tamable species gains `tame_food` (item id) and
  `tame_level` (int, 1 = untrained-tamable; wolf 1, boar 2, bear 5,
  crocodile 6, swamp_drake 8 …). Untamable species need nothing.
- MonsterDef gains `TameFood` (`tame_food`) + `TameLevel` (`tame_level`,
  default 1). No NpcDataTests-style count pins to bump here (monsters
  have no count pin; verify before RED).

### Pure math (hook-free tested)

- `TamingMath.TameChance(roll, tamingLevel, speciesTameLevel)` →
  window check: base 35% at parity, ±5%/level delta, clamped [5%, 95%].
  Static class, no state — the raid/rare-drop precedent.
- `TamingSystem.RollOverride` (string?: null = rng, "" = forced fail,
  "success" = forced tame) + `RandomSeed` property.

### Flow — player E on a tamable monster

1. InteractSystem.HandleInteract gains a monster branch BEFORE the NPC
   branch (proximity scan of `game.Combat.Monsters` within 96px): if
   the nearest live monster is `tamable` and the player holds its
   `tame_food`, run `TamingSystem.TryTame(player, monster, skills,
   combat, npcs, foods, rng)`. If tamable but food missing → visible
   notification ("The wolf eyes you warily. It wants raw meat."), no
   consumption.
2. TryTame: consume 1 food (standing rule: spent even on failure, like
   bait at cast), award attempt XP (taming, ~4), roll:
   - Fail → visible "The wolf snarls and backs off." and the animal
     stays wild (cooldown on that monster, ~10s, so spamming drains
     food honestly).
   - Success → award success XP (~25), remove from CombatSystem, and
     stage the pet: if the player has no bonded companion → bond as the
     follower (RecruitBehavior "pet", CompanionBehavior extended to
     admit it); else → colony-assigned (wander near colony anchor when
     founded, else stays put near tame site).
3. Level gate: `skills.GetSkill("taming").Level < def.TameLevel` →
   visible refusal ("You need taming level 5 to tame a bear."), food
   NOT consumed (gated attempts aren't losses).

### Pets as recruits

- Tamed animals become RecruitNpc-shaped runtime entities (new internal
  id `pet_<monsterId>_<n>`; not registry NPCs, so persistence
  RestoreSnapshot must serialize them explicitly — the lobby pitfall
  says registry-missing rows vanish; a `TamedAnimalSnapshot` list on
  the taming system owns the round-trip).
- RecruitBehavior `"pet"` joins the dispatch filter 3-part change:
  admission filter, schedule/category resolution (pets bypass the
  worker templates — guard explicitly), pet branch in Tick.
- Colony guard: when the pet is colony-assigned and a live hostile is
  within `GuardRadiusTiles` (6) of the anchor (or of the pet, whichever
  nearer), the pet engages via the existing combat seam; otherwise it
  wanders within ~4 tiles of the anchor point.
- HP: pets carry their def hp; death is permanent, visible loss
  ("Your wolf falls."). No silent despawn.

### Skill

- SkillManager: add `"taming"` to the id list; SubStatCatalog gains
  `["taming"] = ["success_rate"]` in the same slice (success_rate each
  point +2% tame window, additive with level scaling) — stat-menus law:
  a sub-stat lands with its consumer.
- XP: attempt 4xp, success 25xp; level gates on species.

## Testing plan

TamedAnimalTests (new file, ColonySkillGateTests harness shape):

1. E-interact with correct food consumes 1 and either bonds follower
   (forced success via RollOverride) or leaves monster wild (forced
   fail) — assert BOTH by roll override, and assert inventory delta.
2. Gate: taming level below `tame_level` refuses, food NOT consumed.
3. Wrong-food / hostile-unaware notify path: no consumption, visible
   message (standing no-silent-loss rule).
4. Follower bond: first pet bonds, second pet colony-assigns when a
   bond exists; CompanionBehavior.Tick follows the player and
   "Left behind" fires past tether (reuse the follow test shape).
5. Colony guard: hostile within guard radius of anchor → pet engages
   (pet attacks: monster hp decreases after ticks); hostile beyond
   radius → wander only, never crosses the leash.
6. Persistence: save/load round-trips tamed animals (bonded state,
   position, hp, species) — Npc registry-missing pitfall pins this.
7. Skill: taming appears in SkillManager, SubStatCatalog exposes
   success_rate on taming and the window widens with points (pure
   TamingMath test, hook-free).
8. monsters.json data: every tamable species has tame_food +
   tame_level; every tame_food id exists in items.json (raw-row seam,
   like Bootstrap resources).

## Open questions

- Colony-assignment UI for this slice: automatic (bond taken → colony)
  with a follow/release verb deferred, or a minimal assign toggle on
  the pet interact? Default: automatic per item 2 above.
- Guard aggression: pets engage automatically in radius (no attack
  orders) — matches user vote.

## Revision history

- 2026-10-09: draft from clarify round (feed-to-tame, both paths,
  new taming skill, wander+guard colony behavior).
- 2026-10-09: implemented. Notes from the build: (1) the guard test
  taught us pets APPROACH first (dist > 48px) and strike when adjacent —
  one tick at 32px lands the hit; (2) pets are rendered via RenderNPC
  with a `monster/<species>` sprite key (RecruitBehavior "pet" +
  non-empty SpeciesId), not a new renderer; (3) E-interact tests need
  game.Inventory + game.SkillManager set at the GAME level too —
  HandleInteract's null guard reads those, not the player's; (4) pet
  persistence lives on SaveData.TamedAnimals (old saves read empty —
  no pets to restore, no break).
