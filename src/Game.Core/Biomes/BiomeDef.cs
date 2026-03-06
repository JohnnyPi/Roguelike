// src/Game.Core/Biomes/BiomeDef.cs
//
// Immutable biome definition loaded from YAML.
// Three-axis lookup: elevation x moisture x temperature.
//
// Elevation-only biomes (water, beach, peaks) leave Moisture and Temperature
// at their defaults (0..1) so they match any value -- fully backward compatible.
//
// Temperature is derived from elevation (lapse rate) + a per-island offset so
// highland areas are always cold and coastal areas warm.

namespace Game.Core.Biomes;

public sealed class BiomeDef
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string TileId { get; init; } = string.Empty;

    public float ElevationMin { get; init; } = 0.0f;
    public float ElevationMax { get; init; } = 1.0f;

    /// <summary>0 = fully arid/dry, 1 = fully wet/tropical.</summary>
    public float MoistureMin { get; init; } = 0.0f;
    public float MoistureMax { get; init; } = 1.0f;

    /// <summary>
    /// 0 = arctic/cold, 1 = tropical/hot.
    /// Derived from elevation lapse rate + island-local noise offset.
    /// Leave both at 0..1 for biomes that don't care about temperature.
    /// </summary>
    public float TemperatureMin { get; init; } = 0.0f;
    public float TemperatureMax { get; init; } = 1.0f;

    public bool Walkable { get; init; } = true;
    public List<string> Tags { get; init; } = new();
}