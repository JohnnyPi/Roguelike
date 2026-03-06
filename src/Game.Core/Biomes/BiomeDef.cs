// src/Game.Core/Biomes/BiomeDef.cs
//
// Immutable biome definition loaded from YAML.
// Used by OverworldGenerator to map elevation + moisture values to tile types.
//
// Elevation-only biomes (water, beach, mountain peaks) leave MoistureMin/Max
// at their defaults (0..1) so they match any moisture value — fully backward compatible.
//
// Two-axis biomes specify both elevation AND moisture ranges. The generator
// picks the LAST biome in the sorted list whose elevation AND moisture ranges
// both contain the sampled values. Biomes should be ordered so more specific
// (narrower moisture range) entries come after broader ones.

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
    /// when elevation is below this value (and above the previous biome's ElevationMin).
    /// </summary>
    public float ElevationMax { get; init; } = 1.0f;

    /// <summary>Lower elevation bound (0..1). Defaults to 0 for backward compatibility.</summary>
    public float ElevationMin { get; init; } = 0.0f;

    /// <summary>
    /// Lower moisture bound (0..1). 0 = arid/dry side. Defaults to 0.
    /// Moisture is driven by prevailing wind + a noise field.
    /// </summary>
    public float MoistureMin { get; init; } = 0.0f;

    /// <summary>
    /// Upper moisture bound (0..1). 1 = wet/tropical side. Defaults to 1.
    /// Biomes that span the full range (0..1) match regardless of moisture — used
    /// for water, beach, cliffs, and other purely elevation-driven biomes.
    /// </summary>
    public float MoistureMax { get; init; } = 1.0f;

    /// <summary>Whether tiles in this biome are walkable by default.</summary>
    public bool Walkable { get; init; } = true;

    /// <summary>Tags for flexible rule matching.</summary>
    public List<string> Tags { get; init; } = new();
}