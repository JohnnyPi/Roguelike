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
        Window.Title = "Roguelike — Phase 6";
        Window.AllowUserResizing = true;
        Window.ClientSizeChanged += OnWindowResize;

        base.Initialize();
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);

        // Build tile registry (hardcoded for Phase 6 — YAML loader replaces this)
        _tileRegistry = BuildTileRegistry();

        // Create systems
        _camera = new Camera(
            _graphics.PreferredBackBufferWidth,
            _graphics.PreferredBackBufferHeight
        );
        _renderer = new TileRenderer(GraphicsDevice, _spriteBatch);
        _input = new InputHandler();

        // Build initial game state with a test map
        _state = CreateInitialState();
    }

    protected override void Update(GameTime gameTime)
    {
        // Escape to quit
        if (Keyboard.GetState().IsKeyDown(Keys.Escape))
            Exit();

        // Get player input
        var action = _input.GetAction();

        // Process action — returns true if a turn was consumed
        bool turnTaken = _input.ProcessAction(action, _state);

        if (turnTaken)
        {
            // This is where enemy AI turns will go in Phase 6 combat.
            // For now, just cleanup any dead entities.
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

    /// <summary>
    /// Build the tile definition registry.
    /// This is a temporary hardcoded version — Game.Content's YAML loader
    /// replaces this once we wire up content pack loading.
    /// </summary>
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
            }
        };

        foreach (var tile in tiles)
            registry[tile.Id] = tile;

        return registry;
    }

    /// <summary>
    /// Create the initial game state with a small test map.
    /// This gets replaced by the overworld generator in Phase 3
    /// and the dungeon generator in Phase 2.
    /// </summary>
    private GameState CreateInitialState()
    {
        var state = new GameState();

        // Create a small test map: 30x20 with walls around the border
        // and a few interior walls to test collision
        int width = 30;
        int height = 20;
        var map = new TileMap(width, height, _tileRegistry);

        // Fill everything with grass
        map.Fill("base:grass");

        // Border walls
        for (int x = 0; x < width; x++)
        {
            map.SetTile(x, 0, "base:wall");
            map.SetTile(x, height - 1, "base:wall");
        }
        for (int y = 0; y < height; y++)
        {
            map.SetTile(0, y, "base:wall");
            map.SetTile(width - 1, y, "base:wall");
        }

        // A few interior walls to test collision and navigation
        for (int y = 3; y < 10; y++)
            map.SetTile(10, y, "base:wall");

        for (int x = 15; x < 22; x++)
            map.SetTile(x, 12, "base:wall");

        // Some terrain variety
        map.FillRect(5, 14, 4, 3, "base:dirt");
        map.FillRect(20, 3, 3, 3, "base:water");

        // Place a dungeon entrance
        map.SetTile(25, 10, "base:dungeon_entrance");

        state.ActiveMap = map;

        // Create and place the player
        var player = new Player();
        player.SetPosition(5, 5);
        state.Player = player;

        state.Log("Welcome, adventurer. Use WASD to move. Press E to interact.");

        return state;
    }

    private void OnWindowResize(object sender, EventArgs e)
    {
        _camera.UpdateViewport(
            Window.ClientBounds.Width,
            Window.ClientBounds.Height
        );
    }
}