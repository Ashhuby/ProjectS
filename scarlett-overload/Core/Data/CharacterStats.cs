namespace Game.Core.Data;

using Godot;

/// <summary>
/// Base stats for any character. Create instances as .tres files:
///   Resources/Characters/PlayerStats.tres
///   Resources/Characters/TestDummyStats.tres
///
/// Assign via the inspector on any CharacterBase-derived node.
/// </summary>
[GlobalClass]
public partial class CharacterStats : Resource
{
    [ExportGroup("Health")]
    [Export] public int MaxHealth { get; set; } = 100;

    [ExportGroup("Movement")]
    [Export] public float MoveSpeed { get; set; } = 5f;
    [Export] public float Acceleration { get; set; } = 25f;
    [Export] public float RotationSpeed { get; set; } = 10f;

    [ExportGroup("Physics")]
    [Export] public float Gravity { get; set; } = 9.8f;
    [Export] public float KnockbackDecay { get; set; } = 12f;

    [ExportGroup("Combat")]
    /// <summary>
    /// Duration of the parry active window in seconds.
    /// 0 = cannot parry (used for enemies/dummies without a parry).
    /// </summary>
    [Export] public float ParryWindowDuration { get; set; } = 0f;

    /// <summary>
    /// Delay before respawn after death. 0 = no respawn.
    /// </summary>
    [Export] public float RespawnDelay { get; set; } = 0f;
}
