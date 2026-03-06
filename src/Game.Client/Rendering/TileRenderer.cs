// src/Game.Client/Rendering/TileRenderer.cs

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Game.Core;
using Game.Core.Map;
using Game.Core.Tiles;
using Game.Core.Entities;

#nullable enable

namespace Game.Client.Rendering;

/// <summary>
/// Draws the tile grid and entities using colored rectangles.
/// All drawing is offset by the camera position so the viewport
/// follows the player.
///
/// Rendering pipeline order:
///   1. DrawTiles          -- each tile colored by: (base color x hillshade) x light color x FOW state
///   2. DrawCliffEdges     -- dark face bands + shadow bands at cliff drops (baked at gen time)
///   3. DrawPathOverlay    -- mouse path preview (between tiles and entities)
///   4. DrawEntities       -- only entities on currently-visible tiles  [TileRenderer.Entities.cs]
///   5. DrawPlayer         -- always drawn (player always knows where they are)  [TileRenderer.Entities.cs]
///   6. DrawWeatherOverlay -- full-screen alpha tint (overworld only)
///
/// Zoom:
///   All tile positions and sizes use camera.ZoomedTileSize instead of the constant TileSize.
///   TileSize (32) remains the canonical "1x zoom" reference value used by Camera.
///
/// FOW tile states:
///   - Not explored          : solid black
///   - Explored, not visible : ~28% brightness with cool blue desaturation tint
///   - Visible               : base color scaled by hillshade, multiplied by LightMap
///
/// Terrain shading (overworld only, baked):
///   - Hillshade: NW sun gradient applied as a float multiplier to base tile color.
///     Sun-facing slopes brighten; shadow slopes darken. Range ~[0.30, 1.15].
///   - Cliff edges: dark face band + translucent shadow projected onto the low neighbor.
///     Cliff band thickness scales with zoom. Visible at all zoom levels.
///
/// Chunk-aware path (overworld w/ ChunkedWorldMap):
///   DrawTiles detects ChunkedWorldMap and iterates by chunk to reduce dictionary
///   lookups from O(visible_tiles) to O(visible_chunks).  Unloaded chunks are
///   rendered as dark-gray placeholder tiles so the player can see the border.
///
/// Entity drawing is in TileRenderer.Entities.cs (partial class).
/// </summary>
public partial class TileRenderer
{
    /// <summary>Base tile size in pixels at 1.0x zoom.</summary>
    public const int TileSize = 32;

    private readonly SpriteBatch _spriteBatch;
    private readonly Texture2D _pixel;

    /// <summary>
    /// Exposes the 1x1 white pixel texture for external drawing (minimap, HUD overlays).
    /// </summary>
    public Texture2D Pixel => _pixel;

    // Cache base tile colors -- avoids re-parsing hex every frame
    private readonly Dictionary<string, Color> _colorCache = new();

    // Placeholder color drawn where chunks are not yet resident
    private static readonly Color UnloadedChunkColor = new Color(20, 20, 20);

    public TileRenderer(GraphicsDevice graphicsDevice, SpriteBatch spriteBatch)
    {
        _spriteBatch = spriteBatch;

        // 1x1 white pixel texture. Everything is scaled/tinted from this.
        _pixel = new Texture2D(graphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });
    }

    // -- Public draw entry point --------------------------------------

    /// <summary>
    /// Draw the visible portion of the map, all visible entities, and weather.
    /// Call this between SpriteBatch.Begin() and .End().
    /// </summary>
    public void Draw(GameState state, Camera camera,
                     IReadOnlyList<Point>? pathPreview = null,
                     Point? pathDestination = null)
    {
        if (state.ActiveMap == null) return;

        DrawTiles(state, camera);
        DrawPathOverlay(pathPreview, pathDestination, camera);
        DrawEntities(state, camera);
        DrawPlayer(state, camera);
        DrawWeatherOverlay(state, camera);
    }

    // -- Tile drawing -------------------------------------------------

    private void DrawTiles(GameState state, Camera camera)
    {
        var map = state.ActiveMap!;
        int ts = camera.ZoomedTileSize;

        // Use the chunk-aware path when the active map is a ChunkedWorldMap.
        // This reduces chunk dictionary lookups from O(tiles) to O(chunks).
        if (map is ChunkedWorldMap chunked)
        {
            DrawTilesChunked(chunked, camera, ts);
            return;
        }

        DrawTilesFlat(map, camera, ts);
    }

    /// <summary>
    /// Standard flat-array draw path. Used for dungeons (TileMap).
    /// </summary>
    private void DrawTilesFlat(IWorldMap map, Camera camera, int ts)
    {
        bool hasShading = map.HasTerrainShading;
        bool hasFow = map.Visibility != null;
        bool hasLighting = map.Lighting != null;

        int startX = Math.Max(0, (int)(camera.X / ts));
        int startY = Math.Max(0, (int)(camera.Y / ts));
        int endX = Math.Min(map.Width, startX + (camera.ViewportWidth / ts) + 2);
        int endY = Math.Min(map.Height, startY + (camera.ViewportHeight / ts) + 2);

        for (int y = startY; y < endY; y++)
        {
            for (int x = startX; x < endX; x++)
            {
                var tile = map.GetTile(x, y);
                if (tile == null) continue;

                int screenX = (int)(x * ts - camera.X);
                int screenY = (int)(y * ts - camera.Y);

                DrawSingleTile(map, tile, x, y, screenX, screenY, ts,
                               hasFow, hasShading, hasLighting);
            }
        }
    }

    /// <summary>
    /// Chunk-aware draw path. Used for the overworld (ChunkedWorldMap).
    ///
    /// Outer loop: visible chunk range -- O(visible_chunks) manager lookups.
    /// Inner loop: visible cells within each chunk -- direct array access.
    ///
    /// Unloaded chunks render as a dark-gray solid (no tile lookup needed).
    /// </summary>
    private void DrawTilesChunked(ChunkedWorldMap map, Camera camera, int ts)
    {
        bool hasShading = map.HasTerrainShading;
        bool hasFow = map.Visibility != null;
        bool hasLighting = map.Lighting != null;

        int cs = WorldChunk.Size;

        // Visible tile range (with overdraw buffer)
        int startTX = Math.Max(0, (int)(camera.X / ts));
        int startTY = Math.Max(0, (int)(camera.Y / ts));
        int endTX = Math.Min(map.Width, startTX + (camera.ViewportWidth / ts) + 2);
        int endTY = Math.Min(map.Height, startTY + (camera.ViewportHeight / ts) + 2);

        // Convert to chunk range
        int startCX = startTX / cs;
        int startCY = startTY / cs;
        int endCX = Math.Min(map.Width / cs, endTX / cs + 1);
        int endCY = Math.Min(map.Height / cs, endTY / cs + 1);

        for (int cy = startCY; cy < endCY; cy++)
        {
            for (int cx = startCX; cx < endCX; cx++)
            {
                var chunk = map.ChunkManager.GetChunk(cx, cy);

                // Chunk origin in world tile coords
                int originX = cx * cs;
                int originY = cy * cs;

                if (chunk == null)
                {
                    // Chunk not resident -- draw placeholder rectangle for the
                    // visible portion of this chunk
                    int px0 = Math.Max(startTX, originX);
                    int py0 = Math.Max(startTY, originY);
                    int px1 = Math.Min(endTX, originX + cs);
                    int py1 = Math.Min(endTY, originY + cs);

                    int rx = (int)(px0 * ts - camera.X);
                    int ry = (int)(py0 * ts - camera.Y);
                    int rw = (px1 - px0) * ts;
                    int rh = (py1 - py0) * ts;

                    if (rw > 0 && rh > 0)
                        _spriteBatch.Draw(_pixel, new Rectangle(rx, ry, rw, rh),
                                          UnloadedChunkColor);
                    continue;
                }

                // Clamp inner tile range to visible viewport
                int lxStart = Math.Max(0, startTX - originX);
                int lyStart = Math.Max(0, startTY - originY);
                int lxEnd = Math.Min(cs, endTX - originX);
                int lyEnd = Math.Min(cs, endTY - originY);

                // Grab TileIds array directly -- avoids Resolve()->GetChunk()->dict per tile.
                var tileIds = chunk.TileIds;

                for (int ly = lyStart; ly < lyEnd; ly++)
                {
                    for (int lx = lxStart; lx < lxEnd; lx++)
                    {
                        int wx = originX + lx;
                        int wy = originY + ly;

                        // Check dynamic overlay first (sparse -- only set for player-modified tiles)
                        string? tileId;
                        if (chunk.DynamicOverlay != null &&
                            chunk.DynamicOverlay.TryGetValue(ly * cs + lx, out var dynId))
                            tileId = dynId;
                        else
                            tileId = tileIds[ly * cs + lx];

                        if (tileId == null) continue;
                        var tile = map.LookupTileDef(tileId);
                        if (tile == null) continue;

                        int screenX = (int)(wx * ts - camera.X);
                        int screenY = (int)(wy * ts - camera.Y);

                        DrawSingleTile(map, tile, wx, wy, screenX, screenY, ts,
                                       hasFow, hasShading, hasLighting);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Draw one tile at the given screen position, applying FOW, hillshade, and lighting.
    /// Shared by both the flat and chunk-aware paths.
    /// </summary>
    private void DrawSingleTile(IWorldMap map, TileDef tile,
                                 int wx, int wy, int screenX, int screenY, int ts,
                                 bool hasFow, bool hasShading, bool hasLighting)
    {
        if (hasFow)
        {
            bool visible = map.IsVisible(wx, wy);
            bool explored = map.IsExplored(wx, wy);

            if (!explored)
            {
                _spriteBatch.Draw(_pixel,
                    new Rectangle(screenX, screenY, ts, ts),
                    Color.Black);
                return;
            }

            if (!visible)
            {
                var dimColor = DimColor(GetCachedColor(tile), 0.28f);
                _spriteBatch.Draw(_pixel,
                    new Rectangle(screenX, screenY, ts, ts),
                    dimColor);
                return;
            }
        }

        // Fully visible -- apply hillshade then lighting
        var baseColor = GetCachedColor(tile);

        if (hasShading)
        {
            float shade = map.GetShade(wx, wy);
            baseColor = ScaleColor(baseColor, shade);
        }

        var drawColor = hasLighting
            ? MultiplyColors(baseColor, map.Lighting!.GetLight(wx, wy))
            : baseColor;

        _spriteBatch.Draw(_pixel,
            new Rectangle(screenX, screenY, ts, ts),
            drawColor);

        // Border lines are only visible at higher zoom levels -- skip them at smaller
        // tile sizes to avoid burning 2 extra SpriteBatch.Draw calls per tile.
        if (ts >= 20)
            DrawTileBorder(screenX, screenY, ts, drawColor);

        if (hasShading)
            DrawCliffEdges(map, wx, wy, screenX, screenY, ts);
    }

    private void DrawTileBorder(int screenX, int screenY, int ts, Color tileColor)
    {
        var borderColor = new Color(
            (int)(tileColor.R * 0.7f),
            (int)(tileColor.G * 0.7f),
            (int)(tileColor.B * 0.7f)
        );

        // Top edge and left edge only (avoids double-drawing shared edges)
        _spriteBatch.Draw(_pixel, new Rectangle(screenX, screenY, ts, 1), borderColor);
        _spriteBatch.Draw(_pixel, new Rectangle(screenX, screenY, 1, ts), borderColor);
    }

    // -- Terrain shading rendering ------------------------------------

    /// <summary>
    /// Draws dark edge bands on the high side of cliff transitions.
    ///
    /// For each cardinal edge flagged as a cliff on this tile:
    ///   - Draw a thick dark band (4px at 32px zoom, scaled) along that edge.
    ///     This represents the cliff face visible from above.
    ///   - Draw a thinner, lighter shadow band just INSIDE the neighbor tile
    ///     on the low side (cast shadow). The low neighbor draws its own band
    ///     when it is processed in the main loop so no double-dispatch needed.
    ///
    /// Band thickness scales with tile size so it looks consistent at all zooms.
    /// Accepts IWorldMap so it works with both TileMap and ChunkedWorldMap.
    /// </summary>
    private void DrawCliffEdges(IWorldMap map, int tx, int ty,
                                int screenX, int screenY, int ts)
    {
        var edges = map.GetCliffEdges(tx, ty);
        if (edges == TileMap.CliffEdge.None) return;

        var cliffColor = new Color(25, 18, 12, 230);
        var shadowColor = new Color(0, 0, 0, 90);

        int faceThick = Math.Clamp(ts / 8, 2, 8);
        int shadowThick = Math.Clamp(ts / 14, 1, 5);

        if ((edges & TileMap.CliffEdge.North) != 0)
        {
            _spriteBatch.Draw(_pixel,
                new Rectangle(screenX, screenY, ts, faceThick),
                cliffColor);
            _spriteBatch.Draw(_pixel,
                new Rectangle(screenX, screenY - shadowThick, ts, shadowThick),
                shadowColor);
        }

        if ((edges & TileMap.CliffEdge.South) != 0)
        {
            _spriteBatch.Draw(_pixel,
                new Rectangle(screenX, screenY + ts - faceThick, ts, faceThick),
                cliffColor);
            _spriteBatch.Draw(_pixel,
                new Rectangle(screenX, screenY + ts, ts, shadowThick),
                shadowColor);
        }

        if ((edges & TileMap.CliffEdge.East) != 0)
        {
            _spriteBatch.Draw(_pixel,
                new Rectangle(screenX + ts - faceThick, screenY, faceThick, ts),
                cliffColor);
            _spriteBatch.Draw(_pixel,
                new Rectangle(screenX + ts, screenY, shadowThick, ts),
                shadowColor);
        }

        if ((edges & TileMap.CliffEdge.West) != 0)
        {
            _spriteBatch.Draw(_pixel,
                new Rectangle(screenX, screenY, faceThick, ts),
                cliffColor);
            _spriteBatch.Draw(_pixel,
                new Rectangle(screenX - shadowThick, screenY, shadowThick, ts),
                shadowColor);
        }
    }

    // -- Height arrow rendering (removed) -----------------------------
    // Replaced by baked hillshade + DrawCliffEdges.
    // IsNearPlayer kept below for future use (e.g. enemy AI range checks).

    /// <summary>
    /// Returns true if tile (tx,ty) is within <radius> tiles of the player (Chebyshev distance).
    /// </summary>
    private static bool IsNearPlayer(GameState state, int tx, int ty, int radius)
    {
        var p = state.Player;
        if (p == null) return false;
        return Math.Abs(tx - p.X) <= radius && Math.Abs(ty - p.Y) <= radius;
    }

    // -- Path overlay -------------------------------------------------

    private void DrawPathOverlay(IReadOnlyList<Point>? path, Point? destination, Camera camera)
    {
        if (path == null || path.Count == 0) return;

        int ts = camera.ZoomedTileSize;
        var stepColor = new Color(255, 220, 50, 100);
        var destColor = new Color(50, 220, 255, 140);

        for (int i = 0; i < path.Count; i++)
        {
            var tile = path[i];
            bool isDest = (destination.HasValue && tile == destination.Value)
                       || i == path.Count - 1;

            var color = isDest ? destColor : stepColor;
            int screenX = (int)(tile.X * ts - camera.X);
            int screenY = (int)(tile.Y * ts - camera.Y);

            _spriteBatch.Draw(_pixel,
                new Rectangle(screenX, screenY, ts, ts),
                color);

            var borderColor = isDest
                ? new Color(50, 220, 255, 200)
                : new Color(255, 220, 50, 180);

            int b = Math.Max(1, ts / 16);
            _spriteBatch.Draw(_pixel, new Rectangle(screenX, screenY, ts, b), borderColor);
            _spriteBatch.Draw(_pixel, new Rectangle(screenX, screenY + ts - b, ts, b), borderColor);
            _spriteBatch.Draw(_pixel, new Rectangle(screenX, screenY, b, ts), borderColor);
            _spriteBatch.Draw(_pixel, new Rectangle(screenX + ts - b, screenY, b, ts), borderColor);
        }
    }

    // -- Weather overlay ----------------------------------------------

    /// <summary>
    /// Draw a full-screen semi-transparent color overlay for weather effects.
    /// Only active on the overworld, and only when weather has a non-transparent tint.
    /// </summary>
    private void DrawWeatherOverlay(GameState state, Camera camera)
    {
        if (state.Mode != GameMode.Overworld) return;

        var tint = state.Weather.OverlayTint;
        if (tint.A == 0) return;

        _spriteBatch.Draw(_pixel,
            new Rectangle(0, 0, camera.ViewportWidth, camera.ViewportHeight),
            tint);
    }

    // -- Color helpers ------------------------------------------------

    private Color GetCachedColor(TileDef tile)
    {
        if (_colorCache.TryGetValue(tile.Id, out var cached))
            return cached;

        var color = ParseHexColor(tile.Color);
        _colorCache[tile.Id] = color;
        return color;
    }

    /// <summary>
    /// Multiply two colors channel-by-channel (normalized [0,1]).
    /// Used to apply the LightMap color to a tile's base color.
    /// </summary>
    private static Color MultiplyColors(Color a, Color b)
    {
        return new Color(
            (int)(a.R * b.R / 255),
            (int)(a.G * b.G / 255),
            (int)(a.B * b.B / 255),
            255
        );
    }

    /// <summary>
    /// Scale a color's RGB channels by a float factor.
    /// Used to apply the baked hillshade factor to a tile's base color.
    /// Factor > 1.0 brightens (sun-facing slopes), < 1.0 darkens (shadow slopes).
    /// Alpha is preserved.
    /// </summary>
    private static Color ScaleColor(Color c, float factor)
    {
        return new Color(
            Math.Clamp((int)(c.R * factor), 0, 255),
            Math.Clamp((int)(c.G * factor), 0, 255),
            Math.Clamp((int)(c.B * factor), 0, 255),
            c.A
        );
    }

    /// <summary>
    /// Dim a color to a fraction of its brightness with a cool blue desaturation.
    /// Gives "remembered in darkness" tiles their distinctive look.
    /// </summary>
    private static Color DimColor(Color c, float factor)
    {
        int r = (int)(c.R * factor + 20 * (1 - factor));
        int g = (int)(c.G * factor + 22 * (1 - factor));
        int b = (int)(c.B * factor + 35 * (1 - factor));

        return new Color(
            Math.Clamp(r, 0, 255),
            Math.Clamp(g, 0, 255),
            Math.Clamp(b, 0, 255),
            255
        );
    }

    /// <summary>
    /// Parse "#RRGGBB" or "#RRGGBBAA" into an XNA Color.
    /// Public static so HudManager (minimap) can reuse it without a renderer instance.
    /// </summary>
    public static Color ParseHexColor(string hex)
    {
        if (string.IsNullOrEmpty(hex) || hex[0] != '#')
            return Color.Magenta;

        try
        {
            hex = hex.TrimStart('#');
            int r = Convert.ToInt32(hex.Substring(0, 2), 16);
            int g = Convert.ToInt32(hex.Substring(2, 2), 16);
            int b = Convert.ToInt32(hex.Substring(4, 2), 16);
            int a = hex.Length >= 8 ? Convert.ToInt32(hex.Substring(6, 2), 16) : 255;
            return new Color(r, g, b, a);
        }
        catch
        {
            return Color.Magenta;
        }
    }
}