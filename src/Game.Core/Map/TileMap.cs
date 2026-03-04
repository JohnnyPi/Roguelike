// src/Game.Core/Map/TileMap.cs

using Game.Core.Lighting;
using Game.Core.Tiles;
using XnaColor = Microsoft.Xna.Framework.Color;

namespace Game.Core.Map;

/// <summary>
/// 2D grid of tile definition references.
/// The map is the "instance" — it holds which TileDef occupies each cell.
/// TileDefs themselves are immutable and shared.
///
/// Also owns the per-map lighting and visibility state:
///   - Visibility : FOV result (visible / explored per tile)
///   - Lighting   : LightMap accumulation (ambient + point sources)
///
/// These are initialized lazily via InitializeLighting() so maps that
/// don't need lighting (e.g. UI preview maps) stay cheap.
///
/// Height/opacity:
///   - _heightMap      : raw elevation [0..1] stored by generators (overworld only)
///   - GetTileHeight() : coarse 0-4 level from TileDef.Height (used by renderer arrows)
///   - IsOpaque()      : drives FOW blocking -- walls/trees block, water/floor do NOT
/// </summary>
public class TileMap
{
    public int Width { get; }
    public int Height { get; }

    // Flat array for cache-friendly access; index = y * Width + x
    private readonly string[] _tileIds;

    // Registry lookup -- maps tile ID strings to TileDef objects
    private readonly Dictionary<string, TileDef> _tileRegistry;

    // -- HeightMap ----------------------------------------------------
    // Stores raw elevation values [0..1] from the noise generator.
    // Used by TileRenderer to compute height arrows between neighbors.

    private readonly float[] _heightMap;

    /// <summary>
    /// Raw elevation value for a tile (0..1). Set by generators during map creation.
    /// Not the same as TileDef.Height (which is a coarse 0-4 logical level).
    /// </summary>
    public float GetElevation(int x, int y)
        => InBounds(x, y) ? _heightMap[y * Width + x] : 0f;

    /// <summary>Set raw elevation. Call during map generation.</summary>
    public void SetElevation(int x, int y, float elevation)
    {
        if (InBounds(x, y))
            _heightMap[y * Width + x] = elevation;
    }

    /// <summary>
    /// Returns the logical height level (0-4) for a tile from TileDef.Height.
    /// Falls back to 1 (flat) if tile is unknown.
    /// </summary>
    public int GetTileHeight(int x, int y)
    {
        if (!InBounds(x, y)) return 0;
        var def = GetTile(x, y);
        return def?.Height ?? 1;
    }

    /// <summary>
    /// Returns true if the tile blocks line-of-sight for Fog-of-War.
    /// Uses TileDef.BlocksSight.
    ///   Walls, trees, mountains = opaque.
    ///   Water, grass, floor, entrances = transparent.
    /// Out-of-bounds positions are treated as opaque.
    /// </summary>
    public bool IsOpaque(int x, int y)
    {
        if (!InBounds(x, y)) return true;
        var def = GetTile(x, y);
        return def?.BlocksSight ?? false;
    }

    // -- Lighting / FOW -----------------------------------------------

    /// <summary>
    /// Fog-of-war visibility state. Null until InitializeLighting() is called.
    /// TileRenderer checks this before drawing tiles.
    /// </summary>
    public VisibilityMap? Visibility { get; private set; }

    /// <summary>
    /// Per-tile light color accumulation. Null until InitializeLighting() is called.
    /// TileRenderer multiplies tile colors by the value here.
    /// </summary>
    public LightMap? Lighting { get; private set; }

    // -- Construction -------------------------------------------------

    public TileMap(int width, int height, Dictionary<string, TileDef> tileRegistry)
    {
        Width = width;
        Height = height;
        _tileRegistry = tileRegistry;
        _tileIds = new string[width * height];
        _heightMap = new float[width * height];
    }

    // -- Lighting initialization --------------------------------------

    /// <summary>
    /// Set up the VisibilityMap and LightMap for this map instance.
    ///
    /// Must be called once after tile data is fully written (after generation),
    /// because the FOV transparency view is built from tile state.
    ///
    /// FOW blocking uses TileDef.BlocksSight, NOT walkability:
    ///   - Walls, trees, mountains : opaque  (block sight)
    ///   - Water                   : transparent (non-walkable but you can see across)
    ///   - Grass, floor, entrances : transparent
    ///
    /// ambientLight sets the starting ambient color for LightMap.
    /// Use Color.White for fully lit maps (overworld daytime),
    /// Color.Black for pitch-dark dungeons.
    /// </summary>
    public void InitializeLighting(XnaColor ambientLight)
    {
        // IsOpaque() uses TileDef.BlocksSight -- NOT walkability.
        // Water is non-walkable but transparent; the old walkable-only rule was wrong.
        Visibility = new VisibilityMap(Width, Height,
            (x, y) => !IsOpaque(x, y));

        Lighting = new LightMap(Width, Height)
        {
            AmbientLight = ambientLight
        };
    }

    // -- FOW convenience pass-throughs --------------------------------

    /// <summary>True if the tile is currently in the player's FOV.</summary>
    public bool IsVisible(int x, int y) => Visibility?.IsVisible(x, y) ?? true;

    /// <summary>True if the tile has ever been seen by the player.</summary>
    public bool IsExplored(int x, int y) => Visibility?.IsExplored(x, y) ?? true;

    // -- Core tile API ------------------------------------------------

    /// <summary>Check if coordinates are within map bounds.</summary>
    public bool InBounds(int x, int y)
        => x >= 0 && x < Width && y >= 0 && y < Height;

    /// <summary>Get the TileDef at a position. Returns null if out of bounds or unknown ID.</summary>
    public TileDef? GetTile(int x, int y)
    {
        if (!InBounds(x, y)) return null;

        var id = _tileIds[y * Width + x];
        if (id == null) return null;

        _tileRegistry.TryGetValue(id, out var def);
        return def;
    }

    /// <summary>Get the tile ID string at a position.</summary>
    public string? GetTileId(int x, int y)
    {
        if (!InBounds(x, y)) return null;
        return _tileIds[y * Width + x];
    }

    /// <summary>Set a tile by its content ID (e.g. "base:wall").</summary>
    public void SetTile(int x, int y, string tileId)
    {
        if (!InBounds(x, y)) return;
        _tileIds[y * Width + x] = tileId;
    }

    /// <summary>Fill the entire map with a single tile type.</summary>
    public void Fill(string tileId)
    {
        Array.Fill(_tileIds, tileId);
    }

    /// <summary>Check if a position is walkable (in bounds + tile exists + tile.Walkable).</summary>
    public bool IsWalkable(int x, int y)
    {
        var tile = GetTile(x, y);
        return tile?.Walkable ?? false;
    }

    /// <summary>Fill a rectangular region with a tile type.</summary>
    public void FillRect(int x, int y, int width, int height, string tileId)
    {
        for (int ty = y; ty < y + height; ty++)
            for (int tx = x; tx < x + width; tx++)
                SetTile(tx, ty, tileId);
    }
}