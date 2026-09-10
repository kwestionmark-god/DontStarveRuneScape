namespace DontStarveRuneScape.Interactions;

using System.Collections.Generic;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.NPC;

/// <summary>
/// NPCFlows — Handles NPC panel open/close/execute flows.
/// Contains all logic for opening trade/quest/recruit/diplomacy panels,
/// executing their actions, and handling keyboard confirmations.
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

    /// <summary>Open the diplomacy panel for the given NPC.</summary>
    public bool OpenDiplomacyPanel(Npc npc)
    {
        if (_game.DiplomacyPanel == null || _game.Player == null) return false;
        if (npc is not FactionLeaderNpc leaderNpc) return false;
        if (!_game.DiplomacyPanel.OpenSession(leaderNpc)) return false;

        _game.DiplomacyPanel.Visible = true;
        _game.DiplomacyPanel.Player = _game.Player;
        if (_game.Player.ActionSystem != null)
            _game.Player.ActionSystem.AddNotification($"Diplomacy: {npc.Name} -- Press ESC to close", ((byte)100, (byte)180, (byte)100));

        _game.SetState(GameState.DiplomacyPanel);
        return true;
    }

    // ─── Action Handlers ───────────────────────────────────────────────────

    /// <summary>Route a trade action to the appropriate TradeSystem method.</summary>
    public void HandleTradeAction(object action)
    {
        // Action is a tuple: (action_type, ...params)
        if (action is not (string ActionType, object[] Params)) return;

        var game = _game;

        if (ActionType == "close")
        {
            if (game.TradePanel != null)
                game.TradePanel.Close();
            game.SetState(GameState.Playing);
            return;
        }

        if (game.Player == null || game.Player.ActionSystem == null) return;

        switch (ActionType)
        {
            case "buy":
                if (Params.Length >= 2 &&
                    Params[0] is string tradeItemId &&
                    Params[1] is int qty)
                {
                    var result = game.TradeSystem.ExecuteBuy(tradeItemId, qty);
                    var color = result.Success ? ((byte)100, (byte)255, (byte)100) : ((byte)255, (byte)100, (byte)100);
                    game.Player.ActionSystem.AddNotification(result.Message, color);
                }
                break;

            case "sell":
                if (Params.Length >= 2 &&
                    Params[0] is string itemId &&
                    Params[1] is int sellQty)
                {
                    var result = game.TradeSystem.ExecuteSell(itemId, sellQty);
                    var color = result.Success ? ((byte)100, (byte)255, (byte)100) : ((byte)255, (byte)100, (byte)100);
                    game.Player.ActionSystem.AddNotification(result.Message, color);
                }
                break;

            case "barter":
                if (Params.Length >= 3 &&
                    Params[0] is string playerItemId &&
                    Params[1] is int playerQty &&
                    Params[2] is string merchantTradeItemId)
                {
                    var result = game.TradeSystem.ExecuteBarter(playerItemId, playerQty, merchantTradeItemId);
                    var color = result.Success ? ((byte)100, (byte)255, (byte)100) : ((byte)255, (byte)100, (byte)100);
                    game.Player.ActionSystem.AddNotification(result.Message, color);
                }
                break;
        }
    }

    /// <summary>Process quest panel action tuples.</summary>
    public void HandleQuestAction((string ActionType, object[] Params) action)
    {
        var game = _game;

        if (action.ActionType == "close")
        {
            if (game.QuestPanel != null)
                game.QuestPanel.Close();
            game.SetState(GameState.Playing);
            return;
        }

        if (action.ActionType == "accept_quest" && action.Params.Length >= 1)
        {
            string questId = (string)action.Params[0];
            if (game.QuestSystem != null && game.Player != null && game.NPCSystem != null)
            {
                var nearby = game.NPCSystem.CheckProximity(game.Player);
                if (nearby != null)
                {
                    string npcType = nearby.NpcType;
                    if (npcType != "quest_giver" && npcType != "faction_leader")
                    {
                        if (game.Player.ActionSystem != null)
                            game.Player.ActionSystem.AddNotification("This NPC cannot offer quests.", ((byte)180, (byte)100, (byte)100));
                        game.SetState(GameState.Playing);
                        return;
                    }

                    if (nearby.AvailableQuests != null && !nearby.AvailableQuests.Contains(questId))
                    {
                        if (game.Player.ActionSystem != null)
                            game.Player.ActionSystem.AddNotification($"{nearby.Name} does not offer this quest.", ((byte)180, (byte)100, (byte)100));
                        game.SetState(GameState.Playing);
                        return;
                    }

                    var result = game.QuestSystem.AcceptQuest(game.Player, nearby, questId);
                    if (game.Player.ActionSystem != null)
                    {
                        var color = result.Success ? ((byte)100, (byte)255, (byte)100) : ((byte)180, (byte)100, (byte)100);
                        game.Player.ActionSystem.AddNotification(result.Message, color);
                    }
                    game.SetState(GameState.Playing);
                }
                else
                {
                    if (game.Player.ActionSystem != null)
                        game.Player.ActionSystem.AddNotification("No quest giver nearby.", ((byte)180, (byte)100, (byte)100));
                    game.SetState(GameState.Playing);
                }
            }
        }
    }

    /// <summary>Handle recruit panel action tuples.</summary>
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
            ExecuteNegotiation();
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

        if (targetNpc == null)
        {
            if (game.Player != null && game.Player.ActionSystem != null)
                game.Player.ActionSystem.AddNotification($"NPC '{npcId}' not found.", Game.ErrorColor);
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

    /// <summary>Canonical faction negotiation — one path for both keyboard and mouse.</summary>
    private void ExecuteNegotiation()
    {
        var game = _game;

        if (game.Player == null || game.Player.ActionSystem == null) return;
        if (game.DiplomacyPanel == null || game.DiplomacyPanel.FactionInfo == null) return;

        string factionId = game.DiplomacyPanel.FactionInfo.FactionId;
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

        CloseDiplomacyPanel();
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

    // ─── Keyboard Confirm Handlers ─────────────────────────────────────────

    /// <summary>Handle Enter/Space in trade panel: execute current buy/sell/barter selection.</summary>
    public void HandleTradeAcceptKeyboard()
    {
        var game = _game;
        if (game.TradePanel == null || game.TradePanel.TradeSession == null) return;

        string tab = game.TradePanel.Tab;
        var player = game.Player;

        if (tab == "buy" && game.TradePanel.SelectedMerchantIndex >= 0)
        {
            var items = game.TradePanel.TradeSystem.GetTradeItemsForMerchant(game.TradePanel.TradeSession);
            if (game.TradePanel.SelectedMerchantIndex < items.Count)
            {
                var item = items[game.TradePanel.SelectedMerchantIndex];
                var result = game.TradeSystem.ExecuteBuy(item.TradeItemId, game.TradePanel.BuyQuantity);
                if (player != null && player.ActionSystem != null)
                    player.ActionSystem.AddNotification(result.Message, result.Success ? ((byte)100, (byte)255, (byte)100) : ((byte)255, (byte)100, (byte)100));
            }
        }
        else if (tab == "sell" && game.TradePanel.SelectedPlayerIndex >= 0)
        {
            var sellableItems = game.TradePanel.CollectSellableItems();
            if (game.TradePanel.SelectedPlayerIndex < sellableItems.Count)
            {
                var (itemId, quantity, _) = sellableItems[game.TradePanel.SelectedPlayerIndex];
                var result = game.TradeSystem.ExecuteSell(itemId, game.TradePanel.SellQuantity);
                if (player != null && player.ActionSystem != null)
                    player.ActionSystem.AddNotification(result.Message, result.Success ? ((byte)100, (byte)255, (byte)100) : ((byte)255, (byte)100, (byte)100));
            }
        }
        else if (tab == "barter")
        {
            var sellableItems = game.TradePanel.CollectSellableItems();
            if (game.TradePanel.SelectedPlayerIndex is int pIdx && pIdx >= 0 && pIdx < sellableItems.Count)
            {
                var (playerItemId, playerQty, _) = sellableItems[pIdx];
                if (game.TradePanel.SelectedMerchantIndex is int mIdx && game.TradePanel.TradeSession != null)
                {
                    var items = game.TradePanel.TradeSystem.GetTradeItemsForMerchant(game.TradePanel.TradeSession);
                    if (mIdx >= 0 && mIdx < items.Count)
                    {
                        var merchantItem = items[mIdx];
                        var result = game.TradeSystem.ExecuteBarter(playerItemId, 1, merchantItem.TradeItemId);
                        if (player != null && player.ActionSystem != null)
                            player.ActionSystem.AddNotification(result.Message, result.Success ? ((byte)100, (byte)255, (byte)100) : ((byte)255, (byte)100, (byte)100));
                    }
                }
            }
        }

        // Close trade panel after any keyboard trade action
        if (game.TradePanel != null)
        {
            game.TradePanel.Visible = false;
            game.SetState(GameState.Playing);
        }
    }

    /// <summary>Handle Enter/Space in quest panel: accept the selected quest.</summary>
    public void HandleQuestAcceptKeyboard()
    {
        var game = _game;
        if (game.QuestPanel == null || game.QuestPanel.SelectedQuestId == null) return;

        string questId = game.QuestPanel.SelectedQuestId;
        var player = game.Player;

        if (game.QuestSystem != null && player != null && game.NPCSystem != null)
        {
            var nearby = game.NPCSystem.CheckProximity(player);
            if (nearby != null)
            {
                string npcType = nearby.NpcType;
                if (npcType != "quest_giver" && npcType != "faction_leader")
                {
                    if (player.ActionSystem != null)
                        player.ActionSystem.AddNotification("This NPC cannot offer quests.", ((byte)180, (byte)100, (byte)100));
                    return;
                }

                if (nearby.AvailableQuests != null && !nearby.AvailableQuests.Contains(questId))
                {
                    if (player.ActionSystem != null)
                        player.ActionSystem.AddNotification($"{nearby.Name} does not offer this quest.", ((byte)180, (byte)100, (byte)100));
                    return;
                }

                var result = game.QuestSystem.AcceptQuest(player, nearby, questId);
                if (player.ActionSystem != null)
                    player.ActionSystem.AddNotification(result.Message, result.Success ? ((byte)100, (byte)255, (byte)100) : ((byte)180, (byte)100, (byte)100));
            }
            else
            {
                if (player.ActionSystem != null)
                    player.ActionSystem.AddNotification("No NPC found near you to offer this quest.", ((byte)200, (byte)150, (byte)100));
            }
        }

        // Close quest panel after keyboard accept
        if (game.QuestPanel != null)
        {
            game.QuestPanel.Visible = false;
            game.SetState(GameState.Playing);
        }
    }
}