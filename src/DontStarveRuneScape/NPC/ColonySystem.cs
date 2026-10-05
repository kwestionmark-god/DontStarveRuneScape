namespace DontStarveRuneScape.NPC;

using DontStarveRuneScape.Inventory;
using DontStarveRuneScape.World;

/// <summary>
/// One player-founded settlement with a bounded stockpile. Stored quantities
/// share the existing item ids, so crafting and survival can consume them
/// through explicit transfers rather than a second item catalogue.
/// </summary>
public sealed class ColonySystem : IItemStorage
{
    public const int DefaultStorageCapacity = 500;

    public bool IsFounded { get; private set; }
    public int AnchorTileX { get; private set; }
    public int AnchorTileY { get; private set; }
    public int WorkRadiusTiles { get; set; } = 10;
    public int StorageCapacity { get; private set; } = DefaultStorageCapacity;
    public Dictionary<string, int> Stockpile { get; } = [];
    public int StoredUnits => Stockpile.Values.Sum();
    public int FreeCapacity => Math.Max(0, StorageCapacity - StoredUnits);
    /// <summary>Shared worker task list with reservations. Runtime-only:
    /// tasks re-derive from the live world after a load.</summary>
    public ColonyTaskBoard TaskBoard { get; } = new();

    public bool FoundAt(float worldX, float worldY, TileMap? world)
    {
        if (world == null || world.IsCave) return false;
        var tile = world.GetTileAtWorld(worldX, worldY);
        if (tile == null || tile.HasWater) return false;

        AnchorTileX = tile.X;
        AnchorTileY = tile.Y;
        IsFounded = true;
        return true;
    }

    public bool CanStore(string itemId, int quantity)
        => IsFounded && !string.IsNullOrWhiteSpace(itemId) && quantity > 0 && FreeCapacity >= quantity;

    public bool Store(string itemId, int quantity)
    {
        if (!CanStore(itemId, quantity)) return false;
        Stockpile.TryGetValue(itemId, out int current);
        Stockpile[itemId] = current + quantity;
        return true;
    }

    public bool AddItem(string itemId, int quantity) => Store(itemId, quantity);

    public bool CanAdd(string itemId, int quantity) => CanStore(itemId, quantity);

    public int GetItemQuantity(string itemId)
        => Stockpile.TryGetValue(itemId, out int quantity) ? quantity : 0;

    public bool RemoveItem(string itemId, int quantity)
    {
        int stored = GetItemQuantity(itemId);
        if (quantity <= 0 || stored < quantity) return false;
        if (stored == quantity) Stockpile.Remove(itemId);
        else Stockpile[itemId] = stored - quantity;
        return true;
    }

    /// <summary>Move as much of one item as possible from player inventory.</summary>
    public int Deposit(string itemId, Inventory? inventory)
    {
        if (!IsFounded || inventory == null || FreeCapacity <= 0) return 0;
        int quantity = Math.Min(inventory.GetItemQuantity(itemId), FreeCapacity);
        if (quantity <= 0 || !inventory.RemoveItem(itemId, quantity)) return 0;
        Store(itemId, quantity);
        return quantity;
    }

    /// <summary>Move as much of one item as possible into player inventory.</summary>
    public int Withdraw(string itemId, Inventory? inventory)
    {
        if (!IsFounded || inventory == null || !Stockpile.TryGetValue(itemId, out int stored))
            return 0;

        int quantity = stored;
        while (quantity > 0 && !inventory.CanAdd(itemId, quantity)) quantity--;
        if (quantity <= 0 || !inventory.AddItem(itemId, quantity)) return 0;

        stored -= quantity;
        if (stored == 0) Stockpile.Remove(itemId);
        else Stockpile[itemId] = stored;
        return quantity;
    }

    public ColonySnapshot GetSnapshot() => new()
    {
        IsFounded = IsFounded,
        AnchorTileX = AnchorTileX,
        AnchorTileY = AnchorTileY,
        WorkRadiusTiles = WorkRadiusTiles,
        StorageCapacity = StorageCapacity,
        Stockpile = new Dictionary<string, int>(Stockpile),
    };

    public void RestoreSnapshot(ColonySnapshot? snapshot, TileMap? world)
    {
        Stockpile.Clear();
        if (snapshot == null) return;

        StorageCapacity = Math.Max(1, snapshot.StorageCapacity);
        WorkRadiusTiles = Math.Clamp(snapshot.WorkRadiusTiles, 1, 64);
        if (snapshot.Stockpile != null)
        {
            foreach (var (itemId, quantity) in snapshot.Stockpile)
            {
                int accepted = Math.Min(Math.Max(0, quantity), FreeCapacity);
                if (!string.IsNullOrWhiteSpace(itemId) && accepted > 0)
                    Stockpile[itemId] = accepted;
            }
        }

        IsFounded = snapshot.IsFounded
            && world != null
            && !world.IsCave
            && world.GetTile(snapshot.AnchorTileX, snapshot.AnchorTileY) is { HasWater: false };
        if (IsFounded)
        {
            AnchorTileX = snapshot.AnchorTileX;
            AnchorTileY = snapshot.AnchorTileY;
        }
    }
}

public sealed class ColonySnapshot
{
    public bool IsFounded { get; set; }
    public int AnchorTileX { get; set; }
    public int AnchorTileY { get; set; }
    public int WorkRadiusTiles { get; set; } = 10;
    public int StorageCapacity { get; set; } = ColonySystem.DefaultStorageCapacity;
    public Dictionary<string, int> Stockpile { get; set; } = [];
}
