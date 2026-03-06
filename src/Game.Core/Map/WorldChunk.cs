// src/Game.Core/Map/WorldChunk.cs

namespace Game.Core.Map;

/// <summary>
/// One 64x64 tile-grid chunk of the overworld.
///
/// Static terrain (TileIds + HeightMap) is generated from seed + world coords
/// and is NEVER persisted -- it can always be re-derived.
///
/// Player modifications go into DynamicOverlay (sparse dict) so only touched
/// tiles ever need to be saved.  IsDirty is set true on any write so the
/// persistence layer knows which chunks need serialising.
///
/// ShadeMap and CliffEdges are baked lazily from HeightMap via BakeTerrain().
/// They are null until that call and are NOT persisted (always re-baked on load).
/// </summary>
public sealed class WorldChunk
{
    // -- Identity --------------------------------------------------------

    public int ChunkX { get; }
    public int ChunkY { get; }

    // -- Static terrain (generated, not saved) ---------------------------

    /// <summary>Flat tile-ID array [64*64].  Index = localY * Size + localX.</summary>
    public string[] TileIds { get; }

    /// <summary>Raw elevation [0..1] from noise.  Same index layout as TileIds.</summary>
    public float[] HeightMap { get; }

    // -- Dynamic overlay (saved when dirty) ------------------------------

    /// <summary>
    /// Sparse per-cell overrides written by gameplay (mining, building, etc.).
    /// Null until the first player write so untouched chunks have zero heap overhead.
    /// Key = localY * Size + localX.
    /// </summary>
    public Dictionary<int, string>? DynamicOverlay { get; private set; }

    /// <summary>True when DynamicOverlay has unsaved changes.</summary>
    public bool IsDirty { get; private set; }

    // -- Baked shading (derived, not saved) ------------------------------

    private float[]? _shadeMap;
    private byte[]? _cliffEdges;

    public bool IsTerrainBaked => _shadeMap != null;

    // -- Constant --------------------------------------------------------

    public const int Size = 64;

    // -- Construction ----------------------------------------------------

    public WorldChunk(int chunkX, int chunkY)
    {
        ChunkX = chunkX;
        ChunkY = chunkY;
        TileIds = new string[Size * Size];
        HeightMap = new float[Size * Size];
    }

    // -- Tile access -----------------------------------------------------

    /// <summary>
    /// Return the tile ID at local coords, checking DynamicOverlay first.
    /// Returns null if local coords are out of range.
    /// </summary>
    public string? GetTileId(int localX, int localY)
    {
        if (!InBounds(localX, localY)) return null;
        int idx = localY * Size + localX;

        if (DynamicOverlay != null && DynamicOverlay.TryGetValue(idx, out var ov))
            return ov;

        return TileIds[idx];
    }

    /// <summary>
    /// Write a player-driven tile change into the overlay.
    /// Sets IsDirty = true.
    /// </summary>
    public void SetTileId(int localX, int localY, string tileId)
    {
        if (!InBounds(localX, localY)) return;
        DynamicOverlay ??= new Dictionary<int, string>();
        DynamicOverlay[localY * Size + localX] = tileId;
        IsDirty = true;
    }

    /// <summary>
    /// Write into the static TileIds array (called during generation, not
    /// gameplay -- does NOT set IsDirty).
    /// </summary>
    public void SetStaticTileId(int localX, int localY, string tileId)
    {
        if (InBounds(localX, localY))
            TileIds[localY * Size + localX] = tileId;
    }

    // -- Elevation -------------------------------------------------------

    public float GetElevation(int localX, int localY)
        => InBounds(localX, localY) ? HeightMap[localY * Size + localX] : 0f;

    public void SetElevation(int localX, int localY, float value)
    {
        if (InBounds(localX, localY))
            HeightMap[localY * Size + localX] = value;
    }

    // -- Terrain shading -------------------------------------------------

    /// <summary>
    /// Bake hillshading and cliff-edge masks from HeightMap.
    /// Uses the same algorithm as TileMap.BakeTerrainShading() but local-space.
    /// For border tiles, elevation is sampled at the edge (clamped).
    /// </summary>
    public void BakeTerrain()
    {
        _shadeMap = new float[Size * Size];
        _cliffEdges = new byte[Size * Size];

        const float sunX = -0.707f;
        const float sunY = -0.707f;
        const float baseBrightness = 0.75f;
        const float shadingStrength = 0.50f;
        const float minShade = 0.30f;
        const float maxShade = 1.15f;

        for (int ly = 0; ly < Size; ly++)
        {
            for (int lx = 0; lx < Size; lx++)
            {
                int idx = ly * Size + lx;

                float eL = GetElevation(Math.Max(0, lx - 1), ly);
                float eR = GetElevation(Math.Min(Size - 1, lx + 1), ly);
                float eU = GetElevation(lx, Math.Max(0, ly - 1));
                float eD = GetElevation(lx, Math.Min(Size - 1, ly + 1));

                float gradX = (eR - eL) * 0.5f;
                float gradY = (eD - eU) * 0.5f;
                float dot = -(gradX * sunX + gradY * sunY);
                _shadeMap[idx] = Math.Clamp(baseBrightness + dot * shadingStrength,
                                             minShade, maxShade);

                float myElev = GetElevation(lx, ly);
                byte cliffs = 0;

                if (ly > 0 && (myElev - GetElevation(lx, ly - 1)) >= TileMap.CliffThreshold) cliffs |= (byte)TileMap.CliffEdge.North;
                if (ly < Size - 1 && (myElev - GetElevation(lx, ly + 1)) >= TileMap.CliffThreshold) cliffs |= (byte)TileMap.CliffEdge.South;
                if (lx < Size - 1 && (myElev - GetElevation(lx + 1, ly)) >= TileMap.CliffThreshold) cliffs |= (byte)TileMap.CliffEdge.East;
                if (lx > 0 && (myElev - GetElevation(lx - 1, ly)) >= TileMap.CliffThreshold) cliffs |= (byte)TileMap.CliffEdge.West;

                _cliffEdges[idx] = cliffs;
            }
        }
    }

    public float GetShade(int localX, int localY)
    {
        if (_shadeMap == null || !InBounds(localX, localY)) return 1f;
        return _shadeMap[localY * Size + localX];
    }

    public TileMap.CliffEdge GetCliffEdges(int localX, int localY)
    {
        if (_cliffEdges == null || !InBounds(localX, localY)) return TileMap.CliffEdge.None;
        return (TileMap.CliffEdge)_cliffEdges[localY * Size + localX];
    }

    // -- Helpers ---------------------------------------------------------

    public static bool InBounds(int localX, int localY)
        => (uint)localX < Size && (uint)localY < Size;

    /// <summary>Mark dirty flag cleared after a successful save.</summary>
    public void ClearDirty() => IsDirty = false;
}