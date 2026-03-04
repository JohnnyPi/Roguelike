// src/Game.Core/Lighting/DungeonLightingConfig.cs

using Microsoft.Xna.Framework;

namespace Game.Core.Lighting;

/// <summary>
/// Lighting configuration for a dungeon, parsed from the blueprint YAML
/// lighting section. Passed into DungeonGenerator so it can place torches
/// and initialize the TileMap's LightMap with the right ambient color.
///
/// Default values represent a medium-dark torchlit cave.
/// A void dungeon might set AmbientLight to Color.Black and increase TorchRadius.
/// A lit wizard tower might set AmbientLight to a dim blue-white.
/// </summary>
public sealed class DungeonLightingConfig
{
    // -- Ambient light ---------------------------------------------------

    /// <summary>
    /// Base ambient color for the whole dungeon.
    /// This is the minimum light level -- areas outside all torch radii
    /// will be lit to this level.
    ///
    /// Black (0, 0, 0) = pitch dark outside torchlight.
    /// Dim grey (20, 20, 20) = faintly visible dungeon ambient.
    /// </summary>
    public Color AmbientLight { get; set; } = new Color(8, 9, 14);

    // -- Torch placement -------------------------------------------------

    /// <summary>
    /// Average number of torch light sources per room.
    /// 1.0 = one torch per room on average.
    /// 0.5 = roughly every other room has a torch.
    /// 2.0 = two torches per room.
    /// </summary>
    public float TorchesPerRoom { get; set; } = 1.0f;

    /// <summary>Torch light radius in tiles.</summary>
    public float TorchRadius { get; set; } = 5f;

    // -- FOV -------------------------------------------------------------

    /// <summary>
    /// Player FOV radius when inside this dungeon.
    /// Applied to GameState.BaseFovRadius on dungeon entry.
    /// </summary>
    public int PlayerFovRadius { get; set; } = 8;

    // -- Convenience defaults --------------------------------------------

    /// <summary>Standard torchlit cave. Default configuration.</summary>
    public static readonly DungeonLightingConfig Cave = new()
    {
        AmbientLight = new Color(8, 9, 14),
        TorchesPerRoom = 1.0f,
        TorchRadius = 5f,
        PlayerFovRadius = 8
    };

    /// <summary>Pitch-black void dungeon. Only torch areas are visible.</summary>
    public static readonly DungeonLightingConfig Void = new()
    {
        AmbientLight = Color.Black,
        TorchesPerRoom = 0.5f,
        TorchRadius = 6f,
        PlayerFovRadius = 6
    };

    /// <summary>Dim magical dungeon -- cool blue ambience, steady orb lights.</summary>
    public static readonly DungeonLightingConfig Arcane = new()
    {
        AmbientLight = new Color(15, 18, 35),
        TorchesPerRoom = 1.5f,
        TorchRadius = 4.5f,
        PlayerFovRadius = 9
    };
}