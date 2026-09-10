namespace DontStarveRuneScape.Core;

/// <summary>
/// Game state machine.
/// Manages discrete game states. Only one active at a time.
/// </summary>
public enum GameState
{
    /// <summary>Title screen UI. Game entry state.</summary>
    Title,

    /// <summary>Procedural world generation in progress. Loading screen rendered.</summary>
    Loading,

    /// <summary>Save file loading in progress. Shows 'Loading save...' message.</summary>
    LoadingSave,

    /// <summary>Main gameplay loop running. World rendered, systems update.</summary>
    Playing,

    /// <summary>Inventory panel is open. Update logic frozen (except hunger drain).</summary>
    InventoryOpen,

    /// <summary>Skill panel is open. Update logic frozen (except hunger drain).</summary>
    SkillPanel,

    /// <summary>Crafting panel is open. Update logic frozen (except hunger drain).</summary>
    CraftingPanel,

    /// <summary>Building placement panel is open. Update logic frozen (except hunger drain).</summary>
    BuildingPanel,

    /// <summary>Gear management panel is open. Update logic frozen (except hunger drain).</summary>
    GearPanel,

    /// <summary>Trade panel is open. Player is interacting with a merchant NPC.</summary>
    TradePanel,

    /// <summary>Quest panel is open. Update logic frozen (except hunger drain).</summary>
    QuestPanel,

    /// <summary>Recruit panel is open. Player is recruiting an NPC.</summary>
    RecruitPanel,

    /// <summary>Diplomacy panel is open. Player is interacting with a faction leader.</summary>
    DiplomacyPanel,

    /// <summary>World generation or save load failed. Shows error message.</summary>
    Error,

    /// <summary>Character/background selection screen before world generation.</summary>
    CharacterSelect,

    /// <summary>Unified tabbed menu (inventory/skills/crafting/building/gear) is open.</summary>
    DashboardOpen,
}

/// <summary>
/// Extension methods for GameState.
/// </summary>
public static class GameStateExtensions
{
    /// <summary>
    /// All panel-open states where update logic is frozen.
    /// </summary>
    public static readonly GameState[] PanelStates =
    [
        GameState.InventoryOpen, GameState.SkillPanel, GameState.CraftingPanel,
        GameState.BuildingPanel, GameState.GearPanel, GameState.TradePanel,
        GameState.QuestPanel, GameState.RecruitPanel, GameState.DiplomacyPanel,
        GameState.CharacterSelect, GameState.DashboardOpen,
    ];

    /// <summary>
    /// Check if a state is a panel state where gameplay logic is frozen.
    /// </summary>
    public static bool IsPanelState(this GameState state)
    {
        return Array.IndexOf(PanelStates, state) >= 0;
    }
}