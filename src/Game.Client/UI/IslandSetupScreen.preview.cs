// src/Game.Client/UI/IslandSetupScreen.Preview.cs

using Game.Client.Rendering;
using Game.Core.Map;
using Game.Core.Tiles;
using Game.ProcGen.Generators;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

#nullable enable

namespace Game.Client.UI;

public sealed partial class IslandSetupScreen
{
    // -----------------------------------------------------------------
    // Preview generation
    // -----------------------------------------------------------------

    private void RebuildPreview()
    {
        var cfg = BuildConfig();

        int sz = PreviewSize;
        // Preserve aspect ratio so wide chain maps don't look squished in the preview.
        float aspect = (float)cfg.MapWidth / cfg.MapHeight;
        int previewW = sz;
        int previewH = Math.Max(1, (int)(sz / aspect));
        if (previewH > sz) { previewH = sz; previewW = Math.Max(1, (int)(sz * aspect)); }

        float freqScale = (float)previewW / cfg.MapWidth;

        var previewGen = OverworldGenerator.FromConfig(cfg with
        {
            MapWidth = previewW,
            MapHeight = previewH,
            Frequency = cfg.Frequency * freqScale,
            EntranceCount = 0,
            VolcanoBaseRadius = Math.Max(4, (int)(cfg.VolcanoBaseRadius * freqScale)),
        });

        TileMap previewMap;
        if (_biomes.Count > 0)
        {
            var tileDict = new Dictionary<string, TileDef>(_tiles);
            previewMap = previewGen.Generate(tileDict, _seed, _biomes);
        }
        else
        {
            var tileDict = new Dictionary<string, TileDef>(_tiles);
            previewMap = previewGen.Generate(tileDict, _seed);
        }

        // Sample tile colors into pixel array (centered in 256x256 texture)
        var pixels = new Color[sz * sz];
        int offsetX = (sz - previewW) / 2;
        int offsetY = (sz - previewH) / 2;
        for (int y = 0; y < previewH; y++)
        {
            for (int x = 0; x < previewW; x++)
            {
                var tile = previewMap.GetTile(x, y);
                pixels[(y + offsetY) * sz + (x + offsetX)] = tile != null
                    ? GetCachedTileColor(tile)
                    : Color.Black;
            }
        }

        // Mark spawn and entrances (shift coordinates by preview offset)
        var spawnColor = new Color(0, 255, 255, 255);
        MarkDot(pixels, sz, previewGen.SpawnPosition.X + offsetX, previewGen.SpawnPosition.Y + offsetY,
                spawnColor, 3);
        foreach (var ep in previewGen.EntrancePositions)
            MarkDot(pixels, sz, ep.X + offsetX, ep.Y + offsetY, new Color(255, 60, 60, 255), 3);
        foreach (var vp in previewGen.VolcanoCenters)
            MarkDot(pixels, sz, vp.X + offsetX, vp.Y + offsetY, new Color(255, 120, 30, 255), 2);

        _previewTex.SetData(pixels);
    }

    private static void MarkDot(Color[] pixels, int sz, int cx, int cy, Color c, int r)
    {
        for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
                if (dx * dx + dy * dy <= r * r)
                {
                    int px = cx + dx, py = cy + dy;
                    if (px >= 0 && px < sz && py >= 0 && py < sz)
                        pixels[py * sz + px] = c;
                }
    }

    private Color GetCachedTileColor(TileDef tile)
    {
        if (_colorCache.TryGetValue(tile.Id, out var c)) return c;
        c = TileRenderer.ParseHexColor(tile.Color);
        _colorCache[tile.Id] = c;
        return c;
    }
}