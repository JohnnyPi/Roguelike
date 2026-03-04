// src/Game.Client/UI/HudManager.cs
//
// Myra-based HUD overlay for the roguelike.
// Draws on top of the tile renderer's SpriteBatch output.
//
// Components:
//   - Top-left:    HP bar + label
//   - Top-right:   Mode indicator (Overworld / Dungeon) + coordinates
//   - Bottom:      Message log (last N messages, auto-scrolling)
//   - Center-left: Inventory panel (toggled with I key)
//
// Usage in Game1:
//   LoadContent:  MyraEnvironment.Game = this;
//                 _hud = new HudManager();
//   Update:       _hud.Update(gameTime, _state);
//   Draw (after SpriteBatch.End):  _hud.Render();
//
// The HUD uses a Myra Desktop that sits above the game rendering.
// It does NOT consume keyboard input — Myra's focus system is not
// used for gameplay keys (WASD, E, etc.). The inventory panel is
// toggled externally by calling ToggleInventory().

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Myra;
using Myra.Graphics2D;
using Myra.Graphics2D.Brushes;
using Myra.Graphics2D.UI;
using Game.Core;
using Game.Core.Entities;
using Game.Core.Items;

#nullable enable

namespace Game.Client.UI;

public sealed class HudManager
{
    private readonly Desktop _desktop;

    // ── Top-left: HP bar ──────────────────────────────────────────
    private readonly Label _hpLabel;
    private readonly HorizontalProgressBar _hpBar;

    // ── Top-right: Mode + coords ──────────────────────────────────
    private readonly Label _modeLabel;
    private readonly Label _coordsLabel;

    // ── Bottom: Message log ───────────────────────────────────────
    private readonly VerticalStackPanel _logPanel;
    private readonly ScrollViewer _logScroll;
    private readonly List<Label> _logLabels = new();
    private const int MaxVisibleMessages = 8;

    // ── Center-left: Inventory ────────────────────────────────────
    private readonly Panel _inventoryPanel;
    private readonly VerticalStackPanel _inventoryList;
    private readonly Label _inventoryTitle;
    private bool _inventoryVisible;

    // ── Bottom-left: Key hints ────────────────────────────────────
    private readonly Label _hintsLabel;

    // Track how many messages we've rendered so we only update on change
    private int _lastLogCount;

    public HudManager()
    {
        _desktop = new Desktop();

        // ════════════════════════════════════════════════════════════
        //  Root panel: full-screen container with no background
        // ════════════════════════════════════════════════════════════
        var root = new Panel();

        // ────────────────────────────────────────────────────────────
        //  TOP-LEFT: HP bar group
        // ────────────────────────────────────────────────────────────
        var hpGroup = new VerticalStackPanel
        {
            Left = 8,
            Top = 8,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Spacing = 2
        };

        _hpLabel = new Label
        {
            Text = "HP: 30 / 30",
            TextColor = Color.White
        };

        _hpBar = new HorizontalProgressBar
        {
            Width = 180,
            Height = 14,
            Minimum = 0,
            Maximum = 100,
            Value = 100
        };
        // Color the bar fill — Myra uses a Brush for the filler
        _hpBar.Filler = new SolidBrush(Color.Red);

        hpGroup.Widgets.Add(_hpLabel);
        hpGroup.Widgets.Add(_hpBar);
        root.Widgets.Add(hpGroup);

        // ────────────────────────────────────────────────────────────
        //  TOP-RIGHT: Mode indicator + coordinates
        // ────────────────────────────────────────────────────────────
        var topRight = new VerticalStackPanel
        {
            Margin = new Thickness(0, 8, 8, 0),
            Top = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Spacing = 2
        };

        _modeLabel = new Label
        {
            Text = "OVERWORLD",
            TextColor = new Color(120, 200, 255)
        };

        _coordsLabel = new Label
        {
            Text = "(0, 0)",
            TextColor = new Color(180, 180, 180)
        };

        topRight.Widgets.Add(_modeLabel);
        topRight.Widgets.Add(_coordsLabel);
        root.Widgets.Add(topRight);

        // ────────────────────────────────────────────────────────────
        //  BOTTOM: Message log
        // ────────────────────────────────────────────────────────────
        _logPanel = new VerticalStackPanel
        {
            Spacing = 1
        };

        _logScroll = new ScrollViewer
        {
            Margin = new Thickness(8, 0, 0, 8),
            Width = 500,
            Height = 140,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = new SolidBrush(new Color(0, 0, 0, 160)),
            Content = _logPanel,
            ShowHorizontalScrollBar = false,
            ShowVerticalScrollBar = false
        };
        root.Widgets.Add(_logScroll);

        // ────────────────────────────────────────────────────────────
        //  CENTER-LEFT: Inventory panel (hidden by default)
        // ────────────────────────────────────────────────────────────
        _inventoryTitle = new Label
        {
            Text = "══ Inventory ══",
            TextColor = new Color(255, 220, 100)
        };

        _inventoryList = new VerticalStackPanel
        {
            Spacing = 2
        };

        var inventoryInner = new VerticalStackPanel
        {
            Spacing = 4,
            Padding = new Thickness(8)
        };
        inventoryInner.Widgets.Add(_inventoryTitle);
        inventoryInner.Widgets.Add(_inventoryList);

        _inventoryPanel = new Panel
        {
            Left = 8,
            Top = 60,
            Width = 220,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidBrush(new Color(0, 0, 0, 200)),
            Visible = false
        };
        _inventoryPanel.Widgets.Add(inventoryInner);
        root.Widgets.Add(_inventoryPanel);

        // ────────────────────────────────────────────────────────────
        //  BOTTOM-RIGHT: Key hints
        // ────────────────────────────────────────────────────────────
        _hintsLabel = new Label
        {
            Margin = new Thickness(0, 0, 8, 8),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Text = "WASD:Move  E:Interact  I:Inventory  R:Regen  Esc:Quit",
            TextColor = new Color(120, 120, 120)
        };
        root.Widgets.Add(_hintsLabel);

        _desktop.Root = root;
    }

    // ════════════════════════════════════════════════════════════════
    //  Public API
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Toggle inventory panel visibility. Called from Game1 when I is pressed.
    /// </summary>
    public void ToggleInventory()
    {
        _inventoryVisible = !_inventoryVisible;
        _inventoryPanel.Visible = _inventoryVisible;
    }

    /// <summary>Whether the inventory panel is currently shown.</summary>
    public bool IsInventoryVisible => _inventoryVisible;

    /// <summary>
    /// Update HUD contents from game state. Call once per frame in Update().
    /// Cheap: only rebuilds widgets when data actually changes.
    /// </summary>
    public void Update(GameTime gameTime, GameState state)
    {
        if (state?.Player == null) return;

        UpdateHpBar(state.Player);
        UpdateModeLabel(state);
        UpdateCoords(state.Player);
        UpdateMessageLog(state);

        if (_inventoryVisible)
            UpdateInventory(state.Player.Inventory);
    }

    /// <summary>
    /// Render the HUD overlay. Call AFTER SpriteBatch.End() in Draw().
    /// Myra manages its own SpriteBatch internally.
    /// </summary>
    public void Render()
    {
        _desktop.Render();
    }

    // ════════════════════════════════════════════════════════════════
    //  Internal update methods
    // ════════════════════════════════════════════════════════════════

    private void UpdateHpBar(Player player)
    {
        _hpLabel.Text = $"HP: {player.Hp} / {player.MaxHp}";

        float pct = player.MaxHp > 0
            ? (float)player.Hp / player.MaxHp * 100f
            : 0f;
        _hpBar.Value = pct;

        // Color shifts: green > 60%, yellow > 30%, red below
        if (pct > 60f)
            _hpBar.Filler = new SolidBrush(new Color(50, 200, 50));
        else if (pct > 30f)
            _hpBar.Filler = new SolidBrush(new Color(220, 200, 50));
        else
            _hpBar.Filler = new SolidBrush(new Color(220, 50, 50));
    }

    private void UpdateModeLabel(GameState state)
    {
        switch (state.Mode)
        {
            case GameMode.Overworld:
                _modeLabel.Text = "OVERWORLD";
                _modeLabel.TextColor = new Color(120, 200, 255);
                break;
            case GameMode.Dungeon:
                _modeLabel.Text = "DUNGEON";
                _modeLabel.TextColor = new Color(255, 140, 80);
                break;
        }
    }

    private void UpdateCoords(Player player)
    {
        _coordsLabel.Text = $"({player.X}, {player.Y})";
    }

    private void UpdateMessageLog(GameState state)
    {
        // Only rebuild if the log has grown
        if (state.MessageLog.Count == _lastLogCount) return;
        _lastLogCount = state.MessageLog.Count;

        _logPanel.Widgets.Clear();
        _logLabels.Clear();

        // Show the last N messages
        int start = Math.Max(0, state.MessageLog.Count - MaxVisibleMessages);
        for (int i = start; i < state.MessageLog.Count; i++)
        {
            // Fade older messages slightly
            float age = (float)(i - start) / MaxVisibleMessages;
            byte alpha = (byte)(140 + (int)(115 * age)); // 140..255

            var label = new Label
            {
                Text = state.MessageLog[i],
                TextColor = new Color((byte)220, (byte)220, (byte)220, alpha),
                Wrap = true
            };

            _logLabels.Add(label);
            _logPanel.Widgets.Add(label);
        }

        // Auto-scroll to bottom
        _logScroll.ScrollPosition = new Point(0, int.MaxValue / 2);
    }

    private void UpdateInventory(Inventory inventory)
    {
        _inventoryList.Widgets.Clear();

        if (inventory.SlotCount == 0)
        {
            _inventoryList.Widgets.Add(new Label
            {
                Text = "(empty)",
                TextColor = new Color(150, 150, 150)
            });
            _inventoryTitle.Text = "══ Inventory (0) ══";
            return;
        }

        _inventoryTitle.Text = $"══ Inventory ({inventory.SlotCount}) ══";

        foreach (var item in inventory.Items)
        {
            string text = item.Def.Stackable && item.Count > 1
                ? $"  {item.Def.Name} x{item.Count}"
                : $"  {item.Def.Name}";

            // Parse the item color for a visual indicator
            var itemColor = Rendering.TileRenderer.ParseHexColor(item.Def.Color);

            var row = new HorizontalStackPanel { Spacing = 4 };

            // Small color swatch (using a Label with a colored block char)
            row.Widgets.Add(new Label
            {
                Text = "\u25A0", // ■ filled square
                TextColor = itemColor
            });

            row.Widgets.Add(new Label
            {
                Text = text,
                TextColor = Color.White
            });

            _inventoryList.Widgets.Add(row);
        }
    }
}