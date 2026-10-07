# Starting Companion — design

Date: 2026-10-07
Status: draft
Slice: post-roadmap frontier, item 5 (dev-map "Ordered next work" item 6)

## Design law

RS pillar: a companion grows with you — joining as a "companion" recruit
trains the same per-recruit skills slice 1 introduced, sharing the
player's work so the bond is mechanical, not cosmetic.
DS pillar: the companion is a liability you invest in — it needs food,
it tires, it can die — but a loyal one pays off the early grind.
MC pillar: no new systems — the recruit machinery, the per-recruit
skill stats (slice 1), the worker-schedules idle/fallback paths, and the
colony snapshot persistence are all already live; this slice is one new
recruit behavior on top of them.

## Goal

Players near the forest edge meet Mara, a starting companion, early in
the game. Speaking to her offers "Come with me": she becomes a recruited
NPC whose RecruitBehavior is the new "companion". A companion:

- Follows the player at walking pace (never teleports), staying within a
  tether distance; if the player outruns the tether (sprint beyond ~10
  tiles for several seconds), she gives up and stands, marking herself
  "too far" — she does not rubber-band across the map.
- Works alongside the player at whatever the player is doing: standing
  near the player while the player chops a tree, she watches; if the
  player assigns her a job through the normal colony tagging, she
  switches to assistant or guard at the player's hand (existing
  behavior-assign flow unchanged).
- Carries nothing for the colony stockpile (companions never haul); in
  combat she backs away from hostiles instead of engaging (she is a
  nervous survivor, not a trained guard — guards exist).
- Persists through the existing NPCDataSnapshot save path (the
  companion is a RecruitNpc — slice 1's Skills snapshot applies; her
  companion state rides IsRecruited + RecruitBehavior).

## Non-goals

- Companion-specific dialogue trees or friendship meter (slice is
  behavior only; the existing dialogue lines from quest_giver/recruit
  defs ride the existing talk flow).
- Combat companion (attacking alongside the player) — the nervous
  survivor retreats; combat followers are a later slice.
- Companion skill-gating for jobs (assist at level 1 regardless).
- Multiple companions — one is the slice; the system doesn't forbid a
  second companion found later, but settlement and follow logic both
  treat "the companion" as at-most-one at a time through the simple
  convention that the player can only have one RecruitBehavior ==
  "companion" NPC following them.

## Design

### The companion recruit def

New NPC def `companion_mara` (npcs.json):

- npc_type: "recruit" (existing class; slice 1's SkillManager applies)
- name: "Hunter Mara, freshly cast out"
- Faction: forest_villagers
- AvailableBehaviors: ["companion", "assistant", "guard"] — the
  dashboard "assign job" flow already reads this list; companion stays
  opt-out-able any time through the same flow.
- Health 18, max_health 18 (frailer than the forest scout at 25).
- Spawn position: fixed at the forest's southern edge (the player's
  likely early wander), not random.
- Requirement gates zeroed (recruit_commerce_requirement 0,
  recruit_persuasion_requirement 0, recruit_composite_stat 0) — a
  starting companion must not need stats the player hasn't built yet.
- recruit_sprite_key: "recruit" (existing placeholder art).

### The follow behavior

New behavior branch in RecruitmentSystem.Tick matching
RecruitBehavior == "companion" (sibling to the existing "guard" early
branch):

1. Work the player is doing takes priority: if the player stands in a
   gather work area with a visible nearby resource node, the companion
   idle-watches at 2 tiles from the player (no work, no haul — she's
   moral support, not labor). If the player is walking, she follows at
   WalkingSpeed.
2. Follow: compute the player's tile; path to it through the existing
   MoveAlongPath; if she enters the colony's task-board range and a
   gather reservation is needed, she does NOT claim one (the player
   moves too fluidly for the board to keep up; companion follow
   bypasses the task board on the walk).
3. Tether: if the player-to-companion distance exceeds 14 tiles for
   more than 2 seconds of sim time, the companion stops and idles,
   setting a "Left behind" status; the player must return within 7
   tiles for her to resume following. No teleport catch-up.
4. Combat response: if an aggressive monster enters 4 tiles, the
   companion flees directly away from the hostiles at WalkSpeed + 6f
   (a frightened burst, slightly faster than her walk). If the player
   fights, she posts up 8 tiles back and watches.
5. Needs: ColonyHunger/ColonyRest drain and threshold behaviors reuse
   the existing TickColonyNeeds/TickResidentRest without change — she
   needs a bedroll and food like any colonist.

### Persistence

No new fields. The companion saves through the same NPCs snapshot as
every recruit: IsRecruited + RecruitBehavior "companion" capture her
identity; slice 1's Skills round-trip covers her levels. On load, a
"companion" behavior recruits re-enters the follow branch without a
migration step.

## Testing plan

New file `StartingCompanionTests.cs` in the established Harness shape
(ColonyWorkerBrainTests pattern):

1. **Follow**: place Mara 5 tiles east of the player, tick until she's
   within 2 tiles; repeat with the player walking west on the harness
   (fake velocity + position) and assert the gap stays under 8 tiles.
2. **Tether release**: teleport the player 20 tiles away over 5 ticks;
   the companion stops, status reads "Left behind"; walking back inside
   7 tiles resumes follow.
3. **Flee**: spawn a wolf 3 tiles from the pair; the companion's
   velocity points away from the wolf within 2 ticks, and she stays
   8 tiles from the player (not cowering on top of them).
4. **Needs integration**: mara.ColonyHunger = 0 drops her into the
   existing hungry-eat path — food drains from the colony stockpile
   identically to other recruits.
5. **Persistence**: GetSnapshot + RestoreSnapshot round-trip,
   RecruitBehavior "companion" preserved, skills intact (slice-1 hook).

## Open questions

Answered with defaults per standing delegation:

- Companion XP: shares per-recruit slice — follows the player but does
  not gather on her own (no XP unless the player assigns her a
  real recruit job), keeping her progression tied to the player's
  choices.
- Tether distance 14 tiles, give-up 2 seconds of sim: tuned to match
  the nominal WalkSpeed-vs-sprint rates so Sprint still outruns her
  but walking doesn't.
- Mara's loot table: none — she's a survivor, not a pack mule. No
  follower carry mechanic this slice.
