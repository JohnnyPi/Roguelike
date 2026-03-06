// src/Game.ProcGen/Generators/OverworldGenerator.Features.cs
//
// Lightweight world-feature extraction for chunk-streaming startup.
//
// GenerateFeatureSets() runs the full noise + volcano + river pipeline without
// allocating or baking a TileMap. It returns WorldFeatureSets -- the HashSets of
// world positions that ChunkManager needs to stamp cross-chunk features consistently.
//
// This eliminates the double full-map generation that was needed when Generate()
// was called just to scan tile IDs for feature positions.

using System;
using System.Collections.Generic;
using Game.Core.Biomes;

namespace Game.ProcGen.Generators;

public partial class OverworldGenerator
{
    // ── Public API ────────────────────────────────────────────────────

    /// <summary>
    /// Lightweight overworld feature extraction for chunk-streaming startup.
    ///
    /// Runs the same noise + volcano + river pipeline as Generate() but does NOT
    /// allocate a TileMap or bake terrain shading. Returns only the data that
    /// ChunkManager needs for cross-chunk consistency:
    ///   - River tile positions
    ///   - Lava/crater tile positions
    ///   - Volcano peak positions
    ///   - Dungeon entrance positions
    ///   - Player spawn position
    ///
    /// Call this once at world-init; pass the returned sets to every GenerateChunk call.
    /// This eliminates the double full-map generation that was needed when using a
    /// TileMap just to scan for feature tile IDs.
    /// </summary>
    public WorldFeatureSets GenerateFeatureSets(int seed, IReadOnlyList<BiomeDef>? biomes = null)
    {
        var rng = new Random(seed);

        var elevNoise = ConfigureElevationNoise(seed);
        var warpNoise = ConfigureWarpNoise(seed + 7919);
        var moistNoise = ConfigureMoistureNoise(seed + 31337);

        float[] maskedElevation = BuildMaskedElevation(elevNoise, warpNoise, rng);
        var (lavaOverlay, craterOverlay) = StampVolcanoesWithOverlays(maskedElevation, rng);

        // Build river mask using same logic as CarveRivers but into a set
        var riverSet = new HashSet<(int, int)>();
        BuildRiverSet(maskedElevation, rng, riverSet);

        var lavaSet = new HashSet<(int, int)>();
        var craterSet = new HashSet<(int, int)>();
        for (int y = 0; y < MapHeight; y++)
        {
            for (int x = 0; x < MapWidth; x++)
            {
                int i = y * MapWidth + x;
                if (craterOverlay[i]) craterSet.Add((x, y));
                else if (lavaOverlay[i]) lavaSet.Add((x, y));
            }
        }

        // Find spawn (same algorithm as FinishGeneration -> FindWalkableNearCenter)
        var spawnPos = FindWalkableNearCenterFromArrays(maskedElevation, biomes);

        // Place entrances (same rules as PlaceDungeonEntrances)
        var entranceList = PlaceDungeonEntrancesFromArrays(maskedElevation, biomes, spawnPos, rng);
        var entranceSet = new HashSet<(int, int)>(entranceList);

        // SpawnPosition and EntrancePositions are set as side effects of the volcano pass above
        SpawnPosition = spawnPos;
        EntrancePositions = entranceList;

        return new WorldFeatureSets(
            riverSet, lavaSet, craterSet,
            new HashSet<(int, int)>(VolcanoCenters),
            entranceSet, spawnPos, entranceList);
    }

    // ── Result type ───────────────────────────────────────────────────

    /// <summary>Result of GenerateFeatureSets -- all cross-chunk feature position sets.</summary>
    public sealed class WorldFeatureSets
    {
        public HashSet<(int, int)> RiverPositions { get; }
        public HashSet<(int, int)> LavaPositions { get; }
        public HashSet<(int, int)> CraterPositions { get; }
        public HashSet<(int, int)> VolcanoPositions { get; }
        public HashSet<(int, int)> EntrancePositions { get; }
        public (int X, int Y) SpawnPosition { get; }
        public List<(int X, int Y)> EntranceList { get; }

        public WorldFeatureSets(
            HashSet<(int, int)> rivers, HashSet<(int, int)> lava,
            HashSet<(int, int)> craters, HashSet<(int, int)> volcanoes,
            HashSet<(int, int)> entrances, (int X, int Y) spawn,
            List<(int X, int Y)> entranceList)
        {
            RiverPositions = rivers;
            LavaPositions = lava;
            CraterPositions = craters;
            VolcanoPositions = volcanoes;
            EntrancePositions = entrances;
            SpawnPosition = spawn;
            EntranceList = entranceList;
        }
    }
}