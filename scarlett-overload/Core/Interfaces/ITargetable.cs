namespace Game.Core.Interfaces;

using Godot;

/// <summary>
/// Anything that can be targeted — by the camera lock-on system,
/// by enemy AI aggro, or by homing projectiles in the future.
/// </summary>
public interface ITargetable
{
    /// <summary>
    /// World-space position the targeting system aims at.
    /// Usually center-mass, not feet.
    /// </summary>
    Vector3 TargetPosition { get; }

    /// <summary>
    /// False if the target is dead, despawned, or otherwise
    /// no longer valid. Targeting systems should drop invalid targets.
    /// </summary>
    bool IsValidTarget { get; }
}
