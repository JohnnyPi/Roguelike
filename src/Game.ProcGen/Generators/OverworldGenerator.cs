// src/Game.ProcGen/Generators/OverworldGenerator.cs

using System;
using System.Collections.Generic;
using System.Linq;
using Game.Core.Biomes;
using Game.Core.Map;
using Game.Core.Tiles;

namespace Game.ProcGen.Generators;

/// <summary>
/// Overworld island generator.
///
/// Generation pipeline:
///   1. Build two FBm noise fields: elevation + coast warp.
///   2. Multiply elevation by a radial island mask so edges always become ocean.
///      The mask itself is distorted by the warp noise to produce jagged coastlines.
///   3. Map final elevation to biomes (data-driven via BiomeDef list, or legacy fallback).
///   4. Find a walkable spawn near the center island.
///   5. Place one or more dungeon entrances inland with min-spacing constraints.
///
/// Island shape knobs:
///   IslandRadiusScale   - fraction of half-map used as the island radius (0..1)
///   IslandFalloffExp    - how sharply land drops to ocean at edges (1=linear, 2=smooth, 3=cliff)
///   CoastWarpStrength   - how jagged the coastline is (0=perfect circle, 0.25=moderate, 0.5=extreme)
///
/// Entrance placement:
///   EntranceCount       - number of dungeon entrances to place (default 1)
///   MinEntranceSpacing  - minimum Manhattan distance between entrances (default 25)
///
/// Backward compatible: the no-biomes Generate() overload still works.
/// </summary>
public class OverworldGenerator
{
    // ── Noise configuration ──────────────────────────────────────────
    public int MapWidth { get; init; } = 120;
    public int MapHeight { get; init; } = 120;
    public float Frequency { get; init; } = 0.02f;
    public int Octaves { get; init; } = 4;
    public float Lacunarity { get; init; } = 2.0f;
    public float Gain { get; init; } = 0.5f;

    // ── Island shape ─────────────────────────────────────────────────
    /// <summary>Island radius as fraction of half the shortest map dimension. 0.85 leaves a water border.</summary>
    public float IslandRadiusScale { get; init; } = 0.85f;
    /// <summary>Falloff exponent. 2.0 = smooth cosine-ish drop. Higher = steeper cliff to ocean.</summary>
    public float IslandFalloffExp { get; init; } = 2.2f;
    /// <summary>How much domain warp is applied to the island mask (coastline raggedness).</summary>
    public float CoastWarpStrength { get; init; } = 0.18f;

    // ── Entrance placement ───────────────────────────────────────────
    /// <summary>How many dungeon entrances to place on the overworld.</summary>
    public int EntranceCount { get; init; } = 1;
    /// <summary>Minimum Manhattan distance between any two dungeon entrances.</summary>
    public int MinEntranceSpacing { get; init; } = 25;
    /// <summary>Minimum Manhattan distance from spawn to the nearest entrance.</summary>
    public int MinEntranceFromSpawn { get; init; } = 20;

    // ── Legacy thresholds (no-biomes fallback only) ──────────────────
    public float WaterThreshold { get; init; } = 0.30f;
    public float GrassThreshold { get; init; } = 0.55f;
    public float DirtThreshold { get; init; } = 0.75f;

    // ── Tile IDs ─────────────────────────────────────────────────────
    private const string WaterTile = "base:water";
    private const string GrassTile = "base:grass";
    private const string DirtTile = "base:dirt";
    private const string WallTile = "base:wall";
    private const string EntranceTile = "base:dungeon_entrance";

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

    // ─────────────────────────────────────────────────────────────────
    // Public Generate overloads
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Data-driven generation. Biomes must be sorted by ElevationMax ascending.
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
        var warpNoise = ConfigureWarpNoise(actualSeed + 7919); // prime offset keeps warp uncorrelated

        float[] maskedElevation = BuildMaskedElevation(elevNoise, warpNoise);

        for (int y = 0; y < MapHeight; y++)
        {
            for (int x = 0; x < MapWidth; x++)
            {
                float elev = maskedElevation[y * MapWidth + x];
                string tileId = ElevationToTileBiome(elev, biomes);
                map.SetTile(x, y, tileId);
                map.SetElevation(x, y, elev);
            }
        }

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

        float[] maskedElevation = BuildMaskedElevation(elevNoise, warpNoise);

        for (int y = 0; y < MapHeight; y++)
        {
            for (int x = 0; x < MapWidth; x++)
            {
                float elev = maskedElevation[y * MapWidth + x];
                string tileId = ElevationToTileLegacy(elev);
                map.SetTile(x, y, tileId);
                map.SetElevation(x, y, elev);
            }
        }

        FinishGeneration(map, rng);
        return map;
    }

    // ─────────────────────────────────────────────────────────────────
    // Island mask + elevation
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Build the final masked elevation array.
    ///
    /// For each tile:
    ///   1. Sample raw FBm elevation noise -> raw [0..1]
    ///   2. Compute warped distance to center -> warpedDist [0..1]
    ///   3. Compute island mask = clamp(1 - (warpedDist / radius)^exp, 0, 1)
    ///   4. finalElev = raw * mask
    ///      (edges always become 0 = deep water; center can reach full elevation)
    /// </summary>
    private float[] BuildMaskedElevation(FastNoiseLite elevNoise, FastNoiseLite warpNoise)
    {
        float[] elev = new float[MapWidth * MapHeight];

        float cx = MapWidth * 0.5f;
        float cy = MapHeight * 0.5f;
        float maxRadius = MathF.Min(cx, cy) * IslandRadiusScale;

        for (int y = 0; y < MapHeight; y++)
        {
            for (int x = 0; x < MapWidth; x++)
            {
                // Raw terrain elevation [0..1]
                float raw = (elevNoise.GetNoise(x, y) + 1f) * 0.5f;

                // Warp offset for coastline irregularity
                float wx = (warpNoise.GetNoise(x * 1.3f, y * 0.9f) + 1f) * 0.5f - 0.5f;
                float wy = (warpNoise.GetNoise(x * 0.9f + 100f, y * 1.3f + 100f) + 1f) * 0.5f - 0.5f;

                float warpedX = (x - cx) + wx * maxRadius * CoastWarpStrength;
                float warpedY = (y - cy) + wy * maxRadius * CoastWarpStrength;
                float dist = MathF.Sqrt(warpedX * warpedX + warpedY * warpedY);

                // Island mask: 1.0 at center, 0.0 beyond radius, smooth curve between
                float t = dist / maxRadius;
                float mask = 1f - MathF.Pow(Math.Clamp(t, 0f, 1f), IslandFalloffExp);

                elev[y * MapWidth + x] = raw * mask;
            }
        }

        return elev;
    }

    // ─────────────────────────────────────────────────────────────────
    // Tile assignment
    // ─────────────────────────────────────────────────────────────────

    private static string ElevationToTileBiome(float elevation, IReadOnlyList<BiomeDef> biomes)
    {
        for (int i = 0; i < biomes.Count; i++)
        {
            if (elevation < biomes[i].ElevationMax)
                return biomes[i].TileId;
        }
        return biomes[biomes.Count - 1].TileId;
    }

    private string ElevationToTileLegacy(float elevation)
    {
        if (elevation < WaterThreshold) return WaterTile;
        if (elevation < GrassThreshold) return GrassTile;
        if (elevation < DirtThreshold) return DirtTile;
        return WallTile;
    }

    // ─────────────────────────────────────────────────────────────────
    // Noise builders
    // ─────────────────────────────────────────────────────────────────

    private FastNoiseLite ConfigureElevationNoise(int seed)
    {
        var noise = new FastNoiseLite(seed);
        noise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        noise.SetFractalType(FastNoiseLite.FractalType.FBm);
        noise.SetFractalOctaves(Octaves);
        noise.SetFrequency(Frequency);
        noise.SetFractalLacunarity(Lacunarity);
        noise.SetFractalGain(Gain);
        return noise;
    }

    private FastNoiseLite ConfigureWarpNoise(int seed)
    {
        // Lower frequency, fewer octaves — we want broad smooth warping
        var noise = new FastNoiseLite(seed);
        noise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        noise.SetFractalType(FastNoiseLite.FractalType.FBm);
        noise.SetFractalOctaves(2);
        noise.SetFrequency(Frequency * 0.6f);
        noise.SetFractalLacunarity(2.0f);
        noise.SetFractalGain(0.5f);
        return noise;
    }

    // ─────────────────────────────────────────────────────────────────
    // Post-generation: spawn + entrances
    // ─────────────────────────────────────────────────────────────────

    private void FinishGeneration(TileMap map, Random rng)
    {
        // No border walls — ocean naturally surrounds the island
        SpawnPosition = FindWalkableNearCenter(map);
        EntrancePositions = PlaceDungeonEntrances(map, rng);
    }

    /// <summary>
    /// Scan outward from map center in a spiral until a walkable tile is found.
    /// The island mask guarantees center tiles should be land.
    /// </summary>
    private (int X, int Y) FindWalkableNearCenter(TileMap map)
    {
        int cx = MapWidth / 2;
        int cy = MapHeight / 2;

        int maxR = Math.Max(MapWidth, MapHeight) / 2;
        for (int radius = 0; radius < maxR; radius++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (Math.Abs(dx) != radius && Math.Abs(dy) != radius)
                        continue;

                    int tx = cx + dx;
                    int ty = cy + dy;

                    if (map.InBounds(tx, ty) && map.IsWalkable(tx, ty))
                        return (tx, ty);
                }
            }
        }

        return (cx, cy);
    }

    /// <summary>
    /// Place EntranceCount dungeon entrances on walkable, non-coastal tiles.
    ///
    /// Rules:
    ///   - Tile must be walkable (rules out water, shallow water, mountains)
    ///   - Tile must not be tagged [coastal] (rules out beach)
    ///   - Must be >= MinEntranceFromSpawn from the player spawn
    ///   - Each new entrance must be >= MinEntranceSpacing from all prior ones
    ///
    /// Falls back to any walkable tile if constraints can't be satisfied.
    /// </summary>
    private List<(int X, int Y)> PlaceDungeonEntrances(TileMap map, Random rng)
    {
        var placed = new List<(int X, int Y)>();
        int maxAttempts = 800;

        // Build a list of all inland walkable candidates in one pass
        var candidates = new List<(int X, int Y)>();
        for (int y = 1; y < MapHeight - 1; y++)
        {
            for (int x = 1; x < MapWidth - 1; x++)
            {
                if (!map.IsWalkable(x, y)) continue;

                var def = map.GetTile(x, y);
                if (def != null && def.Tags.Contains("coastal")) continue;

                int distFromSpawn = Math.Abs(x - SpawnPosition.X) + Math.Abs(y - SpawnPosition.Y);
                if (distFromSpawn < MinEntranceFromSpawn) continue;

                candidates.Add((x, y));
            }
        }

        // Shuffle the candidate list for random placement
        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            var tmp = candidates[i];
            candidates[i] = candidates[j];
            candidates[j] = tmp;
        }

        int target = Math.Max(1, EntranceCount);
        int attempts = 0;

        foreach (var (cx, cy) in candidates)
        {
            if (placed.Count >= target) break;
            if (++attempts > maxAttempts) break;

            bool tooClose = false;
            foreach (var (px, py) in placed)
            {
                int dist = Math.Abs(cx - px) + Math.Abs(cy - py);
                if (dist < MinEntranceSpacing)
                {
                    tooClose = true;
                    break;
                }
            }

            if (!tooClose)
            {
                map.SetTile(cx, cy, EntranceTile);
                placed.Add((cx, cy));
            }
        }

        // Last-resort fallback: place at spawn if nothing worked
        if (placed.Count == 0)
        {
            map.SetTile(SpawnPosition.X, SpawnPosition.Y, EntranceTile);
            placed.Add(SpawnPosition);
        }

        return placed;
    }
}