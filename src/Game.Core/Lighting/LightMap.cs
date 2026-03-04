// src/Game.Core/Lighting/LightMap.cs

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Game.Core.Lighting;

/// <summary>
/// Flat per-tile Color array representing the combined lighting at every tile.
///
/// How it works:
///   1. Start every tile at AmbientLight (the base illumination for the map).
///   2. For each LightSource, additively blend its contribution into nearby tiles
///      using inverse-square falloff clamped to [0, 1].
///   3. TileRenderer reads the resulting color and multiplies it against the
///      tile's base display color before drawing.
///
/// Recomputed once per turn (turn-based game, so no per-frame cost).
/// Flicker effects are applied by TorchFlicker at render time by injecting
/// per-source intensity offsets -- they don't require a full LightMap rebuild.
/// </summary>
public sealed class LightMap
{
    // -- Data ------------------------------------------------------------

    private readonly Color[] _light;

    public int Width { get; }
    public int Height { get; }

    /// <summary>
    /// Base ambient color. Full bright white = fully lit. Black = pitch dark.
    /// Overworld: driven by WorldClock. Dungeon: from blueprint AmbientLight.
    /// </summary>
    public Color AmbientLight { get; set; } = Color.White;

    // -- Construction ----------------------------------------------------

    public LightMap(int width, int height)
    {
        Width = width;
        Height = height;
        _light = new Color[width * height];

        // Initialize to full white so any map without lighting looks normal
        Array.Fill(_light, Color.White);
    }

    // -- Public API ------------------------------------------------------

    /// <summary>
    /// Get the light color at a tile position.
    /// Returns AmbientLight if out of bounds.
    /// </summary>
    public Color GetLight(int x, int y)
    {
        if (!InBounds(x, y)) return AmbientLight;
        return _light[y * Width + x];
    }

    /// <summary>
    /// Recompute the entire LightMap from ambient + a list of point sources.
    ///
    /// <paramref name="flickerIntensities"/> is an optional parallel array of
    /// per-source intensity overrides (0.0-1.0) applied at render time by
    /// TorchFlicker. Pass null to use each source's base Intensity.
    ///
    /// Call once per turn from Game1 after player/enemy actions resolve.
    /// </summary>
    public void Recompute(IReadOnlyList<LightSource> sources,
                          float[]? flickerIntensities = null)
    {
        // Step 1 -- Flood every tile with ambient
        for (int i = 0; i < _light.Length; i++)
            _light[i] = AmbientLight;

        // Step 2 -- Additive blend each point light
        for (int s = 0; s < sources.Count; s++)
        {
            var src = sources[s];
            float intensity = (flickerIntensities != null && s < flickerIntensities.Length)
                ? flickerIntensities[s]
                : src.Intensity;

            if (intensity <= 0f) continue;

            // Only process tiles within the bounding box of the light's radius
            int minX = Math.Max(0, (int)(src.X - src.Radius));
            int maxX = Math.Min(Width - 1, (int)(src.X + src.Radius));
            int minY = Math.Max(0, (int)(src.Y - src.Radius));
            int maxY = Math.Min(Height - 1, (int)(src.Y + src.Radius));

            float radiusSq = src.Radius * src.Radius;

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float dx = x - src.X;
                    float dy = y - src.Y;
                    float distSq = dx * dx + dy * dy;

                    if (distSq > radiusSq) continue;

                    // Smooth quadratic falloff: 1 at center, 0 at radius
                    float falloff = 1f - (distSq / radiusSq);
                    falloff = falloff * falloff; // squared for nicer gradients

                    float contribution = intensity * falloff;

                    int idx = y * Width + x;
                    _light[idx] = AddLight(_light[idx], src.Color, contribution);
                }
            }
        }
    }

    /// <summary>
    /// Fill the entire map with a flat color (used when switching modes
    /// or when a map has no dynamic lighting yet).
    /// </summary>
    public void FillFlat(Color color)
    {
        Array.Fill(_light, color);
    }

    // -- Private helpers -------------------------------------------------

    private bool InBounds(int x, int y)
        => x >= 0 && x < Width && y >= 0 && y < Height;

    /// <summary>
    /// Additively blend a light color at a given contribution level onto a base color.
    /// Each channel is treated independently and clamped to [0, 255].
    /// This mimics additive HDR blending in a simple integer form.
    /// </summary>
    private static Color AddLight(Color base_, Color lightColor, float contribution)
    {
        int r = base_.R + (int)(lightColor.R * contribution);
        int g = base_.G + (int)(lightColor.G * contribution);
        int b = base_.B + (int)(lightColor.B * contribution);

        return new Color(
            Math.Min(255, r),
            Math.Min(255, g),
            Math.Min(255, b),
            255
        );
    }
}