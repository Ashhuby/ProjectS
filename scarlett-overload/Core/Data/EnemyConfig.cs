namespace Game.Core.Data;

using Godot;

/// <summary>
/// AI configuration for an enemy type. Create instances as .tres:
///   Resources/Enemies/BasicEnemyAI.tres
///
/// Paired with a CharacterStats .tres for health/movement and
/// AttackData .tres files for the attack chain.
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
    /// <summary>Distance at which the enemy can start an attack.</summary>
    [Export] public float AttackRange { get; set; } = 2.5f;

    [ExportGroup("Movement")]
    [Export] public float ChaseSpeed { get; set; } = 5f;
    [Export] public float RotationSpeed { get; set; } = 10f;

    [ExportGroup("Attack Timing")]
    /// <summary>Minimum wait in Engage before starting an attack.</summary>
    [Export] public float AttackCooldownMin { get; set; } = 0.8f;
    /// <summary>Maximum wait — actual cooldown is randomized in this range.</summary>
    [Export] public float AttackCooldownMax { get; set; } = 1.8f;
    [Export] public float TelegraphDuration { get; set; } = 0.5f;
    [Export] public float AttackActiveDuration { get; set; } = 0.2f;
    [Export] public float RecoveryDuration { get; set; } = 0.45f;

    [ExportGroup("Hit Reactions")]
    /// <summary>Normal hit stun — brief flinch, NOT parry stun.</summary>
    [Export] public float StunDuration { get; set; } = 0.3f;
    /// <summary>
    /// Parry stagger — the core vital window. Must be tight enough
    /// that hesitation fails the sequence but long enough for
    /// reaction + dash + attack (~0.9s perfect execution).
    /// </summary>
    [Export] public float ParryStaggerDuration { get; set; } = 2.5f;

    [ExportGroup("Vital System")]
    /// <summary>
    /// Extra stun time added when the player pops the primary vital.
    /// Remaining stun after pop = (ParryStagger remaining) + this value.
    /// Must allow one dash + vital thrust (~0.9s) with some margin.
    /// </summary>
    [Export] public float VitalStunExtension { get; set; } = 1.5f;

    /// <summary>
    /// Half-angle in degrees of the hit cone for vital detection.
    /// 40 = the player must be within an 80° arc centered on the vital direction.
    /// </summary>
    [Export(PropertyHint.Range, "15,90,5")]
    public float VitalAngleTolerance { get; set; } = 40f;

    /// <summary>
    /// If false, vitals cannot spawn on the side directly opposite the
    /// player at the moment of parry. Use for large enemies where
    /// circling to the far side is impractical during the stun window.
    /// </summary>
    [Export] public bool AllowOppositeVitals { get; set; } = true;

    /// <summary>
    /// Damage multiplier applied when the player pops the primary vital.
    /// Multiplied against the attack's base damage.
    /// </summary>
    [Export] public float VitalBurstMultiplier { get; set; } = 2.0f;

    /// <summary>
    /// Damage multiplier applied when the player pops the mini vital
    /// with the vital thrust. Multiplied against the VitalThrust's base damage.
    /// </summary>
    [Export] public float VitalBonusMultiplier { get; set; } = 1.5f;

    /// <summary>
    /// Movement speed multiplier granted to the player after popping
    /// the mini vital. Applied for SpeedBoostDuration seconds.
    /// </summary>
    [Export] public float SpeedBoostMultiplier { get; set; } = 1.5f;

    /// <summary>Duration of the speed boost in seconds.</summary>
    [Export] public float SpeedBoostDuration { get; set; } = 1.0f;

    /// <summary>
    /// Offset radius from enemy center where vital indicators spawn (units).
    /// </summary>
    [Export] public float VitalSpawnRadius { get; set; } = 1.5f;

    [ExportGroup("Attacks")]
    /// <summary>
    /// Pool of attacks this enemy can use. One is chosen randomly
    /// each time the enemy decides to attack.
    /// </summary>
    [Export] public AttackData[] Attacks { get; set; } = System.Array.Empty<AttackData>();

    /// <summary>Pick a random attack from the pool. Null if pool is empty.</summary>
    public AttackData PickAttack()
    {
        if (Attacks == null || Attacks.Length == 0) return null;
        return Attacks[GD.RandRange(0, Attacks.Length - 1)];
    }

    /// <summary>Random cooldown between min and max.</summary>
    public float RollCooldown()
    {
        return (float)GD.RandRange(AttackCooldownMin, AttackCooldownMax);
    }
}
