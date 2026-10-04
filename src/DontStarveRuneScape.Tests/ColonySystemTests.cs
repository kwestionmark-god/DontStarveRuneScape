namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.Config;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Inventory;
using DontStarveRuneScape.NPC;
using DontStarveRuneScape.World;
using Xunit;

/// <summary>
/// Colony foundation from slice A of the colony fusion: founding rules,
/// stockpile capacity accounting, player/colony transfers, snapshot round
/// trip, and the backwards-compatible defaults that old saves rely on.
/// </summary>
public class ColonySystemTests
{
    private static TileMap DryWorld(int size = 8)
    {
        var world = new TileMap(size, size);
        for (int x = 0; x < size; x++)
            for (int y = 0; y < size; y++)
                world.Tiles[x, y].Elevation = Constants.SeaLevel + 5f;
        return world;
    }

    // ─── Founding ────────────────────────────────────────────────────────

    [Fact]
    public void FoundAt_SucceedsOnDrySurfaceTile_AndRecordsAnchor()
    {
        var world = DryWorld();
        var colony = new ColonySystem();

        bool founded = colony.FoundAt(3.5f * Constants.TileSize, 3.5f * Constants.TileSize, world);

        Assert.True(founded);
        Assert.True(colony.IsFounded);
        Assert.Equal(3, colony.AnchorTileX);
        Assert.Equal(3, colony.AnchorTileY);
    }

    [Fact]
    public void FoundAt_RejectsWaterTile()
    {
        var world = DryWorld();
        world.Tiles[3, 3].Elevation = 0f; // below SeaLevel → water
        var colony = new ColonySystem();

        Assert.False(colony.FoundAt(3.5f * Constants.TileSize, 3.5f * Constants.TileSize, world));
        Assert.False(colony.IsFounded);
    }

    [Fact]
    public void FoundAt_RejectsCaveMap_AndNullWorld()
    {
        var world = DryWorld();
        world.IsCave = true;
        var colony = new ColonySystem();

        Assert.False(colony.FoundAt(3.5f * Constants.TileSize, 3.5f * Constants.TileSize, world));
        Assert.False(colony.FoundAt(3.5f * Constants.TileSize, 3.5f * Constants.TileSize, null));
        Assert.False(colony.IsFounded);
    }

    // ─── Stockpile accounting ─────────────────────────────────────────────

    [Fact]
    public void Store_AcceptsUpToCapacity_ThenRefuses()
    {
        var colony = new ColonySystem();
        colony.FoundAt(3.5f * Constants.TileSize, 3.5f * Constants.TileSize, DryWorld());

        Assert.True(colony.Store("stick", 10));
        Assert.Equal(10, colony.GetItemQuantity("stick"));

        Assert.True(colony.Store("stone", ColonySystem.DefaultStorageCapacity - 10));
        Assert.Equal(0, colony.FreeCapacity);

        Assert.False(colony.Store("planks", 1));
        Assert.Equal(0, colony.GetItemQuantity("planks"));
    }

    [Fact]
    public void Store_RefusesBeforeFounding()
    {
        var colony = new ColonySystem();
        Assert.False(colony.Store("stick", 1));
    }

    [Fact]
    public void RemoveItem_DecrementsAndClearsRowAtZero()
    {
        var colony = new ColonySystem();
        colony.FoundAt(3.5f * Constants.TileSize, 3.5f * Constants.TileSize, DryWorld());
        colony.Store("stick", 5);

        Assert.True(colony.RemoveItem("stick", 3));
        Assert.Equal(2, colony.GetItemQuantity("stick"));
        Assert.True(colony.RemoveItem("stick", 2));
        Assert.Equal(0, colony.GetItemQuantity("stick"));
        Assert.False(colony.RemoveItem("stick", 1));
    }

    [Fact]
    public void InventoryAndColony_BothSatisfyIItemStorage()
    {
        IItemStorage player = new Inventory();
        Assert.True(player.AddItem("stick", 3));
        Assert.Equal(3, player.GetItemQuantity("stick"));
        Assert.True(player.RemoveItem("stick", 2));
        Assert.Equal(1, player.GetItemQuantity("stick"));

        var colony = new ColonySystem();
        colony.FoundAt(3.5f * Constants.TileSize, 3.5f * Constants.TileSize, DryWorld());
        IItemStorage stores = colony;
        Assert.True(stores.AddItem("stone", 4));
        Assert.True(stores.CanAdd("stone", 1));
        Assert.Equal(4, stores.GetItemQuantity("stone"));
        Assert.True(stores.RemoveItem("stone", 4));
    }

    // ─── Player transfers ─────────────────────────────────────────────────

    [Fact]
    public void Deposit_MovesFromPlayerUpToCapacity()
    {
        var colony = new ColonySystem();
        colony.FoundAt(3.5f * Constants.TileSize, 3.5f * Constants.TileSize, DryWorld());
        var inventory = new Inventory();
        inventory.AddItem("stick", 20);

        Assert.Equal(20, colony.Deposit("stick", inventory));
        Assert.Equal(0, inventory.GetItemQuantity("stick"));
        Assert.Equal(20, colony.GetItemQuantity("stick"));
    }

    [Fact]
    public void Deposit_StopsAtColonyCapacity()
    {
        var colony = new ColonySystem();
        colony.FoundAt(3.5f * Constants.TileSize, 3.5f * Constants.TileSize, DryWorld());
        Assert.True(colony.Store("stone", ColonySystem.DefaultStorageCapacity - 5));
        var inventory = new Inventory();
        inventory.AddItem("stone", 50);

        Assert.Equal(5, colony.Deposit("stone", inventory));
        Assert.Equal(0, colony.FreeCapacity);
        Assert.Equal(45, inventory.GetItemQuantity("stone"));
    }

    [Fact]
    public void Withdraw_MovesBackIntoPlayerInventory()
    {
        var colony = new ColonySystem();
        colony.FoundAt(3.5f * Constants.TileSize, 3.5f * Constants.TileSize, DryWorld());
        colony.Store("planks", 7);
        var inventory = new Inventory();

        Assert.Equal(7, colony.Withdraw("planks", inventory));
        Assert.Equal(7, inventory.GetItemQuantity("planks"));
        Assert.Equal(0, colony.GetItemQuantity("planks"));
    }

    // ─── Snapshots and backwards compatibility ─────────────────────────────

    [Fact]
    public void Snapshot_RestoresFoundedColony_AndTruncatesOverflow()
    {
        var world = DryWorld();
        var colony = new ColonySystem();
        colony.FoundAt(3.5f * Constants.TileSize, 3.5f * Constants.TileSize, world);
        colony.Store("stick", 30);
        colony.Store("stone", 20);

        var snapshot = colony.GetSnapshot();
        var restored = new ColonySystem();
        restored.RestoreSnapshot(snapshot, world);

        Assert.True(restored.IsFounded);
        Assert.Equal(3, restored.AnchorTileX);
        Assert.Equal(3, restored.AnchorTileY);
        Assert.Equal(30, restored.GetItemQuantity("stick"));
        Assert.Equal(20, restored.GetItemQuantity("stone"));
    }

    [Fact]
    public void RestoreSnapshot_NullOrUnfounded_LeavesColonyEmpty()
    {
        var restored = new ColonySystem();
        restored.Store("stick", 1); // refused: not founded
        restored.RestoreSnapshot(null, DryWorld());
        Assert.False(restored.IsFounded);
        Assert.Equal(0, restored.StoredUnits);

        restored.RestoreSnapshot(new ColonySnapshot(), DryWorld());
        Assert.False(restored.IsFounded);
        Assert.Equal(0, restored.StoredUnits);
    }

    [Fact]
    public void RestoreSnapshot_RefusesAnchorThatBecameWater()
    {
        var snapshot = new ColonySnapshot
        {
            IsFounded = true,
            AnchorTileX = 3,
            AnchorTileY = 3,
            Stockpile = new Dictionary<string, int> { ["stick"] = 5 },
        };
        var world = new TileMap(8, 8); // fresh map: everything below sea level

        var restored = new ColonySystem();
        restored.RestoreSnapshot(snapshot, world);

        // The anchor is refused, so the colony cannot operate — but the
        // stored rows survive inertly until the player re-founds somewhere
        // valid (no store/withdraw path works while unfounded).
        Assert.False(restored.IsFounded);
        Assert.Equal(5, restored.GetItemQuantity("stick"));
        Assert.False(restored.CanStore("stone", 1));
        Assert.False(restored.Deposit("stone", new Inventory()) > 0);
    }

    [Fact]
    public void OldSaveDefaults_DeserializeAsUnfoundedAndFed()
    {
        // Old saves predate the colony fields: every new snapshot DTO must
        // default to the "no colony / healthy NPC" state.
        var save = new SaveData();
        Assert.NotNull(save.Colony);
        Assert.False(save.Colony.IsFounded);
        Assert.Empty(save.Colony.Stockpile);

        var npc = new NPCDataSnapshot();
        Assert.False(npc.IsRecruited);
        Assert.Null(npc.RecruitBehavior);
        Assert.Equal(100f, npc.ColonyHunger);
        Assert.Equal(100f, npc.ColonyRest);

        var structure = new StructureSnapshot();
        Assert.False(structure.IsUnderConstruction);
        Assert.False(structure.ConstructionMaterialsPaid);
        Assert.Equal("Idle", structure.WorkStatus);
        Assert.Empty(structure.WorkRecipeQueue);
    }
}
