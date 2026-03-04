// src/Game.Client/Game1.cs

using Game.Client.Input;
using Game.Client.Rendering;
using Game.Client.UI;
using Game.Content;
using Game.Core;
using Game.Core.Entities;
using Game.Core.Items;
using Game.Core.Tiles;
using Game.Core.Map;
using Game.ProcGen.Generators;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Myra;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

#nullable enable

namespace Game.Client;

public class Game1 : Microsoft.Xna.Framework.Game
{
    private GraphicsDeviceManager _graphics;
    private SpriteBatch _spriteBatch = null!;

    // Our systems
    private TileRenderer _renderer = null!;
    private Camera _camera = null!;
    private InputBindings _bindings = null!;
    private InputHandler _input = null!;
    private HudManager _hud = null!;
    private MouseInputHandler _mouse = null!;
    private PathController _path = null!;

    // Game state
    private GameState _state = null!;

    // Content registry — loaded from YAML by Game.Content
    private ContentRegistry _content = null!;

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
        Window.Title = "Roguelike — Phase 8 (Combat)";
        Window.AllowUserResizing = true;
        Window.ClientSizeChanged += OnWindowResize;

        base.Initialize();
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);

        // ── Initialize Myra UI framework ─────────────────────────────
        MyraEnvironment.Game = this;

        // ── Load all content from YAML ──────────────────────────────
        _content = LoadContentPacks();

        // ── Load key bindings (controls.yml → fallback to defaults) ──
        _bindings = LoadBindings();

        // Create systems
        _camera = new Camera(
            _graphics.PreferredBackBufferWidth,
            _graphics.PreferredBackBufferHeight
        );
        _renderer = new TileRenderer(GraphicsDevice, _spriteBatch);
        _input = new InputHandler(_bindings);
        _mouse = new MouseInputHandler();
        _path = new PathController();   // SightRadius defaults to 8
        _hud = new HudManager(_bindings);

        // Subscribe to map transition events from the interaction system
        _input.OnMapTransition += HandleMapTransition;

        // Start on the overworld
        _state = CreateOverworldState();
    }

    /// <summary>
    /// Discovers and loads key bindings from controls.yml in BasePack.
    /// Searches the same candidate paths as LoadContentPacks().
    /// Falls back to compiled defaults if the file is missing or unreadable.
    /// </summary>
    private InputBindings LoadBindings()
    {
        var exeDir = AppDomain.CurrentDomain.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(exeDir, "content", "BasePack", "config", "controls.yml"),
            Path.Combine(exeDir, "..", "..", "..", "content", "BasePack", "config", "controls.yml"),
            Path.Combine(exeDir, "..", "..", "..", "..", "..", "content", "BasePack", "config", "controls.yml"),
            Path.Combine(Directory.GetCurrentDirectory(), "content", "BasePack", "config", "controls.yml"),
        };

        foreach (var candidate in candidates)
        {
            var resolved = Path.GetFullPath(candidate);
            if (File.Exists(resolved))
            {
                System.Diagnostics.Debug.WriteLine($"[InputBindings] Loaded from: {resolved}");
                return InputBindings.Load(resolved);
            }
        }

        System.Diagnostics.Debug.WriteLine("[InputBindings] controls.yml not found — using defaults.");
        return InputBindings.Defaults();
    }

    /// <summary>
    /// Discover and load content packs from the content/ directory.
    /// Resolves the content root relative to the executable.
    /// Falls back to a hardcoded registry if loading fails (graceful degradation).
    /// </summary>
    private ContentRegistry LoadContentPacks()
    {
        // Look for the content/ directory relative to the executable
        var exeDir = AppDomain.CurrentDomain.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(exeDir, "content"),
            Path.Combine(exeDir, "..", "..", "..", "content"),            // running from bin/Debug/net8.0
            Path.Combine(exeDir, "..", "..", "..", "..", "..", "content"), // deeper nesting
            Path.Combine(Directory.GetCurrentDirectory(), "content"),
        };

        string? contentRoot = null;
        foreach (var candidate in candidates)
        {
            var resolved = Path.GetFullPath(candidate);
            if (Directory.Exists(resolved) &&
                Directory.GetDirectories(resolved).Any(d => File.Exists(Path.Combine(d, "pack.yml"))))
            {
                contentRoot = resolved;
                break;
            }
        }

        if (contentRoot == null)
        {
            System.Diagnostics.Debug.WriteLine("WARNING: No content/ directory found. Using hardcoded fallback.");
            return BuildFallbackRegistry();
        }

        var loader = new ContentLoader();
        var registry = loader.LoadAll(contentRoot);

        // Print load log
        foreach (var msg in loader.Log)
            System.Diagnostics.Debug.WriteLine($"[Content] {msg}");

        // Print errors
        foreach (var err in loader.Errors)
            System.Diagnostics.Debug.WriteLine($"[Content ERROR] {err}");

        // If loading failed critically, fall back
        if (loader.Errors.Count > 0 || registry.Tiles.Count == 0)
        {
            System.Diagnostics.Debug.WriteLine("WARNING: Content loading had errors. Using hardcoded fallback.");
            return BuildFallbackRegistry();
        }

        return registry;
    }

    /// <summary>
    /// Hardcoded fallback registry — identical to the old BuildTileRegistry() +
    /// BuildItemDefs() methods. Used only if YAML loading fails so the game
    /// doesn't crash. Should never be needed once content/ is properly deployed.
    /// </summary>
    private ContentRegistry BuildFallbackRegistry()
    {
        var registry = new ContentRegistry();

        // Tiles (same as old BuildTileRegistry)
        var tiles = new[]
        {
            new TileDef { Id = "base:grass", Name = "Grass", Walkable = true, Color = "#3A7D2C" },
            new TileDef { Id = "base:wall", Name = "Wall", Walkable = false, Color = "#4A4A4A" },
            new TileDef { Id = "base:floor", Name = "Stone Floor", Walkable = true, Color = "#8B8B7A" },
            new TileDef { Id = "base:dirt", Name = "Dirt", Walkable = true, Color = "#7A6033" },
            new TileDef { Id = "base:water", Name = "Water", Walkable = false, Color = "#2255AA" },
            new TileDef { Id = "base:dungeon_entrance", Name = "Dungeon Entrance", Walkable = true, Color = "#AA3333" },
            new TileDef { Id = "base:dungeon_exit", Name = "Dungeon Exit", Walkable = true, Color = "#33AA33" },
        };
        foreach (var t in tiles) registry.RegisterTile(t);

        // Items (same as old BuildItemDefs)
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

    protected override void Update(GameTime gameTime)
    {
        // Advance keyboard + mouse snapshot first
        var keyAction = _input.GetAction();
        _mouse.Poll(_camera, _state.ActiveMap?.Width ?? 0, _state.ActiveMap?.Height ?? 0);

        // ── Meta actions (no turn cost) ───────────────────────────────
        if (_input.IsNewPress(GameAction.Quit))
            Exit();

        if (_input.IsNewPress(GameAction.RegenerateWorld) && _state.Mode == GameMode.Overworld)
        {
            _path.Cancel();
            _state = CreateOverworldState();
        }

        if (_input.IsNewPress(GameAction.ToggleInventory))
            _hud.ToggleInventory();

        // ── Keyboard movement cancels any active path ─────────────────
        if (keyAction != InputHandler.Action.None)
            _path.CancelOnKeyboardInput();

        // ── Mouse → path state machine ────────────────────────────────
        _path.HandleMouse(_mouse, _state);

        // ── Determine whose turn it is to act ─────────────────────────
        bool turnTaken = false;

        if (_path.State == PathController.PathState.Moving)
        {
            // Path movement owns the turn — one step per frame-tick
            turnTaken = _path.Tick(_state);
        }
        else
        {
            // Normal keyboard action
            turnTaken = _input.ProcessAction(keyAction, _state);
        }

        // ── Enemy AI (runs after any turn-consuming action) ───────────
        if (turnTaken && !_state.IsGameOver)
        {
            var enemies = _state.Entities
                .OfType<Enemy>()
                .Where(e => e.IsAlive)
                .ToList();

            foreach (var enemy in enemies)
                enemy.TakeTurn(_state);

            _state.CleanupDead();
        }

        // ── Camera ────────────────────────────────────────────────────
        if (_state.ActiveMap != null)
        {
            _camera.CenterOn(
                _state.Player.X,
                _state.Player.Y,
                _state.ActiveMap.Width,
                _state.ActiveMap.Height
            );
        }

        _hud.Update(gameTime, _state);
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.Black);

        _spriteBatch.Begin(
            SpriteSortMode.Deferred,
            BlendState.AlphaBlend,
            SamplerState.PointClamp,
            null, null, null
        );

        // Pass path data — renderer draws overlay between tiles and entities
        var previewPath = (_path.State == PathController.PathState.Preview ||
                           _path.State == PathController.PathState.Moving)
                          ? _path.PreviewPath
                          : null;

        _renderer.Draw(_state, _camera, previewPath, _path.Destination);

        _spriteBatch.End();

        _hud.Render();
        base.Draw(gameTime);
    }

    // ── Map Transition Handler ────────────────────────────────────

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

        // Generate a new dungeon — tile registry comes from content
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

        // Pass the content registry's tile dictionary, item list, and monster list
        var tileDict = new Dictionary<string, TileDef>(_content.Tiles);
        var monsterList = _content.MonsterList.Count > 0 ? _content.MonsterList.ToList() : null;
        var map = generator.Generate(tileDict, _dungeonSeed, _content.ItemList.ToList(), monsterList);

        // Swap the active map and reposition the player at the dungeon entrance
        _state.ActiveMap = map;
        _state.Mode = GameMode.Dungeon;
        _state.Player.SetPosition(generator.EntrancePosition.X, generator.EntrancePosition.Y);

        // Clear old entities and add freshly spawned items/chests/enemies
        _state.Entities.Clear();
        foreach (var entity in generator.SpawnedEntities)
            _state.Entities.Add(entity);

        // Count enemies for the log message
        int enemyCount = generator.SpawnedEntities.OfType<Enemy>().Count();

        _state.Log("—————————————————————————————");
        _state.Log($"You descend into the dungeon... (seed: {_dungeonSeed}, {generator.Rooms.Count} rooms, {enemyCount} enemies)");
        _state.Log("Find the green exit tile to return to the overworld.");
        _state.Log($"{_bindings.PrimaryKeyLabel(GameAction.MoveNorth)}{_bindings.PrimaryKeyLabel(GameAction.MoveSouth)}{_bindings.PrimaryKeyLabel(GameAction.MoveEast)}{_bindings.PrimaryKeyLabel(GameAction.MoveWest)} to move. Walk into enemies to attack. {_bindings.PrimaryKeyLabel(GameAction.Interact)} to interact.");
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
        _state.Log($"{_bindings.PrimaryKeyLabel(GameAction.MoveNorth)}{_bindings.PrimaryKeyLabel(GameAction.MoveSouth)}{_bindings.PrimaryKeyLabel(GameAction.MoveEast)}{_bindings.PrimaryKeyLabel(GameAction.MoveWest)} to move. {_bindings.PrimaryKeyLabel(GameAction.Interact)} on the red entrance to re-enter.");
    }

    // ── State Creation Methods ──────────────────────────────────────

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

        // Pass biome defs so the generator reads thresholds from data
        var tileDict = new Dictionary<string, TileDef>(_content.Tiles);
        TileMap map;
        if (_content.Biomes.Count > 0)
        {
            map = generator.Generate(tileDict, _overworldSeed, _content.Biomes);
        }
        else
        {
            map = generator.Generate(tileDict, _overworldSeed);
        }

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
            RoomMaxSize = 10
        };

        var tileDict = new Dictionary<string, TileDef>(_content.Tiles);
        var monsterList = _content.MonsterList.Count > 0 ? _content.MonsterList.ToList() : null;
        var map = generator.Generate(tileDict, _dungeonSeed, _content.ItemList.ToList(), monsterList);
        state.ActiveMap = map;

        var player = new Player();
        player.SetPosition(generator.EntrancePosition.X, generator.EntrancePosition.Y);
        state.Player = player;

        foreach (var entity in generator.SpawnedEntities)
            state.Entities.Add(entity);

        int enemyCount = generator.SpawnedEntities.OfType<Enemy>().Count();
        state.Log($"Dungeon generated (seed: {_dungeonSeed}). {generator.Rooms.Count} rooms, {enemyCount} enemies.");
        state.Log($"{_bindings.PrimaryKeyLabel(GameAction.MoveNorth)}{_bindings.PrimaryKeyLabel(GameAction.MoveSouth)}{_bindings.PrimaryKeyLabel(GameAction.MoveEast)}{_bindings.PrimaryKeyLabel(GameAction.MoveWest)} to move. Walk into enemies to attack. {_bindings.PrimaryKeyLabel(GameAction.Interact)} on green exit to leave.");

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