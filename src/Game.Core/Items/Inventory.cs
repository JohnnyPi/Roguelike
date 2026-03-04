// src/Game.Core/Items/Inventory.cs

namespace Game.Core.Items;

/// <summary>
/// Manages a collection of item instances with automatic stacking.
/// Used by the Player class. No size limit for the first playable —
/// a slot cap can be added later.
/// </summary>
public class Inventory
{
    private readonly List<ItemInstance> _items = new();

    /// <summary>Read-only view of all items in the inventory.</summary>
    public IReadOnlyList<ItemInstance> Items => _items;

    /// <summary>Total number of individual items (sum of all stacks).</summary>
    public int TotalItemCount
    {
        get
        {
            int total = 0;
            foreach (var item in _items)
                total += item.Count;
            return total;
        }
    }

    /// <summary>Number of inventory slots in use.</summary>
    public int SlotCount => _items.Count;

    /// <summary>
    /// Add an item to the inventory. Stackable items merge into
    /// existing stacks when possible; overflow creates new slots.
    /// </summary>
    public void Add(ItemDef def, int count = 1)
    {
        int remaining = count;

        if (def.Stackable)
        {
            // Try to stack into existing slots first
            foreach (var existing in _items)
            {
                if (existing.Def.Id == def.Id && remaining > 0)
                {
                    remaining -= existing.TryStack(remaining);
                }
            }
        }

        // Create new slot(s) for whatever didn't fit
        while (remaining > 0)
        {
            int slotAmount = def.Stackable
                ? Math.Min(remaining, def.MaxStack)
                : 1;

            _items.Add(new ItemInstance(def, slotAmount));
            remaining -= slotAmount;
        }
    }

    /// <summary>
    /// Remove a number of items by def ID.
    /// Returns the number actually removed.
    /// </summary>
    public int Remove(string defId, int count = 1)
    {
        int remaining = count;

        for (int i = _items.Count - 1; i >= 0 && remaining > 0; i--)
        {
            if (_items[i].Def.Id != defId) continue;

            remaining -= _items[i].TryRemove(remaining);

            if (_items[i].IsEmpty)
                _items.RemoveAt(i);
        }

        return count - remaining;
    }

    /// <summary>Check how many of a specific item the player has.</summary>
    public int CountOf(string defId)
    {
        int total = 0;
        foreach (var item in _items)
        {
            if (item.Def.Id == defId)
                total += item.Count;
        }
        return total;
    }

    /// <summary>Does the inventory contain at least one of this item?</summary>
    public bool Has(string defId) => CountOf(defId) > 0;

    /// <summary>Clear all items (used on death or debug reset).</summary>
    public void Clear() => _items.Clear();
}