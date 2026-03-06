// src/Game.ProcGen/Generators/DungeonGenerator.cs

using System;
using System.Collections.Generic;
using Game.Core.Entities;
using Game.Core.Items;
using Game.Core.Lighting;
using Game.Core.Map;
using Game.Core.Monsters;
using Game.Core.Tiles;

namespace Game.ProcGen.Generators;

/// <summary>
/// Rooms-and-corridors dungeon generator.
///
/// Algorithm:
///   1. Fill grid with walls
///   2. Place 6-12 non-overlapping rectangular rooms (5-10 tiles each side)
///   3. Connect rooms with L-shaped corridors
///   4. Flood-fill to verify all floor tiles are reachable
///   5. Mark entrance/exit tiles
///   6. Place items, chests, and enemies
///   7. Place torch light sources (driven by LightingConfig)
///
/// Partial class layout:
///   DungeonGenerator.cs           -- config, Room struct, Generate() orchestration, output properties
///   DungeonGenerator.Rooms.cs     -- PlaceRooms, CarveRoom, ConnectRooms, tunnel carvers
///   DungeonGenerator.Connectivity.cs -- EnsureConnectivity, FloodFill
///   DungeonGenerator.Population.cs   -- PlaceItemsAndChests, PlaceEnemies, PlaceTorches
///   DungeonGenerator.Blueprint.cs    -- future blueprint-driven generation entry point
/// </summary>
public partial class DungeonGenerator
{
    // -- Configuration ------------------------------------------------

    public int MapWidth { get; init; } = 60;
    public int MapHeight { get; init; } = 40;
    public int MinRooms { get; init; } = 6;
    public int MaxRooms { get; init; } = 12;
    public int RoomMinSize { get; init; } = 5;
    public int RoomMaxSize { get; init; } = 10;
    public int MaxPlacementAttempts { get; init; } = 200;

    /// <summary>
    /// Lighting configuration for this dungeon instance.
    /// Controls ambient darkness, torch radius, torches-per-room density,
    /// and the player FOV radius while inside.
    /// Defaults to the cave preset; set before calling Generate() to override.
    /// </summary>
    public DungeonLightingConfig LightingConfig { get; set; } = DungeonLightingConfig.Cave;

    // -- Tile IDs -----------------------------------------------------

    internal const string WallTile = "base:wall";
    internal const string FloorTile = "base:floor";
    internal const string EntranceTile = "base:dungeon_entrance";
    internal const string ExitTile = "base:dungeon_exit";

    // -- Output data --------------------------------------------------

    /// <summary>The rooms that were successfully placed.</summary>
    public List<Room> Rooms { get; private set; } = new();

    /// <summary>Grid position of the dungeon entrance (first room center).</summary>
    public (int X, int Y) EntrancePosition { get; private set; }

    /// <summary>Grid position of the dungeon exit (last room center).</summary>
    public (int X, int Y) ExitPosition { get; private set; }

    /// <summary>
    /// Entities spawned during generation (WorldItems, Chests, Enemies).
    /// Game1 should add these to GameState.Entities after generating.
    /// </summary>
    public List<Entity> SpawnedEntities { get; private set; } = new();

    /// <summary>
    /// Torch light sources placed during generation.
    /// Game1 should copy these into GameState.LightSources after generating.
    /// </summary>
    public List<LightSource> LightSources { get; private set; } = new();

    // -- Room helper struct -------------------------------------------

    public readonly struct Room
    {
        public int X { get; }
        public int Y { get; }
        public int Width { get; }
        public int Height { get; }

        public (int X, int Y) Center => (X + Width / 2, Y + Height / 2);

        public Room(int x, int y, int width, int height)
        {
            X = x; Y = y; Width = width; Height = height;
        }

        public bool Overlaps(Room other, int padding = 1)
        {
            return X - padding < other.X + other.Width
                && X + Width + padding > other.X
                && Y - padding < other.Y + other.Height
                && Y + Height + padding > other.Y;
        }
    }

    // -- Main generation method ---------------------------------------

    /// <summary>
    /// Generate a complete dungeon TileMap using the rooms-and-corridors algorithm.
    /// For blueprint-driven generation see DungeonGenerator.Blueprint.cs.
    /// </summary>
    /// <param name="tileRegistry">Tile definitions (needed by TileMap constructor).</param>
    /// <param name="seed">Random seed for reproducible generation.</param>
    /// <param name="itemDefs">Available item definitions for loot placement.</param>
    /// <param name="monsterDefs">Available monster definitions for enemy spawning.</param>
    public TileMap Generate(Dictionary<string, TileDef> tileRegistry, int? seed = null,
        List<ItemDef>? itemDefs = null, List<MonsterDef>? monsterDefs = null)
    {
        var rng = seed.HasValue ? new Random(seed.Value) : new Random();
        var map = new TileMap(MapWidth, MapHeight, tileRegistry);

        // Step 1: Fill entire grid with walls
        map.Fill(WallTile);

        // Step 2: Place rooms
        Rooms = PlaceRooms(rng);

        // Step 3: Carve rooms into the map
        foreach (var room in Rooms)
            CarveRoom(map, room);

        // Step 4: Connect rooms with L-shaped corridors
        ConnectRooms(map, rng);

        // Step 5: Verify connectivity
        EnsureConnectivity(map, rng);

        // Step 6: Mark the entrance in the first room's center
        var entrance = Rooms[0].Center;
        EntrancePosition = entrance;
        map.SetTile(entrance.X, entrance.Y, EntranceTile);

        // Step 7: Mark the exit in the last room's center
        var exitRoom = Rooms[Rooms.Count - 1];
        var exit = exitRoom.Center;
        if (Rooms.Count == 1)
            exit = (exitRoom.X + 1, exitRoom.Y + 1);
        ExitPosition = exit;
        map.SetTile(exit.X, exit.Y, ExitTile);

        // Step 8: Place items, chests, and enemies
        SpawnedEntities = new List<Entity>();
        if (itemDefs != null && itemDefs.Count > 0)
            PlaceItemsAndChests(map, rng, itemDefs);
        if (monsterDefs != null && monsterDefs.Count > 0)
            PlaceEnemies(map, rng, monsterDefs);

        // Step 9: Place torch light sources
        LightSources = new List<LightSource>();
        PlaceTorches(rng);

        return map;
    }

    // -- Backwards-compatible overload (no monsters) ------------------

    /// <summary>Legacy overload without monster defs. Calls the full version with null monsters.</summary>
    public TileMap Generate(Dictionary<string, TileDef> tileRegistry, int? seed,
        List<ItemDef> itemDefs)
    {
        return Generate(tileRegistry, seed, itemDefs, null);
    }
}