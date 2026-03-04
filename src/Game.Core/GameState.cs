// src/Game.Core/GameState.cs

using Game.Core.Entities;
using Game.Core.Map;

namespace Game.Core;

/// <summary>
/// Which mode the game is currently in.
/// Drives which map is rendered and how input is interpreted.
/// </summary>
public enum GameMode
{
    Overworld,
    Dungeon
}

/// <summary>
/// Central game state container. Holds the current mode, active map,
/// player reference, and entity lists.
/// 
/// This is the "single source of truth" that systems read from and write to.
/// Not a singleton — created once in Game1 and passed to systems that need it.
/// </summary>
public class GameState
{
    /// <summary>Current game mode.</summary>
    public GameMode Mode { get; set; } = GameMode.Overworld;

    /// <summary>The map currently being played on.</summary>
    public TileMap? ActiveMap { get; set; }

    /// <summary>Player entity — always exists once the game starts.</summary>
    public Player Player { get; set; } = null!;

    /// <summary>All non-player entities on the current map.</summary>
    public List<Entity> Entities { get; } = new();

    /// <summary>
    /// Preserved overworld map so we don't regenerate it when exiting a dungeon.
    /// </summary>
    public TileMap? OverworldMap { get; set; }

    /// <summary>
    /// Player's position on the overworld, saved when entering a dungeon
    /// so we can restore it on exit.
    /// </summary>
    public (int X, int Y)? OverworldPlayerPosition { get; set; }

    /// <summary>
    /// Message log for combat, pickups, interactions.
    /// The UI reads from this to display the combat log.
    /// </summary>
    public List<string> MessageLog { get; } = new();

    /// <summary>Is the player dead?</summary>
    public bool IsGameOver => Player?.IsDead ?? false;

    /// <summary>Add a message to the log (most recent at the end).</summary>
    public void Log(string message)
    {
        MessageLog.Add(message);

        // Keep the log from growing forever — 200 messages is plenty
        if (MessageLog.Count > 200)
            MessageLog.RemoveAt(0);
    }

    /// <summary>
    /// Get the entity (if any) blocking a specific tile.
    /// Used by movement to detect bump-attacks and blocked paths.
    /// </summary>
    public Entity? GetBlockingEntityAt(int x, int y)
    {
        // Check player first
        if (Player.X == x && Player.Y == y && Player.BlocksMovement)
            return Player;

        // Then other entities
        foreach (var entity in Entities)
        {
            if (entity.IsAlive && entity.BlocksMovement && entity.X == x && entity.Y == y)
                return entity;
        }

        return null;
    }

    /// <summary>Remove dead entities from the list.</summary>
    public void CleanupDead()
    {
        Entities.RemoveAll(e => !e.IsAlive);
    }
}