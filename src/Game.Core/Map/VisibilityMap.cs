// src/Game.Core/Map/VisibilityMap.cs

using GoRogue.FOV;
using SadRogue.Primitives;
using SadRogue.Primitives.GridViews;
using System;

namespace Game.Core.Map;

/// <summary>
/// Per-map visibility state for Fog-of-War.
///
/// Two concepts:
///   - Visible   : currently in the player's FOV this turn (recomputed each turn)
///   - Explored  : ever seen at least once (persists for the dungeon's lifetime)
///
/// Uses GoRogue's RecursiveShadowcastingFOV for accurate LOS with wall blocking.
/// Walls are opaque; floor/entrance/exit tiles are transparent.
///
/// Turn-based: Recompute() should be called once per player action that
/// might change position or radius (movement, teleport, etc.).
/// </summary>
public sealed class VisibilityMap
{
    // ── Backing arrays ──────────────────────────────────────────────
    // Flat [y * Width + x] layout, same as TileMap.

    private readonly bool[] _visible;
    private readonly bool[] _explored;

    public int Width { get; }
    public int Height { get; }

    // ── GoRogue FOV ─────────────────────────────────────────────────

    private readonly RecursiveShadowcastingFOV _fov;

    // ── Construction ────────────────────────────────────────────────

    /// <summary>
    /// Create a VisibilityMap for a given map.
    /// <paramref name="isTransparent"/> should return true for any tile
    /// that does NOT block line-of-sight (floors, open doors, etc.).
    /// </summary>
    public VisibilityMap(int width, int height, Func<int, int, bool> isTransparent)
    {
        Width = width;
        Height = height;

        _visible = new bool[width * height];
        _explored = new bool[width * height];

        // Build the GoRogue transparency grid view from our callback.
        // LambdaGridView wraps any Func<Point, T> into the IGridView<T> interface.
        var transparencyView = new LambdaGridView<bool>(
            width, height,
            p => isTransparent(p.X, p.Y)
        );

        _fov = new RecursiveShadowcastingFOV(transparencyView);
    }

    // ── Public API ──────────────────────────────────────────────────

    /// <summary>Returns true if the tile is currently in the player's FOV.</summary>
    public bool IsVisible(int x, int y)
    {
        if (!InBounds(x, y)) return false;
        return _visible[y * Width + x];
    }

    /// <summary>Returns true if the tile has ever been seen.</summary>
    public bool IsExplored(int x, int y)
    {
        if (!InBounds(x, y)) return false;
        return _explored[y * Width + x];
    }

    /// <summary>
    /// Recompute visibility from <paramref name="originX"/>, <paramref name="originY"/>
    /// with a circular radius of <paramref name="radius"/> tiles.
    ///
    /// Call this once per turn whenever the player moves or the radius changes.
    /// </summary>
    public void Recompute(int originX, int originY, int radius)
    {
        // Clear previous visible set (explored is never cleared)
        Array.Clear(_visible, 0, _visible.Length);

        // GoRogue FOV compute — fills its internal ResultView
        _fov.Calculate(originX, originY, radius, Distance.Euclidean);

        // Copy results into our flat arrays and accumulate explored
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                bool lit = _fov.NewlySeen.Contains(new Point(x, y))
                        || _fov.CurrentFOV.Contains(new Point(x, y));

                // Also check the FOV's bool grid view directly
                lit = _fov.BooleanResultView[x, y];

                int idx = y * Width + x;
                _visible[idx] = lit;
                if (lit) _explored[idx] = true;
            }
        }
    }

    /// <summary>
    /// Force-mark a single tile as explored (e.g. map reveal items, entrance tile).
    /// Does not mark it visible.
    /// </summary>
    public void MarkExplored(int x, int y)
    {
        if (InBounds(x, y))
            _explored[y * Width + x] = true;
    }

    /// <summary>Reveal the entire map as explored (debug / map-reveal items).</summary>
    public void RevealAll()
    {
        Array.Fill(_explored, true);
    }

    /// <summary>Clear all visibility and exploration (use when entering a new map).</summary>
    public void Reset()
    {
        Array.Clear(_visible, 0, _visible.Length);
        Array.Clear(_explored, 0, _explored.Length);
    }

    // ── Private ─────────────────────────────────────────────────────

    private bool InBounds(int x, int y)
        => x >= 0 && x < Width && y >= 0 && y < Height;
}