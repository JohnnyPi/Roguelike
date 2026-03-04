// src/Game.Client/Input/MouseInputHandler.cs
//
// Polls mouse state each frame and exposes clean per-frame events:
//   - Which tile the cursor is hovering over
//   - Whether right-click was freshly pressed this frame
//
// Coordinate conversion:
//   screen px  →  world px  →  tile
//   screenX = tileX * TileSize - camera.X
//   → tileX = (screenX + camera.X) / TileSize
//
// This class owns no game logic. It is purely input translation.
// PathController reads from it to drive path state.

#nullable enable

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Game.Client.Rendering;

namespace Game.Client.Input;

public sealed class MouseInputHandler
{
    private MouseState _previous;
    private MouseState _current;

    // ── Public state (valid after Poll() each frame) ──────────────

    /// <summary>Tile the mouse cursor is currently over. Null if off-map.</summary>
    public Point? HoveredTile { get; private set; }

    /// <summary>Right mouse button freshly pressed this frame.</summary>
    public bool RightClickDown { get; private set; }

    /// <summary>Right mouse button freshly released this frame.</summary>
    public bool RightClickUp { get; private set; }

    /// <summary>Right mouse button currently held.</summary>
    public bool RightClickHeld { get; private set; }

    /// <summary>Left mouse button freshly pressed this frame.</summary>
    public bool LeftClickDown { get; private set; }

    /// <summary>Current screen-space mouse position in pixels.</summary>
    public Point ScreenPosition { get; private set; }

    // ── Per-frame update ──────────────────────────────────────────

    /// <summary>
    /// Call once per frame before reading any properties.
    /// Converts screen position to tile coordinates using camera offset.
    /// </summary>
    public void Poll(Camera camera, int mapWidth, int mapHeight)
    {
        _previous = _current;
        _current = Mouse.GetState();

        ScreenPosition = new Point(_current.X, _current.Y);

        // Convert screen pixels → tile grid
        int worldX = (int)(_current.X + camera.X);
        int worldY = (int)(_current.Y + camera.Y);
        int tileX = worldX / TileRenderer.TileSize;
        int tileY = worldY / TileRenderer.TileSize;

        // Null if outside map bounds
        HoveredTile = (tileX >= 0 && tileX < mapWidth && tileY >= 0 && tileY < mapHeight)
            ? new Point(tileX, tileY)
            : null;

        // Fresh-press detection (same pattern as keyboard handler)
        RightClickDown = _current.RightButton == ButtonState.Pressed
                      && _previous.RightButton == ButtonState.Released;

        RightClickUp = _current.RightButton == ButtonState.Released
                      && _previous.RightButton == ButtonState.Pressed;

        RightClickHeld = _current.RightButton == ButtonState.Pressed;

        LeftClickDown = _current.LeftButton == ButtonState.Pressed
                      && _previous.LeftButton == ButtonState.Released;
    }
}