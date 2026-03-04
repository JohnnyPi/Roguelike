// src/Game.Client/Rendering/TileRenderer.cs

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Game.Core;
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
///   1. DrawTiles         — each tile colored by: base color × light color × FOW state
///   2. DrawPathOverlay   — mouse path preview (between tiles and entities)
///   3. DrawEntities      — only entities on currently-visible tiles
///   4. DrawPlayer        — always drawn (player always knows where they are)
///   5. DrawWeatherOverlay — full-screen alpha tint (overworld only)
///
/// FOW tile states:
///   - Not explored : drawn as solid black (player has never seen this tile)
///   - Explored, not visible : drawn at ~30% brightness with a blue desaturation
///                             tint (player remembers seeing it, but it's in the dark)
///   - Visible       : drawn at full color, multiplied by the LightMap color
///
/// Lighting:
///   TileMap.Lighting provides a per-tile Color. This is multiplied against the
///   tile's base color before drawing. Full white = no modification.
/// </summary>
public class TileRenderer
{
    public const int TileSize = 32;

    private readonly SpriteBatch _spriteBatch;
    private readonly Texture2D _pixel;

    // Cache base tile colors — avoids re-parsing hex every frame
    private readonly Dictionary<string, Color> _colorCache = new();

    public TileRenderer(GraphicsDevice graphicsDevice, SpriteBatch spriteBatch)
    {
        _spriteBatch = spriteBatch;

        // 1×1 white pixel texture. Everything is scaled/tinted from this.
        _pixel = new Texture2D(graphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });
    }

    // ── Public draw entry point ──────────────────────────────────────

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

    // ── Tile drawing ─────────────────────────────────────────────────

    private void DrawTiles(GameState state, Camera camera)
    {
        var map = state.ActiveMap!;

        // Calculate visible tile range from camera bounds (add 2 for overdraw buffer)
        int startX = Math.Max(0, (int)(camera.X / TileSize));
        int startY = Math.Max(0, (int)(camera.Y / TileSize));
        int endX = Math.Min(map.Width, startX + (camera.ViewportWidth / TileSize) + 2);
        int endY = Math.Min(map.Height, startY + (camera.ViewportHeight / TileSize) + 2);

        bool hasFow = map.Visibility != null;
        bool hasLighting = map.Lighting != null;

        for (int y = startY; y < endY; y++)
        {
            for (int x = startX; x < endX; x++)
            {
                var tile = map.GetTile(x, y);
                if (tile == null) continue;

                var screenPos = new Vector2(
                    x * TileSize - camera.X,
                    y * TileSize - camera.Y
                );

                if (hasFow)
                {
                    bool visible = map.IsVisible(x, y);
                    bool explored = map.IsExplored(x, y);

                    if (!explored)
                    {
                        // Never seen — draw solid black
                        _spriteBatch.Draw(
                            _pixel,
                            new Rectangle((int)screenPos.X, (int)screenPos.Y, TileSize, TileSize),
                            Color.Black
                        );
                        continue; // skip border, no tile drawn
                    }

                    if (!visible)
                    {
                        // Remembered but in darkness — dim + desaturate
                        var dimColor = DimColor(GetCachedColor(tile), 0.28f);
                        _spriteBatch.Draw(
                            _pixel,
                            new Rectangle((int)screenPos.X, (int)screenPos.Y, TileSize, TileSize),
                            dimColor
                        );
                        // No border on remembered tiles — keeps them visually subordinate
                        continue;
                    }
                }

                // Fully visible tile — apply lighting
                var baseColor = GetCachedColor(tile);
                var drawColor = hasLighting
                    ? MultiplyColors(baseColor, map.Lighting!.GetLight(x, y))
                    : baseColor;

                _spriteBatch.Draw(
                    _pixel,
                    new Rectangle((int)screenPos.X, (int)screenPos.Y, TileSize, TileSize),
                    drawColor
                );

                DrawTileBorder(screenPos, drawColor);
            }
        }
    }

    private void DrawTileBorder(Vector2 screenPos, Color tileColor)
    {
        var borderColor = new Color(
            (int)(tileColor.R * 0.7f),
            (int)(tileColor.G * 0.7f),
            (int)(tileColor.B * 0.7f)
        );

        int x = (int)screenPos.X;
        int y = (int)screenPos.Y;

        // Top edge and left edge only (avoids double-drawing shared edges)
        _spriteBatch.Draw(_pixel, new Rectangle(x, y, TileSize, 1), borderColor);
        _spriteBatch.Draw(_pixel, new Rectangle(x, y, 1, TileSize), borderColor);
    }

    // ── Entity drawing ───────────────────────────────────────────────

    private void DrawEntities(GameState state, Camera camera)
    {
        var map = state.ActiveMap!;

        foreach (var entity in state.Entities)
        {
            if (!entity.IsAlive) continue;

            // Only draw entities on currently-visible tiles
            if (map.Visibility != null && !map.IsVisible(entity.X, entity.Y))
                continue;

            var screenPos = new Vector2(
                entity.X * TileSize - camera.X,
                entity.Y * TileSize - camera.Y
            );

            if (entity is WorldItem worldItem)
                DrawWorldItem(screenPos, worldItem);
            else if (entity is Chest chest)
                DrawChest(screenPos, chest);
            else if (entity is Enemy enemy)
                DrawEnemy(screenPos, enemy);
            else
            {
                // Generic entity fallback: red square
                int inset = 4;
                _spriteBatch.Draw(
                    _pixel,
                    new Rectangle(
                        (int)screenPos.X + inset,
                        (int)screenPos.Y + inset,
                        TileSize - inset * 2,
                        TileSize - inset * 2
                    ),
                    Color.Red
                );
            }
        }
    }

    /// <summary>
    /// Draw an enemy as a colored square with a small HP bar underneath.
    /// Color comes from the MonsterDef.
    /// </summary>
    private void DrawEnemy(Vector2 screenPos, Enemy enemy)
    {
        var color = GetEnemyColor(enemy);
        int inset = 4;
        int x = (int)screenPos.X;
        int y = (int)screenPos.Y;

        _spriteBatch.Draw(
            _pixel,
            new Rectangle(x + inset, y + inset, TileSize - inset * 2, TileSize - inset * 2),
            color
        );

        var darkerColor = new Color(
            (int)(color.R * 0.6f),
            (int)(color.G * 0.6f),
            (int)(color.B * 0.6f)
        );
        int innerInset = 8;
        _spriteBatch.Draw(
            _pixel,
            new Rectangle(x + innerInset, y + innerInset, TileSize - innerInset * 2, TileSize - innerInset * 2),
            darkerColor
        );

        // HP bar (only if damaged)
        if (enemy.Hp < enemy.MaxHp)
        {
            int barWidth = TileSize - inset * 2;
            int barHeight = 3;
            int barY = y + TileSize - inset - barHeight;
            int barX = x + inset;

            _spriteBatch.Draw(_pixel, new Rectangle(barX, barY, barWidth, barHeight), new Color(40, 40, 40));

            float hpPct = (float)enemy.Hp / enemy.MaxHp;
            int fillWidth = Math.Max(1, (int)(barWidth * hpPct));
            var hpColor = hpPct > 0.5f ? new Color(50, 200, 50) : new Color(220, 50, 50);
            _spriteBatch.Draw(_pixel, new Rectangle(barX, barY, fillWidth, barHeight), hpColor);
        }
    }

    private void DrawWorldItem(Vector2 screenPos, WorldItem item)
    {
        var color = GetItemColor(item.ItemDef);
        int size = 10;
        int offset = (TileSize - size) / 2;

        _spriteBatch.Draw(
            _pixel,
            new Rectangle((int)screenPos.X + offset, (int)screenPos.Y + offset, size, size),
            color
        );
    }

    private void DrawChest(Vector2 screenPos, Chest chest)
    {
        int inset = 5;
        var color = chest.IsOpen
            ? new Color(80, 70, 60)
            : new Color(180, 130, 50);

        _spriteBatch.Draw(
            _pixel,
            new Rectangle(
                (int)screenPos.X + inset,
                (int)screenPos.Y + inset + 4,
                TileSize - inset * 2,
                TileSize - inset * 2 - 4
            ),
            color
        );

        var lidColor = chest.IsOpen ? new Color(60, 55, 45) : new Color(200, 150, 60);
        _spriteBatch.Draw(
            _pixel,
            new Rectangle(
                (int)screenPos.X + inset - 1,
                (int)screenPos.Y + inset,
                TileSize - inset * 2 + 2,
                5
            ),
            lidColor
        );
    }

    // ── Path overlay ─────────────────────────────────────────────────

    private void DrawPathOverlay(IReadOnlyList<Point>? path, Point? destination, Camera camera)
    {
        if (path == null || path.Count == 0) return;

        var stepColor = new Color(255, 220, 50, 100);
        var destColor = new Color(50, 220, 255, 140);

        for (int i = 0; i < path.Count; i++)
        {
            var tile = path[i];
            bool isDest = (destination.HasValue && tile == destination.Value)
                       || i == path.Count - 1;

            var color = isDest ? destColor : stepColor;

            var screenPos = new Vector2(
                tile.X * TileSize - camera.X,
                tile.Y * TileSize - camera.Y
            );

            _spriteBatch.Draw(
                _pixel,
                new Rectangle((int)screenPos.X, (int)screenPos.Y, TileSize, TileSize),
                color
            );

            var borderColor = isDest
                ? new Color(50, 220, 255, 200)
                : new Color(255, 220, 50, 180);

            int b = 2;
            _spriteBatch.Draw(_pixel, new Rectangle((int)screenPos.X, (int)screenPos.Y, TileSize, b), borderColor);
            _spriteBatch.Draw(_pixel, new Rectangle((int)screenPos.X, (int)screenPos.Y + TileSize - b, TileSize, b), borderColor);
            _spriteBatch.Draw(_pixel, new Rectangle((int)screenPos.X, (int)screenPos.Y, b, TileSize), borderColor);
            _spriteBatch.Draw(_pixel, new Rectangle((int)screenPos.X + TileSize - b, (int)screenPos.Y, b, TileSize), borderColor);
        }
    }

    // ── Player drawing ───────────────────────────────────────────────

    private void DrawPlayer(GameState state, Camera camera)
    {
        var player = state.Player;
        if (player == null || player.IsDead) return;

        var screenPos = new Vector2(
            player.X * TileSize - camera.X,
            player.Y * TileSize - camera.Y
        );

        int inset = 3;
        _spriteBatch.Draw(
            _pixel,
            new Rectangle(
                (int)screenPos.X + inset,
                (int)screenPos.Y + inset,
                TileSize - inset * 2,
                TileSize - inset * 2
            ),
            Color.Cyan
        );

        int innerInset = 8;
        _spriteBatch.Draw(
            _pixel,
            new Rectangle(
                (int)screenPos.X + innerInset,
                (int)screenPos.Y + innerInset,
                TileSize - innerInset * 2,
                TileSize - innerInset * 2
            ),
            Color.White
        );
    }

    // ── Weather overlay ───────────────────────────────────────────────

    /// <summary>
    /// Draw a full-screen semi-transparent color overlay for weather effects.
    /// Only active on the overworld, and only when weather has a non-transparent tint.
    /// </summary>
    private void DrawWeatherOverlay(GameState state, Camera camera)
    {
        if (state.Mode != GameMode.Overworld) return;

        var tint = state.Weather.OverlayTint;
        if (tint.A == 0) return; // Clear weather — nothing to draw

        _spriteBatch.Draw(
            _pixel,
            new Rectangle(0, 0, camera.ViewportWidth, camera.ViewportHeight),
            tint
        );
    }

    // ── Color helpers ─────────────────────────────────────────────────

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
    /// Multiply two colors channel-by-channel (normalized to [0, 1]).
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
    /// Dim a color to a fraction of its brightness, and add a slight cool blue
    /// desaturation to give "remembered in darkness" tiles their distinctive look.
    /// </summary>
    private static Color DimColor(Color c, float factor)
    {
        // Dim and blend slightly toward a cool grey-blue
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

    /// <summary>Parse "#RRGGBB" or "#RRGGBBAA" into an XNA Color.</summary>
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