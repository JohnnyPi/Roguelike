// src/Game.Core/Entities/WorldItem.cs

using Game.Core.Items;

namespace Game.Core.Entities;

/// <summary>
/// An item lying on the dungeon floor. When the player walks onto
/// the same tile, it's automatically picked up and added to inventory.
/// 
/// WorldItems don't block movement — the player walks right over them.
/// </summary>
public class WorldItem : Entity
{
    /// <summary>The item definition this drop represents.</summary>
    public ItemDef ItemDef { get; }

    /// <summary>How many items in this pile (for stackable items).</summary>
    public int Count { get; }

    public WorldItem(ItemDef itemDef, int count = 1)
    {
        ItemDef = itemDef;
        Count = count;
        Name = itemDef.Name;
        DefId = itemDef.Id;
        BlocksMovement = false; // player walks over items
    }
}