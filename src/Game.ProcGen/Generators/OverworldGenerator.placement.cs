// src/Game.ProcGen/Generators/OverworldGenerator.Placement.cs
//
// Post-generation placement of the player spawn and dungeon entrances.
//
// Two versions of each core method exist:
//   TileMap version   -- called by FinishGeneration() after Generate(). Has access to
//                        IsWalkable() and tile Tags for precise candidate filtering.
//   Array version     -- called by GenerateFeatureSets(). Works directly from the raw
//                        elevation array, avoiding the cost of a full TileMap allocation.

using System;
using System.Collections.Generic;
using Game.Core.Biomes;
using Game.Core.Map;

namespace Game.ProcGen.Generators;

public partial class OverworldGenerator
{
    // ── FinishGeneration ──────────────────────────────────────────────

    private void FinishGeneration(TileMap map, Random rng)
    {
        // No border walls -- ocean naturally surrounds the island
        SpawnPosition = FindWalkableNearCenter(map);
        EntrancePositions = PlaceDungeonEntrances(map, rng);

        // Bake hillshading + cliff edges from the final elevation data.
        // Must run after all tiles are written so elevation is complete.
        map.BakeTerrainShading();
    }

    // ── Spawn placement (TileMap) ─────────────────────────────────────

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

    // ── Entrance placement (TileMap) ──────────────────────────────────

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

    // ── Spawn placement (array-based, no TileMap) ─────────────────────

    /// <summary>
    /// Find a walkable spawn position using the elevation array directly
    /// (no TileMap needed). Used by GenerateFeatureSets().
    /// Walkable = land above water, not an extreme peak.
    /// </summary>
    private (int X, int Y) FindWalkableNearCenterFromArrays(
        float[] elev, IReadOnlyList<BiomeDef>? biomes)
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
                    if (Math.Abs(dx) != radius && Math.Abs(dy) != radius) continue;
                    int tx = cx + dx;
                    int ty = cy + dy;
                    if (tx < 0 || tx >= MapWidth || ty < 0 || ty >= MapHeight) continue;
                    float e = elev[ty * MapWidth + tx];
                    // Walkable = land above water, not extreme peak
                    bool walkable = e > WaterThreshold && e < 0.92f;
                    if (walkable) return (tx, ty);
                }
            }
        }
        return (cx, cy);
    }

    // ── Entrance placement (array-based, no TileMap) ──────────────────

    /// <summary>
    /// Place dungeon entrances using the elevation array directly.
    /// Used by GenerateFeatureSets(). Cannot check the [coastal] tag, so uses a
    /// slightly higher elevation floor (0.35) as a proxy for inland tiles.
    /// </summary>
    private List<(int X, int Y)> PlaceDungeonEntrancesFromArrays(
        float[] elev, IReadOnlyList<BiomeDef>? biomes,
        (int X, int Y) spawn, Random rng)
    {
        var placed = new List<(int X, int Y)>();
        int target = Math.Max(1, EntranceCount);
        const int maxAttempts = 800;

        var candidates = new List<(int X, int Y)>();
        for (int y = 1; y < MapHeight - 1; y++)
        {
            for (int x = 1; x < MapWidth - 1; x++)
            {
                float e = elev[y * MapWidth + x];
                // Walkable land, not beach (very low land), not extreme peak
                if (e <= WaterThreshold || e < 0.35f || e > 0.92f) continue;

                int distFromSpawn = Math.Abs(x - spawn.X) + Math.Abs(y - spawn.Y);
                if (distFromSpawn < MinEntranceFromSpawn) continue;

                candidates.Add((x, y));
            }
        }

        // Shuffle
        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            var tmp = candidates[i]; candidates[i] = candidates[j]; candidates[j] = tmp;
        }

        int attempts = 0;
        foreach (var (cx, cy) in candidates)
        {
            if (placed.Count >= target || ++attempts > maxAttempts) break;
            bool tooClose = false;
            foreach (var (px, py) in placed)
                if (Math.Abs(cx - px) + Math.Abs(cy - py) < MinEntranceSpacing)
                { tooClose = true; break; }
            if (!tooClose) placed.Add((cx, cy));
        }

        if (placed.Count == 0) placed.Add(spawn);
        return placed;
    }
}