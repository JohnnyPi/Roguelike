// src/Game.Client/Rendering/Camera.cs

using System;

namespace Game.Client.Rendering;

/// <summary>
/// Tracks the visible viewport in world-pixel coordinates and manages zoom level.
///
/// Zoom changes the effective tile size:
///   ZoomedTileSize = TileRenderer.TileSize * Zoom
/// All coordinate math (rendering, mouse picking) must use ZoomedTileSize,
/// not TileRenderer.TileSize directly.
///
/// Zoom is applied on the overworld (and dungeons). Mousewheel scroll drives
/// AdjustZoom() from Game1.Update().
/// </summary>
public class Camera
{
    // ── Zoom ─────────────────────────────────────────────────────────
    private float _zoom = 1.0f;

    /// <summary>Minimum zoom multiplier (0.25x = see 4x more of the map).</summary>
    public const float ZoomMin = 0.25f;

    /// <summary>Maximum zoom multiplier (3x = very close up).</summary>
    public const float ZoomMax = 3.0f;

    /// <summary>Current zoom multiplier. 1.0 = default 32px tiles.</summary>
    public float Zoom => _zoom;

    /// <summary>
    /// Tile size in pixels at the current zoom level.
    /// Minimum 2px to prevent divide-by-zero in coordinate conversions.
    /// </summary>
    public int ZoomedTileSize => Math.Max(2, (int)(TileRenderer.TileSize * _zoom));

    // ── Position ─────────────────────────────────────────────────────
    /// <summary>Top-left X in zoomed world pixels.</summary>
    public float X { get; private set; }

    /// <summary>Top-left Y in zoomed world pixels.</summary>
    public float Y { get; private set; }

    public int ViewportWidth { get; private set; }
    public int ViewportHeight { get; private set; }

    public Camera(int viewportWidth, int viewportHeight)
    {
        ViewportWidth = viewportWidth;
        ViewportHeight = viewportHeight;
    }

    public void UpdateViewport(int width, int height)
    {
        ViewportWidth = width;
        ViewportHeight = height;
    }

    /// <summary>
    /// Adjust zoom level by delta. Positive = zoom in, negative = zoom out.
    /// Typical usage: AdjustZoom(scrollWheelDelta / 1200f)
    /// Clamps to [ZoomMin, ZoomMax].
    /// </summary>
    public void AdjustZoom(float delta)
    {
        _zoom = Math.Clamp(_zoom + delta, ZoomMin, ZoomMax);
    }

    /// <summary>
    /// Center the camera on a tile, clamped to map bounds.
    /// Call this after AdjustZoom() each frame so ZoomedTileSize is current.
    /// </summary>
    public void CenterOn(int gridX, int gridY, int mapWidth, int mapHeight)
    {
        int ts = ZoomedTileSize;

        float worldCenterX = gridX * ts + ts / 2f;
        float worldCenterY = gridY * ts + ts / 2f;

        float targetX = worldCenterX - ViewportWidth / 2f;
        float targetY = worldCenterY - ViewportHeight / 2f;

        float maxX = mapWidth * ts - ViewportWidth;
        float maxY = mapHeight * ts - ViewportHeight;

        X = Math.Clamp(targetX, 0, Math.Max(0, maxX));
        Y = Math.Clamp(targetY, 0, Math.Max(0, maxY));
    }
}