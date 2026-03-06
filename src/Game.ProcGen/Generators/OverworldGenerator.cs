// src/Game.ProcGen/Generators/OverworldGenerator.cs
//
// Partial class split:
//   OverworldGenerator.cs           -- this file: config, properties, Generate() overloads, tile lookup
//   OverworldGenerator.Noise.cs     -- noise builders, BuildMaskedElevation, BuildMoistureField
//   OverworldGenerator.Volcanoes.cs -- volcano stamping, lava flows, caldera, NudgeTowardDownhill
//   OverworldGenerator.Rivers.cs    -- CarveRivers, BuildRiverSet, WalkRiverDownhill
//   OverworldGenerator.Chunks.cs    -- ChunkNoiseContext, BuildNoiseContext, GenerateChunk
//   OverworldGenerator.Placement.cs -- FinishGeneration, spawn/entrance placement (TileMap + array)
//   OverworldGenerator.Features.cs  -- GenerateFeatureSets, WorldFeatureSets

using Game.Core.Biomes;
using Game.Core.Map;
using Game.Core.Tiles;
using Game.Core.WorldGen;
using System;
using System.Collections.Generic;

namespace Game.ProcGen.Generators;

/// <summary>
/// Overworld island generator.
///
/// Generation pipeline:
///   1. Build FBm elevation noise + coast warp noise.
///   2. Multiply elevation by a radial island mask (warped coastline).
///   3. Build a moisture field: base FBm noise + directional wind gradient.
///      Wind direction is controlled by PrevailingWindAngleDeg (0=North, 90=East).
///      Windward slopes receive high moisture; leeward slopes receive low moisture.
///   4. Map (elevation, moisture) to biomes -- two-axis Whittaker-style lookup.
///      Biomes with MoistureMin/Max spanning 0..1 match any moisture (water, peaks, etc.)
///   5. Find a walkable spawn near the center island.
///   6. Place one or more dungeon entrances inland with min-spacing constraints.
///
/// Island shape knobs:
///   IslandRadiusScale      - fraction of half-map used as the island radius (0..1)
///   IslandFalloffExp       - how sharply land drops to ocean at edges (1=linear, 2=smooth, 3=cliff)
///   CoastWarpStrength      - how jagged the coastline is (0=perfect circle, 0.25=moderate, 0.5=extreme)
///
/// Moisture knobs:
///   PrevailingWindAngleDeg - wind direction in degrees (0=blows from North, 90=blows from East)
///                            Windward side = wet (jungle/rainforest). Leeward = dry (savanna/scrub).
///   WindGradientStrength   - how strongly the wind gradient biases moisture (0=off, 1=full dominance)
///   MoistureNoiseWeight    - blend between pure-gradient (0) and noisy moisture (1). 0.5 is natural.
///
/// Entrance placement:
///   EntranceCount          - number of dungeon entrances to place (default 1)
///   MinEntranceSpacing     - minimum Manhattan distance between entrances (default 25)
///
/// Backward compatible: the no-biomes Generate() overload still works.
/// </summary>
public partial class OverworldGenerator
{
    // ── Factory ──────────────────────────────────────────────────────

    /// <summary>
    /// Create a generator pre-configured from an <see cref="IslandGenConfig"/>.
    /// The returned instance is fully initialized; call Generate() directly.
    /// </summary>
    public static OverworldGenerator FromConfig(IslandGenConfig cfg) => new()
    {
        MapWidth = cfg.MapWidth,
        MapHeight = cfg.MapHeight,
        Frequency = cfg.Frequency,
        Octaves = cfg.Octaves,
        Lacunarity = cfg.Lacunarity,
        Gain = cfg.Gain,
        RiverCount = cfg.RiverCount,
        IslandCount = cfg.IslandCount,
        IslandRadiusScale = cfg.IslandRadiusScale,
        IslandFalloffExp = cfg.IslandFalloffExp,
        CoastWarpStrength = cfg.CoastWarpStrength,
        PrevailingWindAngleDeg = cfg.PrevailingWindAngleDeg,
        WindGradientStrength = cfg.WindGradientStrength,
        MoistureNoiseWeight = cfg.MoistureNoiseWeight,
        EntranceCount = cfg.EntranceCount,
        MinEntranceSpacing = cfg.MinEntranceSpacing,
        MinEntranceFromSpawn = cfg.MinEntranceFromSpawn,
        VolcanoCount = cfg.VolcanoCount,
        VolcanoType = cfg.VolcanoType,
        VolcanoBaseRadius = cfg.VolcanoBaseRadius,
        VolcanoPeakHeight = cfg.VolcanoPeakHeight,
        CalderaRadiusFraction = cfg.CalderaRadiusFraction,
        LavaFlowCount = cfg.LavaFlowCount,
    };

    // ── Noise configuration ──────────────────────────────────────────

    public int MapWidth { get; init; } = 512;
    public int MapHeight { get; init; } = 512;
    public float Frequency { get; init; } = 0.008f;
    public int Octaves { get; init; } = 6;
    public float Lacunarity { get; init; } = 2.0f;
    public float Gain { get; init; } = 0.5f;

    // ── Island count (archipelago) ────────────────────────────────────

    /// <summary>Number of islands in the chain. 1 = single island, 2-6 = archipelago.</summary>
    public int IslandCount { get; init; } = 1;

    // ── Island shape ─────────────────────────────────────────────────

    /// <summary>Island radius as fraction of half the shortest map dimension. 0.85 leaves a water border.</summary>
    public float IslandRadiusScale { get; init; } = 0.85f;
    /// <summary>Falloff exponent. 2.0 = smooth cosine-ish drop. Higher = steeper cliff to ocean.</summary>
    public float IslandFalloffExp { get; init; } = 2.2f;
    /// <summary>How much domain warp is applied to the island mask (coastline raggedness).</summary>
    public float CoastWarpStrength { get; init; } = 0.18f;

    // ── Moisture / climate ───────────────────────────────────────────

    /// <summary>
    /// Prevailing wind direction in degrees. 0 = wind blows FROM the North (south face is wet).
    /// 90 = wind blows FROM the East (west face is wet). Controls orographic rain shadow.
    /// </summary>
    public float PrevailingWindAngleDeg { get; init; } = 225f; // default: southwest trade winds

    /// <summary>
    /// How strongly the directional wind gradient dominates moisture.
    /// 0 = no gradient (pure noise). 1 = pure gradient (one side always wet, other always dry).
    /// 0.6 gives a strong but not absolute rain shadow.
    /// </summary>
    public float WindGradientStrength { get; init; } = 0.6f;

    /// <summary>
    /// Weight of the FBm noise component in the final moisture field.
    /// Higher = more local variation that breaks up the gradient.
    /// Lower = cleaner windward/leeward split.
    /// </summary>
    public float MoistureNoiseWeight { get; init; } = 0.5f;

    // ── Entrance placement ───────────────────────────────────────────

    /// <summary>How many dungeon entrances to place on the overworld.</summary>
    public int EntranceCount { get; init; } = 1;
    /// <summary>Minimum Manhattan distance between any two dungeon entrances.</summary>
    public int MinEntranceSpacing { get; init; } = 80;
    /// <summary>Minimum Manhattan distance from spawn to the nearest entrance.</summary>
    public int MinEntranceFromSpawn { get; init; } = 60;

    // ── Volcano stamping ─────────────────────────────────────────────

    /// <summary>Number of volcanoes to stamp. 0 = none.</summary>
    public int VolcanoCount { get; init; } = 1;
    /// <summary>Shield / Strato / Caldera personality.</summary>
    public VolcanoType VolcanoType { get; init; } = VolcanoType.Strato;
    /// <summary>Cone base radius in tiles.</summary>
    public int VolcanoBaseRadius { get; init; } = 60;
    /// <summary>Extra elevation added at the peak above the 0..1 FBm range.</summary>
    public float VolcanoPeakHeight { get; init; } = 0.55f;
    /// <summary>Caldera crater radius as fraction of VolcanoBaseRadius.</summary>
    public float CalderaRadiusFraction { get; init; } = 0.15f;
    /// <summary>Number of lava flow channels radiating from the peak.</summary>
    public int LavaFlowCount { get; init; } = 4;

    /// <summary>Number of rivers to generate. 0 = none.</summary>
    public int RiverCount { get; init; } = 4;

    // ── Legacy thresholds (no-biomes fallback only) ──────────────────

    public float WaterThreshold { get; init; } = 0.30f;
    public float GrassThreshold { get; init; } = 0.55f;
    public float DirtThreshold { get; init; } = 0.75f;

    // ── Tile IDs ─────────────────────────────────────────────────────

    // internal so partial files in this assembly can reference them directly
    internal const string WaterTile = "base:water";
    internal const string GrassTile = "base:grass";
    internal const string DirtTile = "base:dirt";
    internal const string WallTile = "base:wall";
    internal const string EntranceTile = "base:dungeon_entrance";
    internal const string VolcanicPeakTile = "base:volcanic_peak";
    internal const string LavaTile = "base:lava";
    internal const string CraterLakeTile = "base:crater_lake";
    internal const string RiverTile = "base:river";

    // ── Output ───────────────────────────────────────────────────────

    /// <summary>Player spawn position (walkable tile near map center).</summary>
    public (int X, int Y) SpawnPosition { get; private set; }

    /// <summary>
    /// All placed dungeon entrance positions. At least one is always present.
    /// The first entry is also mirrored in EntrancePosition for legacy callers.
    /// </summary>
    public List<(int X, int Y)> EntrancePositions { get; private set; } = new();

    /// <summary>Convenience accessor for the primary (first) dungeon entrance.</summary>
    public (int X, int Y) EntrancePosition => EntrancePositions.Count > 0
        ? EntrancePositions[0]
        : SpawnPosition;

    /// <summary>
    /// World positions of all stamped volcano peaks (center tiles).
    /// Empty if VolcanoCount is 0. Useful for dungeon theming and map icons.
    /// </summary>
    public List<(int X, int Y)> VolcanoCenters { get; private set; } = new();

    // ── Public Generate overloads ────────────────────────────────────

    /// <summary>
    /// Data-driven generation. Biomes are matched by both elevation AND moisture.
    /// Within an elevation band, the first biome whose moisture range contains the
    /// sampled moisture value is used. Biomes with MoistureMin=0/MoistureMax=1
    /// act as catch-all fallbacks for that elevation band.
    /// </summary>
    public TileMap Generate(
        Dictionary<string, TileDef> tileRegistry,
        int? seed,
        IReadOnlyList<BiomeDef> biomes)
    {
        int actualSeed = seed ?? Environment.TickCount;
        var rng = new Random(actualSeed);
        var map = new TileMap(MapWidth, MapHeight, tileRegistry);

        var elevNoise = ConfigureElevationNoise(actualSeed);
        var warpNoise = ConfigureWarpNoise(actualSeed + 7919);
        var moistNoise = ConfigureMoistureNoise(actualSeed + 31337);

        float[] maskedElevation = BuildMaskedElevation(elevNoise, warpNoise, rng);

        // Stamp volcanoes onto the elevation field before tile assignment.
        // Also builds the lava overlay used during tile write.
        var (lavaOverlay, craterOverlay) = StampVolcanoesWithOverlays(maskedElevation, rng);

        float[] moisture = BuildMoistureField(moistNoise, maskedElevation);

        for (int y = 0; y < MapHeight; y++)
        {
            for (int x = 0; x < MapWidth; x++)
            {
                int i = y * MapWidth + x;
                float elev = maskedElevation[i];
                float mois = moisture[i];

                string tileId;
                if (craterOverlay[i])
                    tileId = CraterLakeTile;
                else if (lavaOverlay[i])
                    tileId = LavaTile;
                else
                    tileId = ElevationMoistureToTile(elev, mois, biomes);

                map.SetTile(x, y, tileId);
                map.SetElevation(x, y, elev);
            }
        }

        CarveRivers(map, maskedElevation, rng);
        FinishGeneration(map, rng);
        return map;
    }

    /// <summary>
    /// Legacy fallback: uses hardcoded elevation thresholds, no biome list required.
    /// </summary>
    public TileMap Generate(Dictionary<string, TileDef> tileRegistry, int? seed = null)
    {
        int actualSeed = seed ?? Environment.TickCount;
        var rng = new Random(actualSeed);
        var map = new TileMap(MapWidth, MapHeight, tileRegistry);

        var elevNoise = ConfigureElevationNoise(actualSeed);
        var warpNoise = ConfigureWarpNoise(actualSeed + 7919);

        float[] maskedElevation = BuildMaskedElevation(elevNoise, warpNoise, rng);

        var (lavaOverlay, craterOverlay) = StampVolcanoesWithOverlays(maskedElevation, rng);

        for (int y = 0; y < MapHeight; y++)
        {
            for (int x = 0; x < MapWidth; x++)
            {
                int i = y * MapWidth + x;
                float elev = maskedElevation[i];
                string tileId;
                if (craterOverlay[i])
                    tileId = CraterLakeTile;
                else if (lavaOverlay[i])
                    tileId = LavaTile;
                else
                    tileId = ElevationToTileLegacy(elev);
                map.SetTile(x, y, tileId);
                map.SetElevation(x, y, elev);
            }
        }

        FinishGeneration(map, rng);
        return map;
    }

    // ── Tile assignment ───────────────────────────────────────────────

    /// <summary>
    /// Two-axis biome lookup: elevation + moisture.
    ///
    /// Algorithm:
    ///   1. Collect all biomes where elevationMin &lt;= elev &lt; elevationMax.
    ///   2. Among those, pick the first whose moistureMin &lt;= mois &lt;= moistureMax.
    ///   3. If none match moisture, fall back to the last elevation-matching biome
    ///      (which should be defined as a catch-all with moisture 0..1).
    ///
    /// This means biomes must be ordered in YAML so:
    ///   - Water/beach/peak biomes appear at the top of their elevation band (catch-all).
    ///   - Moisture-specific variants follow them, ordered dry-to-wet or wet-to-dry.
    /// </summary>
    private static string ElevationMoistureToTile(
        float elevation,
        float moisture,
        IReadOnlyList<BiomeDef> biomes)
    {
        BiomeDef? catchAll = null;
        BiomeDef? specific = null;
        float bestRange = float.MaxValue;

        for (int i = 0; i < biomes.Count; i++)
        {
            var b = biomes[i];

            if (elevation < b.ElevationMin || elevation >= b.ElevationMax)
                continue;

            if (moisture >= b.MoistureMin && moisture <= b.MoistureMax)
            {
                float range = b.MoistureMax - b.MoistureMin;
                // Prefer the narrowest (most specific) moisture match
                if (range < bestRange)
                {
                    bestRange = range;
                    specific = b;
                }
                // Track the widest as catch-all fallback
                if (catchAll == null || range > (catchAll.MoistureMax - catchAll.MoistureMin))
                    catchAll = b;
            }
        }

        if (specific != null) return specific.TileId;
        if (catchAll != null) return catchAll.TileId;
        return biomes[biomes.Count - 1].TileId;
    }

    private string ElevationToTileLegacy(float elevation)
    {
        if (elevation < WaterThreshold) return WaterTile;
        if (elevation < GrassThreshold) return GrassTile;
        if (elevation < DirtThreshold) return DirtTile;
        return WallTile;
    }
}