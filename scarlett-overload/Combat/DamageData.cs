using Godot;

public enum AttackType
{
    Light,
    Heavy,
    Thrust
}

public class DamageData
{
    public int Amount { get; set; }
    public Vector3 KnockbackDirection { get; set; }
    public Vector3 HitPosition { get; set; }
    public Node3D Source { get; set; }
    public AttackType Type { get; set; }
}
