// src/Game.Core/Biomes/BiomeDef.cs
//
// Immutable biome definition loaded from YAML.
// Used by OverworldGenerator to map elevation values to tile types.
// Biomes are sorted by ElevationMax — the generator picks the first
// biome whose ElevationMax exceeds the sampled elevation.

namespace Game.Core.Biomes;

public sealed class BiomeDef
{
    /// <summary>Unique content ID, e.g. "base:grassland"</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Display name for UI/debug</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Tile definition ID to use for this biome, e.g. "base:grass"</summary>
    public string TileId { get; init; } = string.Empty;

    /// <summary>
    /// Upper elevation bound (0..1). The generator assigns this biome
    /// when elevation is below this value (and above the previous biome's max).
    /// </summary>
    public float ElevationMax { get; init; } = 1.0f;

    /// <summary>Whether tiles in this biome are walkable by default.</summary>
    public bool Walkable { get; init; } = true;

    /// <summary>Tags for flexible rule matching.</summary>
    public List<string> Tags { get; init; } = new();
}