// src/Game.Core/Map/IWorldMap.cs

using Game.Core.Lighting;
using Game.Core.Tiles;
using Microsoft.Xna.Framework;

namespace Game.Core.Map;

/// <summary>
/// Common map query contract shared by TileMap (dungeons) and
/// ChunkedWorldMap (overworld).  GameState.ActiveMap and OverworldMap
/// are typed as IWorldMap so callers never depend on the concrete type.
///
/// All tile coordinates are in world (absolute) tile space.
/// </summary>
public interface IWorldMap
{
    // -- Dimensions --------------------------------------------------

    int Width { get; }
    int Height { get; }
    bool InBounds(int x, int y);

    // -- Tile queries ------------------------------------------------

    TileDef? GetTile(int x, int y);
    string? GetTileId(int x, int y);
    void SetTile(int x, int y, string tileId);

    // -- Movement / sight --------------------------------------------

    bool IsWalkable(int x, int y);
    bool IsOpaque(int x, int y);

    // -- Elevation & shading -----------------------------------------

    float GetElevation(int x, int y);
    float GetShade(int x, int y);
    TileMap.CliffEdge GetCliffEdges(int x, int y);
    bool HasTerrainShading { get; }
    void BakeTerrainShading();

    // -- Lighting / FOW ----------------------------------------------

    VisibilityMap? Visibility { get; }
    LightMap? Lighting { get; }

    void InitializeLighting(Color ambientLight);

    bool IsVisible(int x, int y);
    bool IsExplored(int x, int y);
}