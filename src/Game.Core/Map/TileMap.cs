// src/Game.Core/Map/TileMap.cs

using Game.Core.Tiles;

namespace Game.Core.Map;

/// <summary>
/// 2D grid of tile definition references.
/// The map is the "instance" — it holds which TileDef occupies each cell.
/// TileDefs themselves are immutable and shared.
/// </summary>
public class TileMap
{
    public int Width { get; }
    public int Height { get; }

    // Flat array for cache-friendly access; index = y * Width + x
    private readonly string[] _tileIds;

    // Registry lookup — maps tile ID strings to TileDef objects
    private readonly Dictionary<string, TileDef> _tileRegistry;

    public TileMap(int width, int height, Dictionary<string, TileDef> tileRegistry)
    {
        Width = width;
        Height = height;
        _tileRegistry = tileRegistry;
        _tileIds = new string[width * height];
    }

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
            {
                SetTile(tx, ty, tileId);
            }
    }
}