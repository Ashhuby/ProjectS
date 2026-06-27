namespace Game.Core.Data;

using Godot;

/// <summary>
/// Dash configuration resource. Create as .tres:
///   Resources/Player/DashStats.tres
///
/// Controls distance, timing, i-frame window, and cooldown.
/// I-frame start/end are fractions of Duration (0–1).
/// At default values: dash lasts 0.2s, i-frames active from
/// 0.05s to 0.15s (middle 50% of the dash).
/// </summary>
[GlobalClass]
public partial class DashStats : Resource
{
    [ExportGroup("Movement")]
    /// <summary>Total distance covered by a single dash (units).</summary>
    [Export] public float Distance { get; set; } = 2f;

    /// <summary>Total duration of the dash in seconds.</summary>
    [Export] public float Duration { get; set; } = 0.2f;

    [ExportGroup("I-Frames")]
    /// <summary>
    /// Fraction of Duration when i-frames begin (0 = dash start).
    /// Hurtbox is disabled from this point until IFrameEnd.
    /// </summary>
    [Export(PropertyHint.Range, "0,1,0.05")]
    public float IFrameStart { get; set; } = 0.25f;

    /// <summary>
    /// Fraction of Duration when i-frames end (1 = dash end).
    /// </summary>
    [Export(PropertyHint.Range, "0,1,0.05")]
    public float IFrameEnd { get; set; } = 0.75f;

    [ExportGroup("Cooldown")]
    /// <summary>Time in seconds before another dash can be performed.</summary>
    [Export] public float Cooldown { get; set; } = 0.5f;
}
