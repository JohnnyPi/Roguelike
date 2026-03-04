// src/Game.Core/Entities/Entity.cs

namespace Game.Core.Entities;

/// <summary>
/// Base class for any game object that exists on the tile grid.
/// Keeps it minimal for first playable — just position, ID, and alive state.
/// When you migrate to Arch ECS later, these become component structs instead.
/// </summary>
public class Entity
{
    /// <summary>Unique runtime instance ID (not the content def ID).</summary>
    public int InstanceId { get; }

    /// <summary>Content definition ID, e.g. "base:goblin" or "base:gold_coin".</summary>
    public string DefId { get; init; } = string.Empty;

    /// <summary>Display name (can differ from def name for named uniques).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Grid X position.</summary>
    public int X { get; set; }

    /// <summary>Grid Y position.</summary>
    public int Y { get; set; }

    /// <summary>Is this entity still active in the world?</summary>
    public bool IsAlive { get; set; } = true;

    /// <summary>Can other entities walk through this tile?</summary>
    public bool BlocksMovement { get; init; }

    // Simple static counter for generating unique instance IDs
    private static int _nextId = 1;

    public Entity()
    {
        InstanceId = _nextId++;
    }

    /// <summary>Move by a delta. Does NOT check collisions — that's the caller's job.</summary>
    public void Move(int dx, int dy)
    {
        X += dx;
        Y += dy;
    }

    /// <summary>Set position directly (used by generators and map transitions).</summary>
    public void SetPosition(int x, int y)
    {
        X = x;
        Y = y;
    }
}