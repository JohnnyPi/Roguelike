// src/Game.Client/Game1.cs

using Game.Client.Input;
using Game.Client.Rendering;
using Game.Client.UI;
using Game.Content;
using Game.Core;
using Game.Core.Entities;
using Game.Core.Items;
using Game.Core.Lighting;
using Game.Core.Map;
using Game.Core.Tiles;
using Game.Core.World;
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

    // ── Systems ──────────────────────────────────────────────────────
    private TileRenderer _renderer = null!;
    private Camera _camera = null!;
    private InputBindings _bindings = null!;
    private InputHandler _input = null!;
    private HudManager _hud = null!;
    private MouseInputHandler _mouse = null!;
    private PathController _path = null!;
    private TorchFlicker _flicker = null!;

    // ── Game state ───────────────────────────────────────────────────
    private GameState _state = null!;

    // ── Content registry — loaded from YAML by Game.Content ─────────
    private ContentRegistry _content = null!;

    // ── Current seeds for display/debugging ─────────────────────────
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
        Window.Title = "Roguelike — Phase 9 (Lighting & FOW)";
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

        // ── Create systems ───────────────────────────────────────────
        _camera = new Camera(
            _graphics.PreferredBackBufferWidth,
            _graphics.PreferredBackBufferHeight
        );
        _renderer = new TileRenderer(GraphicsDevice, _spriteBatch);
        _input = new InputHandler(_bindings);
        _mouse = new MouseInputHandler();
        _path = new PathController();
        _hud = new HudManager(_bindings);
        _flicker = new TorchFlicker();

        // ── Subscribe to map transition events ───────────────────────
        _input.OnMapTransition += HandleMapTransition;

        // ── Start on the overworld ───────────────────────────────────
        _state = CreateOverworldState();

        // ── Subscribe to clock/weather events ────────────────────────
        _state.Clock.PeriodChanged += OnPeriodChanged;
        _state.Weather.WeatherChanged += OnWeatherChanged;
    }

    // ── Event handlers ────────────────────────────────────────────────

    private void OnPeriodChanged(TimePeriod from, TimePeriod to)
    {
        _state.Log($"The {_state.Clock.PeriodName} begins. ({_state.Clock.TimeString})");
    }

    private void OnWeatherChanged(WeatherState from, WeatherState to)
    {
        _state.Log($"The weather turns {_state.Weather.Name}.");
    }

    // ── Key binding loader ────────────────────────────────────────────

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

    // ── Content loader ────────────────────────────────────────────────

    /// <summary>
    /// Discover and load content packs from the content/ directory.
    /// Resolves the content root relative to the executable.
    /// Falls back to a hardcoded registry if loading fails (graceful degradation).
    /// </summary>
    private ContentRegistry LoadContentPacks()
    {
        var exeDir = AppDomain.CurrentDomain.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(exeDir, "content"),
            Path.Combine(exeDir, "..", "..", "..", "content"),
            Path.Combine(exeDir, "..", "..", "..", "..", "..", "content"),
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

        foreach (var msg in loader.Log)
            System.Diagnostics.Debug.WriteLine($"[Content] {msg}");

        foreach (var err in loader.Errors)
            System.Diagnostics.Debug.WriteLine($"[Content ERROR] {err}");

        if (loader.Errors.Count > 0 || registry.Tiles.Count == 0)
        {
            System.Diagnostics.Debug.WriteLine("WARNING: Content loading had errors. Using hardcoded fallback.");
            return BuildFallbackRegistry();
        }

        return registry;
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

    // ── Update ────────────────────────────────────────────────────────

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

            // Unsubscribe old state events before replacing
            _state.Clock.PeriodChanged -= OnPeriodChanged;
            _state.Weather.WeatherChanged -= OnWeatherChanged;

            _state = CreateOverworldState();

            // Subscribe to new state
            _state.Clock.PeriodChanged += OnPeriodChanged;
            _state.Weather.WeatherChanged += OnWeatherChanged;
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
            turnTaken = _path.Tick(_state);
        }
        else
        {
            turnTaken = _input.ProcessAction(keyAction, _state);
        }

        // ── Post-turn simulation ──────────────────────────────────────
        if (turnTaken && !_state.IsGameOver)
        {
            // Enemy AI
            var enemies = _state.Entities
                .OfType<Enemy>()
                .Where(e => e.IsAlive)
                .ToList();

            foreach (var enemy in enemies)
                enemy.TakeTurn(_state);

            _state.CleanupDead();

            // ── Clock & weather tick (overworld only) ─────────────────
            if (_state.Mode == GameMode.Overworld)
            {
                _state.Clock.Tick();
                _state.Weather.Tick(_state.Clock.TimeOfDay);
            }

            // ── FOV recompute ─────────────────────────────────────────
            // Always recompute after any turn — player or enemies may have
            // changed the state that matters (player definitely moved if
            // turnTaken is true from a move action).
            _state.ActiveMap?.Visibility?.Recompute(
                _state.Player.X,
                _state.Player.Y,
                _state.EffectiveFovRadius
            );

            // ── Overworld lighting update from clock + weather ─────────
            // Only the ambient changes on the overworld (no point lights).
            if (_state.Mode == GameMode.Overworld && _state.ActiveMap?.Lighting != null)
            {
                _state.ActiveMap.Lighting.AmbientLight = _state.OverworldAmbient;
                _state.ActiveMap.Lighting.Recompute(_state.LightSources);
            }
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

    // ── Draw ──────────────────────────────────────────────────────────

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.Black);

        // ── Torch flicker (real-time, runs every frame regardless of turns) ──
        // This recomputes the dungeon LightMap each frame with animated per-source
        // intensities. On the overworld there are no LightSources so this is a no-op.
        _flicker.Update(gameTime.ElapsedGameTime.TotalSeconds);
        if (_state.Mode == GameMode.Dungeon
            && _state.ActiveMap?.Lighting != null
            && _state.LightSources.Count > 0)
        {
            var intensities = _flicker.GetIntensities(_state.LightSources);
            _state.ActiveMap.Lighting.Recompute(_state.LightSources, intensities);
        }

        _spriteBatch.Begin(
            SpriteSortMode.Deferred,
            BlendState.AlphaBlend,
            SamplerState.PointClamp,
            null, null, null
        );

        var previewPath = (_path.State == PathController.PathState.Preview ||
                           _path.State == PathController.PathState.Moving)
                          ? _path.PreviewPath
                          : null;

        _renderer.Draw(_state, _camera, previewPath, _path.Destination);

        _hud.DrawMinimap(_spriteBatch, _state, _renderer.Pixel);

        _spriteBatch.End();

        _hud.Render();
        base.Draw(gameTime);
    }

    // ── Map Transition Handler ────────────────────────────────────────

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

        _dungeonSeed = Environment.TickCount;

        var generator = new DungeonGenerator
        {
            MapWidth = 60,
            MapHeight = 40,
            MinRooms = 6,
            MaxRooms = 12,
            RoomMinSize = 5,
            RoomMaxSize = 10,
            // Use the cave lighting preset — will eventually come from blueprint YAML
            LightingConfig = DungeonLightingConfig.Cave
        };

        var tileDict = new Dictionary<string, TileDef>(_content.Tiles);
        var monsterList = _content.MonsterList.Count > 0 ? _content.MonsterList.ToList() : null;
        var map = generator.Generate(tileDict, _dungeonSeed, _content.ItemList.ToList(), monsterList);

        // ── Initialize dungeon lighting ───────────────────────────────
        var lightingCfg = generator.LightingConfig;
        map.InitializeLighting(lightingCfg.AmbientLight);

        // Copy generated torch positions into state
        _state.LightSources = new List<LightSource>(generator.LightSources);

        // Set FOV radius from blueprint config
        _state.BaseFovRadius = lightingCfg.PlayerFovRadius;

        // Initial lighting pass (before flicker kicks in)
        map.Lighting!.Recompute(_state.LightSources);

        // ── Swap active map ───────────────────────────────────────────
        _state.ActiveMap = map;
        _state.Mode = GameMode.Dungeon;
        _state.Player.SetPosition(generator.EntrancePosition.X, generator.EntrancePosition.Y);

        // Initial FOV from entrance position
        map.Visibility?.Recompute(
            generator.EntrancePosition.X,
            generator.EntrancePosition.Y,
            _state.EffectiveFovRadius
        );

        // ── Populate entities ─────────────────────────────────────────
        _state.Entities.Clear();
        foreach (var entity in generator.SpawnedEntities)
            _state.Entities.Add(entity);

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

        // Restore the overworld map and mode
        _state.ActiveMap = _state.OverworldMap;
        _state.Mode = GameMode.Overworld;

        // Restore player position
        var pos = _state.OverworldPlayerPosition.Value;
        _state.Player.SetPosition(pos.X, pos.Y);

        // Clear dungeon entities and light sources
        _state.Entities.Clear();
        _state.LightSources.Clear();

        // Restore overworld FOV radius
        _state.BaseFovRadius = 12;

        // Recompute FOV at restored position
        _state.ActiveMap?.Visibility?.Recompute(
            pos.X,
            pos.Y,
            _state.EffectiveFovRadius
        );

        // Re-sync overworld lighting ambient to current clock/weather state
        if (_state.ActiveMap?.Lighting != null)
        {
            _state.ActiveMap.Lighting.AmbientLight = _state.OverworldAmbient;
            _state.ActiveMap.Lighting.Recompute(_state.LightSources);
        }

        _state.Log("—————————————————————————————");
        _state.Log("You emerge from the dungeon, back on the overworld.");
        _state.Log($"{_bindings.PrimaryKeyLabel(GameAction.MoveNorth)}{_bindings.PrimaryKeyLabel(GameAction.MoveSouth)}{_bindings.PrimaryKeyLabel(GameAction.MoveEast)}{_bindings.PrimaryKeyLabel(GameAction.MoveWest)} to move. {_bindings.PrimaryKeyLabel(GameAction.Interact)} on the red entrance to re-enter.");
    }

    // ── State Creation Methods ────────────────────────────────────────

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

        var tileDict = new Dictionary<string, TileDef>(_content.Tiles);
        TileMap map;
        if (_content.Biomes.Count > 0)
            map = generator.Generate(tileDict, _overworldSeed, _content.Biomes);
        else
            map = generator.Generate(tileDict, _overworldSeed);

        // ── Initialize overworld lighting ─────────────────────────────
        // Ambient starts at the clock's current color (mid-morning by default).
        // No point light sources on the overworld — sky lighting is ambient-only.
        state.BaseFovRadius = 12;
        map.InitializeLighting(state.OverworldAmbient);

        state.ActiveMap = map;
        state.OverworldMap = map;

        // Place the player at the spawn point
        var player = new Player();
        player.SetPosition(generator.SpawnPosition.X, generator.SpawnPosition.Y);
        state.Player = player;

        state.OverworldPlayerPosition = (generator.SpawnPosition.X, generator.SpawnPosition.Y);

        // Initial FOV compute at spawn
        map.Visibility?.Recompute(
            generator.SpawnPosition.X,
            generator.SpawnPosition.Y,
            state.EffectiveFovRadius
        );

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

        map.Visibility?.Recompute(
            generator.EntrancePosition.X,
            generator.EntrancePosition.Y,
            state.EffectiveFovRadius
        );

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