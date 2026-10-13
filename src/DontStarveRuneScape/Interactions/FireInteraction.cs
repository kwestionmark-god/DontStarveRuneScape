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

        // Relight: a burned-out hearth (light_seconds > 0) in reach lights
        // from the same fuel list — the hearth is the multiplier, firemaking
        // gets the XP.
        var hearth = FindNearbyHearth();
        if (hearth != null)
        {
            var hearthFuel = CollectFuel();
            if (hearthFuel.Count == 0)
            {
                if (game.Player.ActionSystem != null)
                    game.Player.ActionSystem.AddNotification(
                        "No fuel to relight the hearth. Need charcoal, logs, or sticks.",
                        (255, 150, 100));
                return;
            }
            foreach (var (itemId, qty) in hearthFuel)
                game.Inventory.RemoveItem(itemId, qty);
            var result = game.Firemaking.LightHearth(hearth.StructureDef!.BurnSeconds,
                hearth.WorldX, hearth.WorldY, game.SkillManager);
            if (game.Player.ActionSystem != null)
                game.Player.ActionSystem.AddNotification(result.Message, result.Success
                    ? Game.SuccessColor : Game.ErrorColor);
            if (result.Success && game.SkillManager != null)
                game.SkillManager.AddXp("firemaking", result.XpGained);
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
    private Structure? FindNearbyHearth()
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
        return structures.Any(s => s.IsActive && !s.IsUnderConstruction
            && s.StructureId is "cooking_station" or "furnace" or "smelter"
            && StructureUtils.IsStructureNearby(structures, game.Player.WorldX, game.Player.WorldY,
                s.StructureId, Constants.CampfireSearchRadiusTiles));
    }
}