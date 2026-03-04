// src/Game.Core/Monsters/MonsterDef.cs
//
// Immutable definition of a monster type, loaded from YAML.
// This is the "def" — the template. Enemy is the runtime instance.

namespace Game.Core.Monsters;

/// <summary>
/// Immutable definition of a monster type, loaded from YAML.
/// The Enemy entity references this for its base stats and display info.
/// </summary>
public sealed class MonsterDef
{
    /// <summary>Unique content ID, e.g. "base:goblin"</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Display name for UI/log messages</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Tags for flexible queries, e.g. ["beast", "flying"]</summary>
    public List<string> Tags { get; init; } = new();

    /// <summary>Maximum hit points</summary>
    public int MaxHp { get; init; } = 1;

    /// <summary>Attack power (raw damage dealt per hit)</summary>
    public int Attack { get; init; } = 1;

    /// <summary>Defense (subtracted from incoming damage, minimum 1 damage)</summary>
    public int Defense { get; init; } = 0;

    /// <summary>
    /// Threat cost for dungeon budget system.
    /// Higher = fewer of this monster spawned per budget.
    /// </summary>
    public int ThreatCost { get; init; } = 1;

    /// <summary>
    /// AI behavior type. Determines how the enemy acts each turn.
    /// "chase" = move toward player if within sight range, wander otherwise.
    /// "wander" = random movement only (passive until attacked).
    /// "guard" = stays in place until player enters sight range.
    /// </summary>
    public string AiBehavior { get; init; } = "chase";

    /// <summary>
    /// How far (in tiles, Manhattan distance) this monster can detect the player.
    /// Used by chase/guard AI to decide when to aggro.
    /// </summary>
    public int SightRange { get; init; } = 8;

    /// <summary>
    /// Packed RGBA color for placeholder vector rendering.
    /// Format: "#RRGGBB"
    /// </summary>
    public string Color { get; init; } = "#FF0000";

    /// <summary>
    /// Glyph character for display (future use with font rendering).
    /// </summary>
    public string Glyph { get; init; } = "?";
}