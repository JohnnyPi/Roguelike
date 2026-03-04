// src/Game.Client/Rendering/TileRenderer.cs

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Game.Core;
using Game.Core.Tiles;
using Game.Core.Entities;
using Game.Core.Items;

namespace Game.Client.Rendering;

/// <summary>
/// Draws the tile grid and entities using colored rectangles.
/// All drawing is offset by the camera position so the viewport
/// follows the player.
/// </summary>
public class TileRenderer
{
    public const int TileSize = 32;

    private readonly SpriteBatch _spriteBatch;
    private readonly Texture2D _pixel;
    private readonly Dictionary<string, Color> _colorCache = new();

    public TileRenderer(GraphicsDevice graphicsDevice, SpriteBatch spriteBatch)
    {
        _spriteBatch = spriteBatch;

        // Create a 1x1 white pixel texture — the foundation of all our vector drawing.
        // We scale and tint this to draw any colored rectangle.
        _pixel = new Texture2D(graphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });
    }

    /// <summary>
    /// Draw the visible portion of the map and all entities.
    /// Call this between SpriteBatch.Begin() and .End().
    /// </summary>
    public void Draw(GameState state, Camera camera)
    {
        if (state.ActiveMap == null) return;

        DrawTiles(state, camera);
        DrawEntities(state, camera);
        DrawPlayer(state, camera);
    }

    private void DrawTiles(GameState state, Camera camera)
    {
        var map = state.ActiveMap!;

        // Calculate visible tile range from camera bounds
        int startX = Math.Max(0, (int)(camera.X / TileSize));
        int startY = Math.Max(0, (int)(camera.Y / TileSize));
        int endX = Math.Min(map.Width, startX + (camera.ViewportWidth / TileSize) + 2);
        int endY = Math.Min(map.Height, startY + (camera.ViewportHeight / TileSize) + 2);

        for (int y = startY; y < endY; y++)
            for (int x = startX; x < endX; x++)
            {
                var tile = map.GetTile(x, y);
                if (tile == null) continue;

                var color = GetCachedColor(tile);
                var screenPos = new Vector2(
                    x * TileSize - camera.X,
                    y * TileSize - camera.Y
                );

                _spriteBatch.Draw(
                    _pixel,
                    new Rectangle((int)screenPos.X, (int)screenPos.Y, TileSize, TileSize),
                    color
                );

                // Draw a subtle border so tiles are distinguishable
                DrawTileBorder(screenPos, color);
            }
    }

    private void DrawTileBorder(Vector2 screenPos, Color tileColor)
    {
        // Darken the tile color for the border
        var borderColor = new Color(
            (int)(tileColor.R * 0.7f),
            (int)(tileColor.G * 0.7f),
            (int)(tileColor.B * 0.7f)
        );

        int x = (int)screenPos.X;
        int y = (int)screenPos.Y;

        // Top edge
        _spriteBatch.Draw(_pixel, new Rectangle(x, y, TileSize, 1), borderColor);
        // Left edge
        _spriteBatch.Draw(_pixel, new Rectangle(x, y, 1, TileSize), borderColor);
    }

    private void DrawEntities(GameState state, Camera camera)
    {
        foreach (var entity in state.Entities)
        {
            if (!entity.IsAlive) continue;

            var screenPos = new Vector2(
                entity.X * TileSize - camera.X,
                entity.Y * TileSize - camera.Y
            );

            if (entity is WorldItem worldItem)
            {
                DrawWorldItem(screenPos, worldItem);
            }
            else if (entity is Chest chest)
            {
                DrawChest(screenPos, chest);
            }
            else if (entity is Enemy enemy)
            {
                DrawEnemy(screenPos, enemy);
            }
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
    /// Color comes from the MonsterDef. Slightly different shape from the player
    /// to be visually distinguishable.
    /// </summary>
    private void DrawEnemy(Vector2 screenPos, Enemy enemy)
    {
        var color = GetEnemyColor(enemy);
        int inset = 4;
        int x = (int)screenPos.X;
        int y = (int)screenPos.Y;

        // Enemy body — slightly rounded look via layered rectangles
        // Outer body
        _spriteBatch.Draw(
            _pixel,
            new Rectangle(
                x + inset,
                y + inset,
                TileSize - inset * 2,
                TileSize - inset * 2
            ),
            color
        );

        // Inner highlight (darker) to distinguish from items
        var darkerColor = new Color(
            (int)(color.R * 0.6f),
            (int)(color.G * 0.6f),
            (int)(color.B * 0.6f)
        );
        int innerInset = 8;
        _spriteBatch.Draw(
            _pixel,
            new Rectangle(
                x + innerInset,
                y + innerInset,
                TileSize - innerInset * 2,
                TileSize - innerInset * 2
            ),
            darkerColor
        );

        // Small HP bar at the bottom of the tile (only if damaged)
        if (enemy.Hp < enemy.MaxHp)
        {
            int barWidth = TileSize - inset * 2;
            int barHeight = 3;
            int barY = y + TileSize - inset - barHeight;
            int barX = x + inset;

            // Background (dark)
            _spriteBatch.Draw(_pixel,
                new Rectangle(barX, barY, barWidth, barHeight),
                new Color(40, 40, 40));

            // Fill (red to green based on HP %)
            float hpPct = (float)enemy.Hp / enemy.MaxHp;
            int fillWidth = Math.Max(1, (int)(barWidth * hpPct));
            var hpColor = hpPct > 0.5f
                ? new Color(50, 200, 50)
                : new Color(220, 50, 50);
            _spriteBatch.Draw(_pixel,
                new Rectangle(barX, barY, fillWidth, barHeight),
                hpColor);
        }
    }

    /// <summary>
    /// Get the display color for an enemy, parsed from its MonsterDef.
    /// </summary>
    private Color GetEnemyColor(Enemy enemy)
    {
        var key = $"enemy:{enemy.Def.Id}";
        if (_colorCache.TryGetValue(key, out var cached))
            return cached;

        var color = ParseHexColor(enemy.Def.Color);
        _colorCache[key] = color;
        return color;
    }

    /// <summary>
    /// Draw a world item as a small colored diamond/square at the tile center.
    /// Uses the item def's color.
    /// </summary>
    private void DrawWorldItem(Vector2 screenPos, WorldItem item)
    {
        var color = GetItemColor(item.ItemDef);
        int size = 10;
        int offset = (TileSize - size) / 2;

        _spriteBatch.Draw(
            _pixel,
            new Rectangle(
                (int)screenPos.X + offset,
                (int)screenPos.Y + offset,
                size,
                size
            ),
            color
        );
    }

    /// <summary>
    /// Draw a chest as a colored square. Brown when closed, dark gray when open.
    /// </summary>
    private void DrawChest(Vector2 screenPos, Chest chest)
    {
        int inset = 5;
        var color = chest.IsOpen
            ? new Color(80, 70, 60)    // dark brown-gray = opened
            : new Color(180, 130, 50); // golden-brown = closed

        // Chest body
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

        // Chest lid (slightly wider, on top)
        var lidColor = chest.IsOpen
            ? new Color(60, 55, 45)
            : new Color(200, 150, 60);
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

    /// <summary>
    /// Get the display color for a world item, parsed from its ItemDef.
    /// </summary>
    private Color GetItemColor(ItemDef def)
    {
        if (_colorCache.TryGetValue(def.Id, out var cached))
            return cached;

        var color = ParseHexColor(def.Color);
        _colorCache[def.Id] = color;
        return color;
    }

    private void DrawPlayer(GameState state, Camera camera)
    {
        var player = state.Player;
        if (player == null || player.IsDead) return;

        var screenPos = new Vector2(
            player.X * TileSize - camera.X,
            player.Y * TileSize - camera.Y
        );

        // Player: bright cyan square, slightly inset from tile edges
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

        // Small inner highlight to distinguish from enemies
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

    /// <summary>
    /// Parse hex color from TileDef and cache it.
    /// Avoids re-parsing every frame for every tile.
    /// </summary>
    private Color GetCachedColor(TileDef tile)
    {
        if (_colorCache.TryGetValue(tile.Id, out var cached))
            return cached;

        var color = ParseHexColor(tile.Color);
        _colorCache[tile.Id] = color;
        return color;
    }

    /// <summary>Parse "#RRGGBB" or "#RRGGBBAA" into an XNA Color.</summary>
    public static Color ParseHexColor(string hex)
    {
        if (string.IsNullOrEmpty(hex) || hex[0] != '#')
            return Color.Magenta; // fallback = obvious "missing" color

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