namespace Game.Core.Interfaces;

using System;

/// <summary>
/// Implemented by anything that can take damage: player, enemies,
/// destructible props, bosses with multiple health bars.
/// The combat system operates on this interface, never on concrete types.
/// </summary>
public interface IDamageable
{
    int CurrentHealth { get; }
    int MaxHealth { get; }
    bool IsDead { get; }

    /// <summary>
    /// Single entry point for all damage — hitbox overlap, fall damage,
    /// environmental hazards. Implementations decide whether to absorb,
    /// block, or apply the damage.
    /// </summary>
    void TakeDamage(DamageData data);

    /// <summary>
    /// Fires (currentHealth, maxHealth) whenever health changes.
    /// UI systems subscribe to this without knowing the concrete type.
    /// </summary>
    event Action<int, int> HealthChanged;
}
