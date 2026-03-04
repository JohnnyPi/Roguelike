// src/Game.Client/Game1.cs

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Game.Core;
using Game.Core.Entities;
using Game.Core.Map;
using Game.Core.Tiles;
using Game.Client.Rendering;
using Game.Client.Input;
using Game.ProcGen.Generators;
using Game.Core.Items;

#nullable enable

namespace Game.Client;

public class Game1 : Microsoft.Xna.Framework.Game
{
    private GraphicsDeviceManager _graphics;
    private SpriteBatch _spriteBatch = null!;

    // Our systems
    private TileRenderer _renderer = null!;
    private Camera _camera = null!;
    private InputHandler _input = null!;

    // Game state
    private GameState _state = null!;

    // Tile registry — later this comes from Game.Content YAML loader
    private Dictionary<string, TileDef> _tileRegistry = null!;

    // Item registry — hardcoded for first playable, YAML loader replaces this
    private List<ItemDef> _itemDefs = null!;

    // Current seeds for display/debugging
    private int _overworldSeed;
    private int _dungeonSeed;

    public Game1()
    {
        _graphics = new GraphicsDeviceManager(this);
        Content.RootDirectory = "Content";
        IsMouseVisible = true;

        // Set window size: 1280x800 gives us a 40x25 tile viewport at 32px
        _graphics.PreferredBackBufferWidth = 1280;
        _graphics.PreferredBackBufferHeight = 800;
    }

    protected override void Initialize()
    {
        Window.Title = "Roguelike — Phase 5 (Objects & Items)";
        Window.AllowUserResizing = true;
        Window.ClientSizeChanged += OnWindowResize;

        base.Initialize();
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);

        // Build tile registry (hardcoded for now — YAML loader replaces this)
        _tileRegistry = BuildTileRegistry();

        // Build item registry (hardcoded for now — YAML loader replaces this)
        _itemDefs = BuildItemDefs();

        // Create systems
        _camera = new Camera(
            _graphics.PreferredBackBufferWidth,
            _graphics.PreferredBackBufferHeight
        );
        _renderer = new TileRenderer(GraphicsDevice, _spriteBatch);
        _input = new InputHandler();

        // Subscribe to map transition events from the interaction system
        _input.OnMapTransition += HandleMapTransition;

        // Start on the overworld
        _state = CreateOverworldState();
    }

    protected override void Update(GameTime gameTime)
    {
        // Escape to quit
        if (Keyboard.GetState().IsKeyDown(Keys.Escape))
            Exit();

        // R to regenerate the overworld with a new seed (debug tool)
        if (_input.IsNewKeyPress(Keys.R) && _state.Mode == GameMode.Overworld)
        {
            _state = CreateOverworldState();
        }

        // Get player input
        var action = _input.GetAction();

        // Process action — returns true if a turn was consumed
        bool turnTaken = _input.ProcessAction(action, _state);

        if (turnTaken)
        {
            // Enemy AI turns will go here in Phase 6.
            _state.CleanupDead();
        }

        // Update camera to follow player
        if (_state.ActiveMap != null)
        {
            _camera.CenterOn(
                _state.Player.X,
                _state.Player.Y,
                _state.ActiveMap.Width,
                _state.ActiveMap.Height
            );
        }

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.Black);

        _spriteBatch.Begin(
            SpriteSortMode.Deferred,
            BlendState.AlphaBlend,
            SamplerState.PointClamp,  // crisp pixels, no blurring
            null, null, null
        );

        _renderer.Draw(_state, _camera);

        _spriteBatch.End();

        base.Draw(gameTime);
    }

    // ── Tile Registry (hardcoded, replaced by YAML loader later) ────

    private Dictionary<string, TileDef> BuildTileRegistry()
    {
        var registry = new Dictionary<string, TileDef>();

        var tiles = new[]
        {
            new TileDef
            {
                Id = "base:grass",
                Name = "Grass",
                Walkable = true,
                Color = "#3A7D2C"
            },
            new TileDef
            {
                Id = "base:wall",
                Name = "Wall",
                Walkable = false,
                Color = "#4A4A4A"
            },
            new TileDef
            {
                Id = "base:floor",
                Name = "Stone Floor",
                Walkable = true,
                Color = "#8B8B7A"
            },
            new TileDef
            {
                Id = "base:dirt",
                Name = "Dirt",
                Walkable = true,
                Color = "#7A6033"
            },
            new TileDef
            {
                Id = "base:water",
                Name = "Water",
                Walkable = false,
                Color = "#2255AA"
            },
            new TileDef
            {
                Id = "base:dungeon_entrance",
                Name = "Dungeon Entrance",
                Walkable = true,
                Color = "#AA3333"
            },
            new TileDef
            {
                Id = "base:dungeon_exit",
                Name = "Dungeon Exit",
                Walkable = true,
                Color = "#33AA33"
            }
        };

        foreach (var tile in tiles)
            registry[tile.Id] = tile;

        return registry;
    }

    // ── Item Registry (hardcoded, replaced by YAML loader later) ─────

    /// <summary>
    /// Build the item definitions that match content/BasePack/items/basic_items.yml.
    /// These will be loaded from YAML by Game.Content in a future phase.
    /// </summary>
    private List<ItemDef> BuildItemDefs()
    {
        return new List<ItemDef>
        {
            new ItemDef
            {
                Id = "base:gold_coin",
                Name = "Gold Coin",
                Tags = new List<string> { "currency", "common" },
                Stackable = true,
                MaxStack = 99,
                Color = "#FFD700"
            },
            new ItemDef
            {
                Id = "base:health_potion",
                Name = "Health Potion",
                Tags = new List<string> { "consumable", "healing" },
                Stackable = true,
                MaxStack = 10,
                EffectType = "heal",
                EffectAmount = 15,
                Color = "#FF4444"
            }
        };
    }

    // ── Map Transition Handler ────────────────────────────────────

    /// <summary>
    /// Called by InputHandler when the player presses E on a transition tile.
    /// Routes to the appropriate transition method.
    /// </summary>
    private void HandleMapTransition(InputHandler.TransitionRequest request)
    {
        switch (request)
        {
            case InputHandler.TransitionRequest.EnterDungeon:
                EnterDungeon();
                break;
            case InputHandler.TransitionRequest.ExitDungeon:
                ExitToOverworld();
                break;
        }
    }

    /// <summary>
    /// Transition from overworld into a freshly generated dungeon.
    /// Saves overworld state so we can restore it on exit.
    /// </summary>
    private void EnterDungeon()
    {
        // Save overworld state for return trip
        _state.OverworldMap = _state.ActiveMap;
        _state.OverworldPlayerPosition = (_state.Player.X, _state.Player.Y);

        // Generate a new dungeon
        _dungeonSeed = Environment.TickCount;

        var generator = new DungeonGenerator
        {
            MapWidth = 60,
            MapHeight = 40,
            MinRooms = 6,
            MaxRooms = 12,
            RoomMinSize = 5,
            RoomMaxSize = 10
        };

        var map = generator.Generate(_tileRegistry, _dungeonSeed, _itemDefs);

        // Swap the active map and reposition the player at the dungeon entrance
        _state.ActiveMap = map;
        _state.Mode = GameMode.Dungeon;
        _state.Player.SetPosition(generator.EntrancePosition.X, generator.EntrancePosition.Y);

        // Clear old entities and add freshly spawned items/chests
        _state.Entities.Clear();
        foreach (var entity in generator.SpawnedEntities)
            _state.Entities.Add(entity);

        _state.Log("—————————————————————————————");
        _state.Log($"You descend into the dungeon... (seed: {_dungeonSeed}, {generator.Rooms.Count} rooms)");
        _state.Log("Find the green exit tile to return to the overworld.");
        _state.Log("WASD to move. E to interact with chests and exits.");
    }

    /// <summary>
    /// Transition from dungeon back to the overworld.
    /// Restores the saved overworld map and player position.
    /// </summary>
    private void ExitToOverworld()
    {
        if (_state.OverworldMap == null || _state.OverworldPlayerPosition == null)
        {
            _state.Log("ERROR: No overworld to return to!");
            return;
        }

        // Restore the overworld map
        _state.ActiveMap = _state.OverworldMap;
        _state.Mode = GameMode.Overworld;

        // Restore player position on the overworld
        var pos = _state.OverworldPlayerPosition.Value;
        _state.Player.SetPosition(pos.X, pos.Y);

        // Clear dungeon entities
        _state.Entities.Clear();

        _state.Log("—————————————————————————————");
        _state.Log("You emerge from the dungeon, back on the overworld.");
        _state.Log("WASD to move. E on the red entrance to re-enter.");
    }

    // ── State Creation Methods ──────────────────────────────────────

    /// <summary>
    /// Generate an overworld and place the player on it.
    /// This is the game's starting state.
    /// </summary>
    private GameState CreateOverworldState()
    {
        var state = new GameState();
        state.Mode = GameMode.Overworld;

        _overworldSeed = Environment.TickCount;

        var generator = new OverworldGenerator
        {
            MapWidth = 120,
            MapHeight = 120,
            Frequency = 0.02f,
            Octaves = 4
        };

        var map = generator.Generate(_tileRegistry, _overworldSeed);
        state.ActiveMap = map;

        // Also store as the preserved overworld (for returning from dungeons later)
        state.OverworldMap = map;

        // Place the player at the spawn point
        var player = new Player();
        player.SetPosition(generator.SpawnPosition.X, generator.SpawnPosition.Y);
        state.Player = player;

        // Remember entrance position for later reference
        state.OverworldPlayerPosition = (generator.SpawnPosition.X, generator.SpawnPosition.Y);

        state.Log($"Overworld generated (seed: {_overworldSeed}).");
        state.Log("WASD to move. E to interact. R to regenerate.");
        state.Log("Find the red dungeon entrance tile and press E!");

        return state;
    }

    /// <summary>
    /// Generate a standalone dungeon state. Used for debug/testing only.
    /// Normal gameplay uses EnterDungeon() which preserves overworld state.
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
            RoomMaxSize = 10
        };

        var map = generator.Generate(_tileRegistry, _dungeonSeed, _itemDefs);
        state.ActiveMap = map;

        // Place the player at the dungeon entrance
        var player = new Player();
        player.SetPosition(generator.EntrancePosition.X, generator.EntrancePosition.Y);
        state.Player = player;

        // Add spawned items and chests
        foreach (var entity in generator.SpawnedEntities)
            state.Entities.Add(entity);

        state.Log($"Dungeon generated (seed: {_dungeonSeed}). {generator.Rooms.Count} rooms.");
        state.Log("WASD to move. E on green exit tile to leave.");

        return state;
    }

    private void OnWindowResize(object? sender, EventArgs e)
    {
        _camera.UpdateViewport(
            Window.ClientBounds.Width,
            Window.ClientBounds.Height
        );
    }
}