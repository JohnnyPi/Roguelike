// src/Game.Core/Entities/Enemy.cs
//
// Runtime enemy entity. References a MonsterDef for its base stats.
// AI behavior is processed each turn after the player acts.
//
// AI types:
//   "chase"  - move toward player if within SightRange, wander otherwise
//   "wander" - random movement (passive until attacked, then chases)
//   "guard"  - stationary until player enters SightRange, then chases

using Game.Core.Monsters;

namespace Game.Core.Entities;

public class Enemy : Entity
{
    /// <summary>The monster definition this enemy was created from.</summary>
    public MonsterDef Def { get; }

    public int MaxHp { get; set; }
    public int Hp { get; set; }
    public int Attack { get; set; }
    public int Defense { get; set; }
    public int SightRange { get; set; }
    public string AiBehavior { get; set; }

    /// <summary>
    /// Whether this enemy has been provoked (e.g. attacked while passive).
    /// Wander-type enemies become chasers when provoked.
    /// </summary>
    public bool IsProvoked { get; set; }

    public bool IsDead => Hp <= 0;

    /// <summary>
    /// Per-instance RNG for wander behavior. Seeded from InstanceId so
    /// enemies in the same room don't move in unison, but also not
    /// reconstructed every turn (avoids per-turn allocation).
    /// </summary>
    private readonly Random _rng;

    public Enemy(MonsterDef def)
    {
        Def = def;
        DefId = def.Id;
        Name = def.Name;
        BlocksMovement = true;

        // Copy stats from def (can be modified by buffs/debuffs later)
        MaxHp = def.MaxHp;
        Hp = def.MaxHp;
        Attack = def.Attack;
        Defense = def.Defense;
        SightRange = def.SightRange;
        AiBehavior = def.AiBehavior;

        // Seed from InstanceId for per-enemy variety without shared state
        _rng = new Random((int)(InstanceId * 2654435761u));
    }

    /// <summary>Apply damage after defense calculation. Minimum 1 damage.</summary>
    public int TakeDamage(int rawDamage)
    {
        int actual = Math.Max(1, rawDamage - Defense);
        Hp = Math.Max(0, Hp - actual);
        if (Hp <= 0)
            IsAlive = false;
        return actual;
    }

    /// <summary>
    /// Process one AI turn. Returns the action taken.
    /// Requires the game state to know where the player is and what's walkable.
    /// </summary>
    public AiAction TakeTurn(GameState state)
    {
        if (IsDead || !IsAlive) return AiAction.None;

        var player = state.Player;
        if (player == null || player.IsDead) return AiAction.None;

        int distToPlayer = Math.Abs(X - player.X) + Math.Abs(Y - player.Y);

        // Determine effective behavior
        string behavior = AiBehavior;
        if (IsProvoked && behavior == "wander")
            behavior = "chase"; // provoked wanderers become chasers

        switch (behavior)
        {
            case "chase":
                if (distToPlayer <= SightRange)
                    return TryChaseOrAttack(state, player);
                return TryWander(state);

            case "guard":
                if (distToPlayer <= SightRange)
                    return TryChaseOrAttack(state, player);
                return AiAction.Wait; // guards don't wander

            case "wander":
            default:
                return TryWander(state);
        }
    }

    /// <summary>
    /// Try to move toward the player. If adjacent, attack instead.
    /// Uses simple Manhattan distance-reducing movement (no pathfinding yet).
    /// </summary>
    private AiAction TryChaseOrAttack(GameState state, Player player)
    {
        int distToPlayer = Math.Abs(X - player.X) + Math.Abs(Y - player.Y);

        // Adjacent (distance 1) = attack
        if (distToPlayer == 1)
        {
            int damage = player.TakeDamage(Attack);
            state.Log($"{Name} attacks you for {damage} damage! (HP: {player.Hp}/{player.MaxHp})");

            if (player.IsDead)
                state.Log("You have been slain...");

            return AiAction.Attack;
        }

        // Not adjacent - try to step closer
        return TryStepToward(state, player.X, player.Y);
    }

    /// <summary>
    /// Move one step toward a target position.
    /// Picks the axis with the larger gap first. Falls back to the other axis.
    /// </summary>
    private AiAction TryStepToward(GameState state, int targetX, int targetY)
    {
        int dx = Math.Sign(targetX - X);
        int dy = Math.Sign(targetY - Y);

        // Try primary direction (larger gap first for more natural movement)
        int gapX = Math.Abs(targetX - X);
        int gapY = Math.Abs(targetY - Y);

        // Attempt 1: move along primary axis
        int firstDx, firstDy, secondDx, secondDy;
        if (gapX >= gapY)
        {
            firstDx = dx; firstDy = 0;
            secondDx = 0; secondDy = dy;
        }
        else
        {
            firstDx = 0; firstDy = dy;
            secondDx = dx; secondDy = 0;
        }

        if (firstDx != 0 || firstDy != 0)
        {
            if (CanMoveTo(state, X + firstDx, Y + firstDy))
            {
                state.MoveEntity(this, firstDx, firstDy);
                return AiAction.Move;
            }
        }

        // Attempt 2: move along secondary axis
        if (secondDx != 0 || secondDy != 0)
        {
            if (CanMoveTo(state, X + secondDx, Y + secondDy))
            {
                state.MoveEntity(this, secondDx, secondDy);
                return AiAction.Move;
            }
        }

        // Can't move toward target - wait
        return AiAction.Wait;
    }

    /// <summary>Random movement in a cardinal direction.</summary>
    private AiAction TryWander(GameState state)
    {
        int[] dxs = { 0, 0, 1, -1 };
        int[] dys = { -1, 1, 0, 0 };

        // Shuffle direction order using the persistent per-instance RNG
        int startDir = _rng.Next(4);
        for (int i = 0; i < 4; i++)
        {
            int dir = (startDir + i) % 4;
            int nx = X + dxs[dir];
            int ny = Y + dys[dir];

            if (CanMoveTo(state, nx, ny))
            {
                state.MoveEntity(this, dxs[dir], dys[dir]);
                return AiAction.Move;
            }
        }

        return AiAction.Wait;
    }

    /// <summary>Check if the enemy can move to a position (walkable + not blocked by another entity).</summary>
    private bool CanMoveTo(GameState state, int x, int y)
    {
        if (state.ActiveMap == null || !state.ActiveMap.IsWalkable(x, y))
            return false;

        // Don't walk into the player
        if (state.Player.X == x && state.Player.Y == y)
            return false;

        // O(1) spatial index lookup -- replaces the old O(n) entity scan
        var blocker = state.GetBlockingEntityAt(x, y);
        return blocker == null;
    }
}

/// <summary>Result of an enemy AI turn.</summary>
public enum AiAction
{
    None,
    Wait,
    Move,
    Attack
}