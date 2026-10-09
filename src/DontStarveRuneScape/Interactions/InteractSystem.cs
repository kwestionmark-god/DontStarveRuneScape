namespace DontStarveRuneScape.Interactions;

using DontStarveRuneScape.Actions;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.NPC;
using DontStarveRuneScape.World;

/// <summary>
/// InteractSystem — Handles the E-key interact flow: the nearest NPC opens its
/// type-appropriate panel (trade/quest/recruit/diplomacy), else the nearest
/// resource starts its gather action.
/// </summary>
public sealed class InteractSystem
{
    private readonly Game _game;
    private readonly NPCFlows? _npcFlows;

    public InteractSystem(Game game, NPCFlows? npcFlows = null)
    {
        _game = game;
        _npcFlows = npcFlows;
    }

    /// <summary>Handle E key interact: NPC panels first, then resources.</summary>
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

        // Cave transitions take precedence over NPCs and resources.
        var (playerTileX, playerTileY) = game.Player.GetTilePosition();
        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
        {
            var caveTile = game.World.GetTile(playerTileX + dx, playerTileY + dy);
            if (caveTile == null || (!caveTile.IsCaveEntrance && !caveTile.IsCaveExit)) continue;
            game.CaveWorlds?.InteractWith(caveTile);
            return;
        }

        // NPC interaction: the nearest NPC opens its type-appropriate panel.
        if (game.NPCSystem != null && _npcFlows != null)
        {
            var npc = game.NPCSystem.CheckProximity(game.Player);
            if (npc != null)
            {
                switch (npc.NpcType)
                {
                    case "merchant" when npc is MerchantNpc merchant:
                        _npcFlows.OpenTradePanel(merchant);
                        return;
                    case "recruit" when npc is RecruitNpc recruit:
                        _npcFlows.OpenRecruitPanel(recruit);
                        return;
                    case "faction_leader":
                        // Leaders with available quests open the quest panel;
                        // the rest open the faction overview.
                        if (npc.AvailableQuests.Count > 0)
                            _npcFlows.OpenQuestPanel(npc);
                        else
                            _npcFlows.OpenDiplomacyPanel(npc.FactionId);
                        return;
                    default: // quest_giver and any other type
                        _npcFlows.OpenQuestPanel(npc);
                        return;
                }
            }
        }

        // Sleeping at a lit fire or shelter (night only) takes precedence
        // over resource gathering.
        if (game.FireInteraction?.TrySleep() == true)
            return;

        // Find nearest interactable resource
        var (node, tx, ty, nodeX, nodeY) = game.Player.FindInteractableResource(game.World, 128.0f);

        if (node != null)
        {
            // Determine action type based on resource tool requirement
            string toolReq = node.ResourceDef?.ToolRequirement ?? string.Empty;
            ActionType actionType = toolReq switch
            {
                "axe" => ActionType.Woodcutting,
                "pickaxe" => ActionType.Mining,
                "fishing_rod" => ActionType.Fishing,
                _ => ActionType.Foraging, // No tool requirement — forage (berries, herbs, water, etc.)
            };

            // Delegate action construction to ActionSystem
            string? error = actionSys.StartAction(
                actionType, node, game.SkillManager, game.Inventory,
                tileXy: (tx, ty));

            if (error != null)
                actionSys.AddNotification(error, Game.ErrorColor);
        }
    }
}
