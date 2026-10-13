namespace DontStarveRuneScape.Interactions;

using DontStarveRuneScape.Building;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Skills;
using DontStarveRuneScape.Utils;

/// <summary>
/// FireInteraction — Handles fire-related player interactions.
/// </summary>
public sealed class FireInteraction
{
    private readonly Game _game;

    public FireInteraction(Game game) => _game = game;

    /// <summary>Light a fire at the player's current position: a hearth
    /// (campfire/firepit) in reach lights IT (relight), else a ground fire
    /// from carried fuel.</summary>
    public void HandleLightFire()
    {
        var game = _game;
        if (game.Firemaking == null || game.Inventory == null || game.Player == null)
            return;

        // Check if there's already a fire near the player (1 tile radius)
        var nearbyFires = game.Firemaking.GetFiresInRadius(
            game.Player.WorldX, game.Player.WorldY,
            Constants.TileSize * Constants.FireInteractionRadiusTiles);

        if (nearbyFires.Count > 0)
        {
            if (game.Player.ActionSystem != null)
                game.Player.ActionSystem.AddNotification("Fire already burning here!", (255, 200, 100));
            return;
        }

        // Relight: a burned-out hearth (light_seconds > 0) in reach feeds
        // from the same fuel list and lights — slice 13 reservoir law: the
        // fee stocks the hearth, the first unit pops and burns.
        var hearth = FindNearbyHearth();
        if (hearth != null)
        {
            var hearthFuel = CollectFuel();
            if (hearthFuel.Count == 0 && (hearth.FuelItemId == null || hearth.FuelCount == 0))
            {
                if (game.Player.ActionSystem != null)
                    game.Player.ActionSystem.AddNotification(
                        "No fuel to relight the hearth. Need charcoal, logs, or sticks.",
                        (255, 150, 100));
                return;
            }
            foreach (var (itemId, qty) in hearthFuel)
                StockHearth(hearth, itemId, qty);
            LightIfOut(hearth);
            return;
        }

        var fuelQueue = CollectFuel();
        if (fuelQueue.Count == 0)
        {
            if (game.Player.ActionSystem != null)
                game.Player.ActionSystem.AddNotification("No fuel available. Need charcoal, logs, or sticks.", (255, 150, 100));
            return;
        }

        // Light the fire
        var groundResult = game.Firemaking.LightFire(fuelQueue, game.Inventory, game.Player.WorldX, game.Player.WorldY,
            game.SkillManager);

        if (groundResult.Success)
        {
            if (game.Player.ActionSystem != null)
                game.Player.ActionSystem.AddNotification(groundResult.Message);
            // Grant firemaking XP
            if (game.SkillManager != null)
                game.SkillManager.AddXp("firemaking", groundResult.XpGained);
        }
        else
        {
            if (game.Player.ActionSystem != null)
                game.Player.ActionSystem.AddNotification(groundResult.Message, (255, 150, 100));
        }
    }

    /// <summary>The nearest hearth structure (light_seconds > 0) within the
    /// fire-interaction radius of the player, or null.</summary>
    internal Structure? FindNearbyHearth()
    {
        var game = _game;
        if (game.Player == null || game.BuildingSystem == null)
            return null;
        Structure? best = null;
        float bestSq = float.MaxValue;
        float radiusSq = MathF.Pow(Constants.TileSize * Constants.FireInteractionRadiusTiles, 2f);
        foreach (var structure in game.BuildingSystem.GetAllStructures())
        {
            if (!structure.IsActive || structure.IsUnderConstruction
                || structure.StructureDef is not { LightSeconds: > 0f })
                continue;
            float dx = structure.WorldX - game.Player.WorldX;
            float dy = structure.WorldY - game.Player.WorldY;
            float sq = dx * dx + dy * dy;
            if (sq <= radiusSq && sq < bestSq)
            {
                bestSq = sq;
                best = structure;
            }
        }
        return best;
    }

    /// <summary>Carried fuel, best-first (charcoal, then any log, then
    /// sticks), capped at 2 units of the best fuel — the relight entry fee
    /// and the ground-fire queue share one preference list.</summary>
    private List<(string ItemId, int Quantity)> CollectFuel()
    {
        var game = _game;
        var fuelQueue = new List<(string ItemId, int Quantity)>();
        if (game.Inventory == null) return fuelQueue;
        var preferredFuel = new[] { "charcoal", "oak_logs", "birch_logs", "maple_logs", "stick" };
        foreach (var fuelItem in preferredFuel)
        {
            int qty = game.Inventory.GetItemQuantity(fuelItem);
            if (qty > 0)
            {
                fuelQueue.Add((fuelItem, Math.Min(2, qty)));
                break;
            }
        }
        return fuelQueue;
    }

    /// <summary>Sleep at a nearby lit fire, campfire, or shelter to skip
    /// the night (clock fast-forwards until dawn). Returns true when the
    /// interaction applied — including a refused daytime attempt near a
    /// valid spot — so the caller does not fall through to gathering.</summary>
    public bool TrySleep()
    {
        var game = _game;
        if (game.Player == null || game.DayNight == null)
            return false;

        bool nearFire = game.Firemaking != null &&
            game.Firemaking.GetFiresInRadius(
                game.Player.WorldX, game.Player.WorldY,
                Constants.TileSize * Constants.FireInteractionRadiusTiles).Count > 0;
        bool nearShelter = game.BuildingSystem != null &&
            (StructureUtils.IsStructureNearby(
                game.BuildingSystem.GetAllStructures(),
                game.Player.WorldX, game.Player.WorldY,
                "campfire", Constants.CampfireSearchRadiusTiles) ||
             StructureUtils.IsStructureNearby(
                game.BuildingSystem.GetAllStructures(),
                game.Player.WorldX, game.Player.WorldY,
                "woven_shelter", Constants.CampfireSearchRadiusTiles));
        if (!nearFire && !nearShelter)
            return false;

        if (!game.DayNight.TryStartSleep())
        {
            game.Player.ActionSystem?.AddNotification(
                "You can only sleep at night.", (255, 200, 100));
            return true;
        }
        game.Player.ActionSystem?.AddNotification(
            "You drift off to sleep...", (150, 200, 255));
        return true;
    }

    /// <summary>Check if player is near a campfire structure tile.</summary>
    public bool CheckNearbyCampfire()
    {
        var game = _game;
        if (game.Player == null || game.BuildingSystem == null)
            return false;

        // Campfires are placed as Structures in building_system
        // Validate against the actual structure list.
        var structures = game.BuildingSystem.GetAllStructures();
        return StructureUtils.IsStructureNearby(
            structures,
            game.Player.WorldX,
            game.Player.WorldY,
            "campfire",
            Constants.CampfireSearchRadiusTiles);
    }

    /// <summary>The fire venue is open: a lit fire within the campfire
    /// search radius, or an active heat station (campfire structure,
    /// cooking_station, furnace, smelter) nearby. E opens the fire menu
    /// when this is true.</summary>
    public bool CanOpenFireMenu()
    {
        var game = _game;
        if (game.Player == null)
            return false;

        if (game.Firemaking != null &&
            game.Firemaking.GetFiresInRadius(
                game.Player.WorldX, game.Player.WorldY,
                Constants.TileSize * Constants.CampfireSearchRadiusTiles).Count > 0)
            return true;

        if (game.BuildingSystem == null)
            return false;
        var structures = game.BuildingSystem.GetAllStructures();
        if (structures.Any(s => s.IsActive && !s.IsUnderConstruction
            && s.StructureId is "cooking_station" or "furnace" or "smelter"
            && StructureUtils.IsStructureNearby(structures, game.Player.WorldX, game.Player.WorldY,
                s.StructureId, Constants.CampfireSearchRadiusTiles)))
            return true;

        // Slice 13: the hearth STRUCTURE is the venue's handle — a cold
        // hearth (campfire / lined pit / firepit) opens the menu too, so E
        // near a burned-out fire no longer falls through to sleep.
        float radiusSq = MathF.Pow(Constants.TileSize * Constants.CampfireSearchRadiusTiles, 2f);
        foreach (var s in structures)
        {
            if (!s.IsActive || s.IsUnderConstruction
                || s.StructureDef is not { BurnSeconds: > 0f })
                continue;
            float dx = s.WorldX - game.Player.WorldX;
            float dy = s.WorldY - game.Player.WorldY;
            if (dx * dx + dy * dy <= radiusSq)
                return true;
        }
        return false;
    }

    /// <summary>Feed the nearest hearth's reservoir one unit of fuel —
    /// the stocked fuel type when occupied, else the best carried unit.
    /// Feeding an OUT fire lights it (the light pays 25 XP, feeding 5).
    /// Empty reservoir and nothing carried: opens the inventory so the
    /// player sees what they have — the reservoir's "leads to the
    /// inventory" rule.</summary>
    public bool MenuFeedFire()
    {
        var game = _game;
        var hearth = FindNearbyHearth();
        if (hearth == null || game.Inventory == null || game.Firemaking == null)
            return false;

        if (hearth.FuelItemId is { } stocked && hearth.FuelCount > 0)
        {
            if (hearth.FuelCount >= Building.Structure.FuelCapacityUnits)
            {
                game.Player?.ActionSystem?.AddNotification(
                    "The fire's reservoir is full.", (255, 200, 100));
                return false;
            }
            if (game.Inventory.GetItemQuantity(stocked) <= 0)
            {
                var stockedName = Data.ItemCatalog.Get(stocked)?.Name ?? stocked;
                game.Player?.ActionSystem?.AddNotification(
                    $"The fire is stocked with {stockedName}. You carry none.",
                    (255, 150, 100));
                return false;
            }
            return StockHearth(hearth, stocked, 1) && LightIfOut(hearth);
        }

        var carried = CollectFuel();
        if (carried.Count == 0)
        {
            game.Player?.ActionSystem?.AddNotification(
                "Nothing to feed the fire — carry logs, sticks, or charcoal.",
                (255, 150, 100));
            game.SetState(GameState.InventoryOpen);
            return false;
        }
        return StockHearth(hearth, carried[0].ItemId, 1) && LightIfOut(hearth);
    }

    /// <summary>Stock up to qty units of a fuel into the hearth (capped),
    /// consuming them from the inventory and paying 5 firemaking XP per
    /// unit. Returns true when at least one unit went in.</summary>
    private bool StockHearth(Structure hearth, string fuelId, int qty)
    {
        var game = _game;
        if (game.Inventory == null || game.Firemaking == null)
            return false;
        if (hearth.FuelItemId != null && hearth.FuelCount > 0 && hearth.FuelItemId != fuelId)
            return false;
        int room = Building.Structure.FuelCapacityUnits - hearth.FuelCount;
        int moved = Math.Min(room, Math.Min(qty, game.Inventory.GetItemQuantity(fuelId)));
        if (moved <= 0)
            return false;
        game.Inventory.RemoveItem(fuelId, moved);
        hearth.FuelItemId = fuelId;
        hearth.FuelCount += moved;
        game.SkillManager?.AddXp("firemaking", 5f * moved);
        return true;
    }

    /// <summary>If the hearth's fire is out, pop one stocked unit and light
    /// it (25 firemaking XP + the catch notification). Already-lit: no-op
    /// true — the stock just queues.</summary>
    private bool LightIfOut(Structure hearth)
    {
        var game = _game;
        if (game.Firemaking == null || hearth.StructureDef == null)
            return true;
        bool lit = game.Firemaking.GetFiresInRadius(hearth.WorldX, hearth.WorldY, 4f).Count > 0;
        if (lit || hearth.FuelCount <= 0 || hearth.FuelItemId == null)
            return true;
        string fuelId = hearth.FuelItemId;
        hearth.FuelCount--;
        // The type memory STICKS at zero (the reservoir re-feeds the same
        // type on the next click without a fresh pick).
        var result = game.Firemaking.LightHearthUnit(fuelId, hearth.StructureDef,
            hearth.WorldX, hearth.WorldY, game.SkillManager);
        game.Player?.ActionSystem?.AddNotification(result.Message,
            result.Success ? Game.SuccessColor : Game.ErrorColor);
        if (result.Success && game.SkillManager != null)
            game.SkillManager.AddXp("firemaking", result.XpGained);
        return result.Success;
    }

    /// <summary>The fire-menu header line for the nearest hearth: name,
    /// burn stage ("out" / "embers" / "burning" / "roaring"), and a
    /// "about N min" remainder.</summary>
    public string FireStatusLabel()
    {
        var game = _game;
        var hearth = FindNearbyHearth();
        if (hearth == null)
            return "Fire";
        string name = hearth.StructureDef?.Name ?? hearth.StructureId;
        float perUnit = FiremakingUnitSeconds(hearth, out float rate);
        var fire = game.Firemaking?.GetFiresInRadius(hearth.WorldX, hearth.WorldY, 4f)
            .FirstOrDefault();
        if (fire != null)
        {
            // The burn metric: current unit's remainder + every stocked
            // unit's honest seconds, against a full 10-unit reservoir.
            float total = fire.RemainingTime + hearth.FuelCount * perUnit;
            float capacity = Building.Structure.FuelCapacityUnits * perUnit;
            int stage = Skills.Firemaking.FiremakingSkill.BurnStageOf(total, capacity);
            string stageName = stage == 3 ? "roaring" : stage == 2 ? "burning" : "embers";
            int mins = Math.Max(1, (int)MathF.Ceiling(total / 60f));
            return $"{name} — {stageName} (about {mins} min)";
        }
        if (hearth.FuelCount > 0 && hearth.FuelItemId != null)
        {
            var stockedName = Data.ItemCatalog.Get(hearth.FuelItemId)?.Name ?? hearth.FuelItemId;
            return $"{name} — out (stocked {hearth.FuelCount} {stockedName})";
        }
        return $"{name} — out";
    }

    /// <summary>Detail text for the fuel row: the stocked item's unit
    /// value, or the best carried unit's preview.</summary>
    public string FuelPreviewText()
    {
        var game = _game;
        var hearth = FindNearbyHearth();
        if (hearth == null)
            return string.Empty;
        string? id = hearth.FuelItemId;
        if (id == null || hearth.FuelCount <= 0)
        {
            var carried = CollectFuel();
            if (carried.Count == 0)
                return "no fuel carried";
            id = carried[0].ItemId;
        }
        float perUnit = Skills.Firemaking.FiremakingSkill.FuelTable.TryGetValue(id, out var secs)
            ? secs : Skills.Firemaking.FiremakingSkill.DefaultFuelSeconds;
        var name = Data.ItemCatalog.Get(id)?.Name ?? id;
        return $"+{(int)perUnit}s per {name}";
    }

    /// <summary>The reservoir gauge fraction for the panel's bar:
    /// (current unit remainder + stocked seconds) against a full
    /// reservoir's burn.</summary>
    public float FireGaugeFraction()
    {
        var game = _game;
        var hearth = FindNearbyHearth();
        if (hearth == null)
            return 0f;
        float perUnit = FiremakingUnitSeconds(hearth, out _);
        float capacity = Building.Structure.FuelCapacityUnits * perUnit;
        if (capacity <= 0f)
            return 0f;
        float stocked = hearth.FuelCount * perUnit;
        float burning = game.Firemaking
            ?.GetFiresInRadius(hearth.WorldX, hearth.WorldY, 4f)
            .FirstOrDefault()?.RemainingTime ?? 0f;
        return Math.Clamp((burning + stocked) / capacity, 0f, 1f);
    }

    /// <summary>Per-unit burn seconds for the hearth's stock (or the
    /// default), scaled by the def's burn_rate; rate returned too.</summary>
    private float FiremakingUnitSeconds(Structure hearth, out float rate)
    {
        var def = hearth.StructureDef;
        rate = def?.BurnRate ?? 1f;
        string id = hearth.FuelItemId ?? string.Empty;
        float perUnit = id.Length > 0 && Skills.Firemaking.FiremakingSkill.FuelTable.TryGetValue(id, out var secs)
            ? secs : Skills.Firemaking.FiremakingSkill.DefaultFuelSeconds;
        return perUnit * rate;
    }

    /// <summary>Start the nearest hearth's def upgrade chain step (the
    /// colony-worker upgrade flow; campfire -> lined fire pit).</summary>
    public (bool Success, string Message) TryUpgradeNearestHearth()
    {
        var game = _game;
        var hearth = FindNearbyHearth();
        if (hearth == null || game.BuildingSystem == null || game.SkillManager == null)
            return (false, "Nothing here can be upgraded.");
        var def = hearth.StructureDef;
        if (def?.UpgradesTo == null)
            return (false, $"{def?.Name ?? "This"} is already the top tier.");
        return game.BuildingSystem.UpgradeStructure(hearth, def.UpgradesTo,
            game.SkillManager, game.ColonySystem);
    }
}