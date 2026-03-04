// src/Game.Client/Input/MouseInputHandler.cs
//
// Zoom-aware version: uses camera.ZoomedTileSize for tile coordinate conversion.
// Exposes ScrollWheelDelta for Game1 to drive camera zoom.

#nullable enable

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Game.Client.Rendering;

namespace Game.Client.Input;

public sealed class MouseInputHandler
{
    private MouseState _previous;
    private MouseState _current;

    /// <summary>Tile the cursor is over. Null if off-map.</summary>
    public Point? HoveredTile { get; private set; }

    public bool RightClickDown { get; private set; }
    public bool RightClickUp { get; private set; }
    public bool RightClickHeld { get; private set; }
    public bool LeftClickDown { get; private set; }
    public Point ScreenPosition { get; private set; }

    /// <summary>
    /// Per-frame mouse wheel delta.
    /// Positive = scrolled up/toward user = zoom in.
    /// Negative = scrolled down/away = zoom out.
    /// MonoGame accumulates ScrollWheelValue; this is the per-frame difference.
    /// </summary>
    public int ScrollWheelDelta { get; private set; }

    /// <summary>
    /// Call once per frame before reading any properties.
    /// Uses camera.ZoomedTileSize for zoom-correct tile picking.
    /// </summary>
    public void Poll(Camera camera, int mapWidth, int mapHeight)
    {
        _previous = _current;
        _current = Mouse.GetState();

        ScreenPosition = new Point(_current.X, _current.Y);

        // Zoom-aware screen → tile conversion
        int ts = camera.ZoomedTileSize;
        int worldX = (int)(_current.X + camera.X);
        int worldY = (int)(_current.Y + camera.Y);
        int tileX = worldX / ts;
        int tileY = worldY / ts;

        HoveredTile = (tileX >= 0 && tileX < mapWidth && tileY >= 0 && tileY < mapHeight)
            ? new Point(tileX, tileY)
            : null;

        RightClickDown = _current.RightButton == ButtonState.Pressed
                      && _previous.RightButton == ButtonState.Released;
        RightClickUp = _current.RightButton == ButtonState.Released
                      && _previous.RightButton == ButtonState.Pressed;
        RightClickHeld = _current.RightButton == ButtonState.Pressed;
        LeftClickDown = _current.LeftButton == ButtonState.Pressed
                      && _previous.LeftButton == ButtonState.Released;

        ScrollWheelDelta = _current.ScrollWheelValue - _previous.ScrollWheelValue;
    }
}