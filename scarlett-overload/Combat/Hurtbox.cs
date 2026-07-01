using Godot;
using System;

public partial class Hurtbox : Area3D
{
    public event Action<DamageData> DamageReceived;

    public override void _Ready()
    {
        Monitoring = false;
        Monitorable = true;
    }

    public void ReceiveDamage(DamageData data)
    {
        DamageReceived?.Invoke(data);
    }
}
