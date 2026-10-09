namespace DontStarveRuneScape.NPC;

using DontStarveRuneScape.Config;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.World;
using DontStarveRuneScape.Building;
using DontStarveRuneScape.Crafting;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.Skills;
using DontStarveRuneScape.Combat;
using DontStarveRuneScape.Survival;

/// <summary>
/// RecruitmentSystem — Handles recruited workers and their colony jobs.
/// </summary>
public sealed class RecruitmentSystem
{
    private const float WalkSpeed = 42f;
    private const float HarvestInterval = 4f;
    private const float ProductionInterval = 5f;
    private const int DefaultWorkRadiusTiles = 10;
    private readonly Dictionary<string, float> _workTimers = [];
    private readonly Dictionary<string, (int X, int Y)> _targets = [];
    private readonly Dictionary<string, (int X, int Y)> _homes = [];
    private readonly Dictionary<string, WorkerPathState> _paths = [];
    private readonly Dictionary<string, (Structure Structure, (int X, int Y)? Tile)> _approaches = [];
    private readonly HashSet<(int X, int Y)> _unreachableResourceTiles = [];
    private TileMap? _pathCacheWorld;
    private float _unreachableRetryTimer;
    private readonly Dictionary<string, int> _patrolPoints = [];
    private readonly Dictionary<string, float> _patrolTimers = [];
    // Worker-schedules slice: guard-shift parity (recruit order alternates day/
    // night) and per-NPC free-time wander targets with a short repick timer.
    private readonly Dictionary<string, bool> _nightShiftGuards = [];
    private readonly Dictionary<string, ((int X, int Y)? Tile, float RepickTimer)> _wanderTargets = [];
    // Starting-companion slice: the "companion" behavior engine — one bonded
    // follower per player, follow/tether/flee steering.
    private CompanionBehavior? _companion;
    private static readonly (int X, int Y)[] PatrolOffsets = [(-3, 0), (0, -3), (3, 0), (0, 3)];

    private sealed class WorkerPathState
    {
        public TileMap World { get; init; } = null!;
        public int TargetX { get; init; }
        public int TargetY { get; init; }
        public List<(int X, int Y)>? Nodes { get; init; }
        public int NextNode { get; set; }
    }

    /// <summary>
    /// Run one worker simulation step. Assistants tend compatible staffed
    /// workstations first, then gather tool-free resources in their work area.
    /// Other recruit behaviors remain under their own AI.
    /// </summary>
    public void Tick(float dt, NPCSystem? npcSystem = null, Player? player = null,
        TileMap? world = null, ColonySystem? colony = null,
        BuildingSystem? buildings = null, CraftingSystem? crafting = null,
        SkillManager? skills = null, CombatSystem? combat = null, FoodRegistry? foods = null,
        FactionRegistry? factions = null, FactionSystem? factionSystem = null,
        DontStarveRuneScape.Seasons.WeatherSystem? weather = null,
        DontStarveRuneScape.Skills.Firemaking.FiremakingSkill? firemaking = null,
        DontStarveRuneScape.World.DayNightCycle? clock = null)
    {
        if (dt <= 0f || npcSystem == null || player?.Inventory == null || world == null)
            return;

        // Companion engine binds to this player; a fresh player instance
        // (new game) starts a fresh bond.
        if (_companion == null || !_companion.BoundTo(player))
            _companion = new CompanionBehavior(player);

        if (colony?.IsFounded == true && foods != null)
            TickColonyNeeds(dt, npcSystem, colony, foods);
        bool inCave = world.IsCave;
        if (!ReferenceEquals(_pathCacheWorld, world))
        {
            _pathCacheWorld = world;
            _unreachableResourceTiles.Clear();
            _targets.Clear();
            _homes.Clear();
            _paths.Clear();
            _approaches.Clear();
            // Stale surface reservations must not survive a map switch.
            colony?.TaskBoard.Clear();
        }
        _unreachableRetryTimer += Math.Min(dt, 0.25f);
        if (_unreachableRetryTimer >= 30f)
        {
            _unreachableRetryTimer = 0f;
            _unreachableResourceTiles.Clear();
        }

        var claimed = new HashSet<(int X, int Y)>();
        var claimedWorkplaces = new HashSet<Structure>();
        var liveIds = new HashSet<string>();
        if (colony?.IsFounded == true && buildings != null)
        {
            foreach (var structure in buildings.Structures)
            {
                if (!structure.IsActive || structure.AssignedNpcId == null) continue;
                var assigned = npcSystem.NPCs.FirstOrDefault(npc => npc.NpcId == structure.AssignedNpcId);
                if (assigned == null || !assigned.IsActive || !assigned.IsRecruited)
                {
                    structure.AssignedNpcId = null;
                    structure.WorkStatus = structure.IsUnderConstruction ? "Awaiting worker"
                        : structure.WorkRecipeId == null ? "Idle" : "Waiting for worker";
                }
            }
            if (crafting != null && skills != null)
                PlanRecipeDependencies(colony, buildings, crafting, skills);
        }

        foreach (var npc in npcSystem.NPCs)
        {
            if (!npc.IsActive || !npc.IsRecruited
                || npc.RecruitBehavior is not ("assistant" or "guard" or "companion"))
                continue;

            liveIds.Add(npc.NpcId);
            _workTimers.TryGetValue(npc.NpcId, out float timer);
            timer += Math.Min(dt, 0.25f);
            _workTimers[npc.NpcId] = timer;

            int workerX = (int)(npc.WorldX / Constants.TileSize);
            int workerY = (int)(npc.WorldY / Constants.TileSize);
            (int X, int Y) home;
            if (inCave)
                home = (world.SpawnX, world.SpawnY);
            else if (colony?.IsFounded == true)
                home = (colony.AnchorTileX, colony.AnchorTileY);
            else if (_homes.TryGetValue(npc.NpcId, out var savedHome))
                home = savedHome;
            else
                home = (workerX, workerY);
            _homes[npc.NpcId] = home;

            // ── Worker-schedules slice ──────────────────────────────────────
            // Resolve the current schedule category. Null clock → Work (today's
            // behaviour). Guard shift alternates by recruit order: first guard
            // is Daytime, second Nighttime, third Daytime…
            ScheduleCategory? category = null;
            if (clock != null && npc.RecruitBehavior != "companion")
            {
                float hour = clock.HourOfDay;
                if (npc.RecruitBehavior == "guard")
                {
                    if (!_nightShiftGuards.TryGetValue(npc.NpcId, out bool night))
                    {
                        int guardIndex = _nightShiftGuards.Count;
                        night = guardIndex % 2 == 1;
                        _nightShiftGuards[npc.NpcId] = night;
                    }
                    var sched = ScheduleTemplates.For("guard",
                        night ? WorkShift.Nighttime : WorkShift.Daytime);
                    category = sched?.SlotFor(hour) ?? ScheduleCategory.MilitaryDuty;
                }
                else // assistant
                {
                    var sched = ScheduleTemplates.For("assistant", null);
                    category = sched?.SlotFor(hour) ?? ScheduleCategory.Work;
                }
            }

            // Bedtime: SLEEP slots take rest even at full rest meter (the
            // urgent-threat gate inside TickResidentRest still aborts).
            bool bedtime = category == ScheduleCategory.Sleep;
            // Casual meals: during a NOURISHMENT slot eat at 45 hunger instead
            // of the urgent 20 threshold handled in TickColonyNeeds.
            bool casualMeal = category == ScheduleCategory.Nourishment
                              && npc.ColonyHunger < WorkerSchedule.CasualHungerThreshold
                              && npc.ColonyHunger > 0f;

            if (casualMeal && colony?.IsFounded == true && foods != null)
            {
                var meal = foods.All()
                    .Where(food => food.HungerRestoration > 0 && colony.GetItemQuantity(food.ItemId) > 0)
                    .OrderBy(food => food.IsRaw)
                    .ThenByDescending(food => food.HungerRestoration)
                    .FirstOrDefault();
                if (meal != null && colony.RemoveItem(meal.ItemId, 1))
                {
                    npc.ColonyHunger = Math.Min(100f, npc.ColonyHunger + meal.HungerRestoration);
                    npc.ColonyNeedStatus = npc.ColonyHunger <= 15f ? "Starving"
                        : npc.ColonyHunger <= 35f ? "Hungry" : "Fed";
                }
            }

            if (TickResidentRest(npc, home, dt, world, colony, buildings, combat, weather, firemaking, bedtime))
            {
                // Resting releases the worker's claims so others can take
                // them; carried goods stay with the worker.
                colony?.TaskBoard.ReleaseAllFor(npc.NpcId);
                continue;
            }
            if (npc.RecruitBehavior == "guard")
            {
                TickGuard(npc, home, dt, world, colony, combat, player, skills, factions, factionSystem);
                continue;
            }
            // Companion: follow/tether/flee steering — a full behavior, not a
            // scheduled job. Needs (hunger/rest) already ran above via
            // TickColonyNeeds / TickResidentRest like every other colonist;
            // companions bypass the schedule templates and the task board.
            if (npc.RecruitBehavior == "companion")
            {
                if (npc is RecruitNpc companion)
                {
                    _companion.Combat = combat;
                    _companion.World = world;
                    _companion.Register(companion);
                    _companion.Tick(dt, companion);
                }
                continue;
            }
            bool needsFood = colony?.IsFounded == true && npc.ColonyHunger <= 0f;
            // Night floor: during a Work slot at night (22:00–05:00), outdoor
            // worker dispatch is refused; the worker wanders near the anchor
            // or sleeps instead.
            bool nightFloor = category == ScheduleCategory.Work
                              && clock != null && clock.IsNight;
            if (nightFloor)
            {
                // Refuse outdoor dispatch. Fall back to free-time wander near
                // the anchor (or rest if tired — handled above).
                TickWander(npc, home, dt, world, colony);
                continue;
            }
            // FREE_TIME slots (the short evening at camp): no production,
            // gather, or construction dispatch — wander and socialize.
            if (category == ScheduleCategory.FreeTime)
            {
                TickWander(npc, home, dt, world, colony);
                continue;
            }
            int workRadius = inCave ? Math.Max(world.Width, world.Height)
                : colony?.IsFounded == true
                ? colony.WorkRadiusTiles
                : DefaultWorkRadiusTiles;
            int workRadiusSq = workRadius * workRadius;

            var workplace = inCave || needsFood ? null
                : FindWorkplace(npc, home, workRadiusSq, colony, buildings, crafting, skills);
            var missingInputs = needsFood ? null : GetMissingWorkInputs(npc, workplace, colony, buildings, crafting);
            if (!needsFood && missingInputs == null)
                workplace ??= FindGardenPlot(npc, home, workRadiusSq, colony, buildings);
            Structure? constructionSite = workplace == null && !inCave && !needsFood
                ? FindConstructionSite(npc, home, workRadiusSq, colony, buildings)
                : null;
            if (constructionSite != null && colony != null)
            {
                constructionSite.AssignedNpcId = npc.NpcId;
                missingInputs = GetMissingConstructionInputs(constructionSite, colony, buildings);
                if (missingInputs.Count == 0 && !constructionSite.ConstructionMaterialsPaid)
                {
                    // An in-progress upgrade charges the SUCCESSOR tier's
                    // materials; a blueprint charges its own def's materials.
                    var materialSource = constructionSite.UpgradingToId != null
                        ? buildings!.Registry!.GetStructure(constructionSite.UpgradingToId)!.Materials
                        : constructionSite.StructureDef.Materials;
                    foreach (var material in materialSource
                                 .GroupBy(row => row.ItemId, StringComparer.Ordinal))
                        if (!colony.RemoveItem(material.Key, material.Sum(row => row.Quantity)))
                            throw new InvalidOperationException("Construction materials changed during job start.");
                    constructionSite.ConstructionMaterialsPaid = true;
                }
                constructionSite.WorkStatus = !constructionSite.ConstructionMaterialsPaid
                    ? "Waiting for materials"
                    : constructionSite.UpgradingToId != null ? "Upgrading (builder en route)"
                    : "Builder en route";
            }
            if (constructionSite?.ConstructionMaterialsPaid == true && colony != null)
            {
                var approach = FindWorkplaceApproach(world, constructionSite, npc);
                if (approach == null)
                {
                    constructionSite.WorkStatus = "No reachable construction approach";
                    continue;
                }
                float goalX = (approach.Value.X + 0.5f) * Constants.TileSize;
                float goalY = (approach.Value.Y + 0.5f) * Constants.TileSize;
                float dx = goalX - npc.WorldX;
                float dy = goalY - npc.WorldY;
                if (dx * dx + dy * dy > 16f)
                {
                    MoveAlongPath(npc, world, approach.Value.X, approach.Value.Y, dt, WalkSpeed);
                    constructionSite.WorkStatus = "Builder en route";
                    continue;
                }
                npc.VelocityX = npc.VelocityY = 0f;
                constructionSite.WorkProgress += Math.Min(dt, 0.25f);
                bool upgrading = constructionSite.UpgradingToId != null;
                constructionSite.WorkStatus = upgrading
                    ? $"Upgrading ({Math.Min(99, (int)(constructionSite.WorkProgress / 20f * 100f))}%)"
                    : $"Building ({Math.Min(99, (int)(constructionSite.WorkProgress / 20f * 100f))}%)";
                if (constructionSite.WorkProgress >= 20f)
                {
                    if (upgrading && buildings?.Registry?.GetStructure(constructionSite.UpgradingToId) is { } successor)
                    {
                        // Swap the placed structure to the successor tier in
                        // place: id, def, HP, and tile occupancy follow the new
                        // def; position is preserved.
                        constructionSite.StructureId = successor.Id;
                        constructionSite.StructureDef = successor;
                        constructionSite.Health = (int)successor.Hp;
                        constructionSite.MaxHealth = (int)successor.Hp;
                        constructionSite.UpgradingToId = null;
                        constructionSite.IsUnderConstruction = false;
                        constructionSite.ConstructionMaterialsPaid = false;
                        constructionSite.WorkProgress = 0f;
                        constructionSite.AssignedNpcId = null;
                        var upgradedTile = world.GetTile(constructionSite.TileX, constructionSite.TileY);
                        if (upgradedTile != null)
                            upgradedTile.Structure = successor.OccupiesTile ? successor : null;
                        constructionSite.WorkStatus = $"Upgraded to {successor.Name}";
                    }
                    else
                    {
                        constructionSite.IsUnderConstruction = false;
                        constructionSite.ConstructionMaterialsPaid = false;
                        constructionSite.WorkProgress = 0f;
                        constructionSite.AssignedNpcId = null;
                        constructionSite.WorkStatus = "Construction complete";
                    }
                    skills?.AddXp("construction", 10f);
                    // Per-recruit XP: the builder who finished the site
                    // trains construction alongside the player.
                    if (npc is RecruitNpc builderRecruit)
                        builderRecruit.Skills.AddXpWithNotification("construction", 10f);
                }
                continue;
            }
            if (workplace != null && claimedWorkplaces.Add(workplace))
            {
                // ── Colony skill gate: the station's def must be placeable
                // by the player; above-tier stations read visibly and keep
                // the worker off the job. Rechecked each tick, so a level-up
                // unblocks the next scan. ─────────────────────────────────
                if (skills != null
                    && skills.GetSkillLevel("construction") < workplace.StructureDef.RequiresSkillLevel)
                {
                    workplace.WorkStatus = "Awaiting builder competence";
                    continue; // don't bind, don't dispatch
                }
                _targets.Remove(npc.NpcId);
                workplace.AssignedNpcId = npc.NpcId;

                // ── Danger feedback (phase-5 blocked-work: danger) ──────────
                // A hostile near the claimed workplace pauses the job: the
                // worker backs off toward home and the structure shows why.
                // Guards are unaffected (they engage threats elsewhere).
                if (HasHostileNear(workplace, npc, combat))
                {
                    workplace.WorkStatus = "Danger nearby — work paused";
                    if (colony?.IsFounded == true)
                    {
                        var homeX = (home.X + 0.5f) * Constants.TileSize;
                        var homeY = (home.Y + 0.5f) * Constants.TileSize;
                        float awayX = homeX - npc.WorldX;
                        float awayY = homeY - npc.WorldY;
                        float away = MathF.Sqrt(awayX * awayX + awayY * awayY);
                        if (away > 1f)
                            MoveAlongPath(npc, world, home.X, home.Y, dt, WalkSpeed);
                        else
                            npc.VelocityX = npc.VelocityY = 0f;
                    }
                    else
                        npc.VelocityX = npc.VelocityY = 0f;
                    continue;
                }

                var approach = FindWorkplaceApproach(world, workplace, npc);
                if (approach == null)
                {
                    workplace.WorkStatus = "No reachable approach";
                    continue;
                }
                float stationX = (approach.Value.X + 0.5f) * Constants.TileSize;
                float stationY = (approach.Value.Y + 0.5f) * Constants.TileSize;
                float stationMoveX = stationX - npc.WorldX;
                float stationMoveY = stationY - npc.WorldY;
                float stationDistance = MathF.Sqrt(stationMoveX * stationMoveX + stationMoveY * stationMoveY);
                if (stationDistance > 4f)
                {
                    MoveAlongPath(npc, world, approach.Value.X, approach.Value.Y, dt, WalkSpeed);
                    workplace.WorkStatus = "Worker en route";
                    continue;
                }

                npc.VelocityX = npc.VelocityY = 0f;
                _workTimers[npc.NpcId] = 0f;
                bool gatherInputs = false;
                if (workplace.StructureId == "garden_plot" && colony != null)
                {
                    TickGardenPlot(workplace, colony, world.SeasonSystem?.CurrentSeason ?? "spring", dt);
                    continue;
                }
                else if (workplace.WorkRecipeId is { } recipeId && crafting != null && skills != null
                    && colony != null)
                {
                    var recipe = crafting.Registry?.GetRecipe(recipeId);
                    if (recipe == null)
                    {
                        workplace.WorkStatus = "Missing recipe";
                    }
                    else if (!HasInputs(colony, recipe))
                    {
                        workplace.WorkStatus = "Waiting for materials";
                        gatherInputs = true;
                    }
                    else if (!CanFitCraftOutput(colony, recipe))
                    {
                        workplace.WorkStatus = "Waiting for stockpile space";
                    }
                    else
                    {
                        workplace.WorkProgress += Math.Min(dt, 0.25f);
                        workplace.WorkStatus = $"Working: {recipe.Name}";
                        if (workplace.WorkProgress >= ProductionInterval)
                        {
                            var result = crafting.Craft(recipeId, colony, skills,
                                new HashSet<string>(StringComparer.Ordinal) { workplace.StructureId });
                            workplace.WorkStatus = result.Success ? $"Produced {recipe.OutputItem}" : result.Message;
                            workplace.WorkProgress = 0f;
                            if (result.Success && workplace.HasManualWorkOrder)
                            {
                                if (workplace.WorkRecipeQueue.Count > 0)
                                {
                                    workplace.WorkRecipeId = workplace.WorkRecipeQueue[0];
                                    workplace.WorkRecipeQueue.RemoveAt(0);
                                    workplace.WorkStatus = "Order complete; next queued";
                                }
                                else
                                {
                                    workplace.WorkRecipeId = null;
                                    workplace.WorkOrdersPaused = true;
                                    workplace.AssignedNpcId = null;
                                    workplace.IsDependencyOrder = false;
                                    workplace.WorkStatus = "Orders complete";
                                }
                            }
                        }
                    }
                }
                if (!gatherInputs) continue;
            }

            // Hauling: a surface-colony worker carrying goods walks them to
            // the stockpile before starting any new work. This one phase
            // serves gatherers, recipe-input runs, and construction supply.
            bool haulsHome = !inCave && colony?.IsFounded == true && npc.CarriedQuantity > 0;
            if (haulsHome)
            {
                TickDepositCarried(npc, colony!, world, dt);
                continue;
            }

            (int X, int Y)? target = null;
            if (_targets.TryGetValue(npc.NpcId, out var current))
            {
                var node = world.GetTile(current.X, current.Y)?.ResourceNode;
                int homeDx = current.X - home.X;
                int homeDy = current.Y - home.Y;
                bool inWorkArea = homeDx * homeDx + homeDy * homeDy <= workRadiusSq;
                bool inSeason = true;
                if (node?.ResourceDef is { } targetDef && targetDef.Seasons.Length > 0
                    && world.SeasonSystem != null)
                    inSeason = targetDef.Seasons.Contains(world.SeasonSystem.CurrentSeason);
                bool edible = !needsFood || (node != null && foods?.Get(node.YieldItem) != null);
                bool neededInput = missingInputs == null || node != null && missingInputs.Contains(node.YieldItem);
                bool reservationHeld = ReservationHeldByWorker(npc, colony, inCave, current.X, current.Y);
                if (inWorkArea && inSeason && edible && WorkerPathfinder.CanStand(world, current.X, current.Y)
                    && !_unreachableResourceTiles.Contains(current)
                    && node != null && !node.IsDepleted
                    && neededInput && CanWorkerHarvest(node, player.Inventory, colony, inCave)
                    && reservationHeld
                    && !claimed.Contains(current))
                    target = current;
                else if (!reservationHeld)
                {
                    // The claim expired (rest interruption, world switch) —
                    // the worker re-selects and re-claims through the board.
                    _targets.Remove(npc.NpcId);
                    ReleaseGatherReservation(colony, npc, current.X, current.Y);
                }
            }

            if (!target.HasValue)
            {
                float bestDistanceSq = float.MaxValue;
                foreach (var (tile, node) in world.GetResourceNodesInRadius(home.X, home.Y, workRadius))
                {
                    if (claimed.Contains((tile.X, tile.Y)) || node.IsDepleted
                        || !CanWorkerHarvest(node, player.Inventory, colony, inCave))
                        continue;
                    // ── Colony skill gate: the recruit's own gathering
                    // level must cover the node's required_level. ────────
                    int nodeGate = Math.Max(1, node.ResourceDef?.RequiredLevel ?? 1);
                    if (npc is RecruitNpc gatherRecruit
                        && gatherRecruit.Skills.GetSkillLevel(GatherSkillFor(node.ResourceDef)) < nodeGate)
                    {
                        continue; // above-level nodes are not claimed; the
                                  // worker falls through to idle/wander
                    }
                    if (colony?.IsFounded == true && !inCave
                        && colony.TaskBoard.IsTileReserved(tile.X, tile.Y)
                        && !ReservationHeldByWorker(npc, colony, inCave, tile.X, tile.Y))
                        continue;
                    if (!WorkerPathfinder.CanStand(world, tile.X, tile.Y)) continue;
                    if (_unreachableResourceTiles.Contains((tile.X, tile.Y))) continue;
                    if (needsFood && foods?.Get(node.YieldItem) == null)
                        continue;
                    if (missingInputs != null && !missingInputs.Contains(node.YieldItem))
                        continue;
                    if (node.ResourceDef?.Seasons is { Length: > 0 } seasons
                        && world.SeasonSystem != null
                        && !seasons.Contains(world.SeasonSystem.CurrentSeason))
                        continue;
                    bool hasStorage = colony?.IsFounded == true
                        ? colony.CanStore(node.YieldItem, node.YieldQuantity)
                        : player.Inventory.CanAdd(node.YieldItem, node.YieldQuantity);
                    if (node.YieldItem.Length == 0 || !hasStorage)
                        continue;

                    int homeDx = tile.X - home.X;
                    int homeDy = tile.Y - home.Y;
                    if (homeDx * homeDx + homeDy * homeDy > workRadiusSq)
                        continue;
                    int dx = tile.X - workerX;
                    int dy = tile.Y - workerY;
                    float distanceSq = dx * dx + dy * dy;
                    if (distanceSq >= bestDistanceSq)
                        continue;
                    bestDistanceSq = distanceSq;
                    target = (tile.X, tile.Y);
                }
            }

            if (!target.HasValue)
            {
                _targets.Remove(npc.NpcId);
                // No gatherable work this tick — fall back to an idle wander
                // near the anchor instead of freezing in place. The worker
                // stays readable ("Idle") and re-scans for work next tick.
                if (colony?.IsFounded == true)
                {
                    TickWander(npc, home, dt, world, colony);
                    npc.CarryStatus = "Idle";
                }
                else
                {
                    npc.VelocityX = npc.VelocityY = 0f;
                }
                if (needsFood)
                {
                    var hungryStation = buildings?.Structures.FirstOrDefault(s => s.AssignedNpcId == npc.NpcId);
                    if (hungryStation != null) hungryStation.WorkStatus = "No food nearby";
                }
                continue;
            }

            var targetTile = target.Value;
            claimed.Add(targetTile);
            _targets[npc.NpcId] = targetTile;
            // Surface-colony gathers go through the task board: the claim is
            // synchronous, so two workers can never hold the same node.
            if (colony?.IsFounded == true && !inCave)
            {
                string yieldItem = world.GetTile(targetTile.X, targetTile.Y)?.ResourceNode?.YieldItem ?? "";
                if (colony.TaskBoard.ClaimGather(npc.NpcId, targetTile.X, targetTile.Y, yieldItem) == null)
                {
                    _targets.Remove(npc.NpcId);
                    continue;
                }
            }
            if (!MoveAlongPath(npc, world, targetTile.X, targetTile.Y, dt, WalkSpeed))
            {
                if (_paths.TryGetValue(npc.NpcId, out var blockedPath) && blockedPath.Nodes == null)
                {
                    _unreachableResourceTiles.Add(targetTile);
                    _targets.Remove(npc.NpcId);
                    ReleaseGatherReservation(colony, npc, targetTile.X, targetTile.Y);
                }
                continue;
            }

            npc.VelocityX = npc.VelocityY = 0f;
            if (timer < HarvestInterval) continue;
            _workTimers[npc.NpcId] = timer - HarvestInterval;

            var resource = world.GetTile(targetTile.X, targetTile.Y)?.ResourceNode;
            bool canReceive = resource != null && (colony?.IsFounded == true
                ? colony.CanStore(resource.YieldItem, resource.YieldQuantity)
                : player.Inventory.CanAdd(resource.YieldItem, resource.YieldQuantity));
            if (resource == null || resource.IsDepleted
                || !CanWorkerHarvest(resource, player.Inventory, colony, inCave) || !canReceive)
            {
                _targets.Remove(npc.NpcId);
                continue;
            }

            var (itemId, quantity, xp) = resource.Harvest(1f, world.SeasonSystem);
            // Per-recruit yield feedback: a gatherer whose skill level is L
            // yields one doubled harvest every max(1, 20-2*L) intervals,
            // deterministic per recruit (counter, no RNG). This is the
            // recruit-side mirror of the player's gathering sub-stats.
            if (quantity > 0 && npc is RecruitNpc yieldRecruit)
            {
                string gatherSkill = GatherSkillFor(resource.ResourceDef);
                int level = yieldRecruit.Skills.GetSkillLevel(gatherSkill);
                yieldRecruit.GatherIntervalsSinceBonus++;
                int interval = Math.Max(1, 20 - 2 * level);
                if (yieldRecruit.GatherIntervalsSinceBonus >= interval)
                {
                    yieldRecruit.GatherIntervalsSinceBonus = 0;
                    bool bonusFits = colony?.IsFounded == true && !inCave
                        ? colony.CanStore(itemId, quantity)
                        : player.Inventory.CanAdd(itemId, quantity);
                    if (bonusFits)
                        quantity *= 2;
                }
            }
            if (quantity > 0)
            {
                if (!inCave && colony?.IsFounded == true)
                {
                    // Haul home: the goods ride with the worker to the
                    // stockpile instead of teleporting into storage. The
                    // deposit phase guarantees carried is empty here, so a
                    // mismatched leftover is merged defensively.
                    if (npc.CarriedQuantity > 0 && npc.CarriedItemId != itemId)
                    {
                        if (!colony.Store(npc.CarriedItemId!, npc.CarriedQuantity))
                            throw new InvalidOperationException("Worker storage capacity changed during haul merge.");
                        npc.CarriedQuantity = 0;
                    }
                    npc.CarriedItemId = itemId;
                    npc.CarriedQuantity += quantity;
                    npc.CarryStatus = $"Hauling {npc.CarriedQuantity} {itemId}";
                }
                else
                {
                    bool delivered = colony?.IsFounded == true
                        ? colony.Store(itemId, quantity)
                        : player.Inventory.AddItem(itemId, quantity);
                    if (!delivered)
                        throw new InvalidOperationException("Worker storage capacity changed during harvest.");
                }
                if (inCave && resource.RequiresTool)
                    skills?.AddXp("mining", xp);
                // Per-recruit XP: the worker trains the node's gathering
                // skill on every successful harvest, wherever the goods
                // land. AddXpWithNotification messages are discarded —
                // progression surfaces on the dashboard instead.
                if (npc is RecruitNpc workerRecruit)
                {
                    workerRecruit.TotalGathered += quantity;
                    workerRecruit.Skills.AddXpWithNotification(
                        GatherSkillFor(resource.ResourceDef), xp);
                }
            }
            if (resource.IsDepleted)
            {
                _targets.Remove(npc.NpcId);
                ReleaseGatherReservation(colony, npc, targetTile.X, targetTile.Y);
            }
        }

        foreach (var id in _workTimers.Keys.Where(id => !liveIds.Contains(id)).ToArray())
        {
            _workTimers.Remove(id);
            _targets.Remove(id);
            _homes.Remove(id);
            _paths.Remove(id);
            _approaches.Remove(id);
            _patrolPoints.Remove(id);
            _patrolTimers.Remove(id);
            colony?.TaskBoard.ReleaseAllFor(id);
        }
    }

    /// <summary>Which gathering skill a resource node trains: tool-required
    /// nodes train mining (the convention the cave-mining path already
    /// uses), wood-yield nodes train woodcutting, everything else
    /// foraging.</summary>
    private static string GatherSkillFor(ResourceDef? def)
    {
        if (def == null) return "foraging";
        // Rod-gated nodes train fishing (the one skill that can reach
        // their gate); other tool nodes train mining (the convention the
        // cave-mining path already uses).
        if (def.ToolRequirement == "fishing_rod") return "fishing";
        if (def.RequiresTool) return "mining";
        return def.YieldItem is "wood" or "log" or "logs" ? "woodcutting" : "foraging";
    }

    /// <summary>True when the worker holds the board reservation for a tile.
    /// Off-board contexts (no colony, caves) have nothing to reserve.</summary>
    private static bool ReservationHeldByWorker(Npc worker, ColonySystem? colony, bool inCave, int x, int y)
    {
        if (colony?.IsFounded != true || inCave) return true;
        return colony.TaskBoard.FindGatherByTile(x, y) is { } reservation
            && reservation.AssigneeNpcId == worker.NpcId;
    }

    private static void ReleaseGatherReservation(ColonySystem? colony, Npc worker, int x, int y)
    {
        if (colony?.IsFounded != true) return;
        var reservation = colony.TaskBoard.FindGatherByTile(x, y);
        if (reservation != null && reservation.AssigneeNpcId == worker.NpcId)
            colony.TaskBoard.Release(reservation);
    }

    /// <summary>Walk carried goods to the settlement anchor and store them.
    /// Partial deliveries are allowed; a full stockpile keeps the goods on
    /// the worker with a blocked status instead of discarding them.</summary>
    private void TickDepositCarried(Npc npc, ColonySystem colony, TileMap world, float dt)
    {
        var task = colony.TaskBoard.GetOrAddDeposit(npc.NpcId);
        task.ItemId = npc.CarriedItemId ?? string.Empty;
        task.Quantity = npc.CarriedQuantity;

        float anchorX = (colony.AnchorTileX + 0.5f) * Constants.TileSize;
        float anchorY = (colony.AnchorTileY + 0.5f) * Constants.TileSize;
        float dx = anchorX - npc.WorldX;
        float dy = anchorY - npc.WorldY;
        float reach = Constants.TileSize * 1.5f;
        if (dx * dx + dy * dy <= reach * reach)
        {
            npc.VelocityX = npc.VelocityY = 0f;
            string itemId = npc.CarriedItemId ?? string.Empty;
            int quantity = npc.CarriedQuantity;
            int acceptable = Math.Min(quantity, colony.FreeCapacity);
            if (acceptable > 0 && colony.Store(itemId, acceptable))
                quantity -= acceptable;
            if (quantity <= 0)
            {
                npc.CarriedItemId = null;
                npc.CarriedQuantity = 0;
                npc.CarryStatus = string.Empty;
                colony.TaskBoard.Release(task);
            }
            else
            {
                npc.CarriedQuantity = quantity;
                npc.CarryStatus = "Stockpile full";
                task.Status = "Blocked: stockpile full";
            }
            return;
        }

        // MoveAlongPath returns false while still walking; a null plan means
        // no route exists at all.
        if (!MoveAlongPath(npc, world, colony.AnchorTileX, colony.AnchorTileY, dt, WalkSpeed)
            && _paths.TryGetValue(npc.NpcId, out var haulPlan) && haulPlan.Nodes == null)
        {
            npc.CarryStatus = "No route to stockpile";
            task.Status = "Blocked: no route to stockpile";
            return;
        }
        task.Status = $"Hauling {npc.CarriedQuantity} {task.ItemId} to stockpile";
        npc.CarryStatus = task.Status;
    }

    private static bool CanWorkerHarvest(ResourceNode node,
        DontStarveRuneScape.Inventory.Inventory inventory, ColonySystem? colony, bool inCave)
    {
        if (!node.RequiresTool) return true;
        // Tool-required nodes are harvestable when the tool is available
        // (player inventory or colony store) — on the surface as in caves.
        // The surface restriction once applied to every tool node; the
        // bucket-gated water_source made tools a stock-check question
        // instead (cave-mining convention, extended). Without the tool the
        // node is silently skipped — visible in stock, per the MC law.
        string required = node.ResourceDef?.ToolRequirement ?? "";
        bool inPlayerInventory = inventory.Slots.Any(slot => slot.Quantity > 0
            && slot.ItemId != null && (slot.ItemId == required
                || slot.ItemId.EndsWith("_" + required, StringComparison.Ordinal)));
        bool inColonyStore = colony != null && (colony.GetItemQuantity(required) > 0
            || colony.GetItemQuantity("stone_" + required) > 0);
        return inPlayerInventory || inColonyStore;
    }

    private static void TickColonyNeeds(float dt, NPCSystem npcSystem,
        ColonySystem colony, FoodRegistry foods)
    {
        foreach (var npc in npcSystem.NPCs.Where(n => n.IsActive && n.IsRecruited))
        {
            npc.ColonyHunger = Math.Max(0f, npc.ColonyHunger - Math.Min(dt, 0.25f) * 0.04f);
            if (npc.ColonyHunger <= 20f)
            {
                var meal = foods.All()
                    .Where(food => food.HungerRestoration > 0 && colony.GetItemQuantity(food.ItemId) > 0)
                    .OrderBy(food => food.IsRaw)
                    .ThenByDescending(food => food.HungerRestoration)
                    .FirstOrDefault();
                if (meal != null && colony.RemoveItem(meal.ItemId, 1))
                {
                    npc.ColonyHunger = Math.Min(100f, npc.ColonyHunger + meal.HungerRestoration);
                    npc.ColonyNeedStatus = npc.ColonyHunger <= 15f ? "Starving"
                        : npc.ColonyHunger <= 35f ? "Hungry" : "Fed";
                    continue;
                }
            }

            npc.ColonyNeedStatus = npc.ColonyHunger <= 15f ? "Starving"
                : npc.ColonyHunger <= 35f ? "Hungry" : "Fed";
        }
    }

    private bool TickResidentRest(Npc npc, (int X, int Y) home, float dt, TileMap world,
        ColonySystem? colony, BuildingSystem? buildings, CombatSystem? combat,
        DontStarveRuneScape.Seasons.WeatherSystem? weather,
        DontStarveRuneScape.Skills.Firemaking.FiremakingSkill? firemaking,
        bool bedtime = false)
    {
        if (colony?.IsFounded != true)
        {
            npc.ColonyRest = 100f;
            npc.ColonyRestStatus = "Rested";
            return false;
        }

        float radius = Constants.TileSize * 3f;
        bool threatened = combat?.Monsters.Any(monster => monster.IsAlive() && monster.IsHostile
            && MathF.Pow(monster.WorldX - npc.WorldX, 2) + MathF.Pow(monster.WorldY - npc.WorldY, 2)
                <= radius * radius) == true;
        if (threatened)
        {
            npc.ColonyRestStatus = RestStatus(npc.ColonyRest);
            return false;
        }

        string season = world.SeasonSystem?.CurrentSeason ?? "spring";
        string conditions = weather?.CurrentWeather ?? "clear";
        float shelterRadius = Constants.TileSize * 1.5f;
        bool sheltered = buildings?.Structures.Any(structure => structure.IsActive
            && !structure.IsUnderConstruction
            && structure.StructureId is "woven_shelter" or "timber_shelter" or "stone_shelter"
            && MathF.Pow(structure.WorldX - npc.WorldX, 2)
                + MathF.Pow(structure.WorldY - npc.WorldY, 2) <= shelterRadius * shelterRadius) == true;
        bool harshWeather = !sheltered && conditions is "rain" or "storm" or "snow";
        float elapsed = Math.Min(dt, 0.25f);
        bool resting = npc.ColonyRestStatus is "Resting" or "Seeking rest";
        if (!resting && !bedtime)
        {
            float fatigueRate = 0.03f * (season == "winter" ? 1.25f : 1f)
                * (harshWeather ? 1.25f : 1f);
            npc.ColonyRest = Math.Max(0f, npc.ColonyRest - elapsed * fatigueRate);
            if (npc.ColonyRest > 60f)
            {
                npc.ColonyRestStatus = RestStatus(npc.ColonyRest);
                return false;
            }
            resting = true;
        }

        int workRadius = colony.WorkRadiusTiles;
        var candidates = new List<(int X, int Y, bool NearFire, bool NearBench, string? ShelterTier, float Distance)>();
        if (buildings != null)
        {
            foreach (var bench in buildings.Structures.Where(s => s.IsActive
                         && !s.IsUnderConstruction
                         && s.StructureId is "stone_bench" or "woven_shelter"
                             or "timber_shelter" or "stone_shelter"))
            {
                int dxHome = bench.TileX - home.X, dyHome = bench.TileY - home.Y;
                if (dxHome * dxHome + dyHome * dyHome > workRadius * workRadius) continue;
                var approach = FindWorkplaceApproach(world, bench, npc);
                if (approach == null) continue;
                float dx = (approach.Value.X + 0.5f) * Constants.TileSize - npc.WorldX;
                float dy = (approach.Value.Y + 0.5f) * Constants.TileSize - npc.WorldY;
                candidates.Add((approach.Value.X, approach.Value.Y, false, true,
                    bench.StructureId is "woven_shelter" or "timber_shelter" or "stone_shelter"
                        ? bench.StructureId : null,
                    dx * dx + dy * dy));
            }
        }
        if (firemaking != null)
        {
            foreach (var fire in firemaking.GetActiveFires())
            {
                int tileX = (int)(fire.WorldX / Constants.TileSize);
                int tileY = (int)(fire.WorldY / Constants.TileSize);
                int dxHome = tileX - home.X, dyHome = tileY - home.Y;
                if (dxHome * dxHome + dyHome * dyHome > workRadius * workRadius
                    || !WorkerPathfinder.CanStand(world, tileX, tileY)) continue;
                float dx = fire.WorldX - npc.WorldX, dy = fire.WorldY - npc.WorldY;
                candidates.Add((tileX, tileY, true, false, null, dx * dx + dy * dy));
            }
        }
        if (WorkerPathfinder.CanStand(world, home.X, home.Y))
        {
            float dx = (home.X + 0.5f) * Constants.TileSize - npc.WorldX;
            float dy = (home.Y + 0.5f) * Constants.TileSize - npc.WorldY;
            candidates.Add((home.X, home.Y, false, false, null, dx * dx + dy * dy));
        }
        else
        {
            foreach (var offset in new[] { (X: 1, Y: 0), (X: 0, Y: 1), (X: -1, Y: 0), (X: 0, Y: -1) })
            {
                int x = home.X + offset.X, y = home.Y + offset.Y;
                if (!WorkerPathfinder.CanStand(world, x, y)) continue;
                float dx = (x + 0.5f) * Constants.TileSize - npc.WorldX;
                float dy = (y + 0.5f) * Constants.TileSize - npc.WorldY;
                candidates.Add((x, y, false, false, null, dx * dx + dy * dy));
            }
        }

        if (candidates.Count == 0)
        {
            npc.ColonyRestStatus = "No rest place";
            return false;
        }

        var startX = (int)(npc.WorldX / Constants.TileSize);
        var startY = (int)(npc.WorldY / Constants.TileSize);
        var reachable = candidates.OrderByDescending(candidate => candidate.NearFire && season == "winter")
            .ThenByDescending(candidate => candidate.NearBench)
            .ThenBy(candidate => candidate.Distance)
            .Where(candidate => WorkerPathfinder.FindPath(world,
                startX, startY, candidate.X, candidate.Y) != null)
            .ToArray();
        if (reachable.Length == 0)
        {
            npc.ColonyRestStatus = "No safe rest route";
            return false;
        }
        var target = reachable[0];
        float targetX = (target.X + 0.5f) * Constants.TileSize;
        float targetY = (target.Y + 0.5f) * Constants.TileSize;
        float moveX = targetX - npc.WorldX, moveY = targetY - npc.WorldY;
        if (moveX * moveX + moveY * moveY > 16f)
        {
            MoveAlongPath(npc, world, target.X, target.Y, dt, WalkSpeed);
            npc.ColonyRestStatus = "Seeking rest";
            return true;
        }

        npc.VelocityX = npc.VelocityY = 0f;
        // Shelter-tier recovery (design law: mitigate, never nullify — fire
        // stays the best rest spot at 0.22/s): bench 0.16, woven 0.16,
        // timber 0.18, stone 0.20, bare ground 0.08.
        float recovery = target.NearFire ? 0.22f
            : target.ShelterTier switch
            {
                "stone_shelter" => 0.20f,
                "timber_shelter" => 0.18f,
                _ => target.NearBench ? 0.16f : 0.08f,
            };
        if (harshWeather && !target.NearFire) recovery *= 0.5f;
        if (season == "winter" && !target.NearFire) recovery *= 0.75f;
        npc.ColonyRest = Math.Min(100f, npc.ColonyRest + elapsed * recovery);
        // During a bedtime SLEEP slot, stay asleep through the night and wake
        // only when the schedule leaves SLEEP (caller passes bedtime=false).
        if (npc.ColonyRest >= 90f && !bedtime)
        {
            npc.ColonyRestStatus = RestStatus(npc.ColonyRest);
            return false;
        }
        npc.ColonyRestStatus = "Resting";
        return true;
    }

    private static string RestStatus(float rest) => rest switch
    {
        <= 25f => "Exhausted",
        <= 50f => "Tired",
        <= 75f => "Weary",
        _ => "Rested",
    };

    private (int X, int Y)? FindWorkplaceApproach(TileMap world, Structure structure, Npc worker)
    {
        if (_approaches.TryGetValue(worker.NpcId, out var cached)
            && ReferenceEquals(cached.Structure, structure))
        {
            if (cached.Tile.HasValue
                && WorkerPathfinder.CanStand(world, cached.Tile.Value.X, cached.Tile.Value.Y))
                return cached.Tile;
        }

        if (WorkerPathfinder.CanStand(world, structure.TileX, structure.TileY))
        {
            var sameTile = (structure.TileX, structure.TileY);
            _approaches[worker.NpcId] = (structure, sameTile);
            return sameTile;
        }

        int startX = (int)(worker.WorldX / Constants.TileSize);
        int startY = (int)(worker.WorldY / Constants.TileSize);
        var candidates = new[] { (X: 1, Y: 0), (X: 0, Y: 1), (X: -1, Y: 0), (X: 0, Y: -1) }
            .Select(offset => (X: structure.TileX + offset.X, Y: structure.TileY + offset.Y))
            .Where(tile => WorkerPathfinder.CanStand(world, tile.X, tile.Y))
            .Select(tile => (Tile: tile, Path: WorkerPathfinder.FindPath(world, startX, startY, tile.X, tile.Y)))
            .Where(candidate => candidate.Path != null)
            .OrderBy(candidate => candidate.Path!.Count)
            .FirstOrDefault();
        if (candidates.Path == null)
        {
            _approaches[worker.NpcId] = (structure, null);
            return null;
        }
        _approaches[worker.NpcId] = (structure, candidates.Tile);
        return candidates.Tile;
    }

    private bool MoveAlongPath(Npc npc, TileMap world, int targetX, int targetY, float dt, float speed)
    {
        if (!WorkerPathfinder.CanStand(world, targetX, targetY))
        {
            npc.VelocityX = npc.VelocityY = 0f;
            _paths.Remove(npc.NpcId);
            return false;
        }

        int currentX = (int)(npc.WorldX / Constants.TileSize);
        int currentY = (int)(npc.WorldY / Constants.TileSize);
        bool needsPlan = !_paths.TryGetValue(npc.NpcId, out var plan)
            || !ReferenceEquals(plan.World, world)
            || plan.TargetX != targetX || plan.TargetY != targetY;
        if (!needsPlan && plan!.Nodes != null && plan.NextNode < plan.Nodes.Count)
        {
            var next = plan.Nodes[plan.NextNode];
            needsPlan = !WorkerPathfinder.CanStand(world, next.X, next.Y)
                || Math.Abs(next.X - currentX) > 1 || Math.Abs(next.Y - currentY) > 1;
        }
        if (needsPlan)
        {
            var nodes = WorkerPathfinder.FindPath(world, currentX, currentY, targetX, targetY);
            plan = new WorkerPathState { World = world, TargetX = targetX, TargetY = targetY, Nodes = nodes };
            _paths[npc.NpcId] = plan;
        }
        if (plan!.Nodes == null)
        {
            npc.VelocityX = npc.VelocityY = 0f;
            return false;
        }

        while (plan.NextNode < plan.Nodes.Count)
        {
            var node = plan.Nodes[plan.NextNode];
            float waypointX = (node.X + 0.5f) * Constants.TileSize;
            float waypointY = (node.Y + 0.5f) * Constants.TileSize;
            float dx = waypointX - npc.WorldX;
            float dy = waypointY - npc.WorldY;
            float distance = MathF.Sqrt(dx * dx + dy * dy);
            if (distance <= 4f)
            {
                plan.NextNode++;
                continue;
            }

            float step = Math.Min(distance, speed * Math.Min(dt, 0.25f));
            npc.VelocityX = dx / distance * speed;
            npc.VelocityY = dy / distance * speed;
            npc.WorldX += dx / distance * step;
            npc.WorldY += dy / distance * step;
            return false;
        }

        float goalX = (targetX + 0.5f) * Constants.TileSize;
        float goalY = (targetY + 0.5f) * Constants.TileSize;
        float goalDx = goalX - npc.WorldX;
        float goalDy = goalY - npc.WorldY;
        float goalDistance = MathF.Sqrt(goalDx * goalDx + goalDy * goalDy);
        if (goalDistance > 4f)
        {
            float step = Math.Min(goalDistance, speed * Math.Min(dt, 0.25f));
            npc.VelocityX = goalDx / goalDistance * speed;
            npc.VelocityY = goalDy / goalDistance * speed;
            npc.WorldX += goalDx / goalDistance * step;
            npc.WorldY += goalDy / goalDistance * step;
            return false;
        }

        npc.VelocityX = npc.VelocityY = 0f;
        return true;
    }

    /// <summary>
    /// Free-time / night-floor wander: pick a random standable tile within
    /// WanderRadiusTiles of the home anchor, walk to it, pause 2–4 s, repick.
    /// Reservation-free and dispatch-free; nothing is claimed or produced.
    /// </summary>
    private void TickWander(Npc npc, (int X, int Y) home, float dt, TileMap world,
        ColonySystem? colony)
    {
        _wanderTargets.TryGetValue(npc.NpcId, out var state);
        npc.VelocityX = npc.VelocityY = 0f; // default; moving overwrites below

        if (state.Tile.HasValue)
        {
            (int tx, int ty) = state.Tile.Value;
            float txWorld = (tx + 0.5f) * Constants.TileSize;
            float tyWorld = (ty + 0.5f) * Constants.TileSize;
            float dx = txWorld - npc.WorldX, dy = tyWorld - npc.WorldY;
            if (dx * dx + dy * dy > 16f)
            {
                MoveAlongPath(npc, world, tx, ty, dt, WalkSpeed * 0.6f);
                return;
            }
            // Arrived: count down the repick pause.
            state.RepickTimer -= Math.Min(dt, 0.25f);
            if (state.RepickTimer > 0f)
            {
                _wanderTargets[npc.NpcId] = state;
                return;
            }
        }

        // Pick a fresh standable tile inside the wander radius.
        var rng = new Random(npc.NpcId.GetHashCode() ^ Environment.TickCount);
        for (int attempt = 0; attempt < 8; attempt++)
        {
            int ox = rng.Next(-WorkerSchedule.WanderRadiusTiles, WorkerSchedule.WanderRadiusTiles + 1);
            int oy = rng.Next(-WorkerSchedule.WanderRadiusTiles, WorkerSchedule.WanderRadiusTiles + 1);
            int x = home.X + ox, y = home.Y + oy;
            if (x < 0 || y < 0 || x >= world.Width || y >= world.Height) continue;
            if (!WorkerPathfinder.CanStand(world, x, y)) continue;
            float repick = WorkerSchedule.WanderRepickMinSeconds
                + (float)rng.NextDouble()
                * (WorkerSchedule.WanderRepickMaxSeconds - WorkerSchedule.WanderRepickMinSeconds);
            _wanderTargets[npc.NpcId] = ((x, y), repick);
            return;
        }
        // No standable tile nearby — idle in place.
        _wanderTargets[npc.NpcId] = (null, WorkerSchedule.WanderRepickMinSeconds);
    }

    private void TickGuard(Npc guard, (int X, int Y) home, float dt, TileMap world,
        ColonySystem? colony, CombatSystem? combat, Player player, SkillManager? skills,
        FactionRegistry? factions, FactionSystem? factionSystem)
    {
        float anchorX = (home.X + 0.5f) * Constants.TileSize;
        float anchorY = (home.Y + 0.5f) * Constants.TileSize;
        float defenseRadius = (colony?.IsFounded == true ? 6f : 4f) * Constants.TileSize;
        float radiusSq = defenseRadius * defenseRadius;
        var threat = combat?.Monsters.Where(m => m.IsAlive() && m.IsHostile
                && IsSettlementThreat(m, factions, factionSystem))
            .Select(m => (Monster: m, Dx: m.WorldX - anchorX, Dy: m.WorldY - anchorY))
            .Where(candidate => candidate.Dx * candidate.Dx + candidate.Dy * candidate.Dy <= radiusSq)
            .OrderBy(candidate => candidate.Dx * candidate.Dx + candidate.Dy * candidate.Dy)
            .FirstOrDefault();

        if (threat.HasValue && threat.Value.Monster != null)
        {
            var target = threat.Value.Monster;
            float dx = target.WorldX - guard.WorldX;
            float dy = target.WorldY - guard.WorldY;
            float distance = MathF.Sqrt(dx * dx + dy * dy);
            if (distance > Constants.TileSize * 0.75f)
            {
                MoveGuard(guard, world, target.WorldX, target.WorldY, dt);
                guard.RecruitBehavior = "guard";
                return;
            }

            guard.VelocityX = guard.VelocityY = 0f;
            if (combat != null && skills != null && player.Inventory != null)
                combat.GuardAttack(guard, target, player.Inventory, skills, dt);
            return;
        }

        _patrolTimers.TryGetValue(guard.NpcId, out float patrolTimer);
        patrolTimer += Math.Min(dt, 0.25f);
        if (patrolTimer >= 8f)
        {
            patrolTimer -= 8f;
            _patrolPoints.TryGetValue(guard.NpcId, out int point);
            _patrolPoints[guard.NpcId] = (point + 1) % PatrolOffsets.Length;
        }
        _patrolTimers[guard.NpcId] = patrolTimer;
        _patrolPoints.TryGetValue(guard.NpcId, out int patrolIndex);
        var offset = PatrolOffsets[patrolIndex % PatrolOffsets.Length];
        float targetX = (home.X + offset.X + 0.5f) * Constants.TileSize;
        float targetY = (home.Y + offset.Y + 0.5f) * Constants.TileSize;
        float patrolDx = targetX - guard.WorldX;
        float patrolDy = targetY - guard.WorldY;
        if (patrolDx * patrolDx + patrolDy * patrolDy <= 16f)
            guard.VelocityX = guard.VelocityY = 0f;
        else
            MoveGuard(guard, world, targetX, targetY, dt);
    }

    private static bool IsSettlementThreat(Monster monster, FactionRegistry? factions,
        FactionSystem? factionSystem)
    {
        var matchingFactions = factions?.Factions.Values
            .Where(faction => faction.HostileMonsterTypes.Contains(monster.MonsterId, StringComparer.Ordinal))
            .ToArray();
        if (matchingFactions == null || matchingFactions.Length == 0) return true;

        return matchingFactions.Max(faction => faction.BaseHostility
            + (QuestSystem.DefaultStanding - (factionSystem?.StandingOf(faction.FactionId)
                ?? QuestSystem.DefaultStanding))) >= 0.5f;
    }

    /// <summary>True when a live hostile monster stands within the danger
    /// radius of the workplace or the worker (workers back off; the job
    /// pauses with visible feedback until the threat clears).</summary>
    private static bool HasHostileNear(Structure workplace, Npc worker, CombatSystem? combat)
    {
        if (combat == null) return false;
        float radius = Constants.TileSize * 3f;
        float radiusSq = radius * radius;
        return combat.Monsters.Any(monster => monster.IsAlive() && monster.IsHostile
            && (MathF.Pow(monster.WorldX - workplace.WorldX, 2)
                    + MathF.Pow(monster.WorldY - workplace.WorldY, 2) <= radiusSq
                || MathF.Pow(monster.WorldX - worker.WorldX, 2)
                    + MathF.Pow(monster.WorldY - worker.WorldY, 2) <= radiusSq));
    }

    private void MoveGuard(Npc guard, TileMap world, float targetX, float targetY, float dt)
    {
        int tileX = (int)(targetX / Constants.TileSize);
        int tileY = (int)(targetY / Constants.TileSize);
        if (!MoveAlongPath(guard, world, tileX, tileY, dt, 55f)) return;
        float dx = targetX - guard.WorldX;
        float dy = targetY - guard.WorldY;
        float distance = MathF.Sqrt(dx * dx + dy * dy);
        if (distance <= 0.001f) return;
        float step = Math.Min(distance, 55f * Math.Min(dt, 0.25f));
        guard.VelocityX = dx / distance * 55f;
        guard.VelocityY = dy / distance * 55f;
        guard.WorldX += dx / distance * step;
        guard.WorldY += dy / distance * step;
    }

    private static Structure? FindWorkplace(Npc worker, (int X, int Y) home, int workRadiusSq,
        ColonySystem? colony, BuildingSystem? buildings, CraftingSystem? crafting, SkillManager? skills)
    {
        if (colony?.IsFounded != true || buildings == null || crafting?.Registry == null || skills == null)
            return null;

        Structure? assigned = null;
        Structure? nearest = null;
        float nearestDistanceSq = float.MaxValue;
        foreach (var structure in buildings.Structures)
        {
            if (!structure.IsActive || structure.IsUnderConstruction) continue;
            if (structure.AssignedNpcId != null && structure.AssignedNpcId != worker.NpcId) continue;

            int dxHome = structure.TileX - home.X;
            int dyHome = structure.TileY - home.Y;
            if (dxHome * dxHome + dyHome * dyHome > workRadiusSq) continue;

            if (structure.WorkOrdersPaused)
            {
                if (structure.AssignedNpcId == worker.NpcId)
                {
                    structure.AssignedNpcId = null;
                    structure.WorkStatus = "Work orders paused";
                }
                continue;
            }

            CraftRecipe? recipe = null;
            if (!string.IsNullOrWhiteSpace(structure.WorkRecipeId))
                recipe = crafting.Registry.GetRecipe(structure.WorkRecipeId);
            else if (!structure.HasManualWorkOrder)
                recipe = crafting.Registry.Recipes.Values
                    .Where(candidate => Supports(candidate, structure.StructureId)
                        && skills.GetSkillLevel(candidate.RequiredSkill) >= candidate.RequiredLevel
                        && HasInputs(colony, candidate)
                        && CanFitCraftOutput(colony, candidate))
                    .OrderBy(candidate => candidate.Tier)
                    .ThenBy(candidate => candidate.Name, StringComparer.Ordinal)
                    .FirstOrDefault();

            if (recipe == null || !Supports(recipe, structure.StructureId)
                || skills.GetSkillLevel(recipe.RequiredSkill) < recipe.RequiredLevel)
            {
                if (structure.AssignedNpcId == worker.NpcId)
                {
                    structure.WorkStatus = recipe == null ? "Waiting for a work order" : "Worker skill too low";
                    return null;
                }
                continue;
            }

            if (!HasInputs(colony, recipe))
            {
                structure.WorkStatus = "Waiting for materials";
                if (structure.AssignedNpcId == worker.NpcId) return null;
                if (!string.IsNullOrWhiteSpace(structure.WorkRecipeId))
                {
                    float orderDx = (structure.TileX + 0.5f) * Constants.TileSize - worker.WorldX;
                    float orderDy = (structure.TileY + 0.5f) * Constants.TileSize - worker.WorldY;
                    float orderDistanceSq = orderDx * orderDx + orderDy * orderDy;
                    if (orderDistanceSq < nearestDistanceSq)
                    {
                        nearestDistanceSq = orderDistanceSq;
                        nearest = structure;
                    }
                }
                continue;
            }
            if (!CanFitCraftOutput(colony, recipe))
            {
                structure.WorkStatus = "Waiting for stockpile space";
                if (structure.AssignedNpcId == worker.NpcId) return null;
                continue;
            }

            if (string.IsNullOrWhiteSpace(structure.WorkRecipeId))
                structure.WorkRecipeId = recipe.RecipeId;
            if (structure.AssignedNpcId == worker.NpcId)
            {
                assigned = structure;
                break;
            }

            float dx = (structure.TileX + 0.5f) * Constants.TileSize - worker.WorldX;
            float dy = (structure.TileY + 0.5f) * Constants.TileSize - worker.WorldY;
            float distanceSq = dx * dx + dy * dy;
            if (distanceSq < nearestDistanceSq)
            {
                nearestDistanceSq = distanceSq;
                nearest = structure;
            }
        }

        return assigned ?? nearest;
    }

    private static HashSet<string>? GetMissingWorkInputs(Npc worker, Structure? selectedWorkplace,
        ColonySystem? colony, BuildingSystem? buildings, CraftingSystem? crafting)
    {
        if (colony?.IsFounded != true || crafting?.Registry == null) return null;
        var structure = selectedWorkplace?.WorkRecipeId != null
            ? selectedWorkplace
            : buildings?.Structures.FirstOrDefault(candidate => candidate.AssignedNpcId == worker.NpcId
                && !string.IsNullOrWhiteSpace(candidate.WorkRecipeId));
        if (structure?.WorkRecipeId is not { } recipeId) return null;
        var recipe = crafting.Registry.GetRecipe(recipeId);
        if (recipe == null) return null;
        var missing = recipe.Inputs.Where(input => colony.GetItemQuantity(input.ItemId) < input.Quantity)
            .Select(input => input.ItemId).ToHashSet(StringComparer.Ordinal);
        return missing.Count > 0 ? missing : null;
    }

    private static void PlanRecipeDependencies(ColonySystem colony, BuildingSystem buildings,
        CraftingSystem crafting, SkillManager skills)
    {
        var registry = crafting.Registry;
        if (registry == null) return;
        foreach (var consumer in buildings.Structures.Where(s => s.IsActive
                     && !s.IsUnderConstruction && !s.WorkOrdersPaused
                     && !string.IsNullOrWhiteSpace(s.WorkRecipeId)))
        {
            var recipe = registry.GetRecipe(consumer.WorkRecipeId!);
            if (recipe == null) continue;
            QueueMissingRecipeInputs(recipe, consumer, 0,
                new HashSet<string>(StringComparer.Ordinal) { recipe.OutputItem },
                colony, buildings, registry, skills);
        }

        foreach (var site in buildings.Structures.Where(s => s.IsActive && s.IsUnderConstruction
                     && !s.ConstructionMaterialsPaid && IsWithinColony(s, colony)))
        {
            foreach (var material in site.StructureDef.Materials
                         .GroupBy(row => row.ItemId, StringComparer.Ordinal))
            {
                int missing = material.Sum(row => row.Quantity) - colony.GetItemQuantity(material.Key);
                if (missing <= 0) continue;
                var constructionDemand = new CraftRecipe
                {
                    RecipeId = $"construction:{site.StructureId}:{site.TileX}:{site.TileY}:{material.Key}",
                    Name = $"Construction material: {material.Key}",
                    OutputItem = $"construction:{site.StructureId}:{site.TileX}:{site.TileY}",
                    Inputs = [(material.Key, missing)],
                    Tier = int.MaxValue,
                };
                QueueMissingRecipeInputs(constructionDemand, site, 0,
                    new HashSet<string>(StringComparer.Ordinal) { constructionDemand.OutputItem },
                    colony, buildings, registry, skills);
            }
        }
    }

    private static void QueueMissingRecipeInputs(CraftRecipe currentRecipe, Structure destination,
        int depth, HashSet<string> visiting, ColonySystem colony, BuildingSystem buildings,
        RecipeRegistry registry, SkillManager skills)
    {
        if (depth >= 8) return;
        foreach (var input in currentRecipe.Inputs
                     .GroupBy(row => row.ItemId, StringComparer.Ordinal)
                     .Select(group => (ItemId: group.Key,
                         Missing: Math.Max(0, group.Sum(row => row.Quantity)
                             - colony.GetItemQuantity(group.Key)))))
        {
            if (input.Missing == 0 || !visiting.Add(input.ItemId)) continue;
            var producerRecipe = registry.Recipes.Values
                .Where(candidate => candidate.OutputItem == input.ItemId
                    && candidate.Tier < currentRecipe.Tier
                    && skills.GetSkillLevel(candidate.RequiredSkill) >= candidate.RequiredLevel
                    && CanFitCraftOutput(colony, candidate))
                .OrderByDescending(candidate => candidate.Tier)
                .ThenBy(candidate => candidate.Name, StringComparer.Ordinal)
                .FirstOrDefault();
            if (producerRecipe != null)
            {
                var producer = buildings.Structures.FirstOrDefault(station => station.IsActive
                    && !station.IsUnderConstruction
                    && station != destination && !station.WorkOrdersPaused
                    && ((station.WorkRecipeId == null && !station.HasManualWorkOrder)
                        || (station.WorkRecipeId == producerRecipe.RecipeId && station.IsDependencyOrder))
                    && (station.AssignedNpcId == null || station.AssignedNpcId == destination.AssignedNpcId)
                    && Supports(producerRecipe, station.StructureId)
                    && IsWithinColony(station, colony));
                if (producer != null)
                {
                    if (producer.WorkRecipeId != producerRecipe.RecipeId)
                    {
                        producer.WorkRecipeId = producerRecipe.RecipeId;
                        producer.WorkRecipeQueue.Clear();
                        int runs = (input.Missing + producerRecipe.OutputQuantity - 1)
                            / producerRecipe.OutputQuantity;
                        for (int run = 1; run < runs; run++)
                            producer.WorkRecipeQueue.Add(producerRecipe.RecipeId);
                        producer.HasManualWorkOrder = true;
                        producer.IsDependencyOrder = true;
                        producer.WorkStatus = "Dependency queued";
                    }
                    QueueMissingRecipeInputs(producerRecipe, producer, depth + 1,
                        visiting, colony, buildings, registry, skills);
                }
            }
            visiting.Remove(input.ItemId);
        }
    }

    private static bool IsWithinColony(Structure structure, ColonySystem colony)
    {
        int dx = structure.TileX - colony.AnchorTileX;
        int dy = structure.TileY - colony.AnchorTileY;
        return dx * dx + dy * dy <= colony.WorkRadiusTiles * colony.WorkRadiusTiles;
    }

    private static Structure? FindGardenPlot(Npc worker, (int X, int Y) home, int workRadiusSq,
        ColonySystem? colony, BuildingSystem? buildings)
    {
        if (colony?.IsFounded != true || buildings == null) return null;
        return buildings.Structures.Where(s => s.IsActive && s.StructureId == "garden_plot"
                && (s.AssignedNpcId == null || s.AssignedNpcId == worker.NpcId))
            .Where(s =>
            {
                int dx = s.TileX - home.X;
                int dy = s.TileY - home.Y;
                return dx * dx + dy * dy <= workRadiusSq;
            })
            .OrderBy(s => s.AssignedNpcId == worker.NpcId ? 0 : 1)
            .ThenBy(s => Math.Abs(s.TileX - (int)(worker.WorldX / Constants.TileSize))
                + Math.Abs(s.TileY - (int)(worker.WorldY / Constants.TileSize)))
            .FirstOrDefault();
    }

    private static Structure? FindConstructionSite(Npc worker, (int X, int Y) home,
        int workRadiusSq, ColonySystem? colony, BuildingSystem? buildings)
    {
        if (colony?.IsFounded != true || buildings == null) return null;
        return buildings.Structures.Where(site => site.IsActive && site.IsUnderConstruction
                && (site.AssignedNpcId == null || site.AssignedNpcId == worker.NpcId))
            .Where(site =>
            {
                int dx = site.TileX - home.X;
                int dy = site.TileY - home.Y;
                return dx * dx + dy * dy <= workRadiusSq;
            })
            .OrderBy(site => Math.Abs(site.TileX - (int)(worker.WorldX / Constants.TileSize))
                + Math.Abs(site.TileY - (int)(worker.WorldY / Constants.TileSize)))
            .FirstOrDefault();
    }

    private static HashSet<string> GetMissingConstructionInputs(Structure site, ColonySystem colony,
        BuildingSystem? buildings = null)
        // An upgrading site needs its SUCCESSOR tier's materials.
        => (site.UpgradingToId != null && buildings != null
                ? buildings.Registry!.GetStructure(site.UpgradingToId)!.Materials
                : site.StructureDef.Materials)
            .GroupBy(material => material.ItemId, StringComparer.Ordinal)
            .Where(group => colony.GetItemQuantity(group.Key) < group.Sum(material => material.Quantity))
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);

    private static void TickGardenPlot(Structure plot, ColonySystem colony, string season, float dt)
    {
        if (season == "winter")
        {
            plot.WorkStatus = plot.WorkProgress > 0f ? "Crop dormant (winter)" : "Fallow (winter)";
            return;
        }

        if (plot.WorkProgress <= 0f)
        {
            if (!colony.RemoveItem("wheat", 1))
            {
                plot.WorkStatus = "Waiting for wheat seed";
                return;
            }
            plot.WorkProgress = 0.001f;
            plot.WorkStatus = "Growing wheat";
        }

        if (!colony.CanStore("wheat", 3))
        {
            plot.WorkStatus = "Waiting for stockpile space";
            return;
        }

        plot.WorkProgress += Math.Min(dt, 0.25f);
        plot.WorkStatus = $"Growing wheat ({Math.Min(99, (int)(plot.WorkProgress / 30f * 100f))}%)";
        if (plot.WorkProgress >= 30f)
        {
            if (colony.Store("wheat", 3))
            {
                plot.WorkProgress = 0f;
                plot.WorkStatus = "Harvested wheat";
            }
        }
    }

    private static bool Supports(CraftRecipe recipe, string structureId)
    {
        if (!string.IsNullOrWhiteSpace(recipe.RequiresStructure)
            && recipe.RequiresStructure != structureId)
            return false;
        if (recipe.RequiresCampfire
            && structureId is not ("campfire" or "cooking_station" or "furnace" or "smelter"))
            return false;
        return !string.IsNullOrWhiteSpace(recipe.RequiresStructure) || recipe.RequiresCampfire;
    }

    private static bool HasInputs(ColonySystem colony, CraftRecipe recipe)
        => recipe.Inputs
            .GroupBy(input => input.ItemId, StringComparer.Ordinal)
            .All(group => colony.GetItemQuantity(group.Key) >= group.Sum(input => input.Quantity));

    private static bool CanFitCraftOutput(ColonySystem colony, CraftRecipe recipe)
        => colony.CanStore(recipe.OutputItem, recipe.OutputQuantity);

    /// <summary>Called when an NPC is recruited.</summary>
    public void OnRecruit(string npcId, string behavior)
    {
        _workTimers[npcId] = 0f;
        _targets.Remove(npcId);
        _homes.Remove(npcId);
        // A companion recruit bonds on recruitment; re-selecting companion
        // after a job re-assignment re-binds (Register keeps an existing
        // bond for a different npc intact only if it was released first).
        if (behavior == "companion")
            _companion?.Bond(npcId);
    }

    /// <summary>Called when an NPC is dismissed.</summary>
    public void OnDismiss(string npcId)
    {
        _workTimers.Remove(npcId);
        _targets.Remove(npcId);
        _homes.Remove(npcId);
        // Releasing the holder clears the bond so a future companion (or the
        // same recruit re-recruited) can take it.
        _companion?.Release(npcId);
    }
}
