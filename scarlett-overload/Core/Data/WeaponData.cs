namespace Game.Core.Data;

using Godot;

/// <summary>
/// Defines a complete weapon: its combo chain, trail visuals, and scene.
/// Create instances as .tres:
///   Resources/Weapons/Rapier.tres
///
/// The AttackChain array holds one AttackData per combo step.
/// Attack1 reads AttackChain[0], Attack2 reads AttackChain[1], etc.
/// Adding a third combo hit = add a third .tres to the array, no code change.
///
/// VitalThrustAttack is separate from the combo chain — it fires only
/// when the vital system loads it after popping a primary vital.
/// </summary>
[GlobalClass]
public partial class WeaponData : Resource
{
    [ExportGroup("Identity")]
    [Export] public string WeaponName { get; set; } = "Sword";
    [Export] public PackedScene WeaponScene { get; set; }

    [ExportGroup("Combat")]
    /// <summary>
    /// Ordered attack definitions for this weapon's combo chain.
    /// Index 0 = first swing, index 1 = second swing, etc.
    /// </summary>
    [Export] public AttackData[] AttackChain { get; set; } = System.Array.Empty<AttackData>();

    /// <summary>
    /// Special thrust attack fired when the mini vital is active.
    /// Not part of the normal combo chain — gated by VitalSystem state.
    /// Null means no vital thrust available (weapon doesn't support it).
    /// </summary>
    [Export] public AttackData VitalThrustAttack { get; set; }

    [ExportGroup("Trail Visuals")]
    [Export] public Color TrailTipColor { get; set; } = new Color(1f, 0.95f, 0.8f, 0.95f);
    [Export] public Color TrailBaseColor { get; set; } = new Color(1f, 0.6f, 0.2f, 0.5f);
    [Export] public int TrailMaxPoints { get; set; } = 14;
    [Export] public float TrailJitter { get; set; } = 0.025f;

    /// <summary>
    /// How many combo steps this weapon supports.
    /// </summary>
    public int MaxComboSteps => AttackChain?.Length ?? 0;

    /// <summary>
    /// Safely retrieve attack data for a combo step.
    /// Returns null if the step is out of range.
    /// </summary>
    public AttackData GetAttack(int comboStep)
    {
        if (AttackChain == null || comboStep < 0 || comboStep >= AttackChain.Length)
            return null;
        return AttackChain[comboStep];
    }
}
