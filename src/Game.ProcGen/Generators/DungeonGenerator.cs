// src/Game.ProcGen/Generators/DungeonGenerator.cs

using System;
using System.Collections.Generic;
using Game.Core.Entities;
using Game.Core.Items;
using Game.Core.Lighting;
using Game.Core.Map;
using Game.Core.Monsters;
using Game.Core.Tiles;

namespace Game.ProcGen.Generators;

/// <summary>
/// Rooms-and-corridors dungeon generator.
///
/// Algorithm:
///   1. Fill grid with walls
///   2. Place 6–12 non-overlapping rectangular rooms (5–10 tiles each side)
///   3. Connect rooms with L-shaped corridors
///   4. Flood-fill to verify all floor tiles are reachable
///   5. Mark entrance/exit tiles
///   6. Place items, chests, and enemies
///   7. Place torch light sources (driven by LightingConfig)
/// </summary>
public class DungeonGenerator
{
    // ── Configuration ────────────────────────────────────────────────

    public int MapWidth { get; init; } = 60;
    public int MapHeight { get; init; } = 40;
    public int MinRooms { get; init; } = 6;
    public int MaxRooms { get; init; } = 12;
    public int RoomMinSize { get; init; } = 5;
    public int RoomMaxSize { get; init; } = 10;
    public int MaxPlacementAttempts { get; init; } = 200;

    /// <summary>
    /// Lighting configuration for this dungeon instance.
    /// Controls ambient darkness, torch radius, torches-per-room density,
    /// and the player FOV radius while inside.
    /// Defaults to the cave preset; set before calling Generate() to override.
    /// </summary>
    public DungeonLightingConfig LightingConfig { get; set; } = DungeonLightingConfig.Cave;

    // ── Tile IDs ──────────────────────────────────────────────────────

    private const string WallTile = "base:wall";
    private const string FloorTile = "base:floor";
    private const string EntranceTile = "base:dungeon_entrance";
    private const string ExitTile = "base:dungeon_exit";

    // ── Output data ───────────────────────────────────────────────────

    /// <summary>The rooms that were successfully placed.</summary>
    public List<Room> Rooms { get; private set; } = new();

    /// <summary>Grid position of the dungeon entrance (first room center).</summary>
    public (int X, int Y) EntrancePosition { get; private set; }

    /// <summary>Grid position of the dungeon exit (last room center).</summary>
    public (int X, int Y) ExitPosition { get; private set; }

    /// <summary>
    /// Entities spawned during generation (WorldItems, Chests, Enemies).
    /// Game1 should add these to GameState.Entities after generating.
    /// </summary>
    public List<Entity> SpawnedEntities { get; private set; } = new();

    /// <summary>
    /// Torch light sources placed during generation.
    /// Game1 should copy these into GameState.LightSources after generating.
    /// </summary>
    public List<LightSource> LightSources { get; private set; } = new();

    // ── Room helper struct ────────────────────────────────────────────

    public readonly struct Room
    {
        public int X { get; }
        public int Y { get; }
        public int Width { get; }
        public int Height { get; }

        public (int X, int Y) Center => (X + Width / 2, Y + Height / 2);

        public Room(int x, int y, int width, int height)
        {
            X = x; Y = y; Width = width; Height = height;
        }

        public bool Overlaps(Room other, int padding = 1)
        {
            return X - padding < other.X + other.Width
                && X + Width + padding > other.X
                && Y - padding < other.Y + other.Height
                && Y + Height + padding > other.Y;
        }
    }

    // ── Main generation method ────────────────────────────────────────

    /// <summary>
    /// Generate a complete dungeon TileMap.
    /// </summary>
    /// <param name="tileRegistry">Tile definitions (needed by TileMap constructor).</param>
    /// <param name="seed">Random seed for reproducible generation.</param>
    /// <param name="itemDefs">Available item definitions for loot placement.</param>
    /// <param name="monsterDefs">Available monster definitions for enemy spawning.</param>
    public TileMap Generate(Dictionary<string, TileDef> tileRegistry, int? seed = null,
        List<ItemDef>? itemDefs = null, List<MonsterDef>? monsterDefs = null)
    {
        var rng = seed.HasValue ? new Random(seed.Value) : new Random();
        var map = new TileMap(MapWidth, MapHeight, tileRegistry);

        // Step 1: Fill entire grid with walls
        map.Fill(WallTile);

        // Step 2: Place rooms
        Rooms = PlaceRooms(rng);

        // Step 3: Carve rooms into the map
        foreach (var room in Rooms)
            CarveRoom(map, room);

        // Step 4: Connect rooms with L-shaped corridors
        ConnectRooms(map, rng);

        // Step 5: Verify connectivity
        EnsureConnectivity(map, rng);

        // Step 6: Mark the entrance in the first room's center
        var entrance = Rooms[0].Center;
        EntrancePosition = entrance;
        map.SetTile(entrance.X, entrance.Y, EntranceTile);

        // Step 7: Mark the exit in the last room's center
        var exitRoom = Rooms[Rooms.Count - 1];
        var exit = exitRoom.Center;
        if (Rooms.Count == 1)
            exit = (exitRoom.X + 1, exitRoom.Y + 1);
        ExitPosition = exit;
        map.SetTile(exit.X, exit.Y, ExitTile);

        // Step 8: Place items, chests, and enemies
        SpawnedEntities = new List<Entity>();
        if (itemDefs != null && itemDefs.Count > 0)
            PlaceItemsAndChests(map, rng, itemDefs);
        if (monsterDefs != null && monsterDefs.Count > 0)
            PlaceEnemies(map, rng, monsterDefs);

        // Step 9: Place torch light sources
        LightSources = new List<LightSource>();
        PlaceTorches(rng);

        return map;
    }

    // ── Backwards-compatible overload (no monsters) ───────────────────

    /// <summary>Legacy overload without monster defs. Calls the full version with null monsters.</summary>
    public TileMap Generate(Dictionary<string, TileDef> tileRegistry, int? seed,
        List<ItemDef> itemDefs)
    {
        return Generate(tileRegistry, seed, itemDefs, null);
    }

    // ── Room placement ────────────────────────────────────────────────

    private List<Room> PlaceRooms(Random rng)
    {
        int targetRooms = rng.Next(MinRooms, MaxRooms + 1);
        var rooms = new List<Room>();

        for (int attempt = 0; attempt < MaxPlacementAttempts && rooms.Count < targetRooms; attempt++)
        {
            int w = rng.Next(RoomMinSize, RoomMaxSize + 1);
            int h = rng.Next(RoomMinSize, RoomMaxSize + 1);
            int x = rng.Next(1, MapWidth - w - 1);
            int y = rng.Next(1, MapHeight - h - 1);

            var candidate = new Room(x, y, w, h);

            bool overlaps = false;
            foreach (var existing in rooms)
            {
                if (candidate.Overlaps(existing))
                {
                    overlaps = true;
                    break;
                }
            }

            if (!overlaps)
                rooms.Add(candidate);
        }

        return rooms;
    }

    private void CarveRoom(TileMap map, Room room)
    {
        for (int y = room.Y; y < room.Y + room.Height; y++)
            for (int x = room.X; x < room.X + room.Width; x++)
                map.SetTile(x, y, FloorTile);
    }

    // ── Corridor carving ──────────────────────────────────────────────

    private void ConnectRooms(TileMap map, Random rng)
    {
        for (int i = 0; i < Rooms.Count - 1; i++)
        {
            var (x1, y1) = Rooms[i].Center;
            var (x2, y2) = Rooms[i + 1].Center;

            if (rng.Next(2) == 0)
            {
                CarveHorizontalTunnel(map, x1, x2, y1);
                CarveVerticalTunnel(map, y1, y2, x2);
            }
            else
            {
                CarveVerticalTunnel(map, y1, y2, x1);
                CarveHorizontalTunnel(map, x1, x2, y2);
            }
        }
    }

    private void CarveHorizontalTunnel(TileMap map, int x1, int x2, int y)
    {
        int start = Math.Min(x1, x2);
        int end = Math.Max(x1, x2);
        for (int x = start; x <= end; x++)
            if (map.InBounds(x, y))
                map.SetTile(x, y, FloorTile);
    }

    private void CarveVerticalTunnel(TileMap map, int y1, int y2, int x)
    {
        int start = Math.Min(y1, y2);
        int end = Math.Max(y1, y2);
        for (int y = start; y <= end; y++)
            if (map.InBounds(x, y))
                map.SetTile(x, y, FloorTile);
    }

    // ── Connectivity verification ─────────────────────────────────────

    private void EnsureConnectivity(TileMap map, Random rng)
    {
        var allFloors = new HashSet<(int X, int Y)>();
        for (int y = 0; y < MapHeight; y++)
            for (int x = 0; x < MapWidth; x++)
            {
                var id = map.GetTileId(x, y);
                if (id == FloorTile || id == EntranceTile || id == ExitTile)
                    allFloors.Add((x, y));
            }

        if (allFloors.Count == 0) return;

        var start = allFloors.GetEnumerator();
        start.MoveNext();
        var reached = FloodFill(map, start.Current.X, start.Current.Y);

        int maxRepairs = 50;
        int repairs = 0;

        while (reached.Count < allFloors.Count && repairs < maxRepairs)
        {
            (int ux, int uy) unreachedTile = default;
            (int rx, int ry) reachedTile = default;
            int bestDist = int.MaxValue;

            foreach (var floor in allFloors)
            {
                if (reached.Contains(floor)) continue;
                foreach (var r in reached)
                {
                    int dist = Math.Abs(floor.X - r.X) + Math.Abs(floor.Y - r.Y);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        unreachedTile = floor;
                        reachedTile = r;
                    }
                }
            }

            if (rng.Next(2) == 0)
            {
                CarveHorizontalTunnel(map, reachedTile.rx, unreachedTile.ux, reachedTile.ry);
                CarveVerticalTunnel(map, reachedTile.ry, unreachedTile.uy, unreachedTile.ux);
            }
            else
            {
                CarveVerticalTunnel(map, reachedTile.ry, unreachedTile.uy, reachedTile.rx);
                CarveHorizontalTunnel(map, reachedTile.rx, unreachedTile.ux, unreachedTile.uy);
            }

            reached = FloodFill(map, start.Current.X, start.Current.Y);
            repairs++;
        }
    }

    private HashSet<(int X, int Y)> FloodFill(TileMap map, int startX, int startY)
    {
        var visited = new HashSet<(int, int)>();
        var queue = new Queue<(int X, int Y)>();

        queue.Enqueue((startX, startY));
        visited.Add((startX, startY));

        int[] dx = { 0, 0, 1, -1 };
        int[] dy = { 1, -1, 0, 0 };

        while (queue.Count > 0)
        {
            var (cx, cy) = queue.Dequeue();
            for (int i = 0; i < 4; i++)
            {
                int nx = cx + dx[i];
                int ny = cy + dy[i];

                if (!map.InBounds(nx, ny)) continue;
                if (visited.Contains((nx, ny))) continue;

                var id = map.GetTileId(nx, ny);
                if (id == FloorTile || id == EntranceTile || id == ExitTile)
                {
                    visited.Add((nx, ny));
                    queue.Enqueue((nx, ny));
                }
            }
        }

        return visited;
    }

    // ── Item and chest placement ──────────────────────────────────────

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

            // Place 0–2 ground items
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

    // ── Enemy placement ───────────────────────────────────────────────

    /// <summary>
    /// Place enemies throughout the dungeon rooms.
    /// Skips the entrance room (room 0) to give the player a safe start.
    /// Each eligible room gets 1–2 enemies chosen randomly from available defs.
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

            // 1–2 enemies per room (70% chance of 1, 30% chance of 2)
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

    // ── Torch placement ───────────────────────────────────────────────

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