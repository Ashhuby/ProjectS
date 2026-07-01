using Game.Autoloads;
using Game.Core.Data;
using Godot;
using System;

/// <summary>
/// Handles player dash: velocity burst, i-frame window, and cooldown.
///
/// Plain C# class — same composition pattern as PlayerMovement and
/// PlayerCombat. PlayerCharacter creates it, PlayerCombat manages
/// the state transition (Free → Dashing → Free).
///
/// I-frames work by toggling the player's hurtbox collision via a
/// delegate — when the hurtbox is off, no damage events fire at all.
///
/// Requires "dash" input action in Project Settings → Input Map.
/// </summary>
public class PlayerDash
{
    private readonly DashStats _stats;
    private readonly Action<bool> _setHurtboxActive;

    // ── State ─────────────────────────────────────────────────────────

    private bool _isDashing;
    private float _elapsed;
    private float _cooldownRemaining;
    private Vector3 _dashVelocity;
    private bool _hurtboxDisabled;

    public bool IsDashing => _isDashing;
    public bool CanDash => !_isDashing && _cooldownRemaining <= 0f;
    public Vector3 DashVelocity => _dashVelocity;

    // ══════════════════════════════════════════════════════════════════
    //  CONSTRUCTION
    // ══════════════════════════════════════════════════════════════════

    /// <param name="stats">Dash configuration resource.</param>
    /// <param name="setHurtboxActive">
    /// Delegate to CharacterBase.SetHurtboxActive — toggles the
    /// player's hurtbox for i-frame invincibility.
    /// </param>
    public PlayerDash(DashStats stats, Action<bool> setHurtboxActive)
    {
        _stats = stats ?? new DashStats();
        _setHurtboxActive = setHurtboxActive;
    }

    // ══════════════════════════════════════════════════════════════════
    //  START
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Begin a dash in the given world-space direction.
    /// Direction must be normalized and projected to XZ plane by the caller.
    /// </summary>
    public void Start(Vector3 direction)
    {
        _isDashing = true;
        _elapsed = 0f;
        _hurtboxDisabled = false;

        float speed = _stats.Distance / _stats.Duration;
        _dashVelocity = direction * speed;

        EventBus.Instance?.EmitDashStarted();
        GD.Print($"[Dash] Started — dir: ({direction.X:F2}, {direction.Z:F2}), speed: {speed:F1}");
    }

    // ══════════════════════════════════════════════════════════════════
    //  TICK — called every physics frame by PlayerCombat
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Update dash progress, i-frame window, and cooldown.
    /// Call this every physics frame regardless of combat state —
    /// the cooldown needs to tick even when not dashing.
    /// </summary>
    public void Tick(float dt)
    {
        // Always tick cooldown
        if (_cooldownRemaining > 0f)
            _cooldownRemaining -= dt;

        if (!_isDashing)
            return;

        _elapsed += dt;
        float progress = _elapsed / _stats.Duration;

        // ── I-frame window ────────────────────────────────────────
        bool shouldBeInvincible = progress >= _stats.IFrameStart
                               && progress <= _stats.IFrameEnd;

        if (shouldBeInvincible && !_hurtboxDisabled)
        {
            _setHurtboxActive(false);
            _hurtboxDisabled = true;
        }
        else if (!shouldBeInvincible && _hurtboxDisabled)
        {
            _setHurtboxActive(true);
            _hurtboxDisabled = false;
        }

        // ── Completion ────────────────────────────────────────────
        if (progress >= 1f)
            Complete();
    }

    // ══════════════════════════════════════════════════════════════════
    //  COMPLETION
    // ══════════════════════════════════════════════════════════════════

    private void Complete()
    {
        _isDashing = false;
        _dashVelocity = Vector3.Zero;
        _cooldownRemaining = _stats.Cooldown;

        // Fail-safe: always re-enable hurtbox on dash end
        if (_hurtboxDisabled)
        {
            _setHurtboxActive(true);
            _hurtboxDisabled = false;
        }

        EventBus.Instance?.EmitDashEnded();
        GD.Print("[Dash] Complete");
    }

    /// <summary>
    /// Force-cancel the dash immediately (e.g. player died mid-dash).
    /// Re-enables hurtbox as a safety measure.
    /// </summary>
    public void ForceCancel()
    {
        if (!_isDashing) return;

        _isDashing = false;
        _dashVelocity = Vector3.Zero;

        if (_hurtboxDisabled)
        {
            _setHurtboxActive(true);
            _hurtboxDisabled = false;
        }

        GD.Print("[Dash] Force cancelled");
    }

    // ══════════════════════════════════════════════════════════════════
    //  DIRECTION HELPER
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Compute dash direction from movement input and camera orientation.
    /// If no input, dashes backward relative to camera.
    /// Returns a normalized XZ-plane vector in world space.
    /// </summary>
    public static Vector3 ComputeDirection(CharacterBody3D owner)
    {
        Vector2 inputDir = Input.GetVector(
            "move_left", "move_right", "move_forward", "move_back");

        Camera3D cam = owner.GetViewport().GetCamera3D();

        Vector3 camForward = -cam.GlobalTransform.Basis.Z;
        camForward.Y = 0f;
        camForward = camForward.Normalized();

        Vector3 camRight = cam.GlobalTransform.Basis.X;
        camRight.Y = 0f;
        camRight = camRight.Normalized();

        if (inputDir != Vector2.Zero)
        {
            // Dash in input direction relative to camera
            Vector3 dir = (camRight * inputDir.X - camForward * inputDir.Y).Normalized();
            return dir;
        }

        // No input — dash backward relative to camera
        return -camForward;
    }
}
