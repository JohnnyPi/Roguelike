// src/Game.ProcGen/Generators/OverworldGenerator.cs

using System;
using System.Collections.Generic;
using Game.Core.Map;
using Game.Core.Tiles;

namespace Game.ProcGen.Generators;

/// <summary>
/// Overworld map generator (Phase 3).
/// 
/// Uses FastNoiseLite to produce an elevation noise field, then maps
/// elevation thresholds to tile types:
///   - Very low  → water  (non-walkable)
///   - Low       → grass
///   - Mid       → dirt
///   - High      → stone/wall (non-walkable mountains)
/// 
/// Places one dungeon entrance on a valid walkable tile.
/// 
/// Thresholds are hardcoded for first playable; later they'll come
/// from YAML biome definitions.
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

    // Elevation thresholds (noise returns -1..1, we remap to 0..1)
    // These define the biome bands from lowest to highest elevation.
    public float WaterThreshold { get; init; } = 0.30f;   // below this → water
    public float GrassThreshold { get; init; } = 0.55f;   // below this → grass
    public float DirtThreshold { get; init; } = 0.75f;    // below this → dirt
    // Above DirtThreshold → stone/wall (mountains)

    // Tile IDs — match the tile registry
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

    // ── Main generation method ──────────────────────────────────────

    /// <summary>
    /// Generate the overworld TileMap.
    /// </summary>
    /// <param name="tileRegistry">Tile definitions (needed by TileMap constructor).</param>
    /// <param name="seed">Random seed. Null = random.</param>
    public TileMap Generate(Dictionary<string, TileDef> tileRegistry, int? seed = null)
    {
        int actualSeed = seed ?? Environment.TickCount;
        var rng = new Random(actualSeed);
        var map = new TileMap(MapWidth, MapHeight, tileRegistry);

        // Step 1: Generate elevation noise field and assign tiles
        var noise = new FastNoiseLite(actualSeed);
        noise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        noise.SetFractalType(FastNoiseLite.FractalType.FBm);
        noise.SetFractalOctaves(Octaves);
        noise.SetFrequency(Frequency);
        noise.SetFractalLacunarity(Lacunarity);
        noise.SetFractalGain(Gain);

        // Build the elevation map and assign tiles
        for (int y = 0; y < MapHeight; y++)
        {
            for (int x = 0; x < MapWidth; x++)
            {
                // GetNoise returns -1..1, remap to 0..1
                float elevation = (noise.GetNoise(x, y) + 1f) / 2f;

                string tileId = ElevationToTile(elevation);
                map.SetTile(x, y, tileId);
            }
        }

        // Step 2: Add a solid border of wall tiles so the player can't walk off the edge
        AddBorderWalls(map);

        // Step 3: Find a walkable spawn point near the center
        SpawnPosition = FindWalkableNearCenter(map, rng);

        // Step 4: Place one dungeon entrance on a valid walkable tile
        //         (away from spawn so the player has to explore a bit)
        EntrancePosition = PlaceDungeonEntrance(map, rng);

        return map;
    }

    // ── Helpers ─────────────────────────────────────────────────────

    /// <summary>Map an elevation value (0..1) to a tile ID based on thresholds.</summary>
    private string ElevationToTile(float elevation)
    {
        if (elevation < WaterThreshold) return WaterTile;
        if (elevation < GrassThreshold) return GrassTile;
        if (elevation < DirtThreshold) return DirtTile;
        return WallTile; // mountains
    }

    /// <summary>Add a 1-tile border of walls around the map perimeter.</summary>
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

    /// <summary>
    /// Find a walkable tile near the center of the map for the player spawn.
    /// Spirals outward from center until a walkable tile is found.
    /// </summary>
    private (int X, int Y) FindWalkableNearCenter(TileMap map, Random rng)
    {
        int cx = MapWidth / 2;
        int cy = MapHeight / 2;

        // Spiral outward from center
        for (int radius = 0; radius < Math.Max(MapWidth, MapHeight) / 2; radius++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    // Only check the perimeter of this radius ring
                    if (Math.Abs(dx) != radius && Math.Abs(dy) != radius)
                        continue;

                    int tx = cx + dx;
                    int ty = cy + dy;

                    if (map.IsWalkable(tx, ty))
                        return (tx, ty);
                }
            }
        }

        // Absolute fallback: scan entire map
        for (int y = 1; y < MapHeight - 1; y++)
            for (int x = 1; x < MapWidth - 1; x++)
                if (map.IsWalkable(x, y))
                    return (x, y);

        // If somehow nothing is walkable, put them at center anyway
        return (cx, cy);
    }

    /// <summary>
    /// Place the dungeon entrance on a walkable tile, preferably some distance
    /// from the player spawn so there's exploration before finding it.
    /// </summary>
    private (int X, int Y) PlaceDungeonEntrance(TileMap map, Random rng)
    {
        int minDistFromSpawn = 20; // minimum Manhattan distance from spawn
        int maxAttempts = 500;

        // Collect candidate tiles: walkable + at least minDist from spawn
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

            // Once we have enough candidates, pick one
            if (candidates.Count >= 10)
                break;
        }

        // Pick a random candidate, or fallback to any walkable tile
        (int ex, int ey) entrance;
        if (candidates.Count > 0)
        {
            entrance = candidates[rng.Next(candidates.Count)];
        }
        else
        {
            // Fallback: relax the distance requirement
            entrance = FindWalkableNearCenter(map, rng);
        }

        map.SetTile(entrance.ex, entrance.ey, EntranceTile);
        return (entrance.ex, entrance.ey);
    }
}