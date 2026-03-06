// src/Game.Client/UI/IslandSetupScreen.Draw.cs

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

#nullable enable

namespace Game.Client.UI;

public sealed partial class IslandSetupScreen
{
    // -----------------------------------------------------------------
    // Draw
    // -----------------------------------------------------------------

    public void Draw()
    {
        _sb.Begin(samplerState: SamplerState.PointClamp);

        // -- Background -----------------------------------------------
        Fill(new Rectangle(0, 0, _vpW, _vpH), BgDark);

        // -- Title bar ------------------------------------------------
        Fill(new Rectangle(0, 0, _vpW, 55), new Color(10, 16, 28, 255));
        DrawBorder(new Rectangle(0, 0, _vpW, 55), BorderColor, 1);
        DrawText("ISLAND GENERATOR", new Vector2(_vpW / 2 - 100, 14), TitleColor);
        DrawText($"Seed: {_seed}", new Vector2(_vpW / 2 + 80, 16), TextDim, scale: 0.85f);

        // -- Preview panel --------------------------------------------
        var outerPrev = _previewRect; outerPrev.Inflate(4, 4);
        Fill(outerPrev, new Color(10, 16, 28, 240));
        DrawBorder(outerPrev, BorderColor, 1);
        _sb.Draw(_previewTex, _previewRect, Color.White);

        // -- Slider panel ---------------------------------------------
        Fill(_panelRect, PanelBg);
        DrawBorder(_panelRect, BorderColor, 1);

        int labelX = _panelRect.X + 8;

        for (int i = 0; i < _sliders.Count; i++)
        {
            var s = _sliders[i];
            var t = s.TrackRect;
            int labelY = t.Y + t.Height / 2 - 7;

            // Label
            DrawText(s.Label, new Vector2(labelX, labelY), TextColor, scale: 0.8f);

            // Track background
            Fill(t, SliderTrack);
            DrawBorder(t, new Color(40, 60, 90, 180), 1);

            // Fill
            int fillW = (int)(t.Width * s.Normalized);
            if (fillW > 0)
                Fill(new Rectangle(t.X, t.Y, fillW, t.Height), SliderFill);

            // Thumb
            var thumb = GetThumbRect(s);
            Fill(thumb, s.Dragging ? Color.White : SliderThumb);
            DrawBorder(thumb, new Color(200, 230, 255, 200), 1);

            // Value label
            DrawText(s.FormatValue(),
                     new Vector2(t.Right + 6, labelY),
                     TextColor, scale: 0.78f);
        }

        // -- Buttons --------------------------------------------------
        DrawButton(_btnRegen, "REGENERATE", _hoveredBtn == 0 ? BtnHover : BtnNormal);
        DrawButton(_btnRandom, "RANDOMIZE", _hoveredBtn == 1 ? BtnHover : BtnNormal);
        DrawButton(_btnStart, "START GAME >>",
                   _hoveredBtn == 2 ? BtnStartHov : BtnStart,
                   textColor: new Color(180, 255, 200, 255));

        // -- Hint -----------------------------------------------------
        DrawText("Enter = Start  |  R = Randomize  |  Scroll to adjust sliders",
                 new Vector2(_vpW / 2 - 200, _vpH - 20), TextDim, scale: 0.75f);

        _sb.End();
    }

    // -----------------------------------------------------------------
    // Drawing helpers
    // -----------------------------------------------------------------

    private static Rectangle GetThumbRect(SliderDef s)
    {
        var t = s.TrackRect;
        int cx = t.X + (int)(t.Width * s.Normalized);
        return new Rectangle(cx - 6, t.Y - 4, 12, t.Height + 8);
    }

    private void Fill(Rectangle r, Color c)
        => _sb.Draw(_pixel, r, c);

    private void DrawBorder(Rectangle r, Color c, int thickness)
    {
        _sb.Draw(_pixel, new Rectangle(r.X, r.Y, r.Width, thickness), c);
        _sb.Draw(_pixel, new Rectangle(r.X, r.Bottom - thickness, r.Width, thickness), c);
        _sb.Draw(_pixel, new Rectangle(r.X, r.Y, thickness, r.Height), c);
        _sb.Draw(_pixel, new Rectangle(r.Right - thickness, r.Y, thickness, r.Height), c);
    }

    private void DrawButton(Rectangle r, string label, Color bg,
                            Color? textColor = null)
    {
        Fill(r, bg);
        DrawBorder(r, new Color(80, 130, 190, 200), 1);
        var tc = textColor ?? TextColor;
        // Center text
        int approxW = label.Length * 7;
        DrawText(label, new Vector2(r.X + (r.Width - approxW) / 2,
                                    r.Y + (r.Height - 14) / 2), tc, scale: 0.85f);
    }

    /// <summary>
    /// Draws text using the SpriteFont if available, otherwise falls back
    /// to a simple pixel-letter approach for critical labels.
    /// </summary>
    private void DrawText(string text, Vector2 pos, Color color, float scale = 1.0f)
    {
        if (_font == null) return;   // font not loaded -- skip text
        _sb.DrawString(_font, text, pos, color, 0f, Vector2.Zero,
                       scale, SpriteEffects.None, 0f);
    }
}