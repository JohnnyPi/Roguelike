// src/Game.Core/GameState.cs

using System;
using System.Collections.Generic;
using Game.Core.Entities;
using Game.Core.Lighting;
using Game.Core.Map;
using Game.Core.World;
using Microsoft.Xna.Framework;

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
/// player reference, entity lists, and the lighting/time simulation.
///
/// This is the "single source of truth" that systems read from and write to.
/// Not a singleton -- created once in Game1 and passed to systems that need it.
/// </summary>
public class GameState
{
    // -- Core ------------------------------------------------------------

    /// <summary>Current game mode.</summary>
    public GameMode Mode { get; set; } = GameMode.Overworld;

    /// <summary>The map currently being played on.</summary>
    public IWorldMap? ActiveMap { get; set; }

    /// <summary>Player entity -- always exists once the game starts.</summary>
    public Player Player { get; set; } = null!;

    /// <summary>All non-player entities on the current map.</summary>
    public List<Entity> Entities { get; } = new();

    /// <summary>
    /// Preserved overworld map so we don't regenerate it when exiting a dungeon.
    /// </summary>
    public IWorldMap? OverworldMap { get; set; }

    /// <summary>
    /// Player's position on the overworld, saved when entering a dungeon
    /// so we can restore it on exit.
    /// </summary>
    public (int X, int Y)? OverworldPlayerPosition { get; set; }

    // -- Lighting & FOW --------------------------------------------------

    /// <summary>
    /// Player FOV radius in tiles.
    /// Base value before weather/status modifiers.
    /// Overworld: 12 (wide open vistas).
    /// Dungeon: 8 (claustrophobic corridors).
    /// </summary>
    public int BaseFovRadius { get; set; } = 12;

    /// <summary>
    /// Effective FOV radius after applying weather, status effects, etc.
    /// Use this for VisibilityMap.Recompute() calls.
    /// </summary>
    public int EffectiveFovRadius
    {
        get
        {
            int radius = BaseFovRadius;
            // Weather penalty (only on overworld)
            if (Mode == GameMode.Overworld)
                radius -= Weather.VisibilityPenalty;
            return Math.Max(2, radius); // never less than 2
        }
    }

    /// <summary>
    /// Active light sources for the current map.
    /// On the overworld this is always empty (ambient light handles everything).
    /// In dungeons this is populated by DungeonGenerator (torches, braziers).
    /// </summary>
    public List<LightSource> LightSources { get; set; } = new();

    // -- World simulation ------------------------------------------------

    /// <summary>
    /// Turn-based world clock. Drives day/night ambient color.
    /// Ticked once per player turn while in Overworld mode.
    /// </summary>
    public WorldClock Clock { get; } = new WorldClock();

    /// <summary>
    /// Weather simulation. Ticked together with Clock on overworld turns.
    /// Provides LightMultiplier and OverlayTint to the renderer.
    /// </summary>
    public WeatherSystem Weather { get; } = new WeatherSystem();

    // -- Computed overworld ambient ---------------------------------------

    /// <summary>
    /// The ambient light color to use this frame on the overworld.
    /// = WorldClock color x WeatherSystem multiplier (channel-by-channel).
    /// </summary>
    public Color OverworldAmbient
    {
        get
        {
            var clock = Clock.AmbientLight;
            var weather = Weather.LightMultiplier;
            return new Color(
                (int)(clock.R * weather.R / 255),
                (int)(clock.G * weather.G / 255),
                (int)(clock.B * weather.B / 255),
                255
            );
        }
    }

    // -- Message log -----------------------------------------------------

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

        // Keep the log from growing forever -- 200 messages is plenty
        if (MessageLog.Count > 200)
            MessageLog.RemoveAt(0);
    }

    // -- Entity queries --------------------------------------------------

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

    public void TryPickupItems()
    {
        for (int i = Entities.Count - 1; i >= 0; i--)
        {
            if (Entities[i] is WorldItem item
                && item.IsAlive
                && item.X == Player.X
                && item.Y == Player.Y)
            {
                Player.Inventory.Add(item.ItemDef, item.Count);
                item.IsAlive = false;
                string display = item.Count > 1
                    ? $"{item.ItemDef.Name} x{item.Count}"
                    : item.ItemDef.Name;
                Log($"Picked up {display}.");
            }
        }
    }
}