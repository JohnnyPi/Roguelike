// src/Game.ProcGen/Generators/OverworldGenerator.cs

using System;
using System.Collections.Generic;
using Game.Core.Biomes;
using Game.Core.Map;
using Game.Core.Tiles;

namespace Game.ProcGen.Generators;

/// <summary>
/// Overworld map generator.
/// 
/// Uses FastNoiseLite to produce an elevation noise field, then maps
/// elevation thresholds to tile types via biome definitions.
/// 
/// Two modes:
///   1. Data-driven (preferred): Accepts a list of BiomeDef from the content
///      registry. Biomes are sorted by ElevationMax ascending; the generator
///      picks the first biome whose threshold exceeds the sampled elevation.
///   2. Legacy fallback: Uses hardcoded threshold constants (preserved for
///      backwards compatibility if no biomes are loaded).
/// 
/// Places one dungeon entrance on a valid walkable tile.
/// </summary>
public class OverworldGenerator
{
    // ── Configuration ───────────────────────────────────────────────
    public int MapWidth { get; init; } = 120;
    public int MapHeight { get; init; } = 120;

    // Noise settings
    public float Frequency { get; init; } = 0.02f;
    public int Octaves { get; init; } = 4;
    public float Lacunarity { get; init; } = 2.0f;
    public float Gain { get; init; } = 0.5f;

    // Legacy elevation thresholds (used only by the no-biomes overload)
    public float WaterThreshold { get; init; } = 0.30f;
    public float GrassThreshold { get; init; } = 0.55f;
    public float DirtThreshold { get; init; } = 0.75f;

    // Legacy tile IDs (used only by the no-biomes overload)
    private const string WaterTile = "base:water";
    private const string GrassTile = "base:grass";
    private const string DirtTile = "base:dirt";
    private const string WallTile = "base:wall";
    private const string EntranceTile = "base:dungeon_entrance";

    // ── Output ──────────────────────────────────────────────────────

    /// <summary>Grid position of the dungeon entrance.</summary>
    public (int X, int Y) EntrancePosition { get; private set; }

    /// <summary>Suggested player spawn position (near the map center on walkable ground).</summary>
    public (int X, int Y) SpawnPosition { get; private set; }

    // ── Data-driven generation (preferred) ──────────────────────────

    /// <summary>
    /// Generate the overworld TileMap using biome definitions from content.
    /// Biomes must be sorted by ElevationMax ascending.
    /// </summary>
    public TileMap Generate(
        Dictionary<string, TileDef> tileRegistry,
        int? seed,
        IReadOnlyList<BiomeDef> biomes)
    {
        int actualSeed = seed ?? Environment.TickCount;
        var rng = new Random(actualSeed);
        var map = new TileMap(MapWidth, MapHeight, tileRegistry);

        var noise = ConfigureNoise(actualSeed);

        // Build the elevation map and assign tiles using biome thresholds
        for (int y = 0; y < MapHeight; y++)
        {
            for (int x = 0; x < MapWidth; x++)
            {
                float elevation = (noise.GetNoise(x, y) + 1f) / 2f;
                string tileId = ElevationToTileBiome(elevation, biomes);
                map.SetTile(x, y, tileId);
            }
        }

        FinishGeneration(map, rng);
        return map;
    }

    /// <summary>Map elevation to a tile ID by walking the sorted biome list.</summary>
    private static string ElevationToTileBiome(float elevation, IReadOnlyList<BiomeDef> biomes)
    {
        for (int i = 0; i < biomes.Count; i++)
        {
            if (elevation < biomes[i].ElevationMax)
                return biomes[i].TileId;
        }

        // Above all thresholds — use the last biome as catch-all
        return biomes[biomes.Count - 1].TileId;
    }

    // ── Legacy generation (no biomes) ───────────────────────────────

    /// <summary>
    /// Generate the overworld TileMap using hardcoded elevation thresholds.
    /// Kept for backwards compatibility; prefer the biome-based overload.
    /// </summary>
    public TileMap Generate(Dictionary<string, TileDef> tileRegistry, int? seed = null)
    {
        int actualSeed = seed ?? Environment.TickCount;
        var rng = new Random(actualSeed);
        var map = new TileMap(MapWidth, MapHeight, tileRegistry);

        var noise = ConfigureNoise(actualSeed);

        for (int y = 0; y < MapHeight; y++)
        {
            for (int x = 0; x < MapWidth; x++)
            {
                float elevation = (noise.GetNoise(x, y) + 1f) / 2f;
                string tileId = ElevationToTileLegacy(elevation);
                map.SetTile(x, y, tileId);
            }
        }

        FinishGeneration(map, rng);
        return map;
    }

    /// <summary>Legacy: map elevation to tile ID using hardcoded thresholds.</summary>
    private string ElevationToTileLegacy(float elevation)
    {
        if (elevation < WaterThreshold) return WaterTile;
        if (elevation < GrassThreshold) return GrassTile;
        if (elevation < DirtThreshold) return DirtTile;
        return WallTile;
    }

    // ── Shared helpers ──────────────────────────────────────────────

    private FastNoiseLite ConfigureNoise(int seed)
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

    /// <summary>Shared post-generation steps: border walls, spawn point, dungeon entrance.</summary>
    private void FinishGeneration(TileMap map, Random rng)
    {
        AddBorderWalls(map);
        SpawnPosition = FindWalkableNearCenter(map, rng);
        EntrancePosition = PlaceDungeonEntrance(map, rng);
    }

    private void AddBorderWalls(TileMap map)
    {
        for (int x = 0; x < MapWidth; x++)
        {
            map.SetTile(x, 0, WallTile);
            map.SetTile(x, MapHeight - 1, WallTile);
        }
        for (int y = 0; y < MapHeight; y++)
        {
            map.SetTile(0, y, WallTile);
            map.SetTile(MapWidth - 1, y, WallTile);
        }
    }

    private (int X, int Y) FindWalkableNearCenter(TileMap map, Random rng)
    {
        int cx = MapWidth / 2;
        int cy = MapHeight / 2;

        for (int radius = 0; radius < Math.Max(MapWidth, MapHeight) / 2; radius++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (Math.Abs(dx) != radius && Math.Abs(dy) != radius)
                        continue;

                    int tx = cx + dx;
                    int ty = cy + dy;

                    if (map.IsWalkable(tx, ty))
                        return (tx, ty);
                }
            }
        }

        for (int y = 1; y < MapHeight - 1; y++)
            for (int x = 1; x < MapWidth - 1; x++)
                if (map.IsWalkable(x, y))
                    return (x, y);

        return (cx, cy);
    }

    private (int X, int Y) PlaceDungeonEntrance(TileMap map, Random rng)
    {
        int minDistFromSpawn = 20;
        int maxAttempts = 500;

        var candidates = new List<(int X, int Y)>();

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            int x = rng.Next(2, MapWidth - 2);
            int y = rng.Next(2, MapHeight - 2);

            if (!map.IsWalkable(x, y)) continue;

            int dist = Math.Abs(x - SpawnPosition.X) + Math.Abs(y - SpawnPosition.Y);
            if (dist >= minDistFromSpawn)
            {
                candidates.Add((x, y));
            }

            if (candidates.Count >= 10)
                break;
        }

        (int ex, int ey) entrance;
        if (candidates.Count > 0)
        {
            entrance = candidates[rng.Next(candidates.Count)];
        }
        else
        {
            entrance = FindWalkableNearCenter(map, rng);
        }

        map.SetTile(entrance.ex, entrance.ey, EntranceTile);
        return (entrance.ex, entrance.ey);
    }
}