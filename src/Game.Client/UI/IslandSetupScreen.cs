// src/Game.Client/UI/IslandSetupScreen.cs
//
// Full-screen island configuration screen shown at startup.
//
// Layout (1280x800 reference):
//
//   +--[TITLE]--------------------------------------------+
//   |           ISLAND GENERATOR                          |
//   +--[PREVIEW minimap]----+--[SLIDERS panel]------------+
//   |                       |  Seed           [value]     |
//   |   512x512 island      |  Map Size       [===o  ]    |
//   |   preview texture     |  Frequency      [==o   ]    |
//   |   (fills left half)   |  Octaves        [====o ]    |
//   |                       |  Island Radius  [===o  ]    |
//   |                       |  Falloff        [==o   ]    |
//   |                       |  Coast Warp     [=o    ]    |
//   |                       |  Wind Angle     [======]    |
//   |                       |  Wind Strength  [===o  ]    |
//   |                       |  Volcano Count  [=o    ]    |
//   |                       |  [RANDOMIZE]  [REGENERATE] |
//   +-----------------------+  [      START GAME      ]   |
//                              +----------------------------+
//
// The minimap preview is rendered into a Texture2D by sampling tile colors
// directly from OverworldGenerator without building a full TileMap -- using a
// separate lightweight color-sample pass at preview resolution (256x256).
//
// Controls:
//   - Drag sliders with LMB
//   - Click REGENERATE to rebuild preview with current settings
//   - Click RANDOMIZE to pick a new random seed and regenerate
//   - Click START GAME to confirm and enter the world
//   - Enter key = START GAME shortcut
//
// Split:
//   IslandSetupScreen.cs         -- fields, constructor, layout, Update, BuildConfig, Dispose
//   IslandSetupScreen.Draw.cs    -- Draw method + drawing helpers
//   IslandSetupScreen.Preview.cs -- RebuildPreview, MarkDot, color cache

using System;
using System.Collections.Generic;
using Game.Core.Biomes;
using Game.Core.Tiles;
using Game.Core.WorldGen;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

#nullable enable

namespace Game.Client.UI;

/// <summary>
/// Startup island configuration screen. Fires <see cref="ConfigConfirmed"/>
/// when the player clicks Start Game. The config carries the full generation
/// parameters; Game1 should generate the world map from it.
/// </summary>
public sealed partial class IslandSetupScreen : IDisposable
{
    // -- Events -----------------------------------------------------------
    /// <summary>Fired when the player confirms settings and wants to start.</summary>
    public event Action<IslandGenConfig>? ConfigConfirmed;

    // -- External references ----------------------------------------------
    private readonly GraphicsDevice _gd;
    private readonly SpriteBatch _sb;
    private readonly Texture2D _pixel;
    private SpriteFont? _font;       // may be null -- we draw without font if unavailable

    // -- Preview texture --------------------------------------------------
    private const int PreviewSize = 256;   // minimap resolution
    private Texture2D _previewTex;
    private bool _previewDirty = true;

    // -- Biomes (needed for color lookup) ---------------------------------
    private readonly IReadOnlyDictionary<string, TileDef> _tiles;
    private readonly IReadOnlyList<BiomeDef> _biomes;

    // -- Layout -----------------------------------------------------------
    private int _vpW, _vpH;
    private Rectangle _previewRect;   // where the minimap is drawn
    private Rectangle _panelRect;     // right-side slider panel

    // -- Colours ----------------------------------------------------------
    private static readonly Color BgDark = new Color(8, 10, 16, 255);
    private static readonly Color PanelBg = new Color(14, 20, 32, 220);
    private static readonly Color BorderColor = new Color(50, 80, 120, 200);
    private static readonly Color SliderTrack = new Color(30, 45, 65, 255);
    private static readonly Color SliderFill = new Color(60, 130, 220, 255);
    private static readonly Color SliderThumb = new Color(120, 200, 255, 255);
    private static readonly Color TextColor = new Color(200, 216, 232, 255);
    private static readonly Color TextDim = new Color(100, 130, 160, 255);
    private static readonly Color BtnNormal = new Color(30, 55, 90, 230);
    private static readonly Color BtnHover = new Color(50, 90, 150, 230);
    private static readonly Color BtnStart = new Color(40, 130, 80, 230);
    private static readonly Color BtnStartHov = new Color(60, 180, 100, 230);
    private static readonly Color TitleColor = new Color(140, 210, 255, 255);

    // -- Slider definitions -----------------------------------------------

    private sealed class SliderDef
    {
        public string Label;
        public float Min, Max, Value;
        public bool IsInt;
        public string? Tooltip;
        public Rectangle TrackRect;   // computed each layout pass
        public bool Dragging;

        public SliderDef(string label, float min, float max, float value,
                         bool isInt = false, string? tooltip = null)
        {
            Label = label; Min = min; Max = max; Value = value;
            IsInt = isInt; Tooltip = tooltip;
        }

        public string FormatValue()
        {
            if (IsInt) return ((int)Math.Round(Value)).ToString();
            return Value.ToString("F3");
        }

        /// <summary>Normalized 0..1 position of the thumb.</summary>
        public float Normalized
        {
            get => (Value - Min) / (Max - Min);
            set => Value = Min + Math.Clamp(value, 0f, 1f) * (Max - Min);
        }
    }

    // Slider indices (keep in sync with _sliders list order)
    private const int SI_SIZE = 0;
    private const int SI_FREQ = 1;
    private const int SI_OCTAVES = 2;
    private const int SI_RADIUS = 3;
    private const int SI_FALLOFF = 4;
    private const int SI_COAST = 5;
    private const int SI_WIND_ANG = 6;
    private const int SI_WIND_STR = 7;
    private const int SI_MOISTURE = 8;
    private const int SI_ISLANDS = 9;
    private const int SI_CHAIN_CURVE = 10;
    private const int SI_CHAIN_SEP = 11;
    private const int SI_VOLC = 12;
    private const int SI_VOLC_RAD = 13;
    private const int SI_RIVERS = 14;

    private readonly List<SliderDef> _sliders = new()
    {
        new SliderDef("Map Size",       64,   2048, 512,  isInt: true,
                      "Width and height in tiles (wide chain maps auto-set height = width/2)"),
        new SliderDef("Frequency",      0.002f, 0.04f, 0.008f,
                      tooltip: "Base noise frequency. Lower = broader terrain features"),
        new SliderDef("Octaves",        1,    8,    6,    isInt: true,
                      tooltip: "FBm octave count. More = finer detail"),
        new SliderDef("Island Radius",  0.40f, 0.95f, 0.85f,
                      tooltip: "Island size as fraction of map half-width"),
        new SliderDef("Falloff",        1.0f, 4.0f, 2.2f,
                      tooltip: "Edge sharpness. Higher = steeper cliff to ocean"),
        new SliderDef("Coast Warp",     0.0f, 0.5f, 0.18f,
                      tooltip: "Coastline raggedness (0=circle, 0.5=extreme)"),
        new SliderDef("Wind Angle",     0f,   360f, 225f, isInt: true,
                      tooltip: "Prevailing wind direction in degrees (0=North)"),
        new SliderDef("Wind Strength",  0.0f, 1.0f, 0.6f,
                      tooltip: "Rain shadow strength (0=off, 1=full)"),
        new SliderDef("Moisture Noise", 0.0f, 1.0f, 0.5f,
                      tooltip: "Blend noise into moisture (0=gradient only, 1=noisy)"),
        new SliderDef("Islands",        1,    8,    1,    isInt: true,
                      tooltip: "Number of islands in the chain (1=single, 2-8=archipelago)"),
        new SliderDef("Chain Curve",    0.0f, 1.0f, 0.25f,
                      tooltip: "Arc amount of the island chain (0=straight, 1=strong curve)"),
        new SliderDef("Island Gap",     10,   200,  40,   isInt: true,
                      tooltip: "Minimum ocean gap between island edges in tiles"),
        new SliderDef("Volcanoes",      0,    4,    1,    isInt: true,
                      tooltip: "Number of volcano cones to stamp"),
        new SliderDef("Volcano Radius", 20,   100,  60,   isInt: true,
                      tooltip: "Volcano base radius in tiles"),
        new SliderDef("Rivers",         0,    8,    4,    isInt: true,
                      tooltip: "Number of rivers carved from highlands to coast"),
    };

    // -- Seed -------------------------------------------------------------
    private int _seed;

    // -- Button rects -----------------------------------------------------
    private Rectangle _btnRegen;
    private Rectangle _btnRandom;
    private Rectangle _btnStart;

    // -- Input state ------------------------------------------------------
    private MouseState _prevMouse;
    private KeyboardState _prevKeys;
    private SliderDef? _dragging;
    private int _hoveredBtn = -1;   // 0=regen, 1=random, 2=start
    private bool _disposed;

    // -- Cached color lookup for preview rendering -----------------------
    private readonly Dictionary<string, Color> _colorCache = new();

    // -----------------------------------------------------------------
    // Construction
    // -----------------------------------------------------------------

    public IslandSetupScreen(GraphicsDevice gd, SpriteBatch sb,
                             IReadOnlyDictionary<string, TileDef> tiles,
                             IReadOnlyList<BiomeDef> biomes,
                             SpriteFont? font = null)
    {
        _gd = gd;
        _sb = sb;
        _tiles = tiles;
        _biomes = biomes;
        _font = font;

        _pixel = new Texture2D(gd, 1, 1);
        _pixel.SetData(new[] { Color.White });

        _previewTex = new Texture2D(gd, PreviewSize, PreviewSize);

        _seed = Environment.TickCount;
        _vpW = gd.Viewport.Width;
        _vpH = gd.Viewport.Height;

        ComputeLayout();
    }

    // -----------------------------------------------------------------
    // Layout
    // -----------------------------------------------------------------

    private void ComputeLayout()
    {
        _vpW = _gd.Viewport.Width;
        _vpH = _gd.Viewport.Height;

        int margin = 20;
        int titleH = 60;
        int bodyY = titleH + margin;
        int bodyH = _vpH - bodyY - margin;

        // Left half: preview
        int previewW = (_vpW / 2) - margin * 2;
        int previewH = Math.Min(previewW, bodyH - 10);
        int previewX = margin;
        int previewY = bodyY + (bodyH - previewH) / 2;
        _previewRect = new Rectangle(previewX, previewY, previewH, previewH); // square

        // Right half: slider panel
        int panelX = _vpW / 2;
        int panelW = _vpW - panelX - margin;
        _panelRect = new Rectangle(panelX, bodyY, panelW, bodyH);

        // Distribute sliders vertically in the panel
        int sliderAreaTop = bodyY + 10;
        int btnAreaH = 80;
        int sliderAreaH = bodyH - btnAreaH - 20;
        int rowH = sliderAreaH / _sliders.Count;
        int labelW = 130;
        int valW = 60;
        int trackX = panelX + labelW + 10;
        int trackW = panelW - labelW - valW - 20;

        for (int i = 0; i < _sliders.Count; i++)
        {
            int rowMid = sliderAreaTop + i * rowH + rowH / 2;
            _sliders[i].TrackRect = new Rectangle(trackX, rowMid - 6, trackW, 12);
        }

        // Buttons at bottom of panel
        int btnY = bodyY + sliderAreaH + 14;
        int btnH = 32;
        int halfW = (panelW - 30) / 2;
        _btnRegen = new Rectangle(panelX + 5, btnY, halfW, btnH);
        _btnRandom = new Rectangle(panelX + 5 + halfW + 10, btnY, halfW, btnH);
        _btnStart = new Rectangle(panelX + 5, btnY + 42, panelW - 10, 36);
    }

    // -----------------------------------------------------------------
    // Update
    // -----------------------------------------------------------------

    public void Update()
    {
        if (_vpW != _gd.Viewport.Width || _vpH != _gd.Viewport.Height)
            ComputeLayout();

        var mouse = Mouse.GetState();
        var keys = Keyboard.GetState();
        var mp = new Point(mouse.X, mouse.Y);
        bool lDown = mouse.LeftButton == ButtonState.Pressed;
        bool lClick = mouse.LeftButton == ButtonState.Pressed
                     && _prevMouse.LeftButton == ButtonState.Released;

        // -- Slider drag -------------------------------------------------
        if (_dragging != null)
        {
            if (lDown)
            {
                var t = _dragging.TrackRect;
                float norm = (float)(mouse.X - t.X) / t.Width;
                _dragging.Normalized = norm;
                _previewDirty = true;
            }
            else
            {
                _dragging.Dragging = false;
                _dragging = null;
            }
        }
        else if (lClick)
        {
            // Check if click hits a slider thumb zone
            foreach (var s in _sliders)
            {
                var t = s.TrackRect;
                var thumbRect = GetThumbRect(s);
                // Expand hit area slightly
                thumbRect.Inflate(6, 8);
                if (thumbRect.Contains(mp))
                {
                    _dragging = s;
                    s.Dragging = true;
                    break;
                }
                // Also allow clicking anywhere on the track
                var trackHit = t; trackHit.Inflate(0, 8);
                if (trackHit.Contains(mp))
                {
                    float norm = (float)(mouse.X - t.X) / t.Width;
                    s.Normalized = norm;
                    _dragging = s;
                    s.Dragging = true;
                    _previewDirty = true;
                    break;
                }
            }
        }

        // -- Button hover ------------------------------------------------
        _hoveredBtn = -1;
        if (_btnRegen.Contains(mp)) _hoveredBtn = 0;
        if (_btnRandom.Contains(mp)) _hoveredBtn = 1;
        if (_btnStart.Contains(mp)) _hoveredBtn = 2;

        // -- Button clicks -----------------------------------------------
        if (lClick)
        {
            if (_btnRegen.Contains(mp))
            {
                _previewDirty = true;
            }
            else if (_btnRandom.Contains(mp))
            {
                _seed = Environment.TickCount;
                _previewDirty = true;
            }
            else if (_btnStart.Contains(mp))
            {
                FireConfirmed();
            }
        }

        // -- Keyboard shortcut -------------------------------------------
        if (keys.IsKeyDown(Keys.Enter) && !_prevKeys.IsKeyDown(Keys.Enter))
            FireConfirmed();
        if (keys.IsKeyDown(Keys.R) && !_prevKeys.IsKeyDown(Keys.R))
        {
            _seed = Environment.TickCount;
            _previewDirty = true;
        }

        // Mouse wheel on sliders: scroll whichever slider the cursor is near
        int scrollDelta = mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue;
        if (scrollDelta != 0)
        {
            foreach (var s in _sliders)
            {
                var rowZone = new Rectangle(_panelRect.X, s.TrackRect.Y - 12,
                                            _panelRect.Width, 28);
                if (rowZone.Contains(mp))
                {
                    float step = s.IsInt ? 1f / (s.Max - s.Min)
                                         : 0.01f / (s.Max - s.Min);
                    s.Normalized += Math.Sign(scrollDelta) * step * 3f;
                    _previewDirty = true;
                    break;
                }
            }
        }

        _prevMouse = mouse;
        _prevKeys = keys;

        // -- Rebuild preview if dirty ------------------------------------
        if (_previewDirty)
        {
            RebuildPreview();
            _previewDirty = false;
        }
    }

    // -----------------------------------------------------------------
    // Config builder
    // -----------------------------------------------------------------

    private IslandGenConfig BuildConfig()
    {
        int sz = (int)Math.Round(_sliders[SI_SIZE].Value);
        if (sz % 2 != 0) sz++;

        int islandCount = (int)Math.Round(_sliders[SI_ISLANDS].Value);

        // For multi-island chains use a wide (2:1) map automatically.
        // A 2:1 ratio gives the Bezier chain room to spread horizontally.
        int mapW = sz;
        int mapH = islandCount >= 3 ? Math.Max(64, sz / 2) : sz;

        // Scale frequency down for larger maps so features remain proportional.
        float baseFreq = _sliders[SI_FREQ].Value;

        return new IslandGenConfig
        {
            Seed = _seed,
            MapWidth = mapW,
            MapHeight = mapH,
            Frequency = baseFreq,
            Octaves = (int)Math.Round(_sliders[SI_OCTAVES].Value),
            IslandRadiusScale = _sliders[SI_RADIUS].Value,
            IslandFalloffExp = _sliders[SI_FALLOFF].Value,
            CoastWarpStrength = _sliders[SI_COAST].Value,
            PrevailingWindAngleDeg = _sliders[SI_WIND_ANG].Value,
            WindGradientStrength = _sliders[SI_WIND_STR].Value,
            MoistureNoiseWeight = _sliders[SI_MOISTURE].Value,
            IslandCount = islandCount,
            ChainCurveAmount = _sliders[SI_CHAIN_CURVE].Value,
            MinIslandSeparation = (int)Math.Round(_sliders[SI_CHAIN_SEP].Value),
            EntranceCount = Math.Max(1, islandCount),
            MinEntranceSpacing = Math.Max(20, sz / 8),
            MinEntranceFromSpawn = Math.Max(15, sz / 10),
            VolcanoCount = (int)Math.Round(_sliders[SI_VOLC].Value),
            VolcanoBaseRadius = (int)Math.Round(_sliders[SI_VOLC_RAD].Value),
            RiverCount = (int)Math.Round(_sliders[SI_RIVERS].Value),
        };
    }

    private void FireConfirmed()
    {
        ConfigConfirmed?.Invoke(BuildConfig());
    }

    // -----------------------------------------------------------------
    // IDisposable
    // -----------------------------------------------------------------

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pixel.Dispose();
        _previewTex.Dispose();
    }
}