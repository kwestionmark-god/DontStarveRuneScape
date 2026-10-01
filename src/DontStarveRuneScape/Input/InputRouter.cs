namespace DontStarveRuneScape.Input;

using DontStarveRuneScape.Core;
using DontStarveRuneScape.Interactions;
using Silk.NET.Input;

/// <summary>
/// InputRouter — Routes input events based on current GameState.
/// </summary>
public sealed class InputRouter
{
    private readonly Game _game;
    private readonly InteractSystem _interactSystem;
    private readonly FireInteraction _fireInteraction;
    private readonly NPCFlows _npcFlows;

    public InputRouter(Game game, InteractSystem interactSystem, FireInteraction fireInteraction, NPCFlows npcFlows)
    {
        _game = game;
        _interactSystem = interactSystem;
        _fireInteraction = fireInteraction;
        _npcFlows = npcFlows;
    }

    /// <summary>Handle an input key based on current state.</summary>
    public void Handle(Key key)
    {
        // The dashboard is a persistent hub only for panels launched from it.
        // NPC interaction panels keep their usual NPC-scoped close behavior.
        if (key == Key.O && _game.TryReturnToDashboard())
            return;

        // Handle panel-specific input
        switch (_game.State)
        {
            case GameState.Title:
                if (key == Key.Up || key == Key.Down)
                    _game.TitleScreen?.HandleKey(_game, key);
                else if (key == Key.Enter || key == Key.Space)
                    _game.TitleScreen?.HandleConfirm(_game);
                else if (key == Key.Escape)
                    _game.TitleScreen?.HandleBack(_game);
                break;
            case GameState.CharacterSelect:
                _game.CharacterSelectPanel?.HandleKey(_game, key);
                break;
            case GameState.Error:
                if (key == Key.Escape || key == Key.Enter || key == Key.Space)
                    _game.SetState(GameState.Title);
                break;
            case GameState.Playing:
                HandlePlayingInput(key);
                break;
            case GameState.TradePanel:
                if (key == Key.Up || key == Key.Down || key == Key.Left || key == Key.Right)
                    _game.TradePanel?.HandleKey(key);
                else if (key == Key.Enter || key == Key.Space)
                    _game.TradePanel?.HandleConfirm();
                else
                    HandleGenericPanelInput(key);
                break;
            case GameState.QuestPanel:
                if (key == Key.Up || key == Key.Down)
                    _game.QuestPanel?.HandleKey(key);
                else if (key == Key.Enter || key == Key.Space)
                    _game.QuestPanel?.HandleConfirm();
                else
                    HandleGenericPanelInput(key);
                break;
            case GameState.RecruitPanel:
                if (key == Key.Up || key == Key.Down)
                    _game.RecruitPanel?.HandleKey(key);
                else if (key == Key.Enter || key == Key.Space)
                    _game.RecruitPanel?.HandleConfirm();
                else
                    HandleGenericPanelInput(key);
                break;
            case GameState.DiplomacyPanel:
                if (key == Key.Up || key == Key.Down)
                    _game.DiplomacyPanel?.HandleKey(key);
                else if (key == Key.Enter || key == Key.Space)
                    _game.DiplomacyPanel?.HandleConfirm();
                else
                    HandleGenericPanelInput(key);
                break;
            case GameState.DashboardOpen:
                if (key == Key.Escape || key == Key.Q)
                    _game.SetState(GameState.Playing);
                else if (key == Key.Left || key == Key.Right || key == Key.Up || key == Key.Down)
                    _game.Dashboard?.HandleKey(key);
                else if (key == Key.Enter || key == Key.Space)
                    _game.Dashboard?.HandleConfirm();
                else
                    HandleGenericPanelInput(key);
                break;
            case GameState.SkillPanel:
                if (key == Key.Up || key == Key.Down)
                    _game.SkillPanel?.HandleKey(key);
                else
                    HandleGenericPanelInput(key);
                break;
            case GameState.InventoryOpen:
                if (key == Key.Up || key == Key.Down || key == Key.Left || key == Key.Right)
                    _game.InventoryPanel?.HandleKey(key);
                else
                    HandleGenericPanelInput(key);
                break;
            case GameState.CraftingPanel:
                if (key == Key.Up || key == Key.Down)
                    _game.CraftingPanel?.HandleKey(key);
                else
                    HandleGenericPanelInput(key);
                break;
            case GameState.BuildingPanel:
                if (key == Key.Up || key == Key.Down)
                    _game.BuildingPanel?.HandleKey(key);
                else
                    HandleGenericPanelInput(key);
                break;
            case GameState.GearPanel:
                if (key == Key.Up || key == Key.Down)
                    _game.GearPanel?.HandleKey(key);
                else
                    HandleGenericPanelInput(key);
                break;
            case GameState.Paused:
                if (key == Key.Escape || key == Key.Q)
                    _game.SetState(GameState.Playing);
                else if (key == Key.Up || key == Key.Down)
                    _game.PauseMenu?.HandleKey(key);
                else if (key == Key.Enter || key == Key.Space)
                    _game.PauseMenu?.HandleConfirm();
                break;
            case GameState.SettingsPanel:
                if (key == Key.Escape || key == Key.Q)
                    _game.SetState(_game.SettingsPanel?.ReturnState ?? GameState.Paused);
                else if (key == Key.Up || key == Key.Down || key == Key.Left
                    || key == Key.Right || key == Key.Enter || key == Key.Space)
                    _game.SettingsPanel?.HandleKey(key);
                break;
        }
    }

    private void HandlePlayingInput(Key key)
    {
        if (key == Key.E)
            _interactSystem.HandleInteract();
        else if (key == Key.F)
            _fireInteraction.HandleLightFire();
        else if (key == Key.J)
            _game.HandleAttackInput();
        else if (key == Key.O)
            _game.SetState(GameState.DashboardOpen);
        else if (key == Key.Escape)
        {
            // Placement mode already consumes Escape to cancel the placement
            // (UpdatePlacement reads ClosePanel); pause only outside it.
            if (!_game.BuildMode)
                _game.SetState(GameState.Paused);
        }
        else if (key == Key.C || key == Key.I)
            _game.SetState(GameState.InventoryOpen);
        else if (key == Key.Tab)
            _game.SetState(GameState.SkillPanel);
        else if (key == Key.H)
            _game.SetState(GameState.CraftingPanel);
        else if (key == Key.B)
            _game.SetState(GameState.BuildingPanel);
        else if (key == Key.G)
            _game.SetState(GameState.GearPanel);
        else if (key == Key.T)
            OpenNpcPanelByType("merchant", "No merchant nearby.");
        else if (key == Key.U)
            OpenNpcPanelByType("quest_giver", "No quest giver nearby.");
        else if (key == Key.K)
            OpenNpcPanelByType("recruit", "No recruit nearby.");
        else if (key == Key.L)
            // Diplomacy is a faction overview: scope it to a nearby leader's
            // faction when one is close, else open unscoped.
            _npcFlows.OpenDiplomacyPanel(
                _game.NPCSystem?.FindNearby(_game.Player!, "faction_leader")?.FactionId);
    }

    /// <summary>Open an NPC panel scoped to the nearest NPC of the given type;
    /// notify when none is nearby.</summary>
    private void OpenNpcPanelByType(string type, string noneNearby)
    {
        var npc = _game.NPCSystem?.FindNearby(_game.Player!, type);
        if (npc == null)
        {
            if (_game.Player?.ActionSystem != null)
                _game.Player.ActionSystem.AddNotification(noneNearby, ((byte)200, (byte)150, (byte)100));
            return;
        }

        switch (type)
        {
            case "merchant" when npc is NPC.MerchantNpc merchant:
                _npcFlows.OpenTradePanel(merchant);
                break;
            case "recruit" when npc is NPC.RecruitNpc recruit:
                _npcFlows.OpenRecruitPanel(recruit);
                break;
            default:
                _npcFlows.OpenQuestPanel(npc);
                break;
        }
    }

    private void HandleGenericPanelInput(Key key)
    {
        if (key == Key.Escape || key == Key.Q)
        {
            CloseAllPanels();
            _game.SetState(GameState.Playing);
            return;
        }
        // Same key that opened the panel closes it again.
        var toggleBack = _game.State switch
        {
            GameState.InventoryOpen => key == Key.C || key == Key.I,
            GameState.SkillPanel => key == Key.Tab,
            GameState.CraftingPanel => key == Key.H,
            GameState.BuildingPanel => key == Key.B,
            GameState.GearPanel => key == Key.G,
            GameState.TradePanel => key == Key.T,
            GameState.QuestPanel => key == Key.U,
            GameState.RecruitPanel => key == Key.K,
            GameState.DiplomacyPanel => key == Key.L,
            GameState.DashboardOpen => key == Key.O,
            _ => false,
        };
        if (toggleBack)
        {
            CloseAllPanels();
            _game.SetState(GameState.Playing);
        }
    }

    /// <summary>Check if a state is a panel state.</summary>
    public bool IsPanelState(GameState state)
    {
        return state is GameState.InventoryOpen or GameState.SkillPanel or GameState.CraftingPanel
            or GameState.BuildingPanel or GameState.GearPanel or GameState.TradePanel
            or GameState.QuestPanel or GameState.RecruitPanel or GameState.DiplomacyPanel
            or GameState.DashboardOpen;
    }

    /// <summary>Close all open panels.</summary>
    public void CloseAllPanels()
    {
        if (_game.InventoryPanel != null) _game.InventoryPanel.Visible = false;
        if (_game.SkillPanel != null) _game.SkillPanel.Visible = false;
        if (_game.CraftingPanel != null) _game.CraftingPanel.Visible = false;
        if (_game.BuildingPanel != null) _game.BuildingPanel.Visible = false;
        if (_game.GearPanel != null) _game.GearPanel.Visible = false;
        if (_game.TradePanel != null) _game.TradePanel.Close();
        if (_game.QuestPanel != null) _game.QuestPanel.Close();
        if (_game.RecruitPanel != null) _game.RecruitPanel.Close();
        if (_game.DiplomacyPanel != null) _game.DiplomacyPanel.Close();
        if (_game.Dashboard != null) _game.Dashboard.Visible = false;
    }
}
