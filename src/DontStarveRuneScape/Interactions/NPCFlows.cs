namespace DontStarveRuneScape.Interactions;

using System.Collections.Generic;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.NPC;

/// <summary>
/// NPCFlows — Handles NPC panel open/close/execute flows: opening the
/// trade/quest/recruit/diplomacy panels, recruiting and dismissing, and the
/// canonical faction negotiation path shared by keyboard and mouse.
/// </summary>
public sealed class NPCFlows
{
    private readonly Game _game;

    public NPCFlows(Game game) => _game = game;

    // ─── Panel Open Methods ────────────────────────────────────────────────

    /// <summary>Open the trade panel with the given merchant.</summary>
    public void OpenTradePanel(MerchantNpc merchant)
    {
        if (_game.TradePanel == null || _game.Player == null) return;

        _game.TradePanel.Player = _game.Player;
        bool opened = _game.TradePanel.OpenSession(merchant);
        if (opened)
            _game.SetState(GameState.TradePanel);
        else if (_game.Player.ActionSystem != null)
            _game.Player.ActionSystem.AddNotification("Cannot trade right now.", ((byte)255, (byte)150, (byte)100));
    }

    /// <summary>Open the quest panel scoped to the given NPC.</summary>
    public void OpenQuestPanel(Npc npc)
    {
        if (_game.QuestPanel == null || _game.Player == null) return;

        _game.QuestPanel.SetPlayer(_game.Player);
        bool opened = _game.QuestPanel.OpenSession(npc);
        if (opened)
            _game.SetState(GameState.QuestPanel);
        else if (_game.Player.ActionSystem != null)
            _game.Player.ActionSystem.AddNotification($"{npc.Name} has no quests available right now.", ((byte)180, (byte)150, (byte)60));
    }

    /// <summary>Open the recruit panel for the given NPC.</summary>
    public bool OpenRecruitPanel(Npc npc)
    {
        if (_game.RecruitPanel == null || _game.Player == null) return false;
        if (npc is not RecruitNpc recruitNpc) return false;
        if (!_game.RecruitPanel.OpenSession(recruitNpc)) return false;

        _game.RecruitPanel.Visible = true;
        _game.RecruitPanel.Player = _game.Player;
        if (_game.Player.ActionSystem != null)
            _game.Player.ActionSystem.AddNotification($"Recruitment: {npc.Name} -- Press ESC to close", ((byte)100, (byte)180, (byte)100));

        _game.SetState(GameState.RecruitPanel);
        return true;
    }

    /// <summary>Open the diplomacy panel (faction overview), optionally scoped
    /// to a faction id.</summary>
    public void OpenDiplomacyPanel(string? factionId)
    {
        if (_game.DiplomacyPanel == null || _game.Player == null) return;

        if (!string.IsNullOrEmpty(factionId))
        {
            var faction = _game.FactionRegistry?.GetFaction(factionId);
            _game.DiplomacyPanel.FactionInfo = new UI.FactionInfo
            {
                FactionId = factionId,
                Name = faction?.Name ?? factionId,
            };
        }
        _game.DiplomacyPanel.Visible = true;
        _game.DiplomacyPanel.Player = _game.Player;
        if (_game.Player.ActionSystem != null)
            _game.Player.ActionSystem.AddNotification("Diplomacy -- Press ESC to close", ((byte)100, (byte)180, (byte)100));

        _game.SetState(GameState.DiplomacyPanel);
    }

    // ─── Action Handlers ───────────────────────────────────────────────────

    /// <summary>Handle recruit panel action tuples (from the panel callback).</summary>
    public void HandleRecruitAction((string ActionType, object[] Params) action)
    {
        var game = _game;

        if (action.ActionType == "recruit" && action.Params.Length >= 2)
        {
            string npcId = (string)action.Params[0];
            string behavior = (string)action.Params[1];
            string? structureId = action.Params.Length >= 3 ? (string)action.Params[2] : null;
            ExecuteRecruitment(npcId, behavior, structureId);
        }
        else if (action.ActionType is "cancel" or "close")
        {
            if (action.ActionType == "cancel" && action.Params.Length >= 1)
            {
                string npcId = (string)action.Params[0];
                ExecuteDismissal(npcId);
            }
            CloseRecruitPanel();
        }
    }

    /// <summary>Handle diplomacy panel action tuples.</summary>
    public void HandleDiplomacyAction((string ActionType, object[] Params) action)
    {
        var game = _game;

        if (action.ActionType == "negotiate")
        {
            ExecuteNegotiation(action.Params.Length >= 1 ? action.Params[0] as string : null);
        }
        else if (action.ActionType is "cancel" or "close")
        {
            CloseDiplomacyPanel();
        }
    }

    // ─── Execute Methods ───────────────────────────────────────────────────

    /// <summary>Execute recruitment of an NPC.</summary>
    private void ExecuteRecruitment(string npcId, string behavior, string? structureId = null)
    {
        var game = _game;

        Npc? targetNpc = null;
        if (game.NPCSystem != null)
        {
            foreach (var npc in game.NPCSystem.NPCs)
            {
                if (npc.NpcId == npcId)
                {
                    targetNpc = npc;
                    break;
                }
            }
        }

        // The panel's live session is the authoritative target when NPCs are
        // exercised without a world NPCSystem (for example, a restored or
        // isolated interaction flow).
        if (targetNpc == null && game.RecruitPanel?.Session is { } session && session.NpcId == npcId)
            targetNpc = session;

        if (targetNpc == null)
        {
            // No world NPC system holds this id — the recruit still joins the player.
            game.Player?.AddRecruit(npcId);
            game.RecruitmentSystem?.OnRecruit(npcId, behavior);
            if (game.Player?.ActionSystem != null)
                game.Player.ActionSystem.AddNotification($"You recruited {npcId} as {behavior}!", ((byte)100, (byte)255, (byte)100));
            CloseRecruitPanel();
            return;
        }

        targetNpc.IsRecruited = true;
        targetNpc.RecruitBehavior = behavior;
        targetNpc.AssignBehavior(behavior); // Initialize HP for guards
        game.Player.AddRecruit(npcId);

        // Wire into RecruitmentSystem
        if (game.RecruitmentSystem != null)
            game.RecruitmentSystem.OnRecruit(npcId, behavior);

        // Assign guard to structure if a structure_id is provided
        if (!string.IsNullOrEmpty(structureId) && game.NPCSystem != null)
        {
            var resultMsg = game.NPCSystem.AssignNpcToStructure(npcId, structureId);
            if (game.Player?.ActionSystem != null)
            {
                var color = resultMsg.Success ? ((byte)100, (byte)255, (byte)100) : ((byte)255, (byte)150, (byte)100);
                game.Player.ActionSystem.AddNotification(resultMsg.Message, color);
            }
        }

        if (game.Player?.ActionSystem != null)
            game.Player.ActionSystem.AddNotification($"You recruited {targetNpc.Name} as {behavior}!", ((byte)100, (byte)255, (byte)100));

        CloseRecruitPanel();
    }

    /// <summary>Dismiss a recruited NPC and clean up recruitment state.</summary>
    private void ExecuteDismissal(string npcId)
    {
        var game = _game;
        if (game.Player == null || game.NPCSystem == null) return;

        Npc? targetNpc = null;
        foreach (var npc in game.NPCSystem.NPCs)
        {
            if (npc.NpcId == npcId)
            {
                targetNpc = npc;
                break;
            }
        }
        if (targetNpc == null) return;

        targetNpc.IsRecruited = false;
        targetNpc.RecruitBehavior = null;
        game.Player.RemoveRecruit(npcId);

        if (game.RecruitmentSystem != null)
            game.RecruitmentSystem.OnDismiss(npcId);

        if (game.Player.ActionSystem != null)
            game.Player.ActionSystem.AddNotification($"You dismissed {targetNpc.Name}.", ((byte)255, (byte)200, (byte)100));
    }

    /// <summary>Canonical faction negotiation — one path for both keyboard and
    /// mouse. The faction id comes from the panel callback when available.</summary>
    private void ExecuteNegotiation(string? factionId = null)
    {
        var game = _game;

        if (game.Player == null || game.Player.ActionSystem == null) return;

        factionId ??= game.DiplomacyPanel?.FactionInfo?.FactionId;
        if (string.IsNullOrEmpty(factionId)) return;
        bool success = false;

        if (game.FactionSystem != null && game.Player != null)
        {
            var result = game.FactionSystem.Negotiate(game.Player, factionId);
            success = result.Success;
            if (game.Player.ActionSystem != null)
                game.Player.ActionSystem.AddNotification(
                    result.Message,
                    result.Success ? ((byte)100, (byte)255, (byte)100) : ((byte)180, (byte)100, (byte)100));
        }

        // Quest tracking counts successful negotiations only
        if (success && game.QuestSystem != null)
            game.QuestSystem.RecordNegotiation(factionId);
    }

    // ─── Close Methods ─────────────────────────────────────────────────────

    /// <summary>Close the recruit panel and return to PLAYING.</summary>
    public void CloseRecruitPanel()
    {
        var game = _game;
        if (game.RecruitPanel != null)
            game.RecruitPanel.Close();
        game.SetState(GameState.Playing);
    }

    /// <summary>Close the diplomacy panel and return to PLAYING.</summary>
    public void CloseDiplomacyPanel()
    {
        var game = _game;
        if (game.DiplomacyPanel != null)
            game.DiplomacyPanel.Close();
        game.SetState(GameState.Playing);
    }
}
