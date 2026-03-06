// src/Game.Client/Rendering/TileRenderer.Entities.cs

using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Game.Core;
using Game.Core.Entities;
using Game.Core.Items;

#nullable enable

namespace Game.Client.Rendering;

/// <summary>
/// Entity drawing methods for TileRenderer.
///
/// Covers DrawEntities (dispatch loop), DrawEnemy, DrawWorldItem,
/// DrawChest, DrawPlayer, and the color helpers that serve them.
///
/// All methods share _spriteBatch, _pixel, and _colorCache defined
/// in TileRenderer.cs.
/// </summary>
public partial class TileRenderer
{
    // -- Entity drawing -----------------------------------------------

    private void DrawEntities(GameState state, Camera camera)
    {
        var map = state.ActiveMap!;
        int ts = camera.ZoomedTileSize;

        // TODO: cull to camera bounds before iterating (see NEXT_STEPS)
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

    // -- Entity color helpers -----------------------------------------

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
}