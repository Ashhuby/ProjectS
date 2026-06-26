using Game.Core.Data;
using Godot;

/// <summary>
/// Handles all player movement: camera-relative input → velocity,
/// rotation toward movement direction or lock-on target, and
/// locomotion animation state (Idle ↔ StartMove ↔ MoveForward).
///
/// This is a plain C# class, not a Node. PlayerCharacter creates it
/// and calls its methods at the right points in the physics loop.
/// </summary>
public class PlayerMovement
{
    private readonly CharacterBody3D _owner;
    private readonly AnimationNodeStateMachinePlayback _playback;
    private readonly CameraController _camera;
    private readonly CharacterStats _stats;

    private Vector3 _lastMoveDir;

    /// <summary>
    /// True when the player is providing movement input this frame.
    /// </summary>
    public bool IsMoving => _lastMoveDir != Vector3.Zero;

    public PlayerMovement(
        CharacterBody3D owner,
        AnimationNodeStateMachinePlayback playback,
        CameraController camera,
        CharacterStats stats)
    {
        _owner = owner;
        _playback = playback;
        _camera = camera;
        _stats = stats;
    }

    // ══════════════════════════════════════════════════════════════════
    //  VELOCITY
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Read input, project onto camera-relative axes, and accelerate
    /// toward the target velocity. Called from CharacterBase.ProcessMovement
    /// only when CombatState is Free and no knockback is active.
    /// </summary>
    public Vector3 ComputeVelocity(Vector3 velocity, float dt)
    {
        float speed = _stats?.MoveSpeed ?? 5f;
        float accel = _stats?.Acceleration ?? 25f;

        Vector2 inputDir = Input.GetVector(
            "move_left", "move_right", "move_forward", "move_back");

        if (inputDir != Vector2.Zero)
        {
            Camera3D cam = _owner.GetViewport().GetCamera3D();

            Vector3 camForward = -cam.GlobalTransform.Basis.Z;
            camForward.Y = 0f;
            camForward = camForward.Normalized();

            Vector3 camRight = cam.GlobalTransform.Basis.X;
            camRight.Y = 0f;
            camRight = camRight.Normalized();

            _lastMoveDir = (camRight * inputDir.X - camForward * inputDir.Y).Normalized();

            velocity.X = Mathf.MoveToward(velocity.X, _lastMoveDir.X * speed, accel * dt);
            velocity.Z = Mathf.MoveToward(velocity.Z, _lastMoveDir.Z * speed, accel * dt);
        }
        else
        {
            velocity.X = Mathf.MoveToward(velocity.X, 0f, accel * dt);
            velocity.Z = Mathf.MoveToward(velocity.Z, 0f, accel * dt);
            _lastMoveDir = Vector3.Zero;
        }

        return velocity;
    }

    // ══════════════════════════════════════════════════════════════════
    //  ROTATION
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Face the lock-on target (if locked) or the movement direction
    /// (if in Free state and moving). Called every physics frame
    /// regardless of combat state — lock-on rotation works during attacks.
    /// </summary>
    public void UpdateRotation(float dt, bool isInCombat)
    {
        float rotSpeed = _stats?.RotationSpeed ?? 10f;

        // Lock-on takes priority — face the target even during attacks
        if (_camera != null && _camera.IsLockedOn && _camera.LockTarget != null)
        {
            Vector3 toTarget = _camera.LockTarget.GlobalPosition - _owner.GlobalPosition;
            toTarget.Y = 0f;

            if (toTarget.LengthSquared() > 0.01f)
            {
                Vector3 dir = toTarget.Normalized();
                float angle = Mathf.Atan2(dir.X, dir.Z);
                _owner.Rotation = new Vector3(
                    _owner.Rotation.X,
                    Mathf.LerpAngle(_owner.Rotation.Y, angle, rotSpeed * dt),
                    _owner.Rotation.Z);
            }
            return;
        }

        // Movement-direction rotation — only in Free state
        if (!isInCombat && _lastMoveDir != Vector3.Zero)
        {
            float angle = Mathf.Atan2(_lastMoveDir.X, _lastMoveDir.Z);
            _owner.Rotation = new Vector3(
                _owner.Rotation.X,
                Mathf.LerpAngle(_owner.Rotation.Y, angle, rotSpeed * dt),
                _owner.Rotation.Z);
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  LOCOMOTION ANIMATION
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Transition between Idle ↔ StartMove ↔ MoveForward.
    /// Called only when CombatState is Free — combat states
    /// drive the AnimationTree directly.
    /// </summary>
    public void UpdateLocomotionAnimation()
    {
        string current = _playback.GetCurrentNode();

        if (_lastMoveDir != Vector3.Zero)
        {
            if (current == "Idle")
                _playback.Travel("StartMove");
        }
        else
        {
            if (current != "Idle")
                _playback.Travel("Idle");
        }
    }
}
