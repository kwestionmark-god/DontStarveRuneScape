namespace DontStarveRuneScape.Interactions;

using DontStarveRuneScape.Actions;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.World;

/// <summary>
/// InteractSystem — Handles the E-key resource-gather flow only.
/// NPC panel routing lives in NPCFlows.
/// </summary>
public sealed class InteractSystem
{
    private readonly Game _game;

    public InteractSystem(Game game) => _game = game;

    /// <summary>Handle E key interact: find nearest resource and start action.</summary>
    public void HandleInteract()
    {
        var game = _game;
        if (game.Player == null || game.World == null || game.Inventory == null || game.SkillManager == null)
            return;

        var actionSys = game.Player.ActionSystem;
        if (actionSys == null) return;

        // If already performing an action, check if it's cooking (recipe-based)
        if (actionSys.Active.State == ActionState.Running)
            return; // Already busy

        // Find nearest interactable resource
        var (node, tx, ty, nodeX, nodeY) = game.Player.FindInteractableResource(game.World, 128.0f);

        if (node != null)
        {
            // Determine action type based on resource tool requirement
            ActionType actionType = node.RequiresTool switch
            {
                "axe" => ActionType.Woodcutting,
                "pickaxe" => ActionType.Mining,
                _ => ActionType.Foraging, // No tool requirement — forage (berries, herbs, water, etc.)
            };

            // Delegate action construction to ActionSystem
            string? error = actionSys.StartAction(
                actionType, node, game.SkillManager, game.Inventory,
                tileXy: (tx, ty));

            if (error != null)
                actionSys.AddNotification(error, game.ErrorColor);
        }
    }
}