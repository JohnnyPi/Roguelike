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

    // Bounds of the last Recompute call -- used to clear only the previous
    // visible region rather than the entire map array.
    private int _prevX0, _prevY0, _prevX1, _prevY1;
    private bool _hasPrev;

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
        // Clear only the region that was visible last turn (avoids full-map memset).
        if (_hasPrev)
        {
            for (int y = _prevY0; y <= _prevY1; y++)
                for (int x = _prevX0; x <= _prevX1; x++)
                    _visible[y * Width + x] = false;
        }
        else
        {
            Array.Clear(_visible, 0, _visible.Length); // first call only
        }

        // GoRogue FOV compute — fills its internal ResultView
        _fov.Calculate(originX, originY, radius, Distance.Euclidean);

        // Copy results into our flat arrays and accumulate explored.
        // Only scan the bounding box around the FOV origin -- avoids iterating
        // the entire (potentially 512x512) map on every player move.
        int x0 = Math.Max(0, originX - radius - 1);
        int y0 = Math.Max(0, originY - radius - 1);
        int x1 = Math.Min(Width - 1, originX + radius + 1);
        int y1 = Math.Min(Height - 1, originY + radius + 1);

        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                bool lit = _fov.BooleanResultView[x, y];
                int idx = y * Width + x;
                _visible[idx] = lit;
                if (lit) _explored[idx] = true;
            }
            // Store bounds for next call's targeted clear
            _prevX0 = x0; _prevY0 = y0;
            _prevX1 = x1; _prevY1 = y1;
            _hasPrev = true;
        }
    }
    /// (e.g.map reveal items, entrance tile).
    
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
        _hasPrev = false;
    }

    // ── Private ─────────────────────────────────────────────────────

    private bool InBounds(int x, int y)
        => x >= 0 && x < Width && y >= 0 && y < Height;
}