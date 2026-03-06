// src/Game.Client/UI/HudManager.Minimap.cs

using Game.Client.Rendering;
using Game.Core;
using Game.Core.Map;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;

#nullable enable

namespace Game.Client.UI;

public sealed partial class HudManager
{
    /// <summary>
    /// Draw the minimap as a floating, draggable overlay window.
    /// Toggled by calling ToggleMinimap() (typically bound to [M]).
    /// Drag the title bar with left mouse button to reposition the window.
    /// Call this every frame regardless of visibility; it handles its own state.
    /// </summary>
    public void DrawMinimap(SpriteBatch spriteBatch, GameState state, Texture2D pixel,
                             SpriteFont? font = null)
    {
        // Handle drag state every frame so we don't lose track on fast moves
        var mouse = Mouse.GetState();
        bool lDown = mouse.LeftButton == ButtonState.Pressed;
        bool lClick = lDown && _prevMinimapMouse.LeftButton == ButtonState.Released;

        var titleBar = new Rectangle(_minimapWindowX, _minimapWindowY,
                                     MinimapSize + MinimapBorder * 2, MinimapTitleH);

        if (_minimapDragging)
        {
            if (lDown)
            {
                _minimapWindowX = mouse.X - _minimapDragOffX;
                _minimapWindowY = mouse.Y - _minimapDragOffY;
                // Clamp to screen
                int vpW = spriteBatch.GraphicsDevice.Viewport.Width;
                int vpH = spriteBatch.GraphicsDevice.Viewport.Height;
                _minimapWindowX = Math.Clamp(_minimapWindowX, 0, vpW - MinimapSize - MinimapBorder * 2);
                _minimapWindowY = Math.Clamp(_minimapWindowY, 0, vpH - MinimapSize - MinimapBorder * 2 - MinimapTitleH);
            }
            else
            {
                _minimapDragging = false;
            }
        }
        else if (lClick && _minimapVisible && titleBar.Contains(new Point(mouse.X, mouse.Y)))
        {
            _minimapDragging = true;
            _minimapDragOffX = mouse.X - _minimapWindowX;
            _minimapDragOffY = mouse.Y - _minimapWindowY;
        }

        _prevMinimapMouse = mouse;

        if (!_minimapVisible) return;
        if (state?.ActiveMap == null || state.Player == null) return;

        var map = state.ActiveMap;
        int mapW = map.Width;
        int mapH = map.Height;

        // Compute px per tile to fit in MinimapSize
        float tpx = Math.Min(
            (float)MinimapSize / mapW,
            (float)MinimapSize / mapH);
        int tilePx = Math.Max(1, (int)tpx);

        int drawW = Math.Min(mapW * tilePx, MinimapSize);
        int drawH = Math.Min(mapH * tilePx, MinimapSize);
        int windowW = drawW + MinimapBorder * 2;
        int windowH = drawH + MinimapBorder * 2 + MinimapTitleH;

        int wx = _minimapWindowX;
        int wy = _minimapWindowY;

        // -- Window chrome ------------------------------------------------
        // Drop shadow
        spriteBatch.Draw(pixel,
            new Rectangle(wx + 4, wy + 4, windowW, windowH),
            new Color(0, 0, 0, 100));

        // Title bar
        bool hoveringTitle = titleBar.Contains(new Point(mouse.X, mouse.Y));
        var titleColor = hoveringTitle || _minimapDragging
            ? new Color(40, 70, 110, 230)
            : new Color(20, 35, 60, 230);
        spriteBatch.Draw(pixel, titleBar, titleColor);

        // Title bar border top
        spriteBatch.Draw(pixel,
            new Rectangle(wx, wy, windowW, 1),
            new Color(80, 140, 200, 180));

        // Title bar text
        if (font != null)
        {
            string title = _minimapDragging ? "MAP  [drag]" : "MAP  [M]";
            var titleColor2 = new Color(160, 210, 255, 230);
            spriteBatch.DrawString(font, title,
                new Vector2(wx + 6, wy + (MinimapTitleH - 14) / 2),
                titleColor2, 0f, Vector2.Zero, 0.75f, SpriteEffects.None, 0f);
        }

        // Map area background
        spriteBatch.Draw(pixel,
            new Rectangle(wx, wy + MinimapTitleH, windowW, windowH - MinimapTitleH),
            new Color(4, 8, 16, 230));

        // Map area border
        var mapAreaRect = new Rectangle(wx, wy + MinimapTitleH, windowW, windowH - MinimapTitleH);
        DrawMinimapBorder(spriteBatch, pixel, mapAreaRect, new Color(55, 90, 130, 200), 1);

        // -- Map tiles ----------------------------------------------------
        int ox = wx + MinimapBorder;
        int oy = wy + MinimapTitleH + MinimapBorder;

        for (int y = 0; y < mapH; y++)
        {
            for (int x = 0; x < mapW; x++)
            {
                bool vis = map.Visibility?.IsVisible(x, y) ?? true;
                bool exp = map.Visibility?.IsExplored(x, y) ?? true;
                if (!exp) continue;

                var tileColor = ParseMinimapTileColor(map, x, y);
                if (!vis)
                    tileColor = new Color((int)(tileColor.R * 0.35f),
                                         (int)(tileColor.G * 0.35f),
                                         (int)(tileColor.B * 0.35f), 200);

                spriteBatch.Draw(pixel,
                    new Rectangle(ox + x * tilePx, oy + y * tilePx, tilePx, tilePx),
                    tileColor);
            }
        }

        // -- Player dot ---------------------------------------------------
        int px = ox + state.Player.X * tilePx;
        int py = oy + state.Player.Y * tilePx;
        int dotSize = Math.Max(3, tilePx + 2);
        spriteBatch.Draw(pixel,
            new Rectangle(px - dotSize / 2, py - dotSize / 2, dotSize, dotSize),
            Color.White);
    }

    private static void DrawMinimapBorder(SpriteBatch sb, Texture2D pixel, Rectangle r, Color c, int t)
    {
        sb.Draw(pixel, new Rectangle(r.X, r.Y, r.Width, t), c);
        sb.Draw(pixel, new Rectangle(r.X, r.Bottom - t, r.Width, t), c);
        sb.Draw(pixel, new Rectangle(r.X, r.Y, t, r.Height), c);
        sb.Draw(pixel, new Rectangle(r.Right - t, r.Y, t, r.Height), c);
    }

    private static Color ParseMinimapTileColor(IWorldMap map, int x, int y)
    {
        var def = map.GetTile(x, y);
        if (def == null) return Color.Black;
        return TileRenderer.ParseHexColor(def.Color);
    }
}