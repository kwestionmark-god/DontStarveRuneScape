namespace DontStarveRuneScape.Interactions;

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

    /// <summary>Light a fire at the player's current position.</summary>
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

        // Find available fuel (charcoal preferred, then logs, then sticks)
        var fuelQueue = new List<(string ItemId, int Quantity)>();
        var preferredFuel = new[] { "charcoal", "log", "oak_logs" };
        foreach (var fuelItem in preferredFuel)
        {
            int qty = game.Inventory.GetItemQuantity(fuelItem);
            if (qty > 0)
            {
                fuelQueue.Add((fuelItem, qty));
                if (qty > 2)
                {
                    // Use up to 2 units of preferred fuel
                    fuelQueue = [(fuelItem, Math.Min(2, qty))];
                    break;
                }
            }
        }

        if (fuelQueue.Count == 0)
        {
            if (game.Player.ActionSystem != null)
                game.Player.ActionSystem.AddNotification("No fuel available. Need charcoal, logs, or sticks.", (255, 150, 100));
            return;
        }

        // Light the fire
        var result = game.Firemaking.LightFire(fuelQueue, game.Inventory, game.Player.WorldX, game.Player.WorldY);

        if (result.Success)
        {
            if (game.Player.ActionSystem != null)
                game.Player.ActionSystem.AddNotification(result.Message);
            // Grant firemaking XP
            if (game.SkillManager != null)
                game.SkillManager.AddXp("firemaking", result.XpGained);
        }
        else
        {
            if (game.Player.ActionSystem != null)
                game.Player.ActionSystem.AddNotification(result.Message, (255, 150, 100));
        }
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
}