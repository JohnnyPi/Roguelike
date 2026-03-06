// src/Game.ProcGen/Generators/DungeonGenerator.Rooms.cs

using System;
using System.Collections.Generic;
using Game.Core.Map;

namespace Game.ProcGen.Generators;

public partial class DungeonGenerator
{
    // -- Room placement -----------------------------------------------

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

    // -- Corridor carving ---------------------------------------------

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
}