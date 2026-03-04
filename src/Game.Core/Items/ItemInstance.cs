// src/Game.Core/Items/ItemInstance.cs

namespace Game.Core.Items;

/// <summary>
/// A runtime instance of an item. References its definition and tracks
/// mutable state like stack count. This is what lives in the inventory
/// and on the ground.
/// </summary>
public class ItemInstance
{
    /// <summary>The item definition this instance was created from.</summary>
    public ItemDef Def { get; }

    /// <summary>Current stack count (1 for non-stackable items).</summary>
    public int Count { get; set; } = 1;

    public ItemInstance(ItemDef def, int count = 1)
    {
        Def = def;
        Count = Math.Clamp(count, 1, def.Stackable ? def.MaxStack : 1);
    }

    /// <summary>
    /// Try to add more items to this stack.
    /// Returns the number actually added (remainder didn't fit).
    /// </summary>
    public int TryStack(int amount)
    {
        if (!Def.Stackable) return 0;

        int space = Def.MaxStack - Count;
        int added = Math.Min(amount, space);
        Count += added;
        return added;
    }

    /// <summary>
    /// Remove items from this stack.
    /// Returns the number actually removed.
    /// </summary>
    public int TryRemove(int amount)
    {
        int removed = Math.Min(amount, Count);
        Count -= removed;
        return removed;
    }

    /// <summary>Is this stack empty (should be removed from inventory)?</summary>
    public bool IsEmpty => Count <= 0;

    public override string ToString()
        => Def.Stackable && Count > 1
            ? $"{Def.Name} x{Count}"
            : Def.Name;
}