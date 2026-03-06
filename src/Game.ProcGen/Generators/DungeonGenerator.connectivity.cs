// src/Game.ProcGen/Generators/DungeonGenerator.Connectivity.cs

using System;
using System.Collections.Generic;
using Game.Core.Map;

namespace Game.ProcGen.Generators;

public partial class DungeonGenerator
{
    // -- Connectivity verification ------------------------------------

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
}