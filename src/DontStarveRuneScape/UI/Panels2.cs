namespace DontStarveRuneScape.UI;

using Silk.NET.OpenGL;
using DontStarveRuneScape.Building;
using DontStarveRuneScape.Skills;
using DontStarveRuneScape.Inventory;
using DontStarveRuneScape.Data;

/// <summary>
/// InventoryPanel — Inventory UI panel.
/// </summary>
public sealed class InventoryPanel
{
    public bool Visible { get; set; } = false;
    public void Render(GL gl, int screenWidth, int screenHeight) { }
}

/// <summary>
/// SkillPanel — Skills UI panel.
/// </summary>
public sealed class SkillPanel
{
    public bool Visible { get; set; } = false;
    public void Render(GL gl, int screenWidth, int screenHeight) { }
}

/// <summary>
/// CraftingPanel — Crafting UI panel.
/// </summary>
public sealed class CraftingPanel
{
    public bool Visible { get; set; } = false;
    public void Render(GL gl, int screenWidth, int screenHeight) { }
}

/// <summary>
/// BuildingPanel — Building/construction UI panel.
/// </summary>
public sealed class BuildingPanel
{
    public bool Visible { get; set; } = false;
    public void Render(GL gl, int screenWidth, int screenHeight, BuildingSystem buildingSystem, SkillManager skillManager) { }
}

/// <summary>
/// GearPanel — Equipment/gear UI panel.
/// </summary>
public sealed class GearPanel
{
    public bool Visible { get; set; } = false;
    public void Render(GL gl, int screenWidth, int screenHeight, PlayerGear gear, Inventory inventory, Dictionary<string, GearItem> gearDefs) { }
}

/// <summary>
/// DashboardPanel — Unified dashboard with 5 tabs.
/// </summary>
public sealed class DashboardPanel
{
    public bool Visible { get; set; } = false;
    public void SetActive(string tab) { }
    public void Render(GL gl, int screenWidth, int screenHeight) { }
}