// src/Game.Core/WorldGen/IslandGenConfig.cs
//
// Data transfer object for all tunable island generation parameters.
//
// Usage:
//   - Create with defaults:        var cfg = new IslandGenConfig();
//   - Customize:                   cfg = cfg with { Seed = 42, EntranceCount = 3 };
//   - Pass to generator:           OverworldGenerator.FromConfig(cfg).Generate(...);
//   - Serialize to YAML/JSON for save/load of named island presets.
//
// Each property carries RangeHint attributes so a preview UI can build
// sliders automatically without hardcoding bounds in the UI layer.

using System;
using System.ComponentModel.DataAnnotations;

namespace Game.Core.WorldGen;

/// <summary>
/// Volcanic personality determines the shape of the stamped cone and whether a
/// caldera crater lake is carved at the summit.
/// </summary>
public enum VolcanoType
{
    /// <summary>Broad, low-slope dome. Wide base, gentle elevation gain, no caldera.</summary>
    Shield,
    /// <summary>Steep-sided classic cone. Narrower base, sharper peak. Default.</summary>
    Strato,
    /// <summary>Collapsed summit. Steep cone but the very top is hollowed into a crater lake.</summary>
    Caldera,
}

/// <summary>
/// Immutable configuration record for overworld island generation.
/// All parameters map 1:1 to OverworldGenerator properties.
/// Use "with" expressions to derive variants from a base preset.
/// </summary>
public sealed record IslandGenConfig
{
    // ── Seed ─────────────────────────────────────────────────────────

    /// <summary>
    /// RNG seed. Null = random (use Environment.TickCount at generation time).
    /// Set an explicit value to reproduce the same island.
    /// </summary>
    public int? Seed { get; init; } = null;

    // ── Map size ─────────────────────────────────────────────────────

    /// <summary>Map width in tiles. Should match MapHeight for square islands.</summary>
    [Range(64, 1024)]
    public int MapWidth { get; init; } = 512;

    /// <summary>Map height in tiles.</summary>
    [Range(64, 1024)]
    public int MapHeight { get; init; } = 512;

    // ── Noise / FBm ──────────────────────────────────────────────────

    /// <summary>
    /// Base noise frequency. Lower = broader, smoother terrain features.
    /// Recommended range: 0.004 (very smooth) to 0.02 (noisy).
    /// Scale this down if you increase MapWidth/MapHeight.
    /// </summary>
    [Range(0.002f, 0.04f)]
    public float Frequency { get; init; } = 0.008f;

    /// <summary>
    /// FBm octave count. More octaves = finer detail at a CPU cost.
    /// 4 is fast, 6 is default, 8 is detailed.
    /// </summary>
    [Range(1, 8)]
    public int Octaves { get; init; } = 6;

    /// <summary>
    /// FBm lacunarity. Controls frequency multiplier per octave.
    /// 2.0 is standard. Values above 2.5 produce harsh terrain.
    /// </summary>
    [Range(1.5f, 3.0f)]
    public float Lacunarity { get; init; } = 2.0f;

    /// <summary>
    /// FBm gain (persistence). Controls amplitude falloff per octave.
    /// 0.5 is standard. Higher = rougher, more fractal surface.
    /// </summary>
    [Range(0.25f, 0.75f)]
    public float Gain { get; init; } = 0.5f;

    // ── Island count (archipelago) ────────────────────────────────────

    /// <summary>
    /// Number of islands to generate. 1 = single island. 2-6 = island chain / archipelago.
    /// Islands are arranged along a randomised chain axis across the map.
    /// </summary>
    [Range(1, 6)]
    public int IslandCount { get; init; } = 1;

    // ── Island shape ─────────────────────────────────────────────────

    /// <summary>
    /// Island radius as a fraction of half the shortest map dimension.
    /// 0.85 leaves a comfortable water border. 0.95 nearly fills the map.
    /// </summary>
    [Range(0.4f, 0.95f)]
    public float IslandRadiusScale { get; init; } = 0.85f;

    /// <summary>
    /// Falloff exponent for the island mask edge.
    /// 1.0 = linear drop to ocean. 2.0 = smooth. 3.5+ = near-cliff coastline.
    /// </summary>
    [Range(1.0f, 4.0f)]
    public float IslandFalloffExp { get; init; } = 2.2f;

    /// <summary>
    /// Coastline raggedness via domain warp.
    /// 0.0 = perfect circle. 0.18 = natural. 0.4+ = very jagged fjord-like.
    /// </summary>
    [Range(0.0f, 0.5f)]
    public float CoastWarpStrength { get; init; } = 0.18f;

    // ── Climate / moisture ───────────────────────────────────────────

    /// <summary>
    /// Prevailing wind direction in degrees (compass bearing the wind blows FROM).
    /// 0 = wind from North (south face is wet).
    /// 90 = wind from East (west face is wet).
    /// 225 = southwest trade winds (default).
    /// </summary>
    [Range(0f, 360f)]
    public float PrevailingWindAngleDeg { get; init; } = 225f;

    /// <summary>
    /// How strongly the directional wind gradient dominates moisture distribution.
    /// 0.0 = no rain shadow (pure noise). 1.0 = hard wet/dry split.
    /// 0.6 gives a strong but naturalistic rain shadow.
    /// </summary>
    [Range(0.0f, 1.0f)]
    public float WindGradientStrength { get; init; } = 0.6f;

    /// <summary>
    /// Blend weight of FBm noise in the final moisture field.
    /// 0.0 = clean gradient only. 1.0 = pure noise (ignores wind).
    /// 0.5 balances global climate with local variation.
    /// </summary>
    [Range(0.0f, 1.0f)]
    public float MoistureNoiseWeight { get; init; } = 0.5f;

    // ── Volcano stamping ─────────────────────────────────────────────

    /// <summary>
    /// Number of volcanoes to stamp onto the island.
    /// 0 = no volcanoes. 1 is the geological focal point. Up to 3 is reasonable.
    /// Volcanoes are placed near the high-elevation interior of the island.
    /// </summary>
    [Range(0, 3)]
    public int VolcanoCount { get; init; } = 1;

    /// <summary>
    /// Volcanic personality for all stamped volcanoes.
    ///   Shield  - broad, low-slope dome (Hawaiian style). Gentle cone, no caldera.
    ///   Strato  - steep-sided cone with a small summit crater. Classic island volcano.
    ///   Caldera - collapsed summit creates a crater lake at the peak.
    /// </summary>
    public VolcanoType VolcanoType { get; init; } = VolcanoType.Strato;

    /// <summary>
    /// Radius of the volcano base in tiles. The cone fades to surrounding terrain at
    /// this distance. Typical: 40-80 on a 512x512 map.
    /// </summary>
    [Range(20, 150)]
    public int VolcanoBaseRadius { get; init; } = 60;

    /// <summary>
    /// How much extra elevation the cone adds at its peak above the 0..1 FBm range.
    /// 0.4 = moderate prominence. 0.8 = tall dominant peak.
    /// The biome table catches elevation > 1.0 via the volcanic_summit biome.
    /// </summary>
    [Range(0.1f, 1.0f)]
    public float VolcanoPeakHeight { get; init; } = 0.55f;

    /// <summary>
    /// For Caldera type: radius of the summit crater as a fraction of VolcanoBaseRadius.
    /// 0.12 = small crater pit. 0.25 = wide caldera lake.
    /// </summary>
    [Range(0.05f, 0.40f)]
    public float CalderaRadiusFraction { get; init; } = 0.15f;

    /// <summary>
    /// Number of lava flow channels carved radially from the volcanic peak.
    /// 0 = no lava flows. 3-6 is natural. Each channel creates a slight elevation
    /// depression that is post-processed into the lava tile.
    /// </summary>
    [Range(0, 8)]
    public int LavaFlowCount { get; init; } = 4;

    // ── Rivers ───────────────────────────────────────────────────────

    /// <summary>
    /// Number of rivers to carve from highland sources down to the coast.
    /// 0 = no rivers. 3-5 is natural for a mid-size island.
    /// Rivers walk downhill using steepest descent and are smoothed with
    /// cellular automata before being written as river tiles.
    /// </summary>
    [Range(0, 8)]
    public int RiverCount { get; init; } = 4;

    // ── Dungeon entrances ────────────────────────────────────────────

    /// <summary>Number of dungeon entrances to place on the overworld.</summary>
    [Range(1, 8)]
    public int EntranceCount { get; init; } = 1;

    /// <summary>
    /// Minimum Manhattan distance between any two dungeon entrances.
    /// Scale with map size: 80 is appropriate for a 512x512 map.
    /// </summary>
    [Range(20, 200)]
    public int MinEntranceSpacing { get; init; } = 80;

    /// <summary>
    /// Minimum Manhattan distance from player spawn to the nearest entrance.
    /// Prevents the player from immediately falling into a dungeon.
    /// </summary>
    [Range(20, 150)]
    public int MinEntranceFromSpawn { get; init; } = 60;

    // ── Named presets ────────────────────────────────────────────────

    /// <summary>Default balanced island — good for most playthroughs.</summary>
    public static IslandGenConfig Default => new();

    /// <summary>
    /// Small island for quick testing or tutorial maps.
    /// 128x128, minimal entrances, low noise detail.
    /// </summary>
    public static IslandGenConfig Small => new()
    {
        MapWidth = 128,
        MapHeight = 128,
        Frequency = 0.02f,
        Octaves = 4,
        IslandRadiusScale = 0.80f,
        EntranceCount = 1,
        MinEntranceSpacing = 20,
        MinEntranceFromSpawn = 15,
    };

    /// <summary>
    /// Large continental island with complex coastline and multiple dungeons.
    /// </summary>
    public static IslandGenConfig Large => new()
    {
        MapWidth = 512,
        MapHeight = 512,
        Frequency = 0.006f,
        Octaves = 7,
        CoastWarpStrength = 0.28f,
        IslandRadiusScale = 0.90f,
        EntranceCount = 4,
        MinEntranceSpacing = 100,
        MinEntranceFromSpawn = 80,
    };

    /// <summary>
    /// Tropical island: strong southwest wind, wet windward / dry leeward split.
    /// </summary>
    public static IslandGenConfig Tropical => new()
    {
        PrevailingWindAngleDeg = 225f,
        WindGradientStrength = 0.8f,
        MoistureNoiseWeight = 0.3f,
        IslandFalloffExp = 1.8f,
        CoastWarpStrength = 0.25f,
    };

    /// <summary>
    /// Harsh volcanic island: steep falloff, low moisture everywhere, jagged coasts.
    /// Single dominant stratovolcano with heavy lava flows.
    /// </summary>
    public static IslandGenConfig Volcanic => new()
    {
        IslandFalloffExp = 3.2f,
        CoastWarpStrength = 0.35f,
        WindGradientStrength = 0.9f,
        MoistureNoiseWeight = 0.2f,
        Octaves = 7,
        Gain = 0.65f,
        VolcanoCount = 1,
        VolcanoType = VolcanoType.Strato,
        VolcanoBaseRadius = 70,
        VolcanoPeakHeight = 0.75f,
        LavaFlowCount = 6,
    };

    /// <summary>
    /// Ancient caldera island: a collapsed volcanic cone dominates the interior.
    /// Wide crater lake at the summit, minimal lava flows (old, cold volcano).
    /// </summary>
    public static IslandGenConfig CalderaIsland => new()
    {
        IslandFalloffExp = 2.8f,
        CoastWarpStrength = 0.22f,
        WindGradientStrength = 0.65f,
        MoistureNoiseWeight = 0.45f,
        Octaves = 6,
        VolcanoCount = 1,
        VolcanoType = VolcanoType.Caldera,
        VolcanoBaseRadius = 80,
        VolcanoPeakHeight = 0.50f,
        CalderaRadiusFraction = 0.22f,
        LavaFlowCount = 1,
    };

    // ── Validation ───────────────────────────────────────────────────

    /// <summary>
    /// Returns true if all values are within their documented safe ranges.
    /// Logs a description of the first violation found via <paramref name="violation"/>.
    /// </summary>
    public bool IsValid(out string violation)
    {
        if (MapWidth < 64 || MapWidth > 1024) { violation = "MapWidth out of range (64-1024)"; return false; }
        if (MapHeight < 64 || MapHeight > 1024) { violation = "MapHeight out of range (64-1024)"; return false; }
        if (Frequency < 0.001f) { violation = "Frequency too low (min 0.001)"; return false; }
        if (Octaves < 1 || Octaves > 8) { violation = "Octaves out of range (1-8)"; return false; }
        if (IslandRadiusScale < 0.1f || IslandRadiusScale > 0.99f) { violation = "IslandRadiusScale out of range (0.1-0.99)"; return false; }
        if (IslandFalloffExp < 0.5f) { violation = "IslandFalloffExp too low (min 0.5)"; return false; }
        if (EntranceCount < 1) { violation = "EntranceCount must be >= 1"; return false; }
        if (MinEntranceSpacing < 1) { violation = "MinEntranceSpacing must be >= 1"; return false; }

        violation = string.Empty;
        return true;
    }

    /// <summary>
    /// Returns a stable display name summarising the key shape parameters.
    /// Useful for the preview UI header and saved preset labels.
    /// Example: "512x512  Radius 0.85  Warp 0.18  Wind 225deg"
    /// </summary>
    public string Summary()
    {
        string seedStr = Seed.HasValue ? $"Seed {Seed}" : "Random seed";
        return $"{MapWidth}x{MapHeight}  {seedStr}  " +
               $"Radius {IslandRadiusScale:F2}  Warp {CoastWarpStrength:F2}  " +
               $"Wind {PrevailingWindAngleDeg:F0}deg  Entrances {EntranceCount}  " +
               $"Volcanoes {VolcanoCount} ({VolcanoType})";
    }
}