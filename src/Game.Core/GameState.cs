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
///
/// Spatial index:
///   _spatialIndex maps (x, y) -> Entity for all blocking entities.
///   Updated by RegisterEntity / UnregisterEntity, and kept in sync with
///   every Move() call via MoveEntity(). GetBlockingEntityAt() is O(1).
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

    // -- Spatial index ---------------------------------------------------

    /// <summary>
    /// O(1) blocking-entity lookup by tile position.
    /// Contains all blocking entities including the player.
    /// Kept in sync via RegisterEntity, UnregisterEntity, and MoveEntity.
    /// </summary>
    private readonly Dictionary<(int, int), Entity> _spatialIndex = new();

    /// <summary>
    /// Register a blocking entity into the spatial index.
    /// Call this after adding an entity to Entities (and for Player after setting position).
    /// Non-blocking entities are silently ignored.
    /// </summary>
    public void RegisterEntity(Entity entity)
    {
        if (!entity.BlocksMovement) return;
        _spatialIndex[(entity.X, entity.Y)] = entity;
    }

    /// <summary>
    /// Remove an entity from the spatial index (e.g. on death or map transition).
    /// Safe to call even if the entity was never registered.
    /// </summary>
    public void UnregisterEntity(Entity entity)
    {
        var key = (entity.X, entity.Y);
        if (_spatialIndex.TryGetValue(key, out var current) && current == entity)
            _spatialIndex.Remove(key);
    }

    /// <summary>
    /// Wipe the entire spatial index. Call this when swapping maps (dungeon
    /// enter/exit) before re-registering entities for the new map.
    /// </summary>
    public void ClearSpatialIndex() => _spatialIndex.Clear();

    /// <summary>
    /// Move a blocking entity and keep the spatial index consistent.
    /// Use this instead of calling entity.Move() directly whenever the
    /// entity is registered in the spatial index.
    /// Non-blocking entities fall through to a plain Move() call.
    /// </summary>
    public void MoveEntity(Entity entity, int dx, int dy)
    {
        if (!entity.BlocksMovement)
        {
            entity.Move(dx, dy);
            return;
        }

        var oldKey = (entity.X, entity.Y);
        entity.Move(dx, dy);
        var newKey = (entity.X, entity.Y);

        // Remove old position only if this entity still owns it
        if (_spatialIndex.TryGetValue(oldKey, out var current) && current == entity)
            _spatialIndex.Remove(oldKey);

        _spatialIndex[newKey] = entity;
    }

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

    private const int MaxLogSize = 200;
    // Allow slight overgrowth before trimming to avoid RemoveAt(0) on every message.
    // Trimming removes the oldest (MaxLogSize / 4) entries in one RemoveRange call.
    private const int LogTrimThreshold = MaxLogSize + MaxLogSize / 4;

    private readonly List<string> _messageLog = new();

    /// <summary>Read-only indexed view of the message log for UI rendering.</summary>
    public IReadOnlyList<string> MessageLog => _messageLog;

    /// <summary>Is the player dead?</summary>
    public bool IsGameOver => Player?.IsDead ?? false;

    /// <summary>Add a message to the log (most recent at the end).</summary>
    public void Log(string message)
    {
        _messageLog.Add(message);
        // Trim a quarter of the buffer in one shot rather than shifting every entry
        // on every message (the old RemoveAt(0) was O(n) per call).
        if (_messageLog.Count >= LogTrimThreshold)
            _messageLog.RemoveRange(0, MaxLogSize / 4);
    }

    // -- Entity queries --------------------------------------------------

    /// <summary>
    /// Get the blocking entity (if any) at a specific tile. O(1) via spatial index.
    /// Used by movement to detect bump-attacks and blocked paths.
    /// </summary>
    public Entity? GetBlockingEntityAt(int x, int y)
    {
        _spatialIndex.TryGetValue((x, y), out var entity);
        // Only return it if it's still alive; dead entities are stale index entries
        return (entity != null && entity.IsAlive) ? entity : null;
    }

    /// <summary>Remove dead entities from the list and the spatial index.</summary>
    public void CleanupDead()
    {
        for (int i = Entities.Count - 1; i >= 0; i--)
        {
            var e = Entities[i];
            if (!e.IsAlive)
            {
                UnregisterEntity(e);
                Entities.RemoveAt(i);
            }
        }
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
                // WorldItems don't block movement so no index entry to remove
                string display = item.Count > 1
                    ? $"{item.ItemDef.Name} x{item.Count}"
                    : item.ItemDef.Name;
                Log($"Picked up {display}.");
            }
        }
    }
}