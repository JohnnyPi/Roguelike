// src/Game.ProcGen/Generators/OverworldGenerator.Noise.cs
//
// Noise configuration and the two core terrain-field builders:
//   ConfigureElevationNoise / ConfigureWarpNoise / ConfigureMoistureNoise
//   BuildMaskedElevation  -- FBm elevation * radial island mask
//   BuildMoistureField    -- directional wind gradient blended with FBm noise

using System;
using Game.Core.WorldGen;

namespace Game.ProcGen.Generators;

public partial class OverworldGenerator
{
    // ── Noise builders ────────────────────────────────────────────────

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
        // Lower frequency, fewer octaves -- broad smooth warping for coastlines
        var noise = new FastNoiseLite(seed);
        noise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        noise.SetFractalType(FastNoiseLite.FractalType.FBm);
        noise.SetFractalOctaves(2);
        noise.SetFrequency(Frequency * 0.6f);
        noise.SetFractalLacunarity(2.0f);
        noise.SetFractalGain(0.5f);
        return noise;
    }

    private FastNoiseLite ConfigureMoistureNoise(int seed)
    {
        // Moisture noise: slightly lower frequency than elevation so moisture
        // zones are broader and more contiguous (feels like climate regions).
        var noise = new FastNoiseLite(seed);
        noise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        noise.SetFractalType(FastNoiseLite.FractalType.FBm);
        noise.SetFractalOctaves(3);
        noise.SetFrequency(Frequency * 0.75f);
        noise.SetFractalLacunarity(2.0f);
        noise.SetFractalGain(0.5f);
        return noise;
    }

    // ── Island mask + elevation ───────────────────────────────────────

    /// <summary>
    /// Build the final masked elevation array.
    ///
    /// For each tile:
    ///   1. Sample raw FBm elevation noise -> raw [0..1]
    ///   2. Compute warped distance to center -> warpedDist [0..1]
    ///   3. Compute island mask = clamp(1 - (warpedDist / radius)^exp, 0, 1)
    ///   4. finalElev = raw * mask
    ///      (edges always become 0 = deep water; center can reach full elevation)
    ///
    /// For archipelago layouts, centers are arranged along a diagonal chain and
    /// the best (highest) mask across all centers is used per tile, allowing
    /// islands to overlap into peninsulas and land bridges.
    /// </summary>
    private float[] BuildMaskedElevation(FastNoiseLite elevNoise, FastNoiseLite warpNoise,
                                          Random? rng = null)
    {
        float[] elev = new float[MapWidth * MapHeight];

        // Build island center list. For an archipelago the centers are arranged
        // along a gently curved chain across the map.
        int count = Math.Max(1, IslandCount);
        var centers = new (float cx, float cy, float radius)[count];

        float mapCx = MapWidth * 0.5f;
        float mapCy = MapHeight * 0.5f;

        if (count == 1)
        {
            float r = MathF.Min(mapCx, mapCy) * IslandRadiusScale;
            centers[0] = (mapCx, mapCy, r);
        }
        else
        {
            // Lay centers along a diagonal chain so islands spread across the map.
            // Each island gets a slightly smaller radius so they fit without total overlap.
            // We use a seeded RNG so the layout is reproducible.
            var chainRng = rng ?? new Random(42);
            float chainRadiusFrac = IslandRadiusScale * 0.55f;   // each island ~55% of solo island radius
            float halfMap = MathF.Min(mapCx, mapCy);
            float spread = halfMap * 1.1f;                        // total spread of the chain

            // Base angle for the chain axis: randomise a little for variety
            float chainAngle = (float)(chainRng.NextDouble() * MathF.PI);

            for (int i = 0; i < count; i++)
            {
                float t = count == 1 ? 0f : (float)i / (count - 1);  // 0..1
                float offset = (t - 0.5f) * spread;

                float cx = mapCx + MathF.Cos(chainAngle) * offset;
                float cy = mapCy + MathF.Sin(chainAngle) * offset;

                // Vary radius a little per island (older chain end = smaller/lower)
                float ageFactor = 0.75f + 0.5f * (float)chainRng.NextDouble();
                float r = MathF.Min(mapCx, mapCy) * chainRadiusFrac * ageFactor;

                // Keep center inside map
                cx = Math.Clamp(cx, r + 4, MapWidth - r - 4);
                cy = Math.Clamp(cy, r + 4, MapHeight - r - 4);

                centers[i] = (cx, cy, r);
            }
        }

        for (int y = 0; y < MapHeight; y++)
        {
            for (int x = 0; x < MapWidth; x++)
            {
                // Raw terrain elevation [0..1]
                float raw = (elevNoise.GetNoise(x, y) + 1f) * 0.5f;

                // Warp offset for coastline irregularity
                float wx = (warpNoise.GetNoise(x * 1.3f, y * 0.9f) + 1f) * 0.5f - 0.5f;
                float wy = (warpNoise.GetNoise(x * 0.9f + 100f, y * 1.3f + 100f) + 1f) * 0.5f - 0.5f;

                // Combine masks from all island centers: use the maximum mask value
                // so islands can overlap (creating peninsulas/land bridges).
                float bestMask = 0f;
                foreach (var (cx, cy, maxRadius) in centers)
                {
                    float warpedX = (x - cx) + wx * maxRadius * CoastWarpStrength;
                    float warpedY = (y - cy) + wy * maxRadius * CoastWarpStrength;
                    float dist = MathF.Sqrt(warpedX * warpedX + warpedY * warpedY);

                    float t = dist / maxRadius;
                    float mask = 1f - MathF.Pow(Math.Clamp(t, 0f, 1f), IslandFalloffExp);
                    if (mask > bestMask) bestMask = mask;
                }

                elev[y * MapWidth + x] = raw * bestMask;
            }
        }

        return elev;
    }

    // ── Moisture field ────────────────────────────────────────────────

    /// <summary>
    /// Build the moisture field for the island.
    ///
    /// Moisture combines two sources:
    ///   A. Directional wind gradient: a smooth ramp across the map aligned with
    ///      PrevailingWindAngleDeg. Tiles on the windward face score high; leeward low.
    ///   B. FBm noise: adds local variation -- valleys, pockets of humidity, etc.
    ///
    /// Ocean tiles (elevation near 0) are left at mid moisture since they won't
    /// be used for biome lookup anyway. Only land pixels matter.
    ///
    /// The two sources are blended: moisture = lerp(gradient, noise, MoistureNoiseWeight)
    /// then clamped to [0..1].
    /// </summary>
    private float[] BuildMoistureField(FastNoiseLite moistNoise, float[] elevation)
    {
        float[] moist = new float[MapWidth * MapHeight];

        float cx = MapWidth * 0.5f;
        float cy = MapHeight * 0.5f;

        // Wind direction vector: the gradient runs along the wind axis.
        // A tile directly in the "windward" direction from center scores 1.0.
        float windRad = PrevailingWindAngleDeg * MathF.PI / 180f;
        float windDx = MathF.Sin(windRad);   // East component
        float windDy = -MathF.Cos(windRad);  // North component (Y flipped in tile coords)

        // Scale factor: normalise dot product to roughly [-1..1] across the map
        float halfSpan = MathF.Min(cx, cy) * IslandRadiusScale;

        for (int y = 0; y < MapHeight; y++)
        {
            for (int x = 0; x < MapWidth; x++)
            {
                int i = y * MapWidth + x;

                // --- A. Wind gradient ---
                // Dot of (tile offset from center) with wind direction.
                // Windward tiles (facing into the wind) get positive values = wetter.
                float dx = x - cx;
                float dy = y - cy;
                float dot = dx * windDx + dy * windDy; // [-halfSpan .. +halfSpan] approx

                // Normalise to [0..1]: 0 = fully leeward, 1 = fully windward
                float gradient = (dot / halfSpan) * 0.5f + 0.5f;
                gradient = Math.Clamp(gradient, 0f, 1f);

                // --- B. FBm noise [0..1] ---
                float noise = (moistNoise.GetNoise(x, y) + 1f) * 0.5f;

                // --- Blend ---
                // gradient dominates by (1 - MoistureNoiseWeight), noise adds local detail
                float blended = gradient * (1f - MoistureNoiseWeight) + noise * MoistureNoiseWeight;

                // Scale the gradient influence by WindGradientStrength.
                // At strength=0 the result is pure noise. At strength=1 the gradient fully governs.
                float noiseOnly = noise;
                float withGradient = blended;
                float final = noiseOnly + (withGradient - noiseOnly) * WindGradientStrength;

                moist[i] = Math.Clamp(final, 0f, 1f);
            }
        }

        return moist;
    }
}