using Godot;

public partial class CameraController : Node3D
{
    [Export] public float MouseSensitivity = 0.002f;
    [Export] public float StickSensitivity = 3.0f;
    [Export] public float FollowSpeed = 12.0f;
    [Export] public float MinPitch = -80.0f;
    [Export] public float MaxPitch = 60.0f;
    [Export] public float LockOnRange = 20.0f;
    [Export] public float LockOnPitchOffset = -20.0f;
    [Export] public float LockOnYawSpeed = 10.0f;
    [Export] public float LockOnPitchSpeed = 5.0f;

    private Node3D _target;
    private SpringArm3D _springArm;
    private Camera3D _camera;

    // Lock-on state
    private bool _isLockedOn = false;
    private Node3D _lockTarget = null;
    private float _switchCooldown = 0f;
    private const float SwitchCooldownDuration = 0.3f;
    private const float MouseSwitchThreshold = 30f;
    private const float StickSwitchThreshold = 0.7f;

    // Lock-on indicator — small white dot at center-mass
    private MeshInstance3D _lockIndicator;
    private StandardMaterial3D _lockIndicatorMat;

    public bool IsLockedOn => _isLockedOn;
    public Node3D LockTarget => _lockTarget;

    // Camera shake
    private float _shakeIntensity = 0f;
    private float _shakeDuration = 0f;
    private float _shakeTimer = 0f;
    private Vector3 _cameraRestPosition;

    public override void _Ready()
    {
        _target = GetParent().GetNode<Node3D>("PlayerCharacter");
        _springArm = GetNode<SpringArm3D>("SpringArm3D");
        _camera = _springArm.GetNode<Camera3D>("Camera3D");
        _cameraRestPosition = _camera.Position;

        Input.MouseMode = Input.MouseModeEnum.Captured;

        CreateLockIndicator();
    }

    private void CreateLockIndicator()
    {
        _lockIndicator = new MeshInstance3D();

        // Small white dot — sits at center-mass, not above the head
        var sphere = new SphereMesh();
        sphere.Radius = 0.06f;
        sphere.Height = 0.12f;
        _lockIndicator.Mesh = sphere;

        _lockIndicatorMat = new StandardMaterial3D();
        _lockIndicatorMat.AlbedoColor = new Color(1f, 1f, 1f, 0.95f);
        _lockIndicatorMat.EmissionEnabled = true;
        _lockIndicatorMat.Emission = Colors.White;
        _lockIndicatorMat.EmissionEnergyMultiplier = 3f;
        _lockIndicatorMat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;

        // Render on top of everything so it's always visible through the enemy
        _lockIndicatorMat.NoDepthTest = true;
        _lockIndicatorMat.RenderPriority = 10;

        _lockIndicator.MaterialOverride = _lockIndicatorMat;

        _lockIndicator.Visible = false;
        _lockIndicator.TopLevel = true;
        AddChild(_lockIndicator);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // Escape toggle — always works
        if (@event.IsActionPressed("ui_cancel"))
        {
            Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured
                ? Input.MouseModeEnum.Visible
                : Input.MouseModeEnum.Captured;
        }

        // Lock-on toggle — always works
        if (@event.IsActionPressed("lock_on"))
        {
            if (_isLockedOn)
                DisengageLock();
            else
                TryLockOn();
        }

        // Mouse input
        if (@event is InputEventMouseMotion mouseMotion)
        {
            if (_isLockedOn)
            {
                // Mouse flick switches target
                if (_switchCooldown <= 0f && Mathf.Abs(mouseMotion.Relative.X) > MouseSwitchThreshold)
                {
                    int direction = mouseMotion.Relative.X > 0f ? 1 : -1;
                    SwitchTarget(direction);
                    _switchCooldown = SwitchCooldownDuration;
                }
            }
            else
            {
                // Normal free-look
                RotateY(-mouseMotion.Relative.X * MouseSensitivity);
                _springArm.RotateX(-mouseMotion.Relative.Y * MouseSensitivity);
                ClampPitch();
            }
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_target == null) return;
        float dt = (float)delta;

        // Cooldown timer
        if (_switchCooldown > 0f)
            _switchCooldown -= dt;

        if (_isLockedOn)
        {
            // Right stick switches target when locked on
            Vector2 stickInput = Input.GetVector("camera_left", "camera_right", "camera_up", "camera_down");
            if (_switchCooldown <= 0f && Mathf.Abs(stickInput.X) > StickSwitchThreshold)
            {
                int direction = stickInput.X > 0f ? 1 : -1;
                SwitchTarget(direction);
                _switchCooldown = SwitchCooldownDuration;
            }

            UpdateLockOn(dt);
        }

        // Follow target smoothly
        GlobalPosition = GlobalPosition.Lerp(_target.GlobalPosition, FollowSpeed * dt);

        // Camera shake
        UpdateShake(dt);
    }

    // ── Lock-On Camera ────────────────────────────────────────────────

    private void UpdateLockOn(float dt)
    {
        if (_lockTarget == null || !GodotObject.IsInstanceValid(_lockTarget))
        {
            DisengageLock();
            return;
        }

        // Check if target is still valid (ITargetable)
        if (_lockTarget is Game.Core.Interfaces.ITargetable targetable && !targetable.IsValidTarget)
        {
            DisengageLock();
            return;
        }

        // Range check — disengage if too far
        float distance = _target.GlobalPosition.DistanceTo(_lockTarget.GlobalPosition);
        if (distance > LockOnRange * 1.2f)
        {
            DisengageLock();
            return;
        }

        // Calculate direction from player to target on XZ plane
        Vector3 rawDir = _lockTarget.GlobalPosition - _target.GlobalPosition;
        float heightDiff = rawDir.Y;
        rawDir.Y = 0f;
        float horizontalDist = rawDir.Length();

        if (horizontalDist < 0.1f) return;

        Vector3 dir = rawDir / horizontalDist;

        // Yaw: rotate rig so camera sits behind player, looking toward target
        float desiredYaw = Mathf.Atan2(-dir.X, -dir.Z);
        float currentYaw = Rotation.Y;
        Rotation = new Vector3(
            Rotation.X,
            Mathf.LerpAngle(currentYaw, desiredYaw, LockOnYawSpeed * dt),
            Rotation.Z
        );

        // Pitch: default offset plus height compensation
        float desiredPitch = Mathf.DegToRad(LockOnPitchOffset);
        desiredPitch -= Mathf.Atan2(heightDiff, horizontalDist) * 0.5f;
        desiredPitch = Mathf.Clamp(desiredPitch, Mathf.DegToRad(MinPitch), Mathf.DegToRad(MaxPitch));

        Vector3 springRot = _springArm.Rotation;
        springRot.X = Mathf.Lerp(springRot.X, desiredPitch, LockOnPitchSpeed * dt);
        _springArm.Rotation = springRot;

        // Update lock indicator — center-mass position (TargetPosition = GlobalPos + (0,1,0))
        if (_lockTarget is Game.Core.Interfaces.ITargetable t)
            _lockIndicator.GlobalPosition = t.TargetPosition;
        else
            _lockIndicator.GlobalPosition = _lockTarget.GlobalPosition + new Vector3(0f, 1f, 0f);
    }

    // ── Target Selection ──────────────────────────────────────────────

    private void TryLockOn()
    {
        Node3D best = FindBestTarget();
        if (best != null)
        {
            _lockTarget = best;
            _isLockedOn = true;
            _lockIndicator.Visible = true;
            GD.Print($"[Camera] Locked on to: {best.Name}");
        }
    }

    public void DisengageLock()
    {
        _isLockedOn = false;
        _lockTarget = null;
        _lockIndicator.Visible = false;
    }

    private Node3D FindBestTarget()
    {
        var lockables = GetTree().GetNodesInGroup("Lockable");
        Node3D best = null;
        float bestScore = float.MaxValue;

        Camera3D cam = _camera;
        Vector3 camForward = -cam.GlobalTransform.Basis.Z;

        foreach (var node in lockables)
        {
            if (node is not Node3D candidate) continue;
            if (candidate is Game.Core.Interfaces.ITargetable t && !t.IsValidTarget) continue;

            float dist = _target.GlobalPosition.DistanceTo(candidate.GlobalPosition);
            if (dist > LockOnRange) continue;

            Vector3 toCandidate = (candidate.GlobalPosition - _target.GlobalPosition).Normalized();
            float dot = camForward.Dot(toCandidate);

            // Behind camera — skip
            if (dot < 0f) continue;

            // Score: prefer close + centered targets
            float score = dist * (1f - dot);
            if (score < bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    private void SwitchTarget(int direction)
    {
        var lockables = GetTree().GetNodesInGroup("Lockable");
        if (lockables.Count < 2) return;

        Camera3D cam = _camera;
        Vector3 camRight = cam.GlobalTransform.Basis.X;
        Node3D best = null;
        float bestScore = float.MaxValue;

        foreach (var node in lockables)
        {
            if (node is not Node3D candidate) continue;
            if (candidate == _lockTarget) continue;
            if (candidate is Game.Core.Interfaces.ITargetable t && !t.IsValidTarget) continue;

            float dist = _target.GlobalPosition.DistanceTo(candidate.GlobalPosition);
            if (dist > LockOnRange) continue;

            Vector3 toCandidate = (candidate.GlobalPosition - _target.GlobalPosition).Normalized();
            float rightDot = camRight.Dot(toCandidate);

            // Only consider targets in the requested direction
            if (direction > 0 && rightDot < 0.1f) continue;
            if (direction < 0 && rightDot > -0.1f) continue;

            float score = dist * (1f - Mathf.Abs(rightDot));
            if (score < bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        if (best != null)
        {
            _lockTarget = best;
            GD.Print($"[Camera] Switched lock to: {best.Name}");
        }
    }

    // ── Camera Shake ──────────────────────────────────────────────────

    public void Shake(float intensity, float duration)
    {
        _shakeIntensity = intensity;
        _shakeDuration = duration;
        _shakeTimer = duration;
    }

    private void UpdateShake(float dt)
    {
        if (_shakeTimer > 0f)
        {
            _shakeTimer -= dt;
            float strength = _shakeIntensity * (_shakeTimer / _shakeDuration);
            _camera.Position = _cameraRestPosition + new Vector3(
                (float)GD.RandRange(-strength, strength),
                (float)GD.RandRange(-strength, strength),
                0f
            );
        }
        else
        {
            _camera.Position = _cameraRestPosition;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private void ClampPitch()
    {
        Vector3 rot = _springArm.Rotation;
        rot.X = Mathf.Clamp(rot.X, Mathf.DegToRad(MinPitch), Mathf.DegToRad(MaxPitch));
        _springArm.Rotation = rot;
    }
}
