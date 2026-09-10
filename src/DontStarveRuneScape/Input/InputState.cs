namespace DontStarveRuneScape.Input;

/// <summary>
/// Per-frame input state snapshot.
/// Held movement/camera flags survive frames; one-shot panel/interact/zoom flags last one frame.
/// </summary>
public sealed class InputState
{
    // Movement (held)
    public bool MoveUp { get; set; }
    public bool MoveDown { get; set; }
    public bool MoveLeft { get; set; }
    public bool MoveRight { get; set; }

    // Camera orbit (held)
    public bool OrbitCCW { get; set; }
    public bool OrbitCW { get; set; }
    public bool OrbitTiltUp { get; set; }
    public bool OrbitTiltDown { get; set; }

    // One-shot actions (consumed each frame)
    public bool Interact { get; set; }
    public bool Confirm { get; set; }
    public bool LightFire { get; set; }
    public bool Attack { get; set; }
    public bool SaveGame { get; set; }

    // Panel toggles (one-shot)
    public bool OpenInventory { get; set; }
    public bool OpenSkillPanel { get; set; }
    public bool OpenCrafting { get; set; }
    public bool OpenBuildingPanel { get; set; }
    public bool OpenQuestPanel { get; set; }
    public bool OpenTradePanel { get; set; }
    public bool OpenRecruitPanel { get; set; }
    public bool OpenDiplomacyPanel { get; set; }
    public bool OpenGearPanel { get; set; }
    public bool ClosePanel { get; set; }

    // Hotbar (one-shot)
    public int HotbarSlot { get; set; } = -1; // 0-7, -1 = none

    // Mouse
    public float MouseX { get; set; }
    public float MouseY { get; set; }
    public bool MouseLeftClick { get; set; }
    public bool MouseRightClick { get; set; }
    public float ZoomDelta { get; set; } // Mouse wheel

    // Click-to-move target (set by router)
    public bool HasClickTarget { get; set; }
    public float ClickTargetX { get; set; }
    public float ClickTargetY { get; set; }

    /// <summary>
    /// Clear one-shot flags at end of frame.
    /// </summary>
    public void ClearFrame()
    {
        Interact = false;
        Confirm = false;
        LightFire = false;
        Attack = false;
        SaveGame = false;

        OpenInventory = false;
        OpenSkillPanel = false;
        OpenCrafting = false;
        OpenBuildingPanel = false;
        OpenQuestPanel = false;
        OpenTradePanel = false;
        OpenRecruitPanel = false;
        OpenDiplomacyPanel = false;
        OpenGearPanel = false;
        ClosePanel = false;

        HotbarSlot = -1;
        MouseLeftClick = false;
        MouseRightClick = false;
        ZoomDelta = 0f;
        HasClickTarget = false;
    }
}