// src/Game.ProcGen/Generators/OverworldGenerator.Volcanoes.cs
//
// Volcano stamping pipeline:
//   StampVolcanoesWithOverlays -- orchestrates per-volcano stamp, builds lava/crater overlays
//   PickVolcanoCenter          -- selects the highest suitable inland tile per volcano
//   StampCone                  -- raises elevation in a cone shape (Shield or Strato profile)
//   StampCaldera               -- hollows the summit and marks crater lake tiles
//   CarveAndMarkLavaFlows      -- radial lava channels walking downhill from the peak
//   NudgeTowardDownhill        -- steering helper used by lava flow walk

using System;
using System.Collections.Generic;
using Game.Core.WorldGen;

namespace Game.ProcGen.Generators;

public partial class OverworldGenerator
{
    // ── Orchestration ─────────────────────────────────────────────────

    /// <summary>
    /// Stamp volcanoes and return two overlays: lava flow tiles and crater lake tiles.
    /// Caldera craters are encoded separately so the tile write can use the right tile.
    /// </summary>
    private (bool[] LavaFlow, bool[] CraterLake) StampVolcanoesWithOverlays(float[] elev, Random rng)
    {
        bool[] lava = new bool[MapWidth * MapHeight];
        bool[] crater = new bool[MapWidth * MapHeight];
        VolcanoCenters = new List<(int X, int Y)>();

        if (VolcanoCount <= 0)
            return (lava, crater);

        float cx = MapWidth * 0.5f;
        float cy = MapHeight * 0.5f;
        float innerRadius = MathF.Min(cx, cy) * IslandRadiusScale * 0.55f;

        for (int v = 0; v < VolcanoCount; v++)
        {
            (int vx, int vy) = PickVolcanoCenter(elev, rng, innerRadius);
            VolcanoCenters.Add((vx, vy));

            StampCone(elev, vx, vy);

            if (VolcanoType == VolcanoType.Caldera)
                StampCaldera(elev, crater, vx, vy);

            if (LavaFlowCount > 0)
                CarveAndMarkLavaFlows(elev, lava, rng, vx, vy);
        }

        // Crater lake takes priority over any lava flow that landed in the same spot
        for (int i = 0; i < crater.Length; i++)
            if (crater[i]) lava[i] = false;

        return (lava, crater);
    }

    // ── Center selection ──────────────────────────────────────────────

    /// <summary>
    /// Choose a volcano center: the highest inland tile that is far enough from
    /// already-placed volcano centers and from the island edge.
    /// Falls back to a random high-elevation inland tile if the search fails.
    /// </summary>
    private (int X, int Y) PickVolcanoCenter(float[] elev, Random rng, float innerRadius)
    {
        float cx = MapWidth * 0.5f;
        float cy = MapHeight * 0.5f;
        int minSep = (int)(VolcanoBaseRadius * 2.2f); // separation between volcanos

        // Collect all candidate tiles inside innerRadius
        var candidates = new List<(int X, int Y, float E)>();
        for (int y = 1; y < MapHeight - 1; y++)
        {
            for (int x = 1; x < MapWidth - 1; x++)
            {
                float dx = x - cx;
                float dy = y - cy;
                if (MathF.Sqrt(dx * dx + dy * dy) > innerRadius)
                    continue;

                float e = elev[y * MapWidth + x];
                if (e < 0.55f) // must already be fairly high ground
                    continue;

                // Check separation from existing volcanos
                bool tooClose = false;
                foreach (var (px, py) in VolcanoCenters)
                {
                    int mdist = Math.Abs(x - px) + Math.Abs(y - py);
                    if (mdist < minSep) { tooClose = true; break; }
                }
                if (!tooClose)
                    candidates.Add((x, y, e));
            }
        }

        if (candidates.Count == 0)
        {
            // Fallback: map center
            return (MapWidth / 2, MapHeight / 2);
        }

        // Sort by elevation descending, pick from the top 15% randomly for variety
        candidates.Sort((a, b) => b.E.CompareTo(a.E));
        int topN = Math.Max(1, candidates.Count / 7);
        var (rx, ry, _) = candidates[rng.Next(topN)];
        return (rx, ry);
    }

    // ── Cone stamping ─────────────────────────────────────────────────

    /// <summary>
    /// Add a volcanic cone elevation boost centred at (vx, vy).
    ///
    /// Shield profile : boost falls off linearly (t). Wide gentle slope.
    /// Strato profile : boost falls off as t^2.5 giving a steeper, sharper peak.
    /// Caldera profile: same as Strato (caldera is stamped separately afterwards).
    /// </summary>
    private void StampCone(float[] elev, int vx, int vy)
    {
        int r = VolcanoBaseRadius;
        float peakBoost = VolcanoPeakHeight;

        for (int y = vy - r; y <= vy + r; y++)
        {
            if (y < 0 || y >= MapHeight) continue;
            for (int x = vx - r; x <= vx + r; x++)
            {
                if (x < 0 || x >= MapWidth) continue;

                float dx = x - vx;
                float dy = y - vy;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist >= r) continue;

                float t = 1f - (dist / r); // 1.0 at center, 0.0 at edge

                float boost = VolcanoType == VolcanoType.Shield
                    ? peakBoost * t                           // linear - broad dome
                    : peakBoost * MathF.Pow(t, 2.5f);        // steep - strato / caldera

                int i = y * MapWidth + x;
                elev[i] = Math.Clamp(elev[i] + boost, 0f, 1f + peakBoost);
            }
        }
    }

    // ── Caldera stamping ──────────────────────────────────────────────

    /// <summary>
    /// Hollow out the summit for a Caldera volcano.
    /// Depresses the elevation within the crater radius back to a bowl shape,
    /// then marks the innermost tiles for crater lake rendering.
    /// </summary>
    private void StampCaldera(float[] elev, bool[] lava, int vx, int vy)
    {
        int craterR = (int)(VolcanoBaseRadius * CalderaRadiusFraction);
        if (craterR < 2) craterR = 2;

        float rimElev = 1f + VolcanoPeakHeight * 0.8f;        // elevation of the crater rim
        float floorElev = rimElev - VolcanoPeakHeight * 0.4f; // crater floor is lower

        int lakeR = Math.Max(1, craterR - 2); // lake sits a few tiles inside the rim

        for (int y = vy - craterR; y <= vy + craterR; y++)
        {
            if (y < 0 || y >= MapHeight) continue;
            for (int x = vx - craterR; x <= vx + craterR; x++)
            {
                if (x < 0 || x >= MapWidth) continue;

                float dx = x - vx;
                float dy = y - vy;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist >= craterR) continue;

                int i = y * MapWidth + x;

                // t=0 at rim, t=1 at dead center
                float t = 1f - (dist / craterR);

                // Depress toward floor with a bowl curve
                float targetElev = rimElev - (rimElev - floorElev) * MathF.Pow(t, 1.5f);
                elev[i] = Math.Max(elev[i] - (elev[i] - targetElev) * t, floorElev);

                // Mark interior as crater lake tile (handled in tile write loop)
                if (dist <= lakeR)
                    lava[i] = true; // reusing lava overlay; tile write will use CraterLakeTile
            }
        }
    }

    // ── Lava flows ────────────────────────────────────────────────────

    /// <summary>
    /// Carve N lava channels radially from the volcano peak.
    ///
    /// Each channel is a thin corridor of tiles marching downhill from the peak.
    /// Tiles in the corridor whose elevation drops rapidly (steep gradient) are
    /// marked in the lava overlay so they render as lava flow.
    ///
    /// The channel walk uses a simple downhill steepest-descent nudged by the
    /// ray angle, so it follows realistic lava paths that hug valleys.
    /// </summary>
    private void CarveAndMarkLavaFlows(float[] elev, bool[] lava, Random rng, int vx, int vy)
    {
        int count = LavaFlowCount;
        float flowLength = VolcanoBaseRadius * 1.4f; // max reach from peak

        for (int f = 0; f < count; f++)
        {
            // Spread channels evenly around the cone, jitter angle a bit
            float baseAngle = (MathF.PI * 2f * f) / count;
            float jitter = ((float)rng.NextDouble() - 0.5f) * (MathF.PI / count);
            float angle = baseAngle + jitter;

            float px = vx;
            float py = vy;

            // Channel starts wide at the peak and narrows toward the end
            float startWidth = VolcanoBaseRadius * 0.12f;
            float endWidth = VolcanoBaseRadius * 0.04f;
            int steps = (int)flowLength;

            for (int s = 0; s < steps; s++)
            {
                // Advance along the ray direction
                px += MathF.Cos(angle);
                py += MathF.Sin(angle);

                int ix = (int)MathF.Round(px);
                int iy = (int)MathF.Round(py);

                if (ix < 1 || ix >= MapWidth - 1 || iy < 1 || iy >= MapHeight - 1)
                    break;

                // Nudge the angle toward the steepest downhill neighbour
                angle = NudgeTowardDownhill(elev, ix, iy, angle, 0.35f);

                // Add random meander to break up straight lines
                angle += ((float)rng.NextDouble() - 0.5f) * 0.15f;

                // Channel width tapers from start to end
                float t = (float)s / steps;
                float channelWidth = startWidth + (endWidth - startWidth) * t;
                int markR = Math.Max(1, (int)MathF.Ceiling(channelWidth));

                // Mark tiles within channel radius as lava
                for (int dy = -markR; dy <= markR; dy++)
                {
                    for (int dx = -markR; dx <= markR; dx++)
                    {
                        int nx = ix + dx;
                        int ny = iy + dy;
                        if (nx < 0 || nx >= MapWidth || ny < 0 || ny >= MapHeight)
                            continue;

                        float ddist = MathF.Sqrt(dx * dx + dy * dy);
                        if (ddist > channelWidth) continue;

                        float e = elev[ny * MapWidth + nx];
                        // Only paint lava on land above water level
                        if (e > 0.32f)
                            lava[ny * MapWidth + nx] = true;
                    }
                }

                // Stop if we've descended into shallow water territory
                if (elev[iy * MapWidth + ix] < 0.33f)
                    break;
            }
        }
    }

    // ── Downhill steering ─────────────────────────────────────────────

    /// <summary>
    /// Gently steer an angle toward the direction of steepest descent from (x,y).
    /// Used to make lava flows hug valleys instead of flying straight radially.
    /// </summary>
    private float NudgeTowardDownhill(float[] elev, int x, int y, float currentAngle, float strength)
    {
        // Sample 8 neighbours and find the lowest
        float bestElev = float.MaxValue;
        float bestAngle = currentAngle;

        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx;
                int ny = y + dy;
                if (nx < 0 || nx >= MapWidth || ny < 0 || ny >= MapHeight) continue;

                float e = elev[ny * MapWidth + nx];
                if (e < bestElev)
                {
                    bestElev = e;
                    bestAngle = MathF.Atan2(dy, dx);
                }
            }
        }

        // Lerp current angle toward downhill angle by strength
        // Simple angular blend (works fine for small nudges)
        float diff = bestAngle - currentAngle;
        // Normalise to [-pi, pi]
        while (diff > MathF.PI) diff -= MathF.PI * 2f;
        while (diff < -MathF.PI) diff += MathF.PI * 2f;
        return currentAngle + diff * strength;
    }
}