// src/Game.ProcGen/Generators/DungeonGenerator.Population.cs

using System;
using System.Collections.Generic;
using Game.Core.Entities;
using Game.Core.Items;
using Game.Core.Lighting;
using Game.Core.Map;
using Game.Core.Monsters;

namespace Game.ProcGen.Generators;

public partial class DungeonGenerator
{
    // -- Item and chest placement -------------------------------------

    private void PlaceItemsAndChests(TileMap map, Random rng, List<ItemDef> itemDefs)
    {
        for (int r = 0; r < Rooms.Count; r++)
        {
            var room = Rooms[r];

            var validPositions = new List<(int X, int Y)>();
            for (int y = room.Y; y < room.Y + room.Height; y++)
                for (int x = room.X; x < room.X + room.Width; x++)
                {
                    var id = map.GetTileId(x, y);
                    if (id == FloorTile)
                        validPositions.Add((x, y));
                }

            if (validPositions.Count == 0) continue;

            // Place 0-2 ground items
            int itemCount = rng.Next(0, 3);
            for (int i = 0; i < itemCount && validPositions.Count > 0; i++)
            {
                int posIdx = rng.Next(validPositions.Count);
                var pos = validPositions[posIdx];
                validPositions.RemoveAt(posIdx);

                var def = itemDefs[rng.Next(itemDefs.Count)];
                int count = def.Stackable ? rng.Next(1, Math.Min(4, def.MaxStack + 1)) : 1;

                var worldItem = new WorldItem(def, count);
                worldItem.SetPosition(pos.X, pos.Y);
                SpawnedEntities.Add(worldItem);
            }

            // 40% chance of a chest
            if (rng.NextDouble() < 0.4 && validPositions.Count > 0)
            {
                int posIdx = rng.Next(validPositions.Count);
                var pos = validPositions[posIdx];
                validPositions.RemoveAt(posIdx);

                var lootDef = itemDefs[rng.Next(itemDefs.Count)];
                int lootCount = lootDef.Stackable ? rng.Next(1, Math.Min(6, lootDef.MaxStack + 1)) : 1;

                var chest = new Chest(lootDef, lootCount);
                chest.SetPosition(pos.X, pos.Y);
                SpawnedEntities.Add(chest);
            }
        }
    }

    // -- Enemy placement ----------------------------------------------

    /// <summary>
    /// Place enemies throughout the dungeon rooms.
    /// Skips the entrance room (room 0) to give the player a safe start.
    /// Each eligible room gets 1-2 enemies chosen randomly from available defs.
    /// Enemies are placed on floor tiles not occupied by other entities.
    /// </summary>
    private void PlaceEnemies(TileMap map, Random rng, List<MonsterDef> monsterDefs)
    {
        var occupiedPositions = new HashSet<(int, int)>();
        foreach (var entity in SpawnedEntities)
            occupiedPositions.Add((entity.X, entity.Y));

        occupiedPositions.Add(EntrancePosition);
        occupiedPositions.Add(ExitPosition);

        for (int r = 1; r < Rooms.Count; r++) // skip room 0 (entrance)
        {
            var room = Rooms[r];

            var validPositions = new List<(int X, int Y)>();
            for (int y = room.Y; y < room.Y + room.Height; y++)
                for (int x = room.X; x < room.X + room.Width; x++)
                {
                    var id = map.GetTileId(x, y);
                    if (id == FloorTile && !occupiedPositions.Contains((x, y)))
                        validPositions.Add((x, y));
                }

            if (validPositions.Count == 0) continue;

            // 1-2 enemies per room (70% chance of 1, 30% chance of 2)
            int enemyCount = rng.NextDouble() < 0.30 ? 2 : 1;

            for (int i = 0; i < enemyCount && validPositions.Count > 0; i++)
            {
                int posIdx = rng.Next(validPositions.Count);
                var pos = validPositions[posIdx];
                validPositions.RemoveAt(posIdx);

                var def = monsterDefs[rng.Next(monsterDefs.Count)];
                var enemy = new Enemy(def);
                enemy.SetPosition(pos.X, pos.Y);
                SpawnedEntities.Add(enemy);

                occupiedPositions.Add((pos.X, pos.Y));
            }
        }
    }

    // -- Torch placement ----------------------------------------------

    /// <summary>
    /// Place torch light sources throughout the dungeon based on LightingConfig.
    ///
    /// Strategy:
    ///   - Room 0 (entrance) always gets a torch so the player is never
    ///     dropped into total darkness.
    ///   - Each subsequent room gets one torch with probability TorchesPerRoom
    ///     (clamped to [0, 1] for a simple roll). Values > 1.0 guarantee one
    ///     torch and give a (value - 1) chance of a second in large rooms.
    ///   - Torches are placed at room centers, shifted by 1 tile when the
    ///     center is occupied by the entrance or exit marker.
    /// </summary>
    private void PlaceTorches(Random rng)
    {
        float torchChance = LightingConfig.TorchesPerRoom;
        float radius = LightingConfig.TorchRadius;

        for (int i = 0; i < Rooms.Count; i++)
        {
            var room = Rooms[i];

            // Entrance room always gets a torch; others are probabilistic
            bool placeTorch = (i == 0) || (rng.NextDouble() < torchChance);
            if (!placeTorch) continue;

            // Large rooms with high torch density may get two torches
            int torchCount = 1;
            if (room.Width >= 8 && room.Height >= 8 && torchChance >= 1.5f)
                torchCount = 2;

            foreach (var (tx, ty) in GetTorchPositions(room, torchCount))
                LightSources.Add(LightSource.Torch(tx, ty, radius));
        }
    }

    /// <summary>
    /// Compute torch positions within a room.
    ///
    /// count == 1 : room center, nudged by +1 X if it lands on entrance/exit.
    /// count == 2 : opposing third-points of the room for even coverage.
    /// </summary>
    private List<(int X, int Y)> GetTorchPositions(Room room, int count)
    {
        var positions = new List<(int X, int Y)>(count);

        if (count == 1)
        {
            var (cx, cy) = room.Center;

            // Shift off entrance/exit tile so the torch doesn't visually overlap
            if ((cx == EntrancePosition.X && cy == EntrancePosition.Y) ||
                (cx == ExitPosition.X && cy == ExitPosition.Y))
            {
                cx += 1;
            }

            positions.Add((cx, cy));
        }
        else
        {
            // Two torches: upper-left third and lower-right third of the room
            positions.Add((room.X + room.Width / 3, room.Y + room.Height / 3));
            positions.Add((room.X + room.Width * 2 / 3, room.Y + room.Height * 2 / 3));
        }

        return positions;
    }
}