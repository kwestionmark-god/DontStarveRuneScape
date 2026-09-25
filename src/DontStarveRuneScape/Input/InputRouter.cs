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
        // Handle panel-specific input
        switch (_game.State)
        {
            case GameState.Playing:
                HandlePlayingInput(key);
                break;
            case GameState.TradePanel:
                HandleTradePanelInput(key);
                break;
            case GameState.QuestPanel:
                HandleQuestPanelInput(key);
                break;
            case GameState.RecruitPanel:
                HandleRecruitPanelInput(key);
                break;
            case GameState.DiplomacyPanel:
                HandleDiplomacyPanelInput(key);
                break;
            case GameState.SkillPanel:
                if (key == Key.Up || key == Key.Down)
                    _game.SkillPanel?.HandleKey(key);
                else
                    HandleGenericPanelInput(key);
                break;
            case GameState.InventoryOpen:
            case GameState.CraftingPanel:
            case GameState.BuildingPanel:
            case GameState.GearPanel:
            case GameState.DashboardOpen:
                HandleGenericPanelInput(key);
                break;
        }
    }

    private void HandlePlayingInput(Key key)
    {
        if (key == Key.E)
            _interactSystem.HandleInteract();
        else if (key == Key.F)
            _fireInteraction.HandleLightFire();
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
            _ => false,
        };
        if (toggleBack)
        {
            CloseAllPanels();
            _game.SetState(GameState.Playing);
        }
    }

    private void HandleTradePanelInput(Key key)
    {
        if (key == Key.Escape || key == Key.Q)
        {
            if (_game.TradePanel != null)
            {
                _game.TradePanel.Close();
            }
            _game.SetState(GameState.Playing);
        }
        else if (key == Key.Enter || key == Key.Space)
        {
            _npcFlows.HandleTradeAcceptKeyboard();
        }
    }

    private void HandleQuestPanelInput(Key key)
    {
        if (key == Key.Escape || key == Key.Q)
        {
            if (_game.QuestPanel != null)
                _game.QuestPanel.Close();
            _game.SetState(GameState.Playing);
        }
        else if (key == Key.Enter || key == Key.Space)
        {
            _npcFlows.HandleQuestAcceptKeyboard();
        }
    }

    private void HandleRecruitPanelInput(Key key)
    {
        if (key == Key.Escape || key == Key.Q)
        {
            _npcFlows.CloseRecruitPanel();
        }
    }

    private void HandleDiplomacyPanelInput(Key key)
    {
        if (key == Key.Escape || key == Key.Q)
        {
            _npcFlows.CloseDiplomacyPanel();
        }
        else if (key == Key.Enter || key == Key.Space)
        {
            // Negotiate on Enter/Space
            if (_game.DiplomacyPanel != null)
            {
                var action = ("negotiate", new object[0]);
                _npcFlows.HandleDiplomacyAction(action);
            }
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
        if (_game.TradePanel != null) _game.TradePanel.Visible = false;
        if (_game.QuestPanel != null) _game.QuestPanel.Visible = false;
        if (_game.RecruitPanel != null) _game.RecruitPanel.Visible = false;
        if (_game.DiplomacyPanel != null) _game.DiplomacyPanel.Visible = false;
        if (_game.Dashboard != null) _game.Dashboard.Visible = false;
    }
}