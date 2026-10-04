namespace DontStarveRuneScape.Inventory;

/// <summary>Small shared storage contract for player bags and colony stores.</summary>
public interface IItemStorage
{
    bool AddItem(string itemId, int quantity);
    bool CanAdd(string itemId, int quantity);
    int GetItemQuantity(string itemId);
    bool RemoveItem(string itemId, int quantity);
}
