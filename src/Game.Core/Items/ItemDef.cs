// src/Game.Core/Items/ItemDef.cs

namespace Game.Core.Items;

/// <summary>
/// Immutable definition of an item type, loaded from YAML.
/// This is the "def" — the template. ItemInstance is the runtime copy.
/// 
/// Matches the schema from content/BasePack/items/basic_items.yml:
///   - id, name, tags, stackable, maxStack, effect
/// </summary>
public sealed class ItemDef
{
    /// <summary>Unique content ID, e.g. "base:gold_coin"</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Display name for UI/log messages</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Tags for flexible queries, e.g. ["currency", "common"]</summary>
    public List<string> Tags { get; init; } = new();

    /// <summary>Can multiple instances occupy one inventory slot?</summary>
    public bool Stackable { get; init; }

    /// <summary>Maximum stack size (only relevant if Stackable is true)</summary>
    public int MaxStack { get; init; } = 1;

    /// <summary>
    /// Effect type applied on use (e.g. "heal"). Empty string = no effect.
    /// Phase 5 supports "heal" only; more types added later.
    /// </summary>
    public string EffectType { get; init; } = string.Empty;

    /// <summary>Effect magnitude (e.g. heal amount). Interpretation depends on EffectType.</summary>
    public int EffectAmount { get; init; }

    /// <summary>
    /// Packed RGBA color for placeholder vector rendering.
    /// Format: "#RRGGBB" — items are drawn as small colored squares on the map.
    /// </summary>
    public string Color { get; init; } = "#FFFF00"; // yellow = default item color
}