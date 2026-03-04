// src/Game.Client/Rendering/TileRenderer.cs

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Game.Core;
using Game.Core.Map;
using Game.Core.Tiles;
using Game.Core.Entities;
using Game.Core.Items;

#nullable enable

namespace Game.Client.Rendering;

/// <summary>
/// Draws the tile grid and entities using colored rectangles.
/// All drawing is offset by the camera position so the viewport
/// follows the player.
///
/// Rendering pipeline order:
///   1. DrawTiles          -- each tile colored by: base color x light color x FOW state
///   2. DrawHeightArrows   -- slope indicators between height-differing neighbors (zoom >= 16px)
///   3. DrawPathOverlay    -- mouse path preview (between tiles and entities)
///   4. DrawEntities       -- only entities on currently-visible tiles
///   5. DrawPlayer         -- always drawn (player always knows where they are)
///   6. DrawWeatherOverlay -- full-screen alpha tint (overworld only)
///
/// Zoom:
///   All tile positions and sizes use camera.ZoomedTileSize instead of the constant TileSize.
///   TileSize (32) remains the canonical "1x zoom" reference value used by Camera.
///
/// FOW tile states:
///   - Not explored        : solid black
///   - Explored, not visible : ~28% brightness with cool blue desaturation tint
///   - Visible             : full color multiplied by LightMap
///
/// Height arrows (shown when ZoomedTileSize >= 16):
///   Each visible tile gets a directional mark on each face showing the height
///   relationship with that neighbor:
///     Arrow pointing IN  = neighbor is lower  (ground slopes down that way)
///     Arrow pointing OUT = neighbor is higher (ground slopes up that way)
///     Flat line          = same height as neighbor
/// </summary>
public class TileRenderer
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

    // Semi-transparent white used for height arrows
    private static readonly Color ArrowColor = new Color(255, 255, 255, 90);

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

        // Use zoom-aware tile size for all position/size calculations this frame
        int ts = camera.ZoomedTileSize;
        bool showArrows = ts >= 16; // arrows unreadable below 16px per tile

        // Calculate visible tile range from camera bounds (+2 tile overdraw buffer)
        int startX = Math.Max(0, (int)(camera.X / ts));
        int startY = Math.Max(0, (int)(camera.Y / ts));
        int endX = Math.Min(map.Width, startX + (camera.ViewportWidth / ts) + 2);
        int endY = Math.Min(map.Height, startY + (camera.ViewportHeight / ts) + 2);

        bool hasFow = map.Visibility != null;
        bool hasLighting = map.Lighting != null;

        for (int y = startY; y < endY; y++)
        {
            for (int x = startX; x < endX; x++)
            {
                var tile = map.GetTile(x, y);
                if (tile == null) continue;

                int screenX = (int)(x * ts - camera.X);
                int screenY = (int)(y * ts - camera.Y);

                if (hasFow)
                {
                    bool visible = map.IsVisible(x, y);
                    bool explored = map.IsExplored(x, y);

                    if (!explored)
                    {
                        // Never seen -- draw solid black, skip border and arrows
                        _spriteBatch.Draw(_pixel,
                            new Rectangle(screenX, screenY, ts, ts),
                            Color.Black);
                        continue;
                    }

                    if (!visible)
                    {
                        // Remembered but in darkness -- dim + cool blue desaturation
                        var dimColor = DimColor(GetCachedColor(tile), 0.28f);
                        _spriteBatch.Draw(_pixel,
                            new Rectangle(screenX, screenY, ts, ts),
                            dimColor);
                        // No border or arrows on dim tiles -- keeps them visually subordinate
                        continue;
                    }
                }

                // Fully visible tile -- apply lighting
                var baseColor = GetCachedColor(tile);
                var drawColor = hasLighting
                    ? MultiplyColors(baseColor, map.Lighting!.GetLight(x, y))
                    : baseColor;

                _spriteBatch.Draw(_pixel,
                    new Rectangle(screenX, screenY, ts, ts),
                    drawColor);

                DrawTileBorder(screenX, screenY, ts, drawColor);

                // Height arrows -- only when zoomed in enough to be readable
                if (showArrows)
                    DrawHeightArrows(map, x, y, screenX, screenY, ts);
            }
        }
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

    // -- Height arrow rendering ---------------------------------------

    /// <summary>
    /// Draws slope indicators on a visible tile based on height vs each of its 4 neighbors.
    ///
    /// Each face gets one of:
    ///   Arrow pointing inward  = neighbor is LOWER  (you'd walk downhill that way)
    ///   Arrow pointing outward = neighbor is HIGHER (you'd walk uphill that way)
    ///   Short flat line        = same height as neighbor
    ///
    /// Arrows are semi-transparent white so they read on any tile color.
    /// Only called when ts >= 16.
    /// </summary>
    private void DrawHeightArrows(TileMap map, int tx, int ty,
                                  int screenX, int screenY, int ts)
    {
        int myH = map.GetTileHeight(tx, ty);

        int cx = screenX + ts / 2;
        int cy = screenY + ts / 2;
        int reach = Math.Max(3, ts / 4); // shaft length from center to face point
        int tipLen = Math.Max(2, ts / 8); // arrowhead arm length

        // North
        DrawFaceArrow(cx, cy, cx, screenY + reach,
                      myH, map.GetTileHeight(tx, ty - 1), tipLen, isVertical: true);
        // South
        DrawFaceArrow(cx, cy, cx, screenY + ts - reach,
                      myH, map.GetTileHeight(tx, ty + 1), tipLen, isVertical: true);
        // West
        DrawFaceArrow(cx, cy, screenX + reach, cy,
                      myH, map.GetTileHeight(tx - 1, ty), tipLen, isVertical: false);
        // East
        DrawFaceArrow(cx, cy, screenX + ts - reach, cy,
                      myH, map.GetTileHeight(tx + 1, ty), tipLen, isVertical: false);
    }

    /// <summary>
    /// Draws one directional indicator from tile center (cx,cy) toward a face point (fx,fy).
    ///
    /// Same height   -> flat line only, no arrowhead
    /// Lower neighbor -> arrowhead at face point  (descending toward that neighbor)
    /// Higher neighbor -> arrowhead at center     (ascending from center toward face)
    /// </summary>
    private void DrawFaceArrow(int cx, int cy, int fx, int fy,
                                int myH, int neighborH, int tipLen, bool isVertical)
    {
        // Always draw the shaft from center to face
        DrawLine(cx, cy, fx, fy, ArrowColor);

        if (neighborH == myH) return; // flat, no arrowhead needed

        // tipX/tipY = where the arrowhead point sits
        int tipX = (neighborH < myH) ? fx : cx; // lower neighbor: tip at face; higher: tip at center
        int tipY = (neighborH < myH) ? fy : cy;

        if (isVertical)
        {
            int dir = Math.Sign(cy - tipY == 0 ? fy - cy : cy - tipY);
            DrawLine(tipX, tipY, tipX - tipLen, tipY + dir * tipLen, ArrowColor);
            DrawLine(tipX, tipY, tipX + tipLen, tipY + dir * tipLen, ArrowColor);
        }
        else
        {
            int dir = Math.Sign(cx - tipX == 0 ? fx - cx : cx - tipX);
            DrawLine(tipX, tipY, tipX + dir * tipLen, tipY - tipLen, ArrowColor);
            DrawLine(tipX, tipY, tipX + dir * tipLen, tipY + tipLen, ArrowColor);
        }
    }

    /// <summary>
    /// Draw a 1px aliased line by walking the longer axis one step at a time.
    /// </summary>
    private void DrawLine(int x1, int y1, int x2, int y2, Color color)
    {
        int dx = x2 - x1;
        int dy = y2 - y1;
        int steps = Math.Max(Math.Abs(dx), Math.Abs(dy));

        if (steps == 0)
        {
            _spriteBatch.Draw(_pixel, new Rectangle(x1, y1, 1, 1), color);
            return;
        }

        float sx = dx / (float)steps;
        float sy = dy / (float)steps;

        for (int i = 0; i <= steps; i++)
            _spriteBatch.Draw(_pixel,
                new Rectangle((int)(x1 + sx * i), (int)(y1 + sy * i), 1, 1),
                color);
    }

    // -- Entity drawing -----------------------------------------------

    private void DrawEntities(GameState state, Camera camera)
    {
        var map = state.ActiveMap!;
        int ts = camera.ZoomedTileSize;

        foreach (var entity in state.Entities)
        {
            if (!entity.IsAlive) continue;

            // Only draw entities on currently-visible tiles
            if (map.Visibility != null && !map.IsVisible(entity.X, entity.Y))
                continue;

            int screenX = (int)(entity.X * ts - camera.X);
            int screenY = (int)(entity.Y * ts - camera.Y);

            if (entity is WorldItem worldItem)
                DrawWorldItem(screenX, screenY, ts, worldItem);
            else if (entity is Chest chest)
                DrawChest(screenX, screenY, ts, chest);
            else if (entity is Enemy enemy)
                DrawEnemy(screenX, screenY, ts, enemy);
            else
            {
                // Generic entity fallback: red square
                int inset = Math.Max(1, ts / 8);
                _spriteBatch.Draw(_pixel,
                    new Rectangle(screenX + inset, screenY + inset,
                                  ts - inset * 2, ts - inset * 2),
                    Color.Red);
            }
        }
    }

    /// <summary>Draw an enemy as a colored square with a small HP bar.</summary>
    private void DrawEnemy(int screenX, int screenY, int ts, Enemy enemy)
    {
        var color = GetEnemyColor(enemy);
        int inset = Math.Max(1, ts / 8);

        _spriteBatch.Draw(_pixel,
            new Rectangle(screenX + inset, screenY + inset,
                          ts - inset * 2, ts - inset * 2),
            color);

        var darkerColor = new Color(
            (int)(color.R * 0.6f),
            (int)(color.G * 0.6f),
            (int)(color.B * 0.6f)
        );
        int innerInset = Math.Max(2, ts / 4);
        _spriteBatch.Draw(_pixel,
            new Rectangle(screenX + innerInset, screenY + innerInset,
                          ts - innerInset * 2, ts - innerInset * 2),
            darkerColor);

        // HP bar (only if damaged)
        if (enemy.Hp < enemy.MaxHp)
        {
            int barWidth = ts - inset * 2;
            int barHeight = Math.Max(2, ts / 10);
            int barY = screenY + ts - inset - barHeight;
            int barX = screenX + inset;

            _spriteBatch.Draw(_pixel,
                new Rectangle(barX, barY, barWidth, barHeight),
                new Color(40, 40, 40));

            float hpPct = (float)enemy.Hp / enemy.MaxHp;
            int fillWidth = Math.Max(1, (int)(barWidth * hpPct));
            var hpColor = hpPct > 0.5f ? new Color(50, 200, 50) : new Color(220, 50, 50);
            _spriteBatch.Draw(_pixel,
                new Rectangle(barX, barY, fillWidth, barHeight),
                hpColor);
        }
    }

    private void DrawWorldItem(int screenX, int screenY, int ts, WorldItem item)
    {
        var color = GetItemColor(item.ItemDef);
        int size = Math.Max(4, ts / 3);
        int offset = (ts - size) / 2;

        _spriteBatch.Draw(_pixel,
            new Rectangle(screenX + offset, screenY + offset, size, size),
            color);
    }

    private void DrawChest(int screenX, int screenY, int ts, Chest chest)
    {
        int inset = Math.Max(2, ts / 6);
        var color = chest.IsOpen ? new Color(80, 70, 60) : new Color(180, 130, 50);
        int lidH = Math.Max(2, ts / 8);

        // Body
        _spriteBatch.Draw(_pixel,
            new Rectangle(screenX + inset, screenY + inset + lidH,
                          ts - inset * 2, ts - inset * 2 - lidH),
            color);

        // Lid
        var lidColor = chest.IsOpen ? new Color(60, 55, 45) : new Color(200, 150, 60);
        _spriteBatch.Draw(_pixel,
            new Rectangle(screenX + inset - 1, screenY + inset,
                          ts - inset * 2 + 2, lidH),
            lidColor);
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

    // -- Player drawing -----------------------------------------------

    private void DrawPlayer(GameState state, Camera camera)
    {
        var player = state.Player;
        if (player == null || player.IsDead) return;

        int ts = camera.ZoomedTileSize;
        int screenX = (int)(player.X * ts - camera.X);
        int screenY = (int)(player.Y * ts - camera.Y);

        int inset = Math.Max(1, ts / 10);
        _spriteBatch.Draw(_pixel,
            new Rectangle(screenX + inset, screenY + inset,
                          ts - inset * 2, ts - inset * 2),
            Color.Cyan);

        int innerInset = Math.Max(2, ts / 4);
        _spriteBatch.Draw(_pixel,
            new Rectangle(screenX + innerInset, screenY + innerInset,
                          ts - innerInset * 2, ts - innerInset * 2),
            Color.White);
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
        if (tint.A == 0) return; // clear weather -- nothing to draw

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

    private Color GetEnemyColor(Enemy enemy)
    {
        var key = $"enemy:{enemy.Def.Id}";
        if (_colorCache.TryGetValue(key, out var cached))
            return cached;

        var color = ParseHexColor(enemy.Def.Color);
        _colorCache[key] = color;
        return color;
    }

    private Color GetItemColor(ItemDef def)
    {
        if (_colorCache.TryGetValue(def.Id, out var cached))
            return cached;

        var color = ParseHexColor(def.Color);
        _colorCache[def.Id] = color;
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