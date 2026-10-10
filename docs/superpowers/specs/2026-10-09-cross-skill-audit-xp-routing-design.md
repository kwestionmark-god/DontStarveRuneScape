# Cross-skill audit — mechanical XP fixes (slice A) — design

Date: 2026-10-09
Status: Draft
Slice line: worker gather XP trains the skill the node actually belongs to;
deadlock remedies land in slice B of the same arc.

## Design law

XP is never mislabeled: the skill a node trains must be derivable from the
node's own data (tool + yield category) and must mirror what the PLAYER
training the same node would receive. The audit's job is to make routing
boring and test-pinned.

## Goal (user-approved, clarify round 2026-10-09)

1. Workers chopping axe-gated trees (oak, birch, maple, pine, spruce,
   willow, dead_tree, elder_wood) train **woodcutting**, not mining.
2. Workers harvesting bucket-gated water_source nodes train **foraging**,
   mirroring the player path (InteractSystem's tool switch has no bucket
   case → player water gathering is Foraging).
3. No behavior change on the player path, on rod nodes (already fixed in
   the fishing slice), or on pickaxe nodes (mining stays correct).

## Non-goals (this slice)

- The "combat" → "attack" skill_xp_rewards fix, the AddXp Level-recompute
  fix, and the grass_rope duplicate-key cleanup — flagged in the clarify
  round, NOT approved for this slice. They remain live findings for the
  audit backlog (slice B decisions).
- Planks un-gating, the low-tier tree_sap recipe, herb_gathering NPC
  attach, and the progression-reachability audit test — slice B.
- Changing any XP amounts or success math.

## Ground — verified in the tree

- `GatherSkillFor` (NPC/RecruitmentSystem.cs:724-733) currently:
  `def.ToolRequirement == "fishing_rod" → "fishing"`, then
  `def.RequiresTool → "mining"` (catches axe trees AND bucket water
  BEFORE the wood check), then a wood-yield check matching only literal
  `"wood" or "log" or "logs"` — which NO yield item is named (real ids:
  oak_logs, birch_logs, driftwood, charcoal). The woodcutting branch is
  dead code; all 8 axe tree nodes train workers' mining.
- The player path routes by tool string, not by RequiresTool
  (Interactions/InteractSystem.cs:121-127): axe → Woodcutting,
  pickaxe → Mining, fishing_rod → Fishing, `_` (incl. bucket) → Foraging.
  Player and worker routing DISAGREE on axe trees and water.
- `ResourceDef.RequiresTool => !string.IsNullOrEmpty(ToolRequirement)`
  (Data/Resource.cs:76); tool strings in resources.json: "axe" (8 trees),
  "pickaxe" (12 ore/stone nodes), "fishing_rod" (fish_spot),
  "bucket" (water_source).
- Worker XP lands at RecruitmentSystem.cs:693-697
  (`workerRecruit.Skills.AddXpWithNotification(GatherSkillFor(...), xp)`);
  the colony-skill gate reads the same function (RecruitmentSystem.cs:505),
  so the fix also corrects WHICH skill the node-gate checks (today a
  level-1 worker passes an oak tree's gate because it checks foraging-
  irrelevant mining… actually checks mining 1 ≥ 1 — the gate itself was
  reading the wrong skill for tree nodes).
- Cave mining XP (RecruitmentSystem.cs:688) is a separate raw-AddXp line
  keyed on `resource.RequiresTool` in caves — pickaxe-only in practice
  (cave nodes are ore), unaffected by this fix.
- Data-count pins: no monsters/items/quests/recipes count changes in this
  slice — GatherSkillFor is pure code, no def rows added or removed.
  (NpcDataTests pins nothing about GatherSkillFor.)
- Precedent: the fishing slice's GatherSkillFor branch order is itself a
  fixed instance of this bug class (its test lives at
  FishingTests.cs:371-385 and pins the fishing branch before the tool
  check). The tree fix extends the same law to axes.

## Design

Rewrite `GatherSkillFor` to mirror the player's tool switch exactly:

```csharp
private static string GatherSkillFor(ResourceDef? def)
{
    if (def == null) return "foraging";
    return def.ToolRequirement switch
    {
        "axe" => "woodcutting",
        "pickaxe" => "mining",
        "fishing_rod" => "fishing",
        _ => "foraging",           // bucket water + all no-tool nodes
    };
}
```

- Axe trees: woodcutting (matches player). Pickaxe: mining (unchanged).
  Rod: fishing (unchanged — branch order inside a switch no longer
  matters, but the FishingTests pin keeps guarding it). Bucket +
  no-tool: foraging (driftwood, charcoal-yielding dead trees stay
  foraging, matching the player path verbatim).
- The dead literal-yield wood check is deleted. If a future no-tool wood
  node should train woodcutting, it gets a tool string or its own branch
  WITH its consumer/test in the same slice (stat-menus law analog).
- The colony node-gate (RecruitmentSystem.cs:503-509) needs no separate
  change — it calls GatherSkillFor, so it now gates trees against the
  worker's woodcutting and water against foraging automatically. This is
  the same skill the node trains, which is the reachability law: a node
  gated on a skill must be reachable by XP earned in that skill from
  entry-level nodes of the same family.

## Testing plan

New test file `GatherSkillRoutingTests.cs` (pure, no harness needed for
the routing assertions; harness shape from ColonySkillGateTests for the
end-to-end ones):

1. Routing table (pure): for every resource def in resources.json,
   GatherSkillFor mirrors the player's tool switch — axe nodes →
   woodcutting, pickaxe → mining, rod → fishing, everything else →
   foraging. Data-driven loop over the real registry rows (raw-row seam:
   DataLoader + Bootstrap's inline conversion), so new defs are covered
   the day they land.
2. Worker chop trains woodcutting (end-to-end): harness worker adjacent
   to an oak tree with an axe in the colony store; after harvest ticks,
   the worker's woodcutting XP rises and mining XP stays 0 (mirror of
   FishingTests' worker-fisher shape).
3. Worker water haul trains foraging: bucket-gated water_source, same
   shape, assert foraging XP > 0, mining == 0.
4. Regression guards: pickaxe node still trains mining (cave-shape test
   exists — pin the surface stone_quarry too); the fishing branch pin at
   FishingTests stays green untouched.
5. Colony gate follow: a level-1 worker is NOT above-gate-blocked from an
   oak tree (required_level 1) but IS blocked from a maple_tree
   (required_level 20) — both via the woodcutting skill the tree now
   trains (the gate reads the right skill after the fix).

## Open questions

- None blocking this slice. Slice B open items (not approved here):
  combat→attack quest rewards, AddXp Level recompute, grass_rope dup key,
  planks un-gate, low-tier sap recipe, herb_gathering attach,
  reachability audit test.

## Revision history

- 2026-10-09: drafted from the cross-skill audit clarify round. Audit
  findings ledger (verified in-tree): worker tree-chops train mining
  (GatherSkillFor order), 3 quests award nonexistent "combat" skill
  (170 XP vanishes), raw AddXp never raises Level (construction is the
  acute case), planks quest-deadlock roots rod/bucket/full_belly,
  first_flame needs tree_sap gated behind maple@wc20, herb_gathering
  offered by nobody, grass_rope duplicate xp_reward key.
