// src/Game.ProcGen/Generators/OverworldGenerator.Rivers.cs
//
// River generation pipeline:
//   CarveRivers       -- full-map path writing RiverTile to a TileMap (called by Generate())
//   BuildRiverSet     -- lightweight path filling a HashSet (called by GenerateFeatureSets())
//   BuildRiverMask    -- shared source-selection + downhill walk + CA smoothing
//   WalkRiverDownhill -- steepest-descent walk from a single source to the coast

using System;
using System.Collections.Generic;
using Game.Core.Map;

namespace Game.ProcGen.Generators;

public partial class OverworldGenerator
{
    // ── Full-map river carve (used with TileMap) ──────────────────────

    /// <summary>
    /// Carve rivers from high-elevation source tiles downhill to the coast.
    ///
    /// Pipeline:
    ///   1. Collect mountain/highland tiles above sourceMinElev as candidates.
    ///   2. Pick up to RiverCount sources with minimum spacing between them.
    ///   3. Walk each source downhill to the sea, marking a river mask.
    ///   4. Two passes of cellular automata: fill gaps (3+ river neighbours -> river),
    ///      prune isolated tiles (0 river neighbours -> remove).
    ///   5. Paint RiverTile on all masked land tiles (3x3 brush for visibility).
    /// </summary>
    private void CarveRivers(TileMap map, float[] elev, Random rng)
    {
        const float sourceMinElev = 0.55f;
        const float oceanThreshold = 0.25f;
        const float peakThreshold = 0.90f;  // don't paint rivers on extreme peaks

        bool[] riverMask = BuildRiverMask(elev, rng, sourceMinElev, oceanThreshold);

        // Paint river tiles -- 3x3 neighbourhood so rivers are visible at minimap scale
        for (int y = 0; y < MapHeight; y++)
        {
            for (int x = 0; x < MapWidth; x++)
            {
                int idx = y * MapWidth + x;
                if (!riverMask[idx]) continue;
                float e = elev[idx];
                if (e >= oceanThreshold && e < peakThreshold)
                {
                    map.SetTile(x, y, RiverTile);
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || nx >= MapWidth || ny < 0 || ny >= MapHeight) continue;
                            float ne = elev[ny * MapWidth + nx];
                            if (ne >= oceanThreshold && ne < peakThreshold)
                                map.SetTile(nx, ny, RiverTile);
                        }
                }
            }
        }
    }

    // ── Lightweight river set (used with GenerateFeatureSets) ─────────

    /// <summary>
    /// Build river world-positions into a HashSet without writing to a TileMap.
    /// Uses the same mask logic as CarveRivers for consistency.
    /// </summary>
    private void BuildRiverSet(float[] elev, Random rng, HashSet<(int, int)> riverSet)
    {
        const float sourceMinElev = 0.55f;
        const float oceanThreshold = 0.25f;
        const float peakThreshold = 0.90f;

        bool[] riverMask = BuildRiverMask(elev, rng, sourceMinElev, oceanThreshold);

        for (int y = 0; y < MapHeight; y++)
            for (int x = 0; x < MapWidth; x++)
            {
                int idx = y * MapWidth + x;
                float e = elev[idx];
                if (riverMask[idx] && e > oceanThreshold && e < peakThreshold)
                    riverSet.Add((x, y));
            }
    }

    // ── Shared mask builder ───────────────────────────────────────────

    /// <summary>
    /// Core river mask logic shared by CarveRivers and BuildRiverSet.
    /// Runs source selection, downhill walks, and two CA smoothing passes.
    /// Returns a flat bool[] where true = river tile candidate.
    /// </summary>
    private bool[] BuildRiverMask(float[] elev, Random rng,
                                   float sourceMinElev, float oceanThreshold)
    {
        bool[] riverMask = new bool[MapWidth * MapHeight];

        // 1. Collect source candidates
        var sources = new List<(int X, int Y)>();
        for (int y = 2; y < MapHeight - 2; y++)
            for (int x = 2; x < MapWidth - 2; x++)
                if (elev[y * MapWidth + x] >= sourceMinElev)
                    sources.Add((x, y));

        // Shuffle
        for (int i = sources.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (sources[i], sources[j]) = (sources[j], sources[i]);
        }

        // 2. Pick sources with minimum spacing
        var chosen = new List<(int X, int Y)>();
        int minSep = MapWidth / 6;
        foreach (var (sx, sy) in sources)
        {
            if (chosen.Count >= RiverCount) break;
            bool tooClose = false;
            foreach (var (ux, uy) in chosen)
                if (Math.Abs(sx - ux) + Math.Abs(sy - uy) < minSep)
                { tooClose = true; break; }
            if (!tooClose)
                chosen.Add((sx, sy));
        }

        // 3. Walk each source downhill
        foreach (var (sx, sy) in chosen)
            WalkRiverDownhill(elev, riverMask, sx, sy, oceanThreshold, rng);

        // 4. Cellular automata smoothing -- 2 passes
        for (int pass = 0; pass < 2; pass++)
        {
            bool[] next = (bool[])riverMask.Clone();
            for (int y = 1; y < MapHeight - 1; y++)
            {
                for (int x = 1; x < MapWidth - 1; x++)
                {
                    int idx = y * MapWidth + x;
                    if (elev[idx] < oceanThreshold) { next[idx] = false; continue; }

                    int n = 0;
                    if (riverMask[(y - 1) * MapWidth + x]) n++;
                    if (riverMask[(y + 1) * MapWidth + x]) n++;
                    if (riverMask[y * MapWidth + x - 1]) n++;
                    if (riverMask[y * MapWidth + x + 1]) n++;

                    if (!riverMask[idx] && n >= 3) next[idx] = true;   // fill gap
                    if (riverMask[idx] && n == 0) next[idx] = false;  // prune isolated
                }
            }
            riverMask = next;
        }

        return riverMask;
    }

    // ── Downhill walk ─────────────────────────────────────────────────

    /// <summary>
    /// Walk downhill from (sx, sy) using steepest 8-directional descent,
    /// marking each visited tile in riverMask. Stops at ocean or a local
    /// minimum (flat plateau). A small random jitter prevents perfectly
    /// straight rivers on flat terrain.
    /// </summary>
    private void WalkRiverDownhill(
        float[] elev, bool[] riverMask,
        int sx, int sy, float oceanThreshold, Random rng)
    {
        int x = sx, y = sy;
        int maxSteps = MapWidth + MapHeight;
        var visited = new HashSet<int>();

        int[] ddx = { 0, 0, -1, 1, -1, 1, -1, 1 };
        int[] ddy = { -1, 1, 0, 0, -1, -1, 1, 1 };
        const float jitter = 0.004f;

        for (int step = 0; step < maxSteps; step++)
        {
            int idx = y * MapWidth + x;

            if (elev[idx] < oceanThreshold) break;  // reached the sea
            if (!visited.Add(idx)) break;            // loop detected

            riverMask[idx] = true;

            // Find steepest downhill neighbour
            float bestElev = elev[idx];
            int bestX = x, bestY = y;

            for (int d = 0; d < 8; d++)
            {
                int nx = x + ddx[d], ny = y + ddy[d];
                if (nx < 1 || nx >= MapWidth - 1 || ny < 1 || ny >= MapHeight - 1)
                    continue;

                float ne = elev[ny * MapWidth + nx]
                           + (float)(rng.NextDouble() - 0.5) * jitter;
                if (ne < bestElev)
                {
                    bestElev = ne;
                    bestX = nx;
                    bestY = ny;
                }
            }

            if (bestX == x && bestY == y) break;  // local minimum / plateau
            x = bestX;
            y = bestY;
        }
    }
}