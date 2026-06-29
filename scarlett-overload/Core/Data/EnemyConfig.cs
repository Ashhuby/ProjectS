namespace Game.Core.Data;

using Godot;

/// <summary>
/// AI configuration for an enemy type. Create instances as .tres.
///
/// IMPORTANT: .tres files bake values at creation time. If a .tres
/// is assigned in the inspector, these code defaults are IGNORED.
/// Always verify values in the editor, not just here.
/// </summary>
[GlobalClass]
public partial class EnemyConfig : Resource
{
    [ExportGroup("Detection")]
    [Export] public float DetectionRange { get; set; } = 12f;
    [Export] public float LoseAggroRange { get; set; } = 20f;

    [ExportGroup("Combat Spacing")]
    [Export] public float AttackRange { get; set; } = 2.5f;

    [ExportGroup("Movement")]
    [Export] public float ChaseSpeed { get; set; } = 5f;
    [Export] public float RotationSpeed { get; set; } = 10f;

    [ExportGroup("Lunge")]
    /// <summary>
    /// Speed toward target during attack. 0 = no lunge (stands still).
    /// </summary>
    [Export] public float LungeSpeed { get; set; } = 0f;

    /// <summary>If true, begin lunging during telegraph at reduced speed.</summary>
    [Export] public bool LungeDuringTelegraph { get; set; } = false;

    /// <summary>Fraction of LungeSpeed used during telegraph creep.</summary>
    [Export(PropertyHint.Range, "0,1,0.05")]
    public float TelegraphLungeFraction { get; set; } = 0.3f;

    [ExportGroup("Attack Timing")]
    [Export] public float AttackCooldownMin { get; set; } = 0.8f;
    [Export] public float AttackCooldownMax { get; set; } = 1.8f;
    [Export] public float TelegraphDuration { get; set; } = 0.5f;
    [Export] public float AttackActiveDuration { get; set; } = 0.2f;

    /// <summary>
    /// Seconds after entering Attacking before the hitbox activates.
    /// Lets lunge enemies close distance before the damage check.
    /// 0 = instant activation (default for non-lunge enemies).
    /// </summary>
    [Export] public float HitboxDelay { get; set; } = 0f;

    [Export] public float RecoveryDuration { get; set; } = 0.45f;

    [ExportGroup("Hit Reactions")]
    [Export] public float StunDuration { get; set; } = 0.3f;
    [Export] public float ParryStaggerDuration { get; set; } = 2.5f;

    [ExportGroup("Vital System")]
    [Export] public float VitalStunExtension { get; set; } = 1.5f;

    [Export(PropertyHint.Range, "15,90,5")]
    public float VitalAngleTolerance { get; set; } = 40f;

    [Export] public bool AllowOppositeVitals { get; set; } = true;
    [Export] public float VitalBurstMultiplier { get; set; } = 2.0f;
    [Export] public float VitalBonusMultiplier { get; set; } = 1.5f;
    [Export] public float SpeedBoostMultiplier { get; set; } = 1.5f;
    [Export] public float SpeedBoostDuration { get; set; } = 1.0f;
    [Export] public float VitalSpawnRadius { get; set; } = 1.5f;

    [ExportGroup("Attacks")]
    [Export] public AttackData[] Attacks { get; set; } = System.Array.Empty<AttackData>();

    public AttackData PickAttack()
    {
        if (Attacks == null || Attacks.Length == 0) return null;
        return Attacks[GD.RandRange(0, Attacks.Length - 1)];
    }

    public float RollCooldown()
    {
        return (float)GD.RandRange(AttackCooldownMin, AttackCooldownMax);
    }
}
