namespace Game.Core.Data;

using Godot;

/// <summary>
/// Defines a single attack in a combo chain. Create instances as .tres files:
///   Resources/Attacks/PlayerAttack1.tres
///   Resources/Attacks/PlayerAttack2.tres
///   Resources/Attacks/DummySlam.tres
///
/// Each .tres holds the numbers. The code reads them. You tweak damage,
/// hitstop, knockback in the editor without recompiling.
/// </summary>
[GlobalClass]
public partial class AttackData : Resource
{
    [ExportGroup("Identity")]
    [Export] public string AttackName { get; set; } = "Attack";
    [Export] public AttackType Type { get; set; } = AttackType.Light;

    [ExportGroup("Damage")]
    [Export] public int Damage { get; set; } = 10;
    [Export] public float KnockbackForce { get; set; } = 5f;

    [ExportGroup("Parry")]
    /// <summary>
    /// If true, this attack can be deflected by the player's parry.
    /// For capsule enemies this flag is the sole parry gate.
    /// For enemies with AnimationPlayer, method call tracks
    /// (SetParriable/ClearParriable) can override per-frame.
    /// </summary>
    [Export] public bool IsParriable { get; set; } = false;

    [ExportGroup("Game Feel")]
    [Export] public float HitStopDuration { get; set; } = 0.06f;
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float HitStopTimeScale { get; set; } = 0.05f;
    [Export] public float CameraShakeIntensity { get; set; } = 0.15f;
    [Export] public float CameraShakeDuration { get; set; } = 0.15f;

    [ExportGroup("Timing")]
    /// <summary>
    /// Grace window after this attack ends where the next input
    /// chains into the following combo step.
    /// </summary>
    [Export] public float ComboWindowDuration { get; set; } = 0.2f;
}
