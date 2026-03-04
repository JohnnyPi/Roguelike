// src/Game.Core/Tiles/TileDef.cs

namespace Game.Core.Tiles;

/// <summary>
/// Immutable tile type definition. Loaded from YAML content packs.
/// This is the def (template), not an instance.
/// </summary>
public sealed class TileDef
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public bool Walkable { get; init; }
    public string Sprite { get; init; } = string.Empty;

    /// <summary>Packed RGBA color. Format: "#RRGGBB" or "#RRGGBBAA"</summary>
    public string Color { get; init; } = "#FF00FF";

    /// <summary>
    /// True = this tile blocks line-of-sight for Fog-of-War.
    /// Walls, trees, mountains = true.
    /// Water, grass, dungeon floor, entrances = false.
    /// KEY: water is non-walkable but transparent (you can see through it).
    /// </summary>
    public bool BlocksSight { get; init; }

    /// <summary>
    /// Logical height level for heightmap arrow rendering.
    /// 0=deep water, 1=lowland/grass, 2=hills/dirt, 3=highland/wall, 4=mountain
    /// The renderer draws slope arrows showing height transitions between neighbors.
    /// </summary>
    public int Height { get; init; } = 1;

    public List<string> Tags { get; init; } = new();
}