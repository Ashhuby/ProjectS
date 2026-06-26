namespace Game.Core.Interfaces;

using Godot;

/// <summary>
/// Extends ITargetable with data the lock-on UI needs to display
/// the reticle, health bar, or name tag above the target.
/// </summary>
public interface ILockOnTarget : ITargetable
{
    /// <summary>
    /// Display name shown on the lock-on reticle or health bar.
    /// </summary>
    string TargetName { get; }

    /// <summary>
    /// Offset from TargetPosition where the lock-on indicator sits.
    /// Override for enemies with unusual proportions (tall bosses, etc.).
    /// </summary>
    Vector3 LockOnOffset => new(0f, 2.2f, 0f);
}
