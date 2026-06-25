using Godot;
using System;
using System.Collections.Generic;

public partial class Hitbox : Area3D
{
    [Export] public int Damage { get; set; } = 10;
    [Export] public float KnockbackForce { get; set; } = 5f;

    private readonly HashSet<Hurtbox> _hitTargets = new();

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
            var knockbackDir = (hurtbox.GlobalPosition - GlobalPosition).Normalized();
            var hitPos = (GlobalPosition + hurtbox.GlobalPosition) / 2f;
            var data = new DamageData
            {
                Amount = Damage,
                KnockbackDirection = knockbackDir * KnockbackForce,
                HitPosition = hitPos,
                Source = FindOwnerCharacter(),
                Type = AttackType.Light
            };
            hurtbox.ReceiveDamage(data);
            HitConnected?.Invoke(data);
        }
    }

    public void Activate()
    {
        _hitTargets.Clear();
        Monitoring = true;
    }

    public void Deactivate()
    {
        Monitoring = false;
        _hitTargets.Clear();
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
