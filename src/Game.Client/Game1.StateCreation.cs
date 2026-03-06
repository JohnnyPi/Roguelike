// src/Game.Client/Game1.StateCreation.cs

using Game.Client.Input;
using Game.Content;
using Game.Core;
using Game.Core.Biomes;
using Game.Core.Entities;
using Game.Core.Items;
using Game.Core.Lighting;
using Game.Core.Map;
using Game.Core.Tiles;
using Game.Core.World;
using Game.Core.WorldGen;
using Game.ProcGen.Generators;
using System;
using System.Collections.Generic;
using System.Linq;

#nullable enable

namespace Game.Client;

public partial class Game1
{
    // -- State Creation Methods -------------------------------------------

    /// <summary>
    /// Quick-regen overworld using sensible defaults (bound to R key in-game).
    /// Delegates to the config-based path so there's no logic duplication.
    /// </summary>
    private GameState CreateOverworldState()
        => CreateOverworldStateFromConfig(new IslandGenConfig
        {
            MapWidth = 512,
            MapHeight = 512,
            Frequency = 0.008f,
            Octaves = 6,
            EntranceCount = 3,
            MinEntranceSpacing = 80,
            MinEntranceFromSpawn = 60,
            CoastWarpStrength = 0.22f,
        });

    /// <summary>
    /// Generate a full overworld from a caller-supplied IslandGenConfig.
    /// Uses ChunkedWorldMap + ChunkManager for streaming tile generation.
    /// </summary>
    private GameState CreateOverworldStateFromConfig(IslandGenConfig cfg)
    {
        var state = new GameState();
        state.Mode = GameMode.Overworld;

        _overworldSeed = cfg.Seed ?? Environment.TickCount;

        var generator = OverworldGenerator.FromConfig(cfg with { Seed = _overworldSeed });

        var tileDict = new Dictionary<string, TileDef>(_content.Tiles);

        // -- Build noise context once -- reused by every GenerateChunk call -
        var noiseCtx = generator.BuildNoiseContext(_overworldSeed);

        // -- Collect cross-chunk feature overlays in a single lightweight pass --
        // GenerateFeatureSets runs the noise + volcano + river pipeline without
        // allocating or baking a full TileMap, eliminating the previous double-generation.
        IReadOnlyList<BiomeDef> biomeList = _content.Biomes.Count > 0
            ? _content.Biomes
            : Array.Empty<BiomeDef>();

        var features = generator.GenerateFeatureSets(
            _overworldSeed,
            biomeList.Count > 0 ? biomeList : null);

        var riverSet = features.RiverPositions;
        var entranceSet = features.EntrancePositions;
        var lavaSet = features.LavaPositions;
        var craterSet = features.CraterPositions;
        var spawnPos = features.SpawnPosition;
        var entranceList = features.EntranceList;

        // -- Build ChunkedWorldMap ----------------------------------------
        var chunkManager = new ChunkManager(
            cfg.MapWidth, cfg.MapHeight,
            residentRadius: 3);

        var chunkedMap = new ChunkedWorldMap(cfg.MapWidth, cfg.MapHeight,
                                             tileDict, chunkManager);

        // Store feature lists on the map for minimap / future use
        chunkedMap.SpawnPosition = spawnPos;
        foreach (var ep in entranceList)
            chunkedMap.EntrancePositions.Add(ep);

        // Wire the generation delegate -- called by ChunkManager on demand
        chunkManager.GenerateChunk = (cx, cy) =>
            generator.GenerateChunk(
                cx, cy, noiseCtx,
                tileDict, biomeList,
                riverPositions: riverSet,
                lavaPositions: lavaSet,
                craterPositions: craterSet,
                entrancePositions: entranceSet);

        // -- Initial chunk load around spawn ------------------------------
        chunkManager.UpdateFocus(spawnPos.X, spawnPos.Y);

        // -- Initialize lighting on the chunked map -----------------------
        state.BaseFovRadius = 12;
        chunkedMap.InitializeLighting(state.OverworldAmbient);

        state.ActiveMap = chunkedMap;
        state.OverworldMap = chunkedMap;

        // -- Player -------------------------------------------------------
        var player = new Player();
        player.SetPosition(spawnPos.X, spawnPos.Y);
        state.Player = player;
        state.OverworldPlayerPosition = (spawnPos.X, spawnPos.Y);

        // Register player in spatial index (player always blocks movement)
        state.RegisterEntity(player);

        // Initial FOV
        chunkedMap.Visibility?.Recompute(spawnPos.X, spawnPos.Y,
                                          state.EffectiveFovRadius);

        state.Log($"Overworld generated (seed: {_overworldSeed}, {cfg.MapWidth}x{cfg.MapHeight}).");
        state.Log(InputBindings.HintLine(_bindings,
            (GameAction.MoveNorth, "Move"),
            (GameAction.Interact, "Interact"),
            (GameAction.ToggleInventory, "Inventory"),
            (GameAction.RegenerateWorld, "Regen")
        ));
        state.Log("Find the red dungeon entrance tile and press E!");

        return state;
    }

    /// <summary>
    /// Generate a standalone dungeon state. Used for debug/testing only.
    /// </summary>
    private GameState CreateDungeonState()
    {
        var state = new GameState();
        state.Mode = GameMode.Dungeon;

        _dungeonSeed = Environment.TickCount;

        var generator = new DungeonGenerator
        {
            MapWidth = 60,
            MapHeight = 40,
            MinRooms = 6,
            MaxRooms = 12,
            RoomMinSize = 5,
            RoomMaxSize = 10,
            LightingConfig = DungeonLightingConfig.Cave
        };

        var tileDict = new Dictionary<string, TileDef>(_content.Tiles);
        var monsterList = _content.MonsterList.Count > 0 ? _content.MonsterList.ToList() : null;
        var map = generator.Generate(tileDict, _dungeonSeed, _content.ItemList.ToList(), monsterList);

        var lightingCfg = generator.LightingConfig;
        map.InitializeLighting(lightingCfg.AmbientLight);

        state.LightSources = new List<LightSource>(generator.LightSources);
        state.BaseFovRadius = lightingCfg.PlayerFovRadius;

        map.Lighting!.Recompute(state.LightSources);

        state.ActiveMap = map;

        var player = new Player();
        player.SetPosition(generator.EntrancePosition.X, generator.EntrancePosition.Y);
        state.Player = player;
        state.RegisterEntity(player);

        map.Visibility?.Recompute(
            generator.EntrancePosition.X,
            generator.EntrancePosition.Y,
            state.EffectiveFovRadius
        );

        foreach (var entity in generator.SpawnedEntities)
        {
            state.Entities.Add(entity);
            state.RegisterEntity(entity);
        }

        int enemyCount = generator.SpawnedEntities.OfType<Enemy>().Count();
        state.Log($"Dungeon generated (seed: {_dungeonSeed}). {generator.Rooms.Count} rooms, {enemyCount} enemies.");
        state.Log($"{_bindings.PrimaryKeyLabel(GameAction.MoveNorth)}{_bindings.PrimaryKeyLabel(GameAction.MoveSouth)}{_bindings.PrimaryKeyLabel(GameAction.MoveEast)}{_bindings.PrimaryKeyLabel(GameAction.MoveWest)} to move. Walk into enemies to attack. {_bindings.PrimaryKeyLabel(GameAction.Interact)} on green exit to leave.");

        return state;
    }

    /// <summary>
    /// Hardcoded fallback registry. Used only if YAML loading fails so the game
    /// doesn't crash. Should never be needed once content/ is properly deployed.
    /// </summary>
    private ContentRegistry BuildFallbackRegistry()
    {
        var registry = new ContentRegistry();

        var tiles = new[]
        {
            new TileDef { Id = "base:grass",            Name = "Grass",           Walkable = true,  Color = "#3A7D2C", BlocksSight = false, Height = 1, Tags = new() { "natural", "overworld" } },
            new TileDef { Id = "base:wall",             Name = "Wall",            Walkable = false, Color = "#4A4A4A", BlocksSight = true,  Height = 3, Tags = new() { "solid", "blocking" } },
            new TileDef { Id = "base:floor",            Name = "Stone Floor",     Walkable = true,  Color = "#8B8B7A", BlocksSight = false, Height = 1, Tags = new() { "dungeon", "constructed" } },
            new TileDef { Id = "base:dirt",             Name = "Dirt",            Walkable = true,  Color = "#7A6033", BlocksSight = false, Height = 2, Tags = new() { "natural", "overworld" } },
            new TileDef { Id = "base:water",            Name = "Water",           Walkable = false, Color = "#2255AA", BlocksSight = false, Height = 0, Tags = new() { "natural", "liquid" } },
            new TileDef { Id = "base:tree",             Name = "Tree",            Walkable = false, Color = "#1A5C10", BlocksSight = true,  Height = 3, Tags = new() { "natural", "overworld", "blocking", "vegetation" } },
            new TileDef { Id = "base:dungeon_entrance", Name = "Dungeon Entrance",Walkable = true,  Color = "#AA3333", BlocksSight = false, Height = 1, Tags = new() { "transition", "entrance" } },
            new TileDef { Id = "base:dungeon_exit",     Name = "Dungeon Exit",    Walkable = true,  Color = "#33AA33", BlocksSight = false, Height = 1, Tags = new() { "transition", "exit" } },
            new TileDef { Id = "base:shallow_water",    Name = "Shallow Water",   Walkable = false, Color = "#3A77CC", BlocksSight = false, Height = 0, Tags = new() { "natural", "liquid", "shallow", "coastal" } },
            new TileDef { Id = "base:beach",            Name = "Beach",           Walkable = true,  Color = "#C8B864", BlocksSight = false, Height = 1, Tags = new() { "natural", "overworld", "coastal" } },
        };
        foreach (var t in tiles) registry.RegisterTile(t);

        registry.RegisterItem(new ItemDef
        {
            Id = "base:gold_coin",
            Name = "Gold Coin",
            Tags = new List<string> { "currency", "common" },
            Stackable = true,
            MaxStack = 99,
            Color = "#FFD700"
        });
        registry.RegisterItem(new ItemDef
        {
            Id = "base:health_potion",
            Name = "Health Potion",
            Tags = new List<string> { "consumable", "healing" },
            Stackable = true,
            MaxStack = 10,
            EffectType = "heal",
            EffectAmount = 15,
            Color = "#FF4444"
        });

        registry.Freeze();
        return registry;
    }
}