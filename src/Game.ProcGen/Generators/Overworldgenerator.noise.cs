// src/Game.ProcGen/Generators/OverworldGenerator.Noise.cs
//
// Noise configuration and terrain field builders:
//   ConfigureElevationNoise / ConfigureWarpNoise / ConfigureMoistureNoise / ConfigureTemperatureNoise
//   BuildIslandCenters         -- Bezier chain placement with guaranteed edge separation
//   BuildMaskedElevation       -- FBm elevation * radial island mask, contrast-stretched
//   BuildMaskedElevationFromCenters
//   BuildMoistureField         -- per-island wind gradient blended with FBm noise
//   BuildTemperatureField      -- elevation lapse rate + per-island base temp offset
//
// Elevation contrast stretch:
//   After masking, the raw distribution clusters in a narrow band because
//   raw*mask is always < raw. We stretch the land portion [waterThreshold..1]
//   back to [waterThreshold..1] so the full elevation range is used, giving
//   all biome bands a fair chance to appear.
//
// Temperature derivation:
//   temp = baseTemp - elevLapseRate * elevation + smallNoise
//   baseTemp varies per island (+/-0.25) so islands feel climatically distinct.
//   High elevation always cold; low coastal always warm within the island's climate.

using System;
using Game.Core.WorldGen;

namespace Game.ProcGen.Generators;

public partial class OverworldGenerator
{
    // ── Noise builders ────────────────────────────────────────────────

    private FastNoiseLite ConfigureElevationNoise(int seed)
    {
        var n = new FastNoiseLite(seed);
        n.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        n.SetFractalType(FastNoiseLite.FractalType.FBm);
        n.SetFractalOctaves(Octaves);
        n.SetFrequency(Frequency);
        n.SetFractalLacunarity(Lacunarity);
        n.SetFractalGain(Gain);
        return n;
    }

    private FastNoiseLite ConfigureWarpNoise(int seed)
    {
        var n = new FastNoiseLite(seed);
        n.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        n.SetFractalType(FastNoiseLite.FractalType.FBm);
        n.SetFractalOctaves(3);
        // Higher frequency than before so coastline has real bumps, not just a gentle oval
        n.SetFrequency(Frequency * 1.8f);
        n.SetFractalLacunarity(2.0f);
        n.SetFractalGain(0.6f);
        return n;
    }

    private FastNoiseLite ConfigureMoistureNoise(int seed)
    {
        // Moisture noise: fixed floor so it stays meaningful on large maps
        float f = Math.Max(Frequency * 0.8f, 2.5f / Math.Max(MapWidth, MapHeight));
        var n = new FastNoiseLite(seed);
        n.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        n.SetFractalType(FastNoiseLite.FractalType.FBm);
        n.SetFractalOctaves(4);
        n.SetFrequency(f);
        n.SetFractalLacunarity(2.0f);
        n.SetFractalGain(0.55f);
        return n;
    }

    private FastNoiseLite ConfigureTemperatureNoise(int seed)
    {
        // Broad low-frequency noise for regional temperature variation
        float f = Math.Max(Frequency * 0.4f, 1.5f / Math.Max(MapWidth, MapHeight));
        var n = new FastNoiseLite(seed);
        n.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        n.SetFractalType(FastNoiseLite.FractalType.FBm);
        n.SetFractalOctaves(3);
        n.SetFrequency(f);
        n.SetFractalLacunarity(2.0f);
        n.SetFractalGain(0.5f);
        return n;
    }

    // ── Island placement: Bezier chain ────────────────────────────────

    /// <summary>
    /// Place islands along a quadratic Bezier arc, then push consecutive pairs
    /// apart until the edge-to-edge gap is at least MinIslandSeparation tiles.
    /// Used by BuildMaskedElevation, BuildMoistureField, BuildTemperatureField,
    /// and BuildNoiseContext -- all call this with the same seed so layouts match.
    /// </summary>
    internal (float cx, float cy, float radius)[] BuildIslandCenters(Random rng)
    {
        int count = Math.Max(1, IslandCount);

        if (count == 1)
        {
            float r = MathF.Min(MapWidth * 0.5f, MapHeight * 0.5f) * IslandRadiusScale;
            return new[] { (MapWidth * 0.5f, MapHeight * 0.5f, r) };
        }

        float marginX = MapWidth * 0.10f;
        float marginY = MapHeight * 0.10f;
        bool horizontal = MapWidth >= MapHeight;

        float p0x, p0y, p2x, p2y;
        if (horizontal)
        {
            p0x = marginX; p0y = MapHeight * 0.5f;
            p2x = MapWidth - marginX; p2y = MapHeight * 0.5f;
        }
        else
        {
            p0x = MapWidth * 0.5f; p0y = marginY;
            p2x = MapWidth * 0.5f; p2y = MapHeight - marginY;
        }

        p0y += (float)(rng.NextDouble() - 0.5) * MapHeight * 0.15f;
        p2y += (float)(rng.NextDouble() - 0.5) * MapHeight * 0.15f;
        if (!horizontal)
        {
            p0x += (float)(rng.NextDouble() - 0.5) * MapWidth * 0.15f;
            p2x += (float)(rng.NextDouble() - 0.5) * MapWidth * 0.15f;
        }

        float axDx = p2x - p0x;
        float axDy = p2y - p0y;
        float axLen = MathF.Sqrt(axDx * axDx + axDy * axDy);
        float perpX = axLen > 0 ? -axDy / axLen : 0f;
        float perpY = axLen > 0 ? axDx / axLen : 1f;

        float curveMag = MathF.Min(MapWidth, MapHeight) * ChainCurveAmount;
        float curveSide = rng.Next(2) == 0 ? 1f : -1f;
        float p1x = (p0x + p2x) * 0.5f + perpX * curveMag * curveSide;
        float p1y = (p0y + p2y) * 0.5f + perpY * curveMag * curveSide;

        float shortHalf = MathF.Min(MapWidth, MapHeight) * 0.5f;
        float baseRadius = shortHalf * IslandRadiusScale * 0.60f;

        var centers = new (float cx, float cy, float radius)[count];
        for (int i = 0; i < count; i++)
        {
            float t = count == 1 ? 0.5f : (float)i / (count - 1);
            float u = 1f - t;
            float bx = u * u * p0x + 2f * u * t * p1x + t * t * p2x;
            float by = u * u * p0y + 2f * u * t * p1y + t * t * p2y;

            float ageFactor = 0.75f + 0.50f * (float)rng.NextDouble();
            float r = baseRadius * ageFactor;

            bx = Math.Clamp(bx, r + 4, MapWidth - r - 4);
            by = Math.Clamp(by, r + 4, MapHeight - r - 4);
            centers[i] = (bx, by, r);
        }

        // Enforce minimum edge-to-edge separation
        float minSep = Math.Max(1, MinIslandSeparation);
        for (int iter = 0; iter < 30; iter++)
        {
            bool anyChanged = false;
            for (int i = 0; i < count - 1; i++)
            {
                var (ax, ay, ar) = centers[i];
                var (bx, by, br) = centers[i + 1];
                float dx = bx - ax, dy = by - ay;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                float needed = ar + br + minSep;
                if (dist < needed && dist > 0.001f)
                {
                    float push = (needed - dist) * 0.5f;
                    float nx = dx / dist, ny = dy / dist;
                    centers[i] = (Math.Clamp(ax - nx * push, ar + 4, MapWidth - ar - 4),
                                      Math.Clamp(ay - ny * push, ar + 4, MapHeight - ar - 4), ar);
                    centers[i + 1] = (Math.Clamp(bx + nx * push, br + 4, MapWidth - br - 4),
                                      Math.Clamp(by + ny * push, br + 4, MapHeight - br - 4), br);
                    anyChanged = true;
                }
            }
            if (!anyChanged) break;
        }

        return centers;
    }

    // ── Elevation contrast stretch ────────────────────────────────────

    /// <summary>
    /// After masking, land elevation is compressed into a narrow upper band
    /// because raw*mask shrinks values. This stretches land pixels back so
    /// the full range [WaterThreshold..1] is used, giving all biome bands
    /// (beach, lowland, midland, highland, peak) a fair chance to appear.
    /// Ocean tiles below WaterThreshold are left untouched.
    /// </summary>
    private void StretchElevation(float[] elev)
    {
        float lo = 1f, hi = 0f;
        for (int i = 0; i < elev.Length; i++)
        {
            if (elev[i] > WaterThreshold)
            {
                if (elev[i] < lo) lo = elev[i];
                if (elev[i] > hi) hi = elev[i];
            }
        }

        if (hi - lo < 0.01f) return; // degenerate -- skip

        float srcSpan = hi - lo;
        float dstLo = WaterThreshold + 0.02f; // small gap so coast isn't right at water
        float dstHi = 0.97f;
        float dstSpan = dstHi - dstLo;

        for (int i = 0; i < elev.Length; i++)
        {
            if (elev[i] <= WaterThreshold) continue;
            float t = (elev[i] - lo) / srcSpan;
            // Apply a mild power curve so mid-land is common and peaks are rarer
            t = MathF.Pow(t, 0.75f);
            elev[i] = dstLo + t * dstSpan;
        }
    }

    // ── Elevation field ───────────────────────────────────────────────

    private float[] BuildMaskedElevationFromCenters(
        FastNoiseLite elevNoise, FastNoiseLite warpNoise,
        (float cx, float cy, float radius)[] centers)
    {
        float[] elev = new float[MapWidth * MapHeight];

        for (int y = 0; y < MapHeight; y++)
            for (int x = 0; x < MapWidth; x++)
            {
                float raw = (elevNoise.GetNoise(x, y) + 1f) * 0.5f;

                // Two-octave domain warp for organic coastlines
                float wx = (warpNoise.GetNoise(x * 1.3f, y * 0.9f) + 1f) * 0.5f - 0.5f;
                float wy = (warpNoise.GetNoise(x * 0.9f + 100f, y * 1.3f + 100f) + 1f) * 0.5f - 0.5f;
                // Second warp layer at different frequency for more fractal coastlines
                float wx2 = (warpNoise.GetNoise(x * 2.7f + 300f, y * 2.1f + 200f) + 1f) * 0.5f - 0.5f;
                float wy2 = (warpNoise.GetNoise(x * 2.1f + 200f, y * 2.7f + 300f) + 1f) * 0.5f - 0.5f;

                float bestMask = 0f;
                foreach (var (cx, cy, r) in centers)
                {
                    float warpedX = (x - cx) + (wx * 0.7f + wx2 * 0.3f) * r * CoastWarpStrength;
                    float warpedY = (y - cy) + (wy * 0.7f + wy2 * 0.3f) * r * CoastWarpStrength;
                    float dist = MathF.Sqrt(warpedX * warpedX + warpedY * warpedY);
                    float t = dist / r;
                    float mask = 1f - MathF.Pow(Math.Clamp(t, 0f, 1f), IslandFalloffExp);
                    if (mask > bestMask) bestMask = mask;
                }
                elev[y * MapWidth + x] = raw * bestMask;
            }

        // Stretch land elevations so all biome bands are reachable
        StretchElevation(elev);
        return elev;
    }

    private float[] BuildMaskedElevation(FastNoiseLite elevNoise, FastNoiseLite warpNoise,
                                          Random? rng = null)
    {
        var centers = BuildIslandCenters(rng ?? new Random(42));
        return BuildMaskedElevationFromCenters(elevNoise, warpNoise, centers);
    }

    // ── Moisture field ────────────────────────────────────────────────

    /// <summary>
    /// Per-island moisture with jittered wind angles so each island can have a
    /// different wet/dry orientation. Final value is mask-weighted average.
    /// </summary>
    private float[] BuildMoistureField(FastNoiseLite moistNoise, float[] elevation,
                                        (float cx, float cy, float radius)[]? islandCenters = null,
                                        Random? rng = null)
    {
        float[] moist = new float[MapWidth * MapHeight];
        var centers = islandCenters ?? BuildIslandCenters(rng ?? new Random(Seed ?? 0));

        // Per-island wind angles: global + jitter so islands differ
        var localRng = new Random(Seed ?? 0);
        var windAngles = new float[centers.Length];
        for (int i = 0; i < centers.Length; i++)
            windAngles[i] = PrevailingWindAngleDeg + (float)(localRng.NextDouble() - 0.5) * 60f;

        for (int y = 0; y < MapHeight; y++)
            for (int x = 0; x < MapWidth; x++)
            {
                int idx = y * MapWidth + x;
                float nm = (moistNoise.GetNoise(x, y) + 1f) * 0.5f;

                float wMoist = 0f, wTotal = 0f;
                for (int i = 0; i < centers.Length; i++)
                {
                    var (icx, icy, ir) = centers[i];
                    float dx = x - icx, dy = y - icy;
                    float dist = MathF.Sqrt(dx * dx + dy * dy);
                    float t = dist / ir;
                    float mask = Math.Clamp(1f - MathF.Pow(Math.Clamp(t, 0f, 1f), IslandFalloffExp), 0f, 1f);
                    if (mask <= 0.001f) continue;

                    float windRad = windAngles[i] * MathF.PI / 180f;
                    float dot = dx * MathF.Sin(windRad) + dy * -MathF.Cos(windRad);
                    float gradient = Math.Clamp((dot / ir) * 0.5f + 0.5f, 0f, 1f);

                    float blended = gradient * (1f - MoistureNoiseWeight) + nm * MoistureNoiseWeight;
                    float localMoist = Math.Clamp(nm + (blended - nm) * WindGradientStrength, 0f, 1f);

                    wMoist += localMoist * mask;
                    wTotal += mask;
                }
                moist[idx] = wTotal < 0.001f ? 0.5f : wMoist / wTotal;
            }
        return moist;
    }

    // ── Temperature field ─────────────────────────────────────────────

    /// <summary>
    /// Build the temperature field.
    ///
    /// Each island gets a base temperature drawn from a broad noise field
    /// (simulating latitude / ocean current variation). On top of that,
    /// a lapse rate drops temperature with elevation so highlands are always cold.
    ///
    /// Formula per tile:
    ///   baseTemp  = mask-weighted average of per-island base temps (noise-derived)
    ///   lapseRate = 0.65 (temperature drops 0.65 per unit of elevation above water)
    ///   temp      = clamp(baseTemp - lapseRate * max(0, elev - waterThreshold), 0, 1)
    ///
    /// Result: coastal tiles near hot islands are 0.7-0.9 (tropical).
    ///         coastal tiles near cold islands are 0.3-0.5 (temperate).
    ///         highland tiles anywhere are 0.0-0.3 (cold).
    /// </summary>
    internal float[] BuildTemperatureField(
        FastNoiseLite tempNoise, float[] elevation,
        (float cx, float cy, float radius)[]? islandCenters = null,
        Random? rng = null)
    {
        float[] temp = new float[MapWidth * MapHeight];
        var centers = islandCenters ?? BuildIslandCenters(rng ?? new Random(Seed ?? 0));

        // Per-island base temperature derived from a broad noise sample at island center
        var baseTempArr = new float[centers.Length];
        for (int i = 0; i < centers.Length; i++)
        {
            var (icx, icy, _) = centers[i];
            // Sample broad noise at island center: range [0..1]
            float n = (tempNoise.GetNoise(icx, icy) + 1f) * 0.5f;
            // Bias toward warm (tropical island setting), range 0.40..0.90
            baseTempArr[i] = 0.40f + n * 0.50f;
        }

        const float lapseRate = 0.70f; // temperature loss per unit elevation above water

        for (int y = 0; y < MapHeight; y++)
            for (int x = 0; x < MapWidth; x++)
            {
                int idx = y * MapWidth + x;
                float el = elevation[idx];

                float wTemp = 0f, wTotal = 0f;
                for (int i = 0; i < centers.Length; i++)
                {
                    var (icx, icy, ir) = centers[i];
                    float dx = x - icx, dy = y - icy;
                    float dist = MathF.Sqrt(dx * dx + dy * dy);
                    float t = dist / ir;
                    float mask = Math.Clamp(1f - MathF.Pow(Math.Clamp(t, 0f, 1f), IslandFalloffExp), 0f, 1f);
                    if (mask <= 0.001f) continue;
                    wTemp += baseTempArr[i] * mask;
                    wTotal += mask;
                }

                float baseTemp = wTotal < 0.001f ? 0.5f : wTemp / wTotal;

                // Apply lapse rate above water line
                float aboveWater = Math.Max(0f, el - WaterThreshold);
                float lapseSpan = 1f - WaterThreshold; // normalise to land range
                float tempVal = baseTemp - lapseRate * (aboveWater / lapseSpan);

                temp[idx] = Math.Clamp(tempVal, 0f, 1f);
            }
        return temp;
    }
}