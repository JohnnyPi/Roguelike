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
    // -- Noise context ---------------------------------------------------------

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
        public FastNoiseLite TempNoise { get; }
        public int Seed { get; }
        public int WorldWidth { get; }
        public int WorldHeight { get; }
        public (float cx, float cy, float radius)[] IslandCenters { get; }
        public float[] WindAngles { get; }
        /// <summary>Per-island base temperature (0..1). High = tropical, Low = cold.</summary>
        public float[] BaseTempArr { get; }

        internal ChunkNoiseContext(
            FastNoiseLite elevNoise,
            FastNoiseLite warpNoise,
            FastNoiseLite moistNoise,
            FastNoiseLite tempNoise,
            int seed,
            int worldWidth,
            int worldHeight,
            (float, float, float)[] islandCenters,
            float[] windAngles,
            float[] baseTempArr)
        {
            ElevNoise = elevNoise;
            WarpNoise = warpNoise;
            MoistNoise = moistNoise;
            TempNoise = tempNoise;
            Seed = seed;
            WorldWidth = worldWidth;
            WorldHeight = worldHeight;
            IslandCenters = islandCenters;
            WindAngles = windAngles;
            BaseTempArr = baseTempArr;
        }
    }

    // -- Context factory -------------------------------------------------------

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
        var tempNoise = ConfigureTemperatureNoise(seed + 54321);

        // Use the canonical Bezier placement so chunks match the full Generate() pass exactly.
        var centers = BuildIslandCenters(rng);

        // Per-island wind angles -- same jitter logic as BuildMoistureField.
        // Use a fresh rng seeded identically so the angles match the full-map pass.
        var windRng = new Random(seed);
        var windAngles = new float[centers.Length];
        for (int i = 0; i < centers.Length; i++)
        {
            float jitter = (float)(windRng.NextDouble() - 0.5) * 60f;
            windAngles[i] = PrevailingWindAngleDeg + jitter;
        }

        // Per-island base temperature -- mirrors BuildTemperatureField exactly.
        // Sample broad noise at each island center, bias toward warm (0.40..0.90).
        var baseTempArr = new float[centers.Length];
        for (int i = 0; i < centers.Length; i++)
        {
            var (icx, icy, _) = centers[i];
            float n = (tempNoise.GetNoise(icx, icy) + 1f) * 0.5f;
            baseTempArr[i] = 0.40f + n * 0.50f;
        }

        return new ChunkNoiseContext(elevNoise, warpNoise, moistNoise, tempNoise, seed,
                                     MapWidth, MapHeight, centers, windAngles, baseTempArr);
    }

    // -- Per-chunk generation --------------------------------------------------

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

        // Wind direction precomputed per-island (stored in context)
        var islandCenters = ctx.IslandCenters;
        var windAngles = ctx.WindAngles;

        for (int ly = 0; ly < cs; ly++)
        {
            for (int lx = 0; lx < cs; lx++)
            {
                int wx = originX + lx;
                int wy = originY + ly;

                // -- Elevation ------------------------------------------------
                float raw = (ctx.ElevNoise.GetNoise(wx, wy) + 1f) * 0.5f;

                // Two-layer domain warp for organic coastlines (matches BuildMaskedElevationFromCenters)
                float warpX = (ctx.WarpNoise.GetNoise(wx * 1.3f, wy * 0.9f) + 1f) * 0.5f - 0.5f;
                float warpY = (ctx.WarpNoise.GetNoise(wx * 0.9f + 100f, wy * 1.3f + 100f) + 1f) * 0.5f - 0.5f;
                float warpX2 = (ctx.WarpNoise.GetNoise(wx * 2.7f + 300f, wy * 2.1f + 200f) + 1f) * 0.5f - 0.5f;
                float warpY2 = (ctx.WarpNoise.GetNoise(wx * 2.1f + 200f, wy * 2.7f + 300f) + 1f) * 0.5f - 0.5f;

                float bestMask = 0f;
                foreach (var (icx, icy, maxRadius) in islandCenters)
                {
                    float warpedX = (wx - icx) + (warpX * 0.7f + warpX2 * 0.3f) * maxRadius * CoastWarpStrength;
                    float warpedY = (wy - icy) + (warpY * 0.7f + warpY2 * 0.3f) * maxRadius * CoastWarpStrength;
                    float dist = MathF.Sqrt(warpedX * warpedX + warpedY * warpedY);
                    float t = dist / maxRadius;
                    float mask = 1f - MathF.Pow(Math.Clamp(t, 0f, 1f), IslandFalloffExp);
                    if (mask > bestMask) bestMask = mask;
                }
                // Note: chunk elevation is NOT contrast-stretched per-chunk (that's a global pass).
                // For chunk-streamed maps the raw*mask value is used; visual biome transitions
                // are handled by the biome thresholds being set relative to the raw range.
                float elev = raw * bestMask;

                // -- Moisture (per-island weighted average) --------------------
                float noiseM = (ctx.MoistNoise.GetNoise(wx, wy) + 1f) * 0.5f;
                float weightedMoist = 0f;
                float totalWeight = 0f;

                for (int i = 0; i < islandCenters.Length; i++)
                {
                    var (icx, icy, ir) = islandCenters[i];
                    float dx = wx - icx;
                    float dy = wy - icy;
                    float dist = MathF.Sqrt(dx * dx + dy * dy);
                    float t = dist / ir;
                    float mask = Math.Clamp(1f - MathF.Pow(Math.Clamp(t, 0f, 1f), IslandFalloffExp), 0f, 1f);
                    if (mask <= 0.001f) continue;

                    float windRad = windAngles[i] * MathF.PI / 180f;
                    float dot = dx * MathF.Sin(windRad) + dy * -MathF.Cos(windRad);
                    float gradient = Math.Clamp((dot / ir) * 0.5f + 0.5f, 0f, 1f);
                    float blended = gradient * (1f - MoistureNoiseWeight) + noiseM * MoistureNoiseWeight;
                    float lm = Math.Clamp(noiseM + (blended - noiseM) * WindGradientStrength, 0f, 1f);

                    weightedMoist += lm * mask;
                    totalWeight += mask;
                }
                float mois = totalWeight < 0.001f ? 0.5f : weightedMoist / totalWeight;

                // -- Temperature (lapse rate + per-island base) ----------------
                float wTemp2 = 0f, wTotal2 = 0f;
                var baseTempArr = ctx.BaseTempArr;
                for (int i = 0; i < islandCenters.Length; i++)
                {
                    var (icx, icy, ir) = islandCenters[i];
                    float dx = wx - icx;
                    float dy = wy - icy;
                    float dist = MathF.Sqrt(dx * dx + dy * dy);
                    float t = dist / ir;
                    float mask = Math.Clamp(1f - MathF.Pow(Math.Clamp(t, 0f, 1f), IslandFalloffExp), 0f, 1f);
                    if (mask <= 0.001f) continue;
                    wTemp2 += baseTempArr[i] * mask;
                    wTotal2 += mask;
                }
                float baseTemp = wTotal2 < 0.001f ? 0.5f : wTemp2 / wTotal2;
                float aboveWater = Math.Max(0f, elev - WaterThreshold);
                float lapseSpan = 1f - WaterThreshold;
                float temp = Math.Clamp(baseTemp - 0.70f * (aboveWater / lapseSpan), 0f, 1f);

                chunk.SetElevation(lx, ly, elev);

                // -- Tile assignment -------------------------------------------
                var key = (wx, wy);
                string tileId;

                if (craterPositions != null && craterPositions.Contains(key)) tileId = CraterLakeTile;
                else if (lavaPositions != null && lavaPositions.Contains(key)) tileId = LavaTile;
                else if (volcanoPositions != null && volcanoPositions.Contains(key)) tileId = VolcanicPeakTile;
                else if (riverPositions != null && riverPositions.Contains(key)) tileId = RiverTile;
                else if (entrancePositions != null && entrancePositions.Contains(key)) tileId = EntranceTile;
                else
                    tileId = biomes != null && biomes.Count > 0
                        ? ElevationMoistureTempToTile(elev, mois, temp, biomes)
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