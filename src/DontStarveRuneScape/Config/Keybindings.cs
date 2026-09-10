namespace DontStarveRuneScape.Config;

using Silk.NET.SDL;

/// <summary>
/// Single source of truth for all keybindings.
/// Maps action names to SDL key constants.
/// Used by InputManager, InputRouter, and UI panels for hints.
/// </summary>
public static class Keybindings
{
    // ─── Keybinding Constants ──────────────────────────────────────────────

    /// <summary>
    /// Gameplay actions (used by InputManager)
    /// </summary>
    public static readonly Dictionary<string, Key> KeybindActions = new()
    {
        // Movement
        ["move_up"] = Key.W,
        ["move_down"] = Key.S,
        ["move_left"] = Key.A,
        ["move_right"] = Key.D,

        // Panels
        ["open_inventory"] = Key.C,
        ["open_inventory_alt"] = Key.I,
        ["open_skill_panel"] = Key.Tab,
        ["open_crafting"] = Key.H,
        ["open_building_panel"] = Key.B,
        ["open_quest_panel"] = Key.U,
        ["open_trade_panel"] = Key.T,
        ["open_recruit_panel"] = Key.K,
        ["open_diplomacy_panel"] = Key.L,
        ["open_gear_panel"] = Key.G,
        ["close_crafting"] = Key.Q,
        ["close_panel"] = Key.Escape,

        // Interaction
        ["interact"] = Key.E,
        ["confirm"] = Key.Return,
        ["confirm_alt"] = Key.Space,
        ["light_fire"] = Key.F,
        ["attack"] = Key.J,

        // Camera
        ["orbit_ccw"] = Key.Left,
        ["orbit_cw"] = Key.Right,
        ["orbit_tilt_up"] = Key.Up,
        ["orbit_tilt_down"] = Key.Down,
        ["orbit_tilt_up_alt"] = Key.PageUp,
        ["orbit_tilt_down_alt"] = Key.PageDown,

        // Hotbar (1-8)
        ["hotbar_1"] = Key.N1,
        ["hotbar_2"] = Key.N2,
        ["hotbar_3"] = Key.N3,
        ["hotbar_4"] = Key.N4,
        ["hotbar_5"] = Key.N5,
        ["hotbar_6"] = Key.N6,
        ["hotbar_7"] = Key.N7,
        ["hotbar_8"] = Key.N8,

        // Save
        ["save_game"] = Key.F5,

        // Quest-specific (one-shot)
        ["quest_accept"] = Key.Return,
        ["faction_negotiate"] = Key.Return,
        ["trade_accept"] = Key.Return,
    };

    /// <summary>
    /// Panel-specific keybinds (used by panels for hints)
    /// These are actions that only apply when a specific panel is open
    /// </summary>
    public static readonly Dictionary<GameState, string[]> KeybindPanelActions = new()
    {
        [GameState.InventoryOpen] =
        [
            "move_up", "move_down", "move_left", "move_right",
            "close_panel",
            "interact",  // Use item
        ],
        [GameState.SkillPanel] =
        [
            "move_up", "move_down", "move_left", "move_right",
            "close_panel",
            "interact",  // Allocate stat
        ],
        [GameState.CraftingPanel] =
        [
            "move_up", "move_down", "move_left", "move_right",
            "close_panel",
            "interact",  // Craft item
            "confirm",   // Craft
        ],
        [GameState.BuildingPanel] =
        [
            "move_up", "move_down", "move_left", "move_right",
            "close_panel",
            "interact",  // Select structure
        ],
        [GameState.GearPanel] =
        [
            "move_up", "move_down", "move_left", "move_right",
            "close_panel",
        ],
        [GameState.TradePanel] =
        [
            "move_up", "move_down", "move_left", "move_right",
            "close_panel",
            "interact",  // Buy/sell
            "confirm",   // Accept trade
        ],
        [GameState.QuestPanel] =
        [
            "move_up", "move_down", "move_left", "move_right",
            "close_panel",
            "interact",  // Accept quest
            "confirm",   // Accept quest
        ],
        [GameState.RecruitPanel] =
        [
            "move_up", "move_down", "move_left", "move_right",
            "close_panel",
            "interact",  // Recruit
        ],
        [GameState.DiplomacyPanel] =
        [
            "move_up", "move_down", "move_left", "move_right",
            "close_panel",
            "interact",  // Negotiate
            "confirm",   // Accept
        ],
    };

    /// <summary>
    /// Human-readable names for UI hints
    /// </summary>
    public static readonly Dictionary<string, string> KeybindDisplayNames = new()
    {
        ["move_up"] = "W",
        ["move_down"] = "S",
        ["move_left"] = "A",
        ["move_right"] = "D",
        ["open_inventory"] = "C",
        ["open_skill_panel"] = "TAB",
        ["open_crafting"] = "H",
        ["open_building_panel"] = "B",
        ["open_quest_panel"] = "U",
        ["open_trade_panel"] = "T",
        ["open_recruit_panel"] = "K",
        ["open_diplomacy_panel"] = "L",
        ["open_gear_panel"] = "G",
        ["close_crafting"] = "Q",
        ["close_panel"] = "ESC",
        ["interact"] = "E",
        ["confirm"] = "ENTER",
        ["confirm_alt"] = "SPACE",
        ["light_fire"] = "F",
        ["attack"] = "J",
        ["orbit_ccw"] = "←",
        ["orbit_cw"] = "→",
        ["orbit_tilt_up"] = "↑",
        ["orbit_tilt_down"] = "↓",
        ["orbit_tilt_up_alt"] = "PGUP",
        ["orbit_tilt_down_alt"] = "PGDN",
        ["hotbar_1"] = "1",
        ["hotbar_2"] = "2",
        ["hotbar_3"] = "3",
        ["hotbar_4"] = "4",
        ["hotbar_5"] = "5",
        ["hotbar_6"] = "6",
        ["hotbar_7"] = "7",
        ["hotbar_8"] = "8",
        ["save_game"] = "F5",
        ["quest_accept"] = "ENTER",
        ["faction_negotiate"] = "ENTER",
        ["trade_accept"] = "ENTER",
    };

    /// <summary>
    /// Get the SDL key constant for a given action name.
    /// </summary>
    public static Key GetKeyForAction(string action)
    {
        return KeybindActions.TryGetValue(action, out var key) ? key : Key.Unknown;
    }

    /// <summary>
    /// Get the list of action names available in a panel state.
    /// </summary>
    public static string[] GetActionsForPanel(GameState state)
    {
        return KeybindPanelActions.TryGetValue(state, out var actions) ? actions : [];
    }

    /// <summary>
    /// Get the human-readable display name for an action.
    /// </summary>
    public static string GetDisplayName(string action)
    {
        return KeybindDisplayNames.TryGetValue(action, out var name) ? name : action.ToUpperInvariant();
    }
}