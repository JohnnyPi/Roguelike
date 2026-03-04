// src/Game.Core/Entities/Player.cs

namespace Game.Core.Entities;

/// <summary>
/// Player entity. Extends Entity with combat stats and inventory.
/// Stats are hardcoded for first playable; later they'll come from
/// a player def or character creation system.
/// </summary>
public class Player : Entity
{
    public int MaxHp { get; set; } = 30;
    public int Hp { get; set; } = 30;
    public int Attack { get; set; } = 5;
    public int Defense { get; set; } = 2;

    /// <summary>Simple inventory — list of item def IDs.</summary>
    public List<string> Inventory { get; } = new();

    public bool IsDead => Hp <= 0;

    public Player()
    {
        Name = "Player";
        BlocksMovement = true;
    }

    /// <summary>Apply damage after defense calculation. Minimum 1 damage.</summary>
    public int TakeDamage(int rawDamage)
    {
        int actual = Math.Max(1, rawDamage - Defense);
        Hp = Math.Max(0, Hp - actual);
        return actual;
    }

    /// <summary>Heal up to max HP.</summary>
    public void Heal(int amount)
    {
        Hp = Math.Min(MaxHp, Hp + amount);
    }

    /// <summary>Add an item def ID to inventory.</summary>
    public void AddItem(string itemDefId)
    {
        Inventory.Add(itemDefId);
    }
}