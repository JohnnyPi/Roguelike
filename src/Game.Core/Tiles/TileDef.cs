// src/Game.Core/Tiles/TileDef.cs

namespace Game.Core.Tiles;

/// <summary>
/// Immutable definition of a tile type, loaded from YAML.
/// This is a "def" — the template, not the instance.
/// </summary>
public sealed class TileDef
{
    /// <summary>Unique content ID, e.g. "base:grass"</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Display name for tooltips/UI</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Can entities walk on this tile?</summary>
    public bool Walkable { get; init; }

    /// <summary>Sprite key for the renderer (unused for now — we draw shapes)</summary>
    public string Sprite { get; init; } = string.Empty;

    /// <summary>
    /// Packed RGBA color for placeholder vector rendering.
    /// Format: "#RRGGBB" or "#RRGGBBAA"
    /// </summary>
    public string Color { get; init; } = "#FF00FF"; // magenta = "missing"

    /// <summary>Tags for flexible rule matching</summary>
    public List<string> Tags { get; init; } = new();
}