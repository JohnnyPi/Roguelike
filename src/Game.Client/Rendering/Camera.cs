// src/Game.Client/Rendering/Camera.cs

using System;

namespace Game.Client.Rendering;

/// <summary>
/// Tracks the top-left corner of the visible area in world pixel coordinates.
/// The renderer subtracts (X, Y) from entity/tile positions to get screen positions.
/// </summary>
public class Camera
{
    /// <summary>Top-left X in world pixels.</summary>
    public float X { get; private set; }

    /// <summary>Top-left Y in world pixels.</summary>
    public float Y { get; private set; }

    /// <summary>Screen width in pixels.</summary>
    public int ViewportWidth { get; private set; }

    /// <summary>Screen height in pixels.</summary>
    public int ViewportHeight { get; private set; }

    public Camera(int viewportWidth, int viewportHeight)
    {
        ViewportWidth = viewportWidth;
        ViewportHeight = viewportHeight;
    }

    /// <summary>
    /// Update viewport dimensions if the window is resized.
    /// </summary>
    public void UpdateViewport(int width, int height)
    {
        ViewportWidth = width;
        ViewportHeight = height;
    }

    /// <summary>
    /// Center the camera on a grid position, clamped to map bounds
    /// so the viewport never shows void beyond the map edges.
    /// </summary>
    /// <param name="gridX">Target tile X (e.g. player.X)</param>
    /// <param name="gridY">Target tile Y (e.g. player.Y)</param>
    /// <param name="mapWidth">Map width in tiles</param>
    /// <param name="mapHeight">Map height in tiles</param>
    public void CenterOn(int gridX, int gridY, int mapWidth, int mapHeight)
    {
        int tileSize = TileRenderer.TileSize;

        // Convert grid center to world pixels (center of the tile, not top-left)
        float worldCenterX = gridX * tileSize + tileSize / 2f;
        float worldCenterY = gridY * tileSize + tileSize / 2f;

        // Offset so the target is at the center of the viewport
        float targetX = worldCenterX - ViewportWidth / 2f;
        float targetY = worldCenterY - ViewportHeight / 2f;

        // Clamp to map bounds so we don't show empty space
        float maxX = mapWidth * tileSize - ViewportWidth;
        float maxY = mapHeight * tileSize - ViewportHeight;

        X = Math.Clamp(targetX, 0, Math.Max(0, maxX));
        Y = Math.Clamp(targetY, 0, Math.Max(0, maxY));
    }
}