using Game.Core.Data;
using Godot;
using System;
using System.Collections.Generic;

public partial class Hitbox : Area3D
{
    // Fallback values when Activate() is called without AttackData
    [Export] public int Damage { get; set; } = 10;
    [Export] public float KnockbackForce { get; set; } = 5f;

    private readonly HashSet<Hurtbox> _hitTargets = new();
    private AttackData _currentAttack;

    // Fires when a hit successfully connects — subscribers can trigger game feel effects
    public event Action<DamageData> HitConnected;

    public override void _Ready()
    {
        Monitoring = false;
        Monitorable = false;
        AreaEntered += OnAreaEntered;
    }

    private void OnAreaEntered(Area3D area)
    {
        if (area is Hurtbox hurtbox && _hitTargets.Add(hurtbox))
        {
            int damage = _currentAttack?.Damage ?? Damage;
            float knockback = _currentAttack?.KnockbackForce ?? KnockbackForce;
            var attackType = _currentAttack?.Type ?? AttackType.Light;

            var knockbackDir = (hurtbox.GlobalPosition - GlobalPosition).Normalized();
            var hitPos = (GlobalPosition + hurtbox.GlobalPosition) / 2f;

            var data = new DamageData
            {
                Amount = damage,
                KnockbackDirection = knockbackDir * knockback,
                HitPosition = hitPos,
                Source = FindOwnerCharacter(),
                Type = attackType
            };
            hurtbox.ReceiveDamage(data);
            HitConnected?.Invoke(data);
        }
    }

    /// <summary>
    /// Activate with AttackData for data-driven damage.
    /// The hitbox reads Damage, KnockbackForce, and Type from the resource.
    /// </summary>
    public void Activate(AttackData attack)
    {
        _currentAttack = attack;
        _hitTargets.Clear();
        Monitoring = true;
    }

    /// <summary>
    /// Activate using [Export] fallback values. Backward compatible —
    /// enemies/dummies that don't have AttackData resources call this.
    /// </summary>
    public void Activate()
    {
        _currentAttack = null;
        _hitTargets.Clear();
        Monitoring = true;
    }

    public void Deactivate()
    {
        Monitoring = false;
        _hitTargets.Clear();
        _currentAttack = null;
    }

    private CharacterBody3D FindOwnerCharacter()
    {
        var node = GetParent();
        while (node != null)
        {
            if (node is CharacterBody3D body) return body;
            node = node.GetParent();
        }
        return null;
    }
}
