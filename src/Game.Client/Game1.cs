// src/Game.Client/Game1.cs

using Game.Client.Input;
using Game.Client.Rendering;
using Game.Client.UI;
using Game.Content;
using Game.Core;
using Game.Core.Entities;
using Game.Core.Items;
using Game.Core.Map;
using Game.Core.Tiles;
using Game.Core.World;
using Game.Core.WorldGen;
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

public partial class Game1 : Microsoft.Xna.Framework.Game
{
    private GraphicsDeviceManager _graphics;
    private SpriteBatch _spriteBatch = null!;

    // -- Systems ----------------------------------------------------------
    private TileRenderer _renderer = null!;
    private Camera _camera = null!;
    private InputBindings _bindings = null!;
    private InputHandler _input = null!;
    private HudManager _hud = null!;
    private MouseInputHandler _mouse = null!;
    private PathController _path = null!;
    private TorchFlicker _flicker = null!;

    // -- Game state -------------------------------------------------------
    private GameState _state = null!;

    // -- Content registry - loaded from YAML by Game.Content -------------
    private ContentRegistry _content = null!;

    // -- Current seeds for display/debugging -----------------------------
    private int _overworldSeed;
    private int _dungeonSeed;

    // -- UI font ----------------------------------------------------------
    private SpriteFont? _uiFont;

    // -- Startup setup screen ---------------------------------------------
    private IslandSetupScreen? _setupScreen;
    private bool _showingSetup = true;

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
        Window.Title = "Roguelike";
        Window.AllowUserResizing = true;
        Window.ClientSizeChanged += OnWindowResize;

        base.Initialize();
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);

        // -- Initialize Myra UI framework ---------------------------------
        MyraEnvironment.Game = this;

        // -- Load all content from YAML ----------------------------------
        _content = LoadContentPacks();

        // -- Load key bindings (controls.yml -> fallback to defaults) ----
        _bindings = LoadBindings();

        // -- Create systems -----------------------------------------------
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

        // -- Subscribe to map transition events ---------------------------
        _input.OnMapTransition += HandleMapTransition;

        // -- Start on the island setup screen -----------------------------
        SpriteFont? uiFont = null;
        try { uiFont = Content.Load<SpriteFont>("Fonts/default"); }
        catch { /* font not built yet -- screen will run silently without labels */ }
        _uiFont = uiFont;

        var biomeList = _content.Biomes.ToList();
        _setupScreen = new IslandSetupScreen(
            GraphicsDevice, _spriteBatch,
            _content.Tiles, biomeList,
            font: uiFont
        );
        _setupScreen.ConfigConfirmed += OnSetupConfirmed;

        // Stub state so _state is never null during Update guards
        _state = new GameState();

        // Subscribe stub state events (will be re-wired in OnSetupConfirmed)
        _state.Clock.PeriodChanged += OnPeriodChanged;
        _state.Weather.WeatherChanged += OnWeatherChanged;
    }

    // -- Setup screen handler ---------------------------------------------

    private void OnSetupConfirmed(IslandGenConfig cfg)
    {
        _setupScreen?.Dispose();
        _setupScreen = null;
        _showingSetup = false;

        // Detach stub state events before replacing
        _state.Clock.PeriodChanged -= OnPeriodChanged;
        _state.Weather.WeatherChanged -= OnWeatherChanged;

        _state = CreateOverworldStateFromConfig(cfg);

        _state.Clock.PeriodChanged += OnPeriodChanged;
        _state.Weather.WeatherChanged += OnWeatherChanged;
    }

    // -- Event handlers ---------------------------------------------------

    private void OnPeriodChanged(TimePeriod from, TimePeriod to)
    {
        _state.Log($"The {_state.Clock.PeriodName} begins. ({_state.Clock.TimeString})");
    }

    private void OnWeatherChanged(WeatherState from, WeatherState to)
    {
        _state.Log($"The weather turns {_state.Weather.Name}.");
    }

    // -- Update -----------------------------------------------------------

    protected override void Update(GameTime gameTime)
    {
        // -- Setup screen intercept ---------------------------------------
        if (_showingSetup)
        {
            _setupScreen?.Update();
            base.Update(gameTime);
            return;
        }

        // Advance keyboard + mouse snapshot first
        var keyAction = _input.GetAction();
        _mouse.Poll(_camera, _state.ActiveMap?.Width ?? 0, _state.ActiveMap?.Height ?? 0);

        // -- Meta actions (no turn cost) ----------------------------------
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

        if (_input.IsNewPress(GameAction.ToggleMinimap))
            _hud.ToggleMinimap();

        // -- Keyboard movement cancels any active path -------------------
        if (keyAction != InputHandler.Action.None)
            _path.CancelOnKeyboardInput();

        // -- Mouse -> path state machine ---------------------------------
        _path.HandleMouse(_mouse, _state);

        // -- Determine whose turn it is to act ---------------------------
        bool turnTaken = false;

        if (_path.State == PathController.PathState.Moving)
        {
            turnTaken = _path.Tick(_state);
        }
        else
        {
            turnTaken = _input.ProcessAction(keyAction, _state);
        }

        // -- Post-turn simulation ----------------------------------------
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

            // -- Clock & weather tick (overworld only) -------------------
            if (_state.Mode == GameMode.Overworld)
            {
                _state.Clock.Tick();
                _state.Weather.Tick(_state.Clock.TimeOfDay);
            }

            // -- FOV recompute -------------------------------------------
            // Always recompute after any turn -- player or enemies may have
            // changed the state that matters (player definitely moved if
            // turnTaken is true from a move action).
            _state.ActiveMap?.Visibility?.Recompute(
                _state.Player.X,
                _state.Player.Y,
                _state.EffectiveFovRadius
            );

            // -- Overworld lighting update from clock + weather ----------
            // Only the ambient changes on the overworld (no point lights).
            if (_state.Mode == GameMode.Overworld && _state.ActiveMap?.Lighting != null)
            {
                _state.ActiveMap.Lighting.AmbientLight = _state.OverworldAmbient;
                _state.ActiveMap.Lighting.Recompute(_state.LightSources);
            }
        }

        // -- Chunk streaming focus update (overworld only) ---------------
        if (turnTaken && _state.Mode == GameMode.Overworld
            && _state.ActiveMap is ChunkedWorldMap chunkedMap)
        {
            chunkedMap.ChunkManager.UpdateFocus(
            _state.Player.X, _state.Player.Y);
        }

        // -- Camera ------------------------------------------------------
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

    // -- Draw -------------------------------------------------------------

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.Black);

        // -- Setup screen intercept ---------------------------------------
        if (_showingSetup)
        {
            _setupScreen?.Draw();
            base.Draw(gameTime);
            return;
        }

        // -- Torch flicker (real-time, runs every frame regardless of turns) --
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

        _hud.DrawMinimap(_spriteBatch, _state, _renderer.Pixel, _uiFont);

        _spriteBatch.End();

        _hud.Render();
        base.Draw(gameTime);
    }

    private void OnWindowResize(object? sender, EventArgs e)
    {
        _camera.UpdateViewport(
            Window.ClientBounds.Width,
            Window.ClientBounds.Height
        );
    }

    // -- Key binding loader -----------------------------------------------

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
            System.IO.Path.Combine(exeDir, "content", "BasePack", "config", "controls.yml"),
            System.IO.Path.Combine(exeDir, "..", "..", "..", "content", "BasePack", "config", "controls.yml"),
            System.IO.Path.Combine(exeDir, "..", "..", "..", "..", "..", "content", "BasePack", "config", "controls.yml"),
            System.IO.Path.Combine(Directory.GetCurrentDirectory(), "content", "BasePack", "config", "controls.yml"),
        };

        foreach (var candidate in candidates)
        {
            var resolved = System.IO.Path.GetFullPath(candidate);
            if (File.Exists(resolved))
            {
                System.Diagnostics.Debug.WriteLine($"[InputBindings] Loaded from: {resolved}");
                return InputBindings.Load(resolved);
            }
        }

        System.Diagnostics.Debug.WriteLine("[InputBindings] controls.yml not found -- using defaults.");
        return InputBindings.Defaults();
    }

    // -- Content loader ---------------------------------------------------

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
            System.IO.Path.Combine(exeDir, "content"),
            System.IO.Path.Combine(exeDir, "..", "..", "..", "content"),
            System.IO.Path.Combine(exeDir, "..", "..", "..", "..", "..", "content"),
            System.IO.Path.Combine(Directory.GetCurrentDirectory(), "content"),
        };

        string? contentRoot = null;
        foreach (var candidate in candidates)
        {
            var resolved = System.IO.Path.GetFullPath(candidate);
            if (Directory.Exists(resolved) &&
                Directory.GetDirectories(resolved).Any(d => File.Exists(System.IO.Path.Combine(d, "pack.yml"))))
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
}