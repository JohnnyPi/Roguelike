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

        // Generate at preview resolution (fast -- no entity spawning, no TileMap)
        // We use a small OverworldGenerator configured to PreviewSize x PreviewSize
        // then sample its raw elevation+moisture to pick tile colors without
        // allocating a full TileMap. Since OverworldGenerator.Generate() is the
        // only public surface, we generate at reduced size and scale frequency.
        int sz = PreviewSize;
        float freqScale = (float)sz / cfg.MapWidth;

        var previewGen = OverworldGenerator.FromConfig(cfg with
        {
            MapWidth = sz,
            MapHeight = sz,
            Frequency = cfg.Frequency * freqScale,
            EntranceCount = 0,          // no entrance placement needed
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

        // Sample tile colors into pixel array
        var pixels = new Color[sz * sz];
        for (int y = 0; y < sz; y++)
        {
            for (int x = 0; x < sz; x++)
            {
                var tile = previewMap.GetTile(x, y);
                pixels[y * sz + x] = tile != null
                    ? GetCachedTileColor(tile)
                    : Color.Black;
            }
        }

        // Mark spawn and entrances
        var spawnColor = new Color(0, 255, 255, 255);
        MarkDot(pixels, sz, previewGen.SpawnPosition.X, previewGen.SpawnPosition.Y,
                spawnColor, 3);
        foreach (var ep in previewGen.EntrancePositions)
            MarkDot(pixels, sz, ep.X, ep.Y, new Color(255, 60, 60, 255), 3);
        foreach (var vp in previewGen.VolcanoCenters)
            MarkDot(pixels, sz, vp.X, vp.Y, new Color(255, 120, 30, 255), 2);

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