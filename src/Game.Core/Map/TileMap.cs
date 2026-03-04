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
///   - Visibility : GoRogue FOV result (visible / explored per tile)
///   - Lighting   : LightMap accumulation (ambient + point sources)
///
/// These are initialized lazily via InitializeLighting() so maps that
/// don't need lighting (e.g. UI preview maps) stay cheap.
/// </summary>
public class TileMap
{
    public int Width { get; }
    public int Height { get; }

    // Flat array for cache-friendly access; index = y * Width + x
    private readonly string[] _tileIds;

    // Registry lookup — maps tile ID strings to TileDef objects
    private readonly Dictionary<string, TileDef> _tileRegistry;

    // ── Lighting / FOW ──────────────────────────────────────────────

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

    // ── Construction ────────────────────────────────────────────────

    public TileMap(int width, int height, Dictionary<string, TileDef> tileRegistry)
    {
        Width = width;
        Height = height;
        _tileRegistry = tileRegistry;
        _tileIds = new string[width * height];
    }

    // ── Lighting initialization ──────────────────────────────────────

    /// <summary>
    /// Set up the VisibilityMap and LightMap for this map instance.
    ///
    /// Must be called once after tile data is fully written (after generation),
    /// because the FOV transparency view is built from the tile walkability state.
    ///
    /// <paramref name="ambientLight"> sets the starting ambient color for LightMap.
    /// Use Color.White for fully lit maps (overworld daytime),
    /// Color.Black for pitch-dark dungeons.
    /// </summary>
    public void InitializeLighting(XnaColor ambientLight)
    {
        // Transparency: a tile is see-through if it's walkable.
        // Walls and water block LOS; floors, entrances, exits don't.
        Visibility = new VisibilityMap(Width, Height,
            (x, y) =>
            {
                var tile = GetTile(x, y);
                return tile?.Walkable ?? false;
            });

        Lighting = new LightMap(Width, Height)
        {
            AmbientLight = ambientLight
        };
    }

    // ── FOW convenience pass-throughs ────────────────────────────────

    /// <summary>True if the tile is currently in the player's FOV.</summary>
    public bool IsVisible(int x, int y) => Visibility?.IsVisible(x, y) ?? true;

    /// <summary>True if the tile has ever been seen by the player.</summary>
    public bool IsExplored(int x, int y) => Visibility?.IsExplored(x, y) ?? true;

    // ── Core tile API (unchanged) ────────────────────────────────────

    /// <summary>Check if coordinates are within map bounds.</summary>
    public bool InBounds(int x, int y)
        => x >= 0 && x < Width && y >= 0 && y < Height;

    /// <summary>Get the TileDef at a position. Returns null if out of bounds.</summary>
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