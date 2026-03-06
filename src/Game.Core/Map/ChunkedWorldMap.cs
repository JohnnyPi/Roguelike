// src/Game.Core/Map/ChunkedWorldMap.cs

using Game.Core.Lighting;
using Game.Core.Tiles;
using Microsoft.Xna.Framework;

namespace Game.Core.Map;

/// <summary>
/// Overworld map backed by a streaming ChunkManager instead of a flat tile array.
///
/// Implements IWorldMap so GameState.ActiveMap / OverworldMap can reference it
/// without knowing the concrete type.  All tile queries translate world coords
/// -> chunk coords + local coords and delegate to the resident WorldChunk.
///
/// Queries for chunks that are not currently resident (outside the resident
/// window) return safe defaults rather than throwing:
///   GetTile / GetTileId  -> null
///   IsWalkable           -> false
///   IsOpaque             -> true  (unloaded = blocks sight)
///   GetElevation/Shade   -> 0f / 1f
///   GetCliffEdges        -> CliffEdge.None
///
/// Rivers and volcanoes are world-level features stored here as lists of
/// world positions.  GenerateChunk stamps them during per-chunk generation.
/// </summary>
public sealed class ChunkedWorldMap : IWorldMap
{
    // -- Dimensions ------------------------------------------------------

    public int Width { get; }
    public int Height { get; }

    public bool InBounds(int x, int y)
        => (uint)x < (uint)Width && (uint)y < (uint)Height;

    // -- Chunk plumbing --------------------------------------------------

    public ChunkManager ChunkManager { get; }

    private readonly Dictionary<string, TileDef> _tileRegistry;

    /// <summary>
    /// Direct tile-def lookup by ID.  Used by TileRenderer inner loop to avoid
    /// the full Resolve() -> GetChunk() -> dictionary path per tile.
    /// </summary>
    public TileDef? LookupTileDef(string id)
    {
        _tileRegistry.TryGetValue(id, out var def);
        return def;
    }

    // -- World-level feature overlays ------------------------------------

    /// <summary>
    /// World-tile positions of all river tiles.  Written by OverworldGenerator
    /// after the full river-carve pass so per-chunk generation can stamp them.
    /// </summary>
    public List<(int X, int Y)> RiverTiles { get; } = new();

    /// <summary>Volcano cone center positions (world tiles).</summary>
    public List<(int X, int Y)> VolcanoCenters { get; } = new();

    /// <summary>Dungeon entrance world positions.</summary>
    public List<(int X, int Y)> EntrancePositions { get; } = new();

    /// <summary>Player spawn world position.</summary>
    public (int X, int Y) SpawnPosition { get; set; }

    // -- Lighting / FOW --------------------------------------------------

    public VisibilityMap? Visibility { get; private set; }
    public LightMap? Lighting { get; private set; }

    // -- HasTerrainShading -----------------------------------------------
    // Chunks bake their own terrain; report true once at least one chunk
    // with baked data exists.  Renderer checks per-tile anyway via GetShade.

    public bool HasTerrainShading => true;

    // -- Construction ----------------------------------------------------

    public ChunkedWorldMap(
        int worldWidth,
        int worldHeight,
        Dictionary<string, TileDef> tileRegistry,
        ChunkManager chunkManager)
    {
        Width = worldWidth;
        Height = worldHeight;
        _tileRegistry = tileRegistry;
        ChunkManager = chunkManager;
    }

    // -- IWorldMap : tile queries ----------------------------------------

    public TileDef? GetTile(int x, int y)
    {
        var id = GetTileId(x, y);
        if (id == null) return null;
        _tileRegistry.TryGetValue(id, out var def);
        return def;
    }

    public string? GetTileId(int x, int y)
    {
        var (chunk, lx, ly) = Resolve(x, y);
        return chunk?.GetTileId(lx, ly);
    }

    public void SetTile(int x, int y, string tileId)
    {
        var (chunk, lx, ly) = Resolve(x, y);
        chunk?.SetTileId(lx, ly, tileId);
    }

    public bool IsWalkable(int x, int y)
        => GetTile(x, y)?.Walkable ?? false;

    public bool IsOpaque(int x, int y)
    {
        if (!InBounds(x, y)) return true;
        var def = GetTile(x, y);
        // Unloaded chunk: treat as opaque (blocking sight is safer than revealing)
        if (def == null) return true;
        return def.BlocksSight;
    }

    // -- IWorldMap : elevation & shading ---------------------------------

    public float GetElevation(int x, int y)
    {
        var (chunk, lx, ly) = Resolve(x, y);
        return chunk?.GetElevation(lx, ly) ?? 0f;
    }

    // SetElevation is not part of IWorldMap but handy for generators
    public void SetElevation(int x, int y, float value)
    {
        var (chunk, lx, ly) = Resolve(x, y);
        if (chunk != null)
            chunk.SetElevation(lx, ly, value);
    }

    public float GetShade(int x, int y)
    {
        var (chunk, lx, ly) = Resolve(x, y);
        return chunk?.GetShade(lx, ly) ?? 1f;
    }

    public TileMap.CliffEdge GetCliffEdges(int x, int y)
    {
        var (chunk, lx, ly) = Resolve(x, y);
        return chunk?.GetCliffEdges(lx, ly) ?? TileMap.CliffEdge.None;
    }

    /// <summary>
    /// Bake terrain shading for all currently resident chunks.
    /// Call once after initial chunk load, then per-chunk at generate time.
    /// </summary>
    public void BakeTerrainShading()
    {
        foreach (var chunk in ChunkManager.GetResidentChunks())
        {
            if (!chunk.IsTerrainBaked)
                chunk.BakeTerrain();
        }
    }

    // -- IWorldMap : lighting / FOW --------------------------------------

    public void InitializeLighting(Color ambientLight)
    {
        // Full-world visibility map -- acceptable at launch for reasonable world sizes.
        // A chunk-based VisibilityMap is a future optimisation (noted in plan Phase 7).
        Visibility = new VisibilityMap(Width, Height,
            (x, y) => !IsOpaque(x, y));

        Lighting = new LightMap(Width, Height)
        {
            AmbientLight = ambientLight
        };
    }

    public bool IsVisible(int x, int y) => Visibility?.IsVisible(x, y) ?? true;
    public bool IsExplored(int x, int y) => Visibility?.IsExplored(x, y) ?? true;

    // -- Helpers ---------------------------------------------------------

    private (WorldChunk? chunk, int localX, int localY) Resolve(int worldX, int worldY)
    {
        if (!InBounds(worldX, worldY)) return (null, 0, 0);

        int cs = WorldChunk.Size;
        int cx = worldX / cs;
        int cy = worldY / cs;
        int lx = worldX % cs;
        int ly = worldY % cs;

        return (ChunkManager.GetChunk(cx, cy), lx, ly);
    }

    private static (int cx, int cy, int lx, int ly) WorldToChunk(int worldX, int worldY)
    {
        int cs = WorldChunk.Size;
        return (worldX / cs, worldY / cs, worldX % cs, worldY % cs);
    }
}