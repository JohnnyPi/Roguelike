// src/Game.Client/UI/HudManager.cs
//
// Redesigned HUD — modern, reactive, dark-panel style.
//
// Layout (1280×800 reference):
//
//   ┌─[STATS]──────┐                        ┌─[MODE]────┐
//   │ VITALITY      │                        │ ◈ OVERWORLD│
//   │ ██████░░░░ 27/30│                      │  X 14  Y 22│
//   │ ATK 5  DEF 2  │                        └───────────┘
//   └──────────────┘
//
//   [INVENTORY panel — toggled with I, slides in left]
//
//   ┌─[LOG]────────────────────────────────┐
//   │ › You slay the goblin.               │
//   │ › Picked up Gold Coin x3.            │
//   └──────────────────────────────────────┘
//   ─────────────────────────────────────────────────────
//   WASD Move  E Interact  I Inventory  Space Wait  R Regen
//
// Design rules:
//   - Semi-transparent dark panels (rgba 0,0,0 ~180-200)
//   - Segmented HP bar (10 blocks) with green→yellow→red coloring
//   - ATK / DEF inline below HP
//   - Mode badge has a border tinted to mode color
//   - Message log: last 8 msgs, older ones dimmed via alpha fade
//   - Status dot bottom-right pulses color with HP state (updated every frame)
//   - Vignette warning overlay when HP < 30%
//   - All panels only rebuild widgets when underlying data changes (cheap)

using Game.Client.Input;
using Game.Client.Rendering;
using Game.Core;
using Game.Core.Entities;
using Game.Core.Items;
using Game.Core.Map;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Myra;
using Myra.Graphics2D;
using Myra.Graphics2D.Brushes;
using Myra.Graphics2D.UI;
using System;
using System.Collections.Generic;

#nullable enable

namespace Game.Client.UI;

public sealed partial class HudManager
{
    private readonly Desktop _desktop;

    // ── Colours (design palette) ─────────────────────────────────────────────
    private static readonly Color PanelBg = new(8, 12, 20, 200);
    private static readonly Color BorderNormal = new(55, 80, 110, 140);
    private static readonly Color TextPrimary = new(200, 216, 232, 255);
    private static readonly Color TextDim = new(88, 112, 140, 255);
    private static readonly Color AccentBlue = new(74, 173, 255, 255);
    private static readonly Color AccentOrange = new(255, 122, 58, 255);
    private static readonly Color Gold = new(232, 184, 75, 255);
    private static readonly Color HpGood = new(74, 255, 170, 255);
    private static readonly Color HpWarn = new(255, 184, 74, 255);
    private static readonly Color HpDanger = new(255, 74, 74, 255);
    private static readonly Color MsgCombat = new(255, 138, 106, 255);
    private static readonly Color MsgPickup = new(106, 255, 202, 255);
    private static readonly Color MsgSystem = new(106, 186, 255, 255);
    private static readonly Color MsgDefault = new(168, 188, 200, 255);

    // ── Top-left: stats group ────────────────────────────────────────────────
    private readonly Label _vitLabel;           // "VITALITY"
    private readonly Label _hpValueLabel;       // "27 / 30"
    private readonly HorizontalProgressBar _hpBar;
    private readonly Label _atkLabel;
    private readonly Label _defLabel;
    private float _lastHpPct = -1f;          // dirty-check

    // ── Top-right: mode + coords ─────────────────────────────────────────────
    private readonly Label _modeIcon;
    private readonly Label _modeLabel;
    private readonly Label _coordsLabel;
    private GameMode _lastMode = (GameMode)(-1);

    // ── Bottom-left: message log ──────────────────────────────────────────────
    private readonly VerticalStackPanel _logPanel;
    private readonly ScrollViewer _logScroll;
    private int _lastLogCount = -1;
    private const int MaxVisibleMessages = 8;

    // ── Center-left: inventory ────────────────────────────────────────────────
    private readonly Panel _inventoryPanel;
    private readonly VerticalStackPanel _inventoryList;
    private readonly Label _inventoryTitle;
    private bool _inventoryVisible;

    // ── Bottom bar ────────────────────────────────────────────────────────────
    private readonly Label _hintsLabel;
    private readonly Label _statusDot;         // pulses color with HP state

    // ── Vignette / danger overlay ─────────────────────────────────────────────
    //  Myra doesn't support radial gradients, so we use a full-screen semi-
    //  transparent border panel that appears only when HP < 30%.
    private readonly Panel _dangerVignette;

    // ── Minimap ──────────────────────────────────────────────────────────────
    private const int MinimapSize = 220;      // map render area in pixels
    private const int MinimapBorder = 6;      // inner padding
    private const int MinimapTitleH = 20;     // title bar height
    private const int MinimapTileMinPx = 1;

    // Minimap window state
    private bool _minimapVisible = false;
    private int _minimapWindowX = 10;         // top-left of the window
    private int _minimapWindowY = 10;
    private bool _minimapDragging = false;
    private int _minimapDragOffX, _minimapDragOffY;

    // Input state for minimap drag
    private MouseState _prevMinimapMouse;

    public bool IsMinimapVisible => _minimapVisible;

    public void ToggleMinimap()
    {
        _minimapVisible = !_minimapVisible;
    }

    public HudManager(InputBindings bindings)
    {
        _desktop = new Desktop();
        var root = new Panel();

        BuildStatsPanel(root, out _vitLabel, out _hpValueLabel, out _hpBar,
                        out _atkLabel, out _defLabel);

        BuildModePanel(root, out _modeIcon, out _modeLabel, out _coordsLabel);

        BuildMessageLog(root, out _logPanel, out _logScroll);

        BuildInventoryPanel(root, out _inventoryPanel, out _inventoryList,
                            out _inventoryTitle);

        BuildBottomBar(root, bindings, out _hintsLabel, out _statusDot);

        BuildDangerVignette(root, out _dangerVignette);

        _desktop.Root = root;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Public API
    // ═══════════════════════════════════════════════════════════════════════════

    public void ToggleInventory()
    {
        _inventoryVisible = !_inventoryVisible;
        _inventoryPanel.Visible = _inventoryVisible;
    }

    public bool IsInventoryVisible => _inventoryVisible;

    public void Update(GameTime gameTime, GameState state)
    {
        if (state?.Player == null) return;

        UpdateStats(state.Player);
        UpdateMode(state);
        _coordsLabel.Text = $"X {state.Player.X,4}  Y {state.Player.Y,4}";
        UpdateMessageLog(state);

        if (_inventoryVisible)
            UpdateInventory(state.Player.Inventory);
    }

    public void Render() => _desktop.Render();

    // ═══════════════════════════════════════════════════════════════════════════
    //  Builders
    // ═══════════════════════════════════════════════════════════════════════════

    private static void BuildStatsPanel(
        Panel root,
        out Label vitLabel, out Label hpValueLabel, out HorizontalProgressBar hpBar,
        out Label atkLabel, out Label defLabel)
    {
        var outer = new VerticalStackPanel
        {
            Left = 10,
            Top = 10,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Spacing = 0
        };

        var panel = new VerticalStackPanel
        {
            Padding = new Thickness(10, 8, 10, 8),
            Spacing = 4,
            Background = new SolidBrush(PanelBg)
        };

        // Row 1: label + value
        var headerRow = new HorizontalStackPanel { Spacing = 0 };
        vitLabel = new Label
        {
            Text = "VITALITY",
            TextColor = TextDim,
            // Myra labels don't support letter-spacing, so uppercase suffices
        };
        hpValueLabel = new Label
        {
            Text = "30 / 30",
            TextColor = HpGood,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        // Push the value label to the right inside the row
        var spacer = new Panel { Width = 1, HorizontalAlignment = HorizontalAlignment.Stretch };
        headerRow.Widgets.Add(vitLabel);
        // Myra doesn't have a "stretch" spacer inside HStack, so we pad via margin
        hpValueLabel.Margin = new Thickness(40, 0, 0, 0);
        headerRow.Widgets.Add(hpValueLabel);
        panel.Widgets.Add(headerRow);

        // Row 2: segmented HP bar (we fake segments with a wide ProgressBar)
        hpBar = new HorizontalProgressBar
        {
            Width = 184,
            Height = 8,
            Minimum = 0,
            Maximum = 100,
            Value = 100,
            Filler = new SolidBrush(HpGood),
        };
        panel.Widgets.Add(hpBar);

        // Row 3: ATK / DEF
        var statsRow = new HorizontalStackPanel { Spacing = 16 };

        atkLabel = new Label { Text = "ATK  5", TextColor = HpWarn };
        defLabel = new Label { Text = "DEF  2", TextColor = AccentBlue };
        statsRow.Widgets.Add(atkLabel);
        statsRow.Widgets.Add(defLabel);
        panel.Widgets.Add(statsRow);

        outer.Widgets.Add(panel);
        root.Widgets.Add(outer);
    }

    private static void BuildModePanel(
        Panel root,
        out Label modeIcon, out Label modeLabel, out Label coordsLabel)
    {
        var stack = new VerticalStackPanel
        {
            Margin = new Thickness(0, 10, 10, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Spacing = 4,
        };

        // Mode badge
        var badgePanel = new HorizontalStackPanel
        {
            Spacing = 6,
            Padding = new Thickness(8, 4, 8, 4),
            Background = new SolidBrush(new Color(74, 173, 255, 24)),
        };
        modeIcon = new Label { Text = "◈", TextColor = AccentBlue };
        modeLabel = new Label { Text = "OVERWORLD", TextColor = AccentBlue };
        badgePanel.Widgets.Add(modeIcon);
        badgePanel.Widgets.Add(modeLabel);
        stack.Widgets.Add(badgePanel);

        // Coordinates
        var coordPanel = new Panel
        {
            Padding = new Thickness(8, 4, 8, 4),
            Background = new SolidBrush(PanelBg),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        coordsLabel = new Label
        {
            Text = "X   0  Y   0",
            TextColor = TextDim,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        coordPanel.Widgets.Add(coordsLabel);
        stack.Widgets.Add(coordPanel);

        root.Widgets.Add(stack);
    }

    private static void BuildMessageLog(
        Panel root,
        out VerticalStackPanel logPanel, out ScrollViewer logScroll)
    {
        logPanel = new VerticalStackPanel { Spacing = 2 };

        var inner = new VerticalStackPanel { Spacing = 0, Padding = new Thickness(8, 6, 8, 6) };

        // Log header
        inner.Widgets.Add(new Label
        {
            Text = "— LOG —",
            TextColor = TextDim,
        });

        inner.Widgets.Add(logPanel);

        logScroll = new ScrollViewer
        {
            Margin = new Thickness(10, 0, 0, 38),  // 38 = bottom bar height
            Width = 440,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = new SolidBrush(PanelBg),
            Content = inner,
            ShowHorizontalScrollBar = false,
            ShowVerticalScrollBar = false,
        };

        root.Widgets.Add(logScroll);
    }

    private static void BuildInventoryPanel(
        Panel root,
        out Panel inventoryPanel, out VerticalStackPanel inventoryList,
        out Label inventoryTitle)
    {
        inventoryTitle = new Label
        {
            Text = "INVENTORY",
            TextColor = Gold,
        };

        var countLabel = new Label
        {
            Text = "0 items",
            TextColor = TextDim,
        };

        var headerRow = new HorizontalStackPanel { Spacing = 0 };
        headerRow.Widgets.Add(inventoryTitle);
        countLabel.Margin = new Thickness(8, 0, 0, 0);
        headerRow.Widgets.Add(countLabel);

        inventoryList = new VerticalStackPanel { Spacing = 3 };

        var inner = new VerticalStackPanel
        {
            Spacing = 6,
            Padding = new Thickness(10, 8, 10, 8),
        };
        inner.Widgets.Add(headerRow);
        inner.Widgets.Add(inventoryList);

        inventoryPanel = new Panel
        {
            Left = 10,
            Top = 120,
            Width = 224,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidBrush(PanelBg),
            Visible = false,
        };
        inventoryPanel.Widgets.Add(inner);
        root.Widgets.Add(inventoryPanel);
    }

    private static void BuildBottomBar(
        Panel root, InputBindings bindings,
        out Label hintsLabel, out Label statusDot)
    {
        var bar = new HorizontalStackPanel
        {
            Padding = new Thickness(10, 5, 10, 5),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = new SolidBrush(new Color(6, 9, 14, 235)),
            Spacing = 0,
        };

        hintsLabel = new Label
        {
            Text = InputBindings.HintLine(bindings,
                (GameAction.MoveNorth, "Move"),
                (GameAction.Interact, "Interact"),
                (GameAction.ToggleInventory, "Inv"),
                (GameAction.ToggleMinimap, "Map"),
                (GameAction.Wait, "Wait"),
                (GameAction.RegenerateWorld, "Regen"),
                (GameAction.Quit, "Quit")),
            TextColor = TextDim,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        // Status dot — right-aligned via a right-aligned label in the panel
        statusDot = new Label
        {
            Text = "● ALIVE",
            TextColor = HpGood,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 0),
        };

        bar.Widgets.Add(hintsLabel);
        // Myra HStack doesn't have flex-grow, so we add the dot separately
        // anchored right via a right-aligned panel overlapping the bar
        root.Widgets.Add(bar);

        var dotPanel = new Panel
        {
            Padding = new Thickness(0, 5, 10, 5),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        dotPanel.Widgets.Add(statusDot);
        root.Widgets.Add(dotPanel);
    }

    private static void BuildDangerVignette(Panel root, out Panel vignette)
    {
        // Approximate a danger flash by covering the screen with a very faint
        // red overlay. Shown only when HP < 30%.
        vignette = new Panel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = new SolidBrush(new Color(200, 30, 30, 28)),
            Visible = false,
        };
        root.Widgets.Add(vignette);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Internal update methods
    // ═══════════════════════════════════════════════════════════════════════════

    private void UpdateStats(Player player)
    {
        float pct = player.MaxHp > 0
            ? (float)player.Hp / player.MaxHp
            : 0f;

        // Only touch widgets when value meaningfully changed
        if (Math.Abs(pct - _lastHpPct) < 0.005f) return;
        _lastHpPct = pct;

        // HP value label
        _hpValueLabel.Text = $"{player.Hp} / {player.MaxHp}";

        // Bar
        _hpBar.Value = pct * 100f;

        // Reactive colour
        Color col = pct > 0.6f ? HpGood
                  : pct > 0.3f ? HpWarn
                                : HpDanger;
        _hpBar.Filler = new SolidBrush(col);
        _hpValueLabel.TextColor = col;

        // Status dot
        _statusDot.TextColor = col;

        // ATK/DEF (static for now, update if player stats change later)
        _atkLabel.Text = $"ATK  {player.Attack}";
        _defLabel.Text = $"DEF  {player.Defense}";

        // Danger vignette
        _dangerVignette.Visible = (pct < 0.3f);
    }

    private void UpdateMode(GameState state)
    {
        if (state.Mode == _lastMode) return;
        _lastMode = state.Mode;

        var player = state.Player;

        switch (state.Mode)
        {
            case GameMode.Overworld:
                _modeIcon.TextColor = AccentBlue;
                _modeLabel.Text = "OVERWORLD";
                _modeLabel.TextColor = AccentBlue;
                _modeIcon.Text = "◈";
                break;

            case GameMode.Dungeon:
                _modeIcon.TextColor = AccentOrange;
                _modeLabel.Text = "DUNGEON";
                _modeLabel.TextColor = AccentOrange;
                _modeIcon.Text = "⬡";
                break;
        }
    }

    private void UpdateMessageLog(GameState state)
    {
        if (state.MessageLog.Count == _lastLogCount) return;
        _lastLogCount = state.MessageLog.Count;

        _logPanel.Widgets.Clear();

        int start = Math.Max(0, state.MessageLog.Count - MaxVisibleMessages);
        for (int i = start; i < state.MessageLog.Count; i++)
        {
            float ageFraction = (float)(i - start) / MaxVisibleMessages;
            byte alpha = (byte)(90 + (int)(165 * ageFraction)); // 90..255

            Color baseColor = ClassifyMessage(state.MessageLog[i]);
            Color colored = new(baseColor.R, baseColor.G, baseColor.B, alpha);

            var row = new HorizontalStackPanel { Spacing = 4 };
            row.Widgets.Add(new Label { Text = "›", TextColor = new Color(TextDim.R, TextDim.G, TextDim.B, alpha) });
            row.Widgets.Add(new Label { Text = state.MessageLog[i], TextColor = colored, Wrap = true });
            _logPanel.Widgets.Add(row);
        }

        _logScroll.ScrollPosition = new Point(0, int.MaxValue / 2);
    }

    private static Color ClassifyMessage(string msg)
    {
        if (ContainsAny(msg, "attack", "damage", "slay", "hit", "struck", "slain"))
            return MsgCombat;
        if (ContainsAny(msg, "pick", "found", "loot", "chest", "gold"))
            return MsgPickup;
        if (ContainsAny(msg, "enter", "exit", "dungeon", "overworld", "regen", "nothing"))
            return MsgSystem;
        return MsgDefault;
    }

    private static bool ContainsAny(string s, params string[] tokens)
    {
        foreach (var t in tokens)
            if (s.Contains(t, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private void UpdateInventory(Inventory inventory)
    {
        _inventoryList.Widgets.Clear();
        _inventoryTitle.Text = $"INVENTORY  ({inventory.SlotCount})";

        if (inventory.SlotCount == 0)
        {
            _inventoryList.Widgets.Add(new Label { Text = "(empty)", TextColor = TextDim });
            return;
        }

        // Cycle through a set of accent colours so every item feels distinct
        Color[] swatchColors =
        {
            new(255, 154, 106, 255), // orange
            new(106, 255, 202, 255), // teal
            new(255, 224, 106, 255), // gold
            new(192, 106, 255, 255), // purple
            new(106, 186, 255, 255), // blue
            new(255, 106, 138, 255), // pink
        };

        int idx = 0;
        foreach (var item in inventory.Items)
        {
            Color swatch = swatchColors[idx % swatchColors.Length];
            idx++;

            var row = new HorizontalStackPanel
            {
                Spacing = 8,
                Padding = new Thickness(4, 2, 4, 2),
            };

            // Colour dot using a filled-square character
            row.Widgets.Add(new Label { Text = "■", TextColor = swatch });

            // Item name
            row.Widgets.Add(new Label
            {
                Text = item.Def.Name,
                TextColor = TextPrimary,
            });

            // Stack count badge (only if stackable and > 1)
            if (item.Def.Stackable && item.Count > 1)
            {
                row.Widgets.Add(new Label
                {
                    Text = $"×{item.Count}",
                    TextColor = Gold,
                });
            }

            _inventoryList.Widgets.Add(row);
        }
    }
}