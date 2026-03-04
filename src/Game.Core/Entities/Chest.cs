// src/Game.Core/Entities/Chest.cs

using Game.Core.Items;

namespace Game.Core.Entities;

/// <summary>
/// A chest world object placed during dungeon generation.
/// The player must stand adjacent and press E to open it.
/// Once opened, it spawns its loot directly into the player's inventory.
/// 
/// Chests block movement (you can't walk through them) and can only
/// be opened once.
/// </summary>
public class Chest : Entity
{
    /// <summary>Has this chest already been opened?</summary>
    public bool IsOpen { get; private set; }

    /// <summary>The item this chest contains.</summary>
    public ItemDef LootDef { get; }

    /// <summary>How many of the item.</summary>
    public int LootCount { get; }

    public Chest(ItemDef lootDef, int lootCount = 1)
    {
        LootDef = lootDef;
        LootCount = lootCount;
        Name = "Chest";
        DefId = "base:chest";
        BlocksMovement = true; // can't walk through chests
    }

    /// <summary>
    /// Open the chest and return the loot.
    /// Returns null if already opened.
    /// </summary>
    public (ItemDef Def, int Count)? Open()
    {
        if (IsOpen) return null;

        IsOpen = true;
        return (LootDef, LootCount);
    }
}