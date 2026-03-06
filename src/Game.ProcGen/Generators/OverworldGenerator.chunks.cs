// src/Game.ProcGen/Generators/OverworldGenerator.Chunks.cs
//
// Per-chunk streaming generation. Samples the SAME global noise fields as the full
// Generate() pass, guaranteeing seamless tile borders between adjacent chunks.
//
// Usage (from ChunkManager delegate):
//
//   var gen = OverworldGenerator.FromConfig(cfg);
//   var ctx = gen.BuildNoiseContext(seed);   // once per world
//
//   chunkManager.GenerateChunk = (cx, cy) =>
//       gen.GenerateChunk(cx, cy, ctx, tileRegistry, biomes,
//                         riverPositions, volcanoPositions, lavaPositions,
//                         craterPositions, entrancePositions);
//
// Volcano and river overlays are built during world-init by GenerateFeatureSets()
// and stored as HashSets. GenerateChunk stamps them into the chunk tile grid.

using System;
using System.Collections.Generic;
using Game.Core.Biomes;
using Game.Core.Map;
using Game.Core.Tiles;
using Game.Core.WorldGen;

namespace Game.ProcGen.Generators;

public partial class OverworldGenerator
{
    // ── Noise context ─────────────────────────────────────────────────

    /// <summary>
    /// Cached noise objects for per-chunk generation.
    /// Build once per world (BuildNoiseContext) and reuse for every GenerateChunk call.
    /// Keeps per-chunk cost to pure sampling + biome lookup -- no noise object allocation.
    /// </summary>
    public sealed class ChunkNoiseContext
    {
        public FastNoiseLite ElevNoise { get; }
        public FastNoiseLite WarpNoise { get; }
        public FastNoiseLite MoistNoise { get; }
        public int Seed { get; }
        public int WorldWidth { get; }
        public int WorldHeight { get; }
        /// <summary>Island center list (cx, cy, radius). Built once; used by each chunk.</summary>
        public (float cx, float cy, float radius)[] IslandCenters { get; }

        internal ChunkNoiseContext(
            FastNoiseLite elevNoise,
            FastNoiseLite warpNoise,
            FastNoiseLite moistNoise,
            int seed,
            int worldWidth,
            int worldHeight,
            (float, float, float)[] islandCenters)
        {
            ElevNoise = elevNoise;
            WarpNoise = warpNoise;
            MoistNoise = moistNoise;
            Seed = seed;
            WorldWidth = worldWidth;
            WorldHeight = worldHeight;
            IslandCenters = islandCenters;
        }
    }

    // ── Context factory ───────────────────────────────────────────────

    /// <summary>
    /// Build and cache the noise objects required by GenerateChunk.
    /// Call once per world init; pass the returned context to every GenerateChunk call.
    /// </summary>
    public ChunkNoiseContext BuildNoiseContext(int seed)
    {
        var rng = new Random(seed);
        var elevNoise = ConfigureElevationNoise(seed);
        var warpNoise = ConfigureWarpNoise(seed + 7919);
        var moistNoise = ConfigureMoistureNoise(seed + 31337);

        // Build island centers using same logic as BuildMaskedElevation
        int count = Math.Max(1, IslandCount);
        var centers = new (float, float, float)[count];
        float mapCx = MapWidth * 0.5f;
        float mapCy = MapHeight * 0.5f;

        if (count == 1)
        {
            float r = MathF.Min(mapCx, mapCy) * IslandRadiusScale;
            centers[0] = (mapCx, mapCy, r);
        }
        else
        {
            float chainRadiusFrac = IslandRadiusScale * 0.55f;
            float halfMap = MathF.Min(mapCx, mapCy);
            float spread = halfMap * 1.1f;
            float chainAngle = (float)(rng.NextDouble() * MathF.PI);

            for (int i = 0; i < count; i++)
            {
                float t = count == 1 ? 0f : (float)i / (count - 1);
                float offset = (t - 0.5f) * spread;
                float cx = mapCx + MathF.Cos(chainAngle) * offset;
                float cy = mapCy + MathF.Sin(chainAngle) * offset;
                float ageFactor = 0.75f + 0.5f * (float)rng.NextDouble();
                float r = MathF.Min(mapCx, mapCy) * chainRadiusFrac * ageFactor;
                cx = Math.Clamp(cx, r + 4, MapWidth - r - 4);
                cy = Math.Clamp(cy, r + 4, MapHeight - r - 4);
                centers[i] = (cx, cy, r);
            }
        }

        return new ChunkNoiseContext(elevNoise, warpNoise, moistNoise, seed,
                                     MapWidth, MapHeight, centers);
    }

    // ── Per-chunk generation ──────────────────────────────────────────

    /// <summary>
    /// Generate a single 64x64 WorldChunk by sampling the global noise field at
    /// the chunk's world-coordinate range.
    ///
    /// The sampling is identical to the full Generate() pass:
    ///   - Elevation noise + island mask uses (worldX, worldY) -- NOT local coords.
    ///   - Moisture field uses the same global formula.
    /// This guarantees seamless tile borders between adjacent chunks.
    ///
    /// Cross-chunk features (rivers, volcanoes, dungeon entrances) are stamped
    /// from precomputed HashSets of world positions. Build those once at world-init
    /// and reuse them across all GenerateChunk calls.
    ///
    /// Per-chunk BakeTerrain() is called at the end -- hillshade at chunk borders
    /// uses clamped edge values (slight inaccuracy) which is visually acceptable.
    /// For pixel-perfect borders, a cross-chunk bake pass can be added later.
    /// </summary>
    public WorldChunk GenerateChunk(
        int chunkX,
        int chunkY,
        ChunkNoiseContext ctx,
        Dictionary<string, TileDef> tileRegistry,
        IReadOnlyList<BiomeDef> biomes,
        HashSet<(int, int)>? riverPositions = null,
        HashSet<(int, int)>? volcanoPositions = null,
        HashSet<(int, int)>? lavaPositions = null,
        HashSet<(int, int)>? craterPositions = null,
        HashSet<(int, int)>? entrancePositions = null)
    {
        var chunk = new WorldChunk(chunkX, chunkY);

        int cs = WorldChunk.Size;
        int originX = chunkX * cs;
        int originY = chunkY * cs;

        float worldCx = ctx.WorldWidth * 0.5f;
        float worldCy = ctx.WorldHeight * 0.5f;

        // Wind direction (same formula as BuildMoistureField)
        float windRad = PrevailingWindAngleDeg * MathF.PI / 180f;
        float windDx = MathF.Sin(windRad);
        float windDy = -MathF.Cos(windRad);
        float halfSpan = MathF.Min(worldCx, worldCy) * IslandRadiusScale;

        var islandCenters = ctx.IslandCenters;

        for (int ly = 0; ly < cs; ly++)
        {
            for (int lx = 0; lx < cs; lx++)
            {
                int wx = originX + lx;
                int wy = originY + ly;

                // ── Elevation ──────────────────────────────────────────
                float raw = (ctx.ElevNoise.GetNoise(wx, wy) + 1f) * 0.5f;

                float warpX = (ctx.WarpNoise.GetNoise(wx * 1.3f, wy * 0.9f) + 1f) * 0.5f - 0.5f;
                float warpY = (ctx.WarpNoise.GetNoise(wx * 0.9f + 100f, wy * 1.3f + 100f) + 1f) * 0.5f - 0.5f;

                // Multi-island mask: best (highest) mask across all island centers
                float bestMask = 0f;
                foreach (var (icx, icy, maxRadius) in islandCenters)
                {
                    float warpedX = (wx - icx) + warpX * maxRadius * CoastWarpStrength;
                    float warpedY = (wy - icy) + warpY * maxRadius * CoastWarpStrength;
                    float dist = MathF.Sqrt(warpedX * warpedX + warpedY * warpedY);
                    float t = dist / maxRadius;
                    float mask = 1f - MathF.Pow(Math.Clamp(t, 0f, 1f), IslandFalloffExp);
                    if (mask > bestMask) bestMask = mask;
                }
                float elev = raw * bestMask;

                // ── Moisture ──────────────────────────────────────────
                float dx = wx - worldCx;
                float dy = wy - worldCy;
                float dot = dx * windDx + dy * windDy;
                float gradient = Math.Clamp((dot / halfSpan) * 0.5f + 0.5f, 0f, 1f);
                float noiseM = (ctx.MoistNoise.GetNoise(wx, wy) + 1f) * 0.5f;
                float blended = gradient * (1f - MoistureNoiseWeight) + noiseM * MoistureNoiseWeight;
                float mois = Math.Clamp(noiseM + (blended - noiseM) * WindGradientStrength, 0f, 1f);

                chunk.SetElevation(lx, ly, elev);

                // ── Tile assignment ────────────────────────────────────
                var key = (wx, wy);
                string tileId;

                if (craterPositions != null && craterPositions.Contains(key)) tileId = CraterLakeTile;
                else if (lavaPositions != null && lavaPositions.Contains(key)) tileId = LavaTile;
                else if (volcanoPositions != null && volcanoPositions.Contains(key)) tileId = VolcanicPeakTile;
                else if (riverPositions != null && riverPositions.Contains(key)) tileId = RiverTile;
                else if (entrancePositions != null && entrancePositions.Contains(key)) tileId = EntranceTile;
                else
                    tileId = biomes != null && biomes.Count > 0
                        ? ElevationMoistureToTile(elev, mois, biomes)
                        : ElevationToTileLegacy(elev);

                chunk.SetStaticTileId(lx, ly, tileId);
            }
        }

        // Bake hillshading from the freshly written HeightMap.
        // Border pixels use clamped-edge neighbours -- minor seam artefact,
        // acceptable until a cross-chunk bake pass is implemented.
        chunk.BakeTerrain();

        return chunk;
    }
}