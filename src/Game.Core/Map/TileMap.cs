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
///
/// Terrain shading (baked at generation time):
///   - _shadeMap   : per-tile hillshade factor [0..1] from NW sun angle gradient.
///                   Applied by TileRenderer as a color multiplier (separate from LightMap).
///                   1.0 = full brightness (facing sun), 0.3 = deep shadow.
///   - _cliffEdges : per-tile bitmask of cliff-drop edges (CliffEdge flags: N/S/E/W).
///                   Set when elevation delta to neighbor exceeds CliffThreshold.
///                   Used by renderer (dark edge bands) and movement rules (impassable).
/// </summary>
public class TileMap : IWorldMap
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

    // -- Terrain shading (baked) --------------------------------------

    // Cliff edge bitmask flags. Stored per-tile in _cliffEdges.
    // A set bit means: from this tile, the neighbor in that direction
    // is a cliff drop (elevation delta > CliffThreshold).
    // Movement rules check this to block traversal without climbing gear.
    [Flags]
    public enum CliffEdge : byte
    {
        None = 0,
        North = 1 << 0,
        South = 1 << 1,
        East = 1 << 2,
        West = 1 << 3,
    }

    // Elevation delta that qualifies as a cliff (vs gentle slope).
    // Tuned for normalized [0..1] elevation values.
    // 0.18 = roughly 2+ biome bands of height difference on a 120x120 island.
    public const float CliffThreshold = 0.18f;

    // Gentle-slope threshold -- renders shading but not cliff bands.
    // Delta between CliffThreshold and SlopeThreshold = shaded slope only.
    public const float SlopeThreshold = 0.04f;

    // Per-tile hillshade factor [0..1]. 1.0 = sun-facing, 0.3 = shadow.
    // Null until BakeTerrainShading() is called.
    private float[]? _shadeMap;

    // Per-tile cliff edge bitmask. Null until BakeTerrainShading() is called.
    private byte[]? _cliffEdges;

    /// <summary>True if terrain shading has been baked for this map.</summary>
    public bool HasTerrainShading => _shadeMap != null;

    /// <summary>
    /// Hillshade factor for this tile [0..1].
    /// Returns 1.0 (full brightness) if shading has not been baked.
    /// </summary>
    public float GetShade(int x, int y)
    {
        if (_shadeMap == null || !InBounds(x, y)) return 1f;
        return _shadeMap[y * Width + x];
    }

    /// <summary>
    /// Cliff edge bitmask for this tile.
    /// Returns CliffEdge.None if shading has not been baked.
    /// A set bit = the neighbor in that direction is a cliff drop FROM this tile.
    /// </summary>
    public CliffEdge GetCliffEdges(int x, int y)
    {
        if (_cliffEdges == null || !InBounds(x, y)) return CliffEdge.None;
        return (CliffEdge)_cliffEdges[y * Width + x];
    }

    /// <summary>
    /// True if the edge from (x,y) toward the given direction is a cliff.
    /// Use this in movement rules: block movement without climbing gear.
    /// </summary>
    public bool IsCliff(int x, int y, CliffEdge direction)
        => (GetCliffEdges(x, y) & direction) != 0;

    /// <summary>
    /// True if this tile is a high-ground cliff top (any cliff edge pointing down).
    /// High-ground tiles grant an FOV radius bonus.
    /// The GameState.EffectiveFovRadius getter should add CliffFovBonus when
    /// the player's current tile returns true here.
    /// </summary>
    public bool IsCliffTop(int x, int y)
        => GetCliffEdges(x, y) != CliffEdge.None;

    /// <summary>FOV radius tiles added for standing on cliff-top high ground.</summary>
    public const int CliffFovBonus = 3;

    /// <summary>
    /// Bake hillshading and cliff edges from the stored _heightMap.
    /// Call once after all tiles and elevations are written (end of generation).
    ///
    /// Hillshade model: NW sun (light direction -1,-1 normalized).
    ///   gradient = finite difference of elevation over 2-tile span.
    ///   dot = -(gradX * sunX + gradY * sunY)
    ///   shade = clamp(BaseBrightness + dot * ShadingStrength, MinShade, MaxShade)
    ///
    /// Cliff edges: for each of the 4 cardinal neighbors, if the elevation
    /// DROP (this tile minus neighbor) exceeds CliffThreshold, mark that edge.
    /// Only drops are cliffs -- approaching uphill is blocked by impassability,
    /// not by the cliff system (mountains are non-walkable tiles).
    /// </summary>
    public void BakeTerrainShading()
    {
        int size = Width * Height;
        _shadeMap = new float[size];
        _cliffEdges = new byte[size];

        // Sun direction: NW at 45 degrees, normalized
        const float sunX = -0.707f;
        const float sunY = -0.707f;

        // Shading config
        const float baseBrightness = 0.75f;
        const float shadingStrength = 0.50f;
        const float minShade = 0.30f;
        const float maxShade = 1.15f; // allow slight over-bright on sun faces

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int idx = y * Width + x;

                // -- Hillshade ----------------------------------------
                // Central difference gradient (clamped at map edges)
                float eL = GetElevation(Math.Max(0, x - 1), y);
                float eR = GetElevation(Math.Min(Width - 1, x + 1), y);
                float eU = GetElevation(x, Math.Max(0, y - 1));
                float eD = GetElevation(x, Math.Min(Height - 1, y + 1));

                float gradX = (eR - eL) * 0.5f;
                float gradY = (eD - eU) * 0.5f;

                float dot = -(gradX * sunX + gradY * sunY);
                float shade = Math.Clamp(baseBrightness + dot * shadingStrength,
                                         minShade, maxShade);
                _shadeMap[idx] = shade;

                // -- Cliff edges --------------------------------------
                float myElev = GetElevation(x, y);
                byte cliffs = 0;

                // North: y-1
                if (y > 0 && (myElev - GetElevation(x, y - 1)) >= CliffThreshold)
                    cliffs |= (byte)CliffEdge.North;
                // South: y+1
                if (y < Height - 1 && (myElev - GetElevation(x, y + 1)) >= CliffThreshold)
                    cliffs |= (byte)CliffEdge.South;
                // East: x+1
                if (x < Width - 1 && (myElev - GetElevation(x + 1, y)) >= CliffThreshold)
                    cliffs |= (byte)CliffEdge.East;
                // West: x-1
                if (x > 0 && (myElev - GetElevation(x - 1, y)) >= CliffThreshold)
                    cliffs |= (byte)CliffEdge.West;

                _cliffEdges[idx] = cliffs;
            }
        }
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
        // _shadeMap and _cliffEdges allocated lazily in BakeTerrainShading()
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