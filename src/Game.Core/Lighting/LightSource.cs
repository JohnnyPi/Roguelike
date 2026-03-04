// src/Game.Core/Lighting/LightSource.cs

using Microsoft.Xna.Framework;

namespace Game.Core.Lighting;

/// <summary>
/// A single point light source placed in the world.
///
/// Used both for dungeon torches/braziers and for any future
/// dynamic sources (fire, magic, explosions).
///
/// Immutable value type -- create a new one to update.
/// Rendering reads these each frame via LightMap.
/// </summary>
public readonly struct LightSource
{
    /// <summary>Tile X coordinate of this light's origin.</summary>
    public int X { get; init; }

    /// <summary>Tile Y coordinate of this light's origin.</summary>
    public int Y { get; init; }

    /// <summary>
    /// Radius in tiles. Full intensity at origin, falls off to 0 at edge.
    /// Typical torch: 4-6. Brazier: 6-8. Campfire: 8-10.
    /// </summary>
    public float Radius { get; init; }

    /// <summary>
    /// Base color of the light (e.g. warm orange for torches, blue for magic).
    /// Blended additively onto the LightMap.
    /// </summary>
    public Color Color { get; init; }

    /// <summary>
    /// Base intensity multiplier [0.0-1.0].
    /// Used by TorchFlicker to animate the effective intensity each frame.
    /// </summary>
    public float Intensity { get; init; }

    /// <summary>
    /// Whether this light flickers (torch, fire).
    /// False for steady sources (magic orb, window).
    /// </summary>
    public bool Flickers { get; init; }

    // -- Convenience constructors ----------------------------------------

    /// <summary>Create a standard warm torch light.</summary>
    public static LightSource Torch(int x, int y, float radius = 5f)
        => new LightSource
        {
            X = x,
            Y = y,
            Radius = radius,
            Color = new Color(255, 180, 80),    // warm amber
            Intensity = 1.0f,
            Flickers = true
        };

    /// <summary>Create a cool magical light source.</summary>
    public static LightSource Magic(int x, int y, float radius = 4f)
        => new LightSource
        {
            X = x,
            Y = y,
            Radius = radius,
            Color = new Color(100, 160, 255),   // cool blue
            Intensity = 0.9f,
            Flickers = false
        };

    /// <summary>Create a dim ambient fill light (e.g. for overcast cave openings).</summary>
    public static LightSource Ambient(int x, int y, float radius, Color color)
        => new LightSource
        {
            X = x,
            Y = y,
            Radius = radius,
            Color = color,
            Intensity = 0.5f,
            Flickers = false
        };
}