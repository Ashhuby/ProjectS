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

    // Lock-on indicator
    private MeshInstance3D _lockIndicator;
    private StandardMaterial3D _lockIndicatorMat;

    public bool IsLockedOn => _isLockedOn;
    public Node3D LockTarget => _lockTarget;

    public override void _Ready()
    {
        _target = GetParent().GetNode<Node3D>("PlayerCharacter");
        _springArm = GetNode<SpringArm3D>("SpringArm3D");
        _camera = _springArm.GetNode<Camera3D>("Camera3D");

        Input.MouseMode = Input.MouseModeEnum.Captured;

        CreateLockIndicator();
    }

    private void CreateLockIndicator()
    {
        _lockIndicator = new MeshInstance3D();
        var sphere = new SphereMesh();
        sphere.Radius = 0.2f;
        sphere.Height = 0.4f;
        _lockIndicator.Mesh = sphere;

        _lockIndicatorMat = new StandardMaterial3D();
        _lockIndicatorMat.AlbedoColor = new Color(1f, 0.85f, 0.1f, 0.8f);
        _lockIndicatorMat.EmissionEnabled = true;
        _lockIndicatorMat.Emission = new Color(1f, 0.85f, 0.1f);
        _lockIndicatorMat.EmissionEnergyMultiplier = 4f;
        _lockIndicatorMat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
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

            UpdateLockOnCamera(dt);
        }
        else
        {
            UpdateFreeLookCamera(dt);
        }

        // Follow player — always
        GlobalPosition = GlobalPosition.Lerp(_target.GlobalPosition, FollowSpeed * dt);
    }

    // ── Free Look ─────────────────────────────────────────────────────

    private void UpdateFreeLookCamera(float dt)
    {
        Vector2 stickInput = Input.GetVector("camera_left", "camera_right", "camera_up", "camera_down");

        if (stickInput != Vector2.Zero)
        {
            RotateY(-stickInput.X * StickSensitivity * dt);
            _springArm.RotateX(-stickInput.Y * StickSensitivity * dt);
            ClampPitch();
        }
    }

    // ── Lock-On Camera ────────────────────────────────────────────────

    private void UpdateLockOnCamera(float dt)
    {
        // Validate target still exists and is in range
        if (!IsInstanceValid(_lockTarget))
        {
            DisengageLock();
            return;
        }

        float distance = _lockTarget.GlobalPosition.DistanceTo(_target.GlobalPosition);
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

        // Update lock indicator position
        _lockIndicator.GlobalPosition = _lockTarget.GlobalPosition + new Vector3(0f, 2.2f, 0f);
        _lockIndicator.RotateY(3f * dt);
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
            GD.Print($"[LockOn] Locked onto {_lockTarget.Name}");
        }
        else
        {
            GD.Print("[LockOn] No valid target found");
        }
    }

    public void DisengageLock()
    {
        _isLockedOn = false;
        _lockTarget = null;
        _lockIndicator.Visible = false;
        GD.Print("[LockOn] Disengaged");
    }

    private void SwitchTarget(int direction)
    {
        if (_lockTarget == null) return;

        var candidates = GetTree().GetNodesInGroup("Lockable");
        Vector2 currentScreenPos = _camera.UnprojectPosition(_lockTarget.GlobalPosition);

        Node3D best = null;
        float bestDelta = float.MaxValue;

        foreach (var node in candidates)
        {
            if (node is not Node3D candidate) continue;
            if (candidate == _lockTarget) continue;

            float distance = candidate.GlobalPosition.DistanceTo(_target.GlobalPosition);
            if (distance > LockOnRange || distance < 0.5f) continue;
            if (_camera.IsPositionBehind(candidate.GlobalPosition)) continue;

            Vector2 screenPos = _camera.UnprojectPosition(candidate.GlobalPosition);
            float screenDelta = screenPos.X - currentScreenPos.X;

            // Only consider targets in the requested direction
            if (direction > 0 && screenDelta <= 0f) continue;
            if (direction < 0 && screenDelta >= 0f) continue;

            float absDelta = Mathf.Abs(screenDelta);
            if (absDelta < bestDelta)
            {
                bestDelta = absDelta;
                best = candidate;
            }
        }

        if (best != null)
        {
            _lockTarget = best;
            GD.Print($"[LockOn] Switched to {_lockTarget.Name}");
        }
    }

    private Node3D FindBestTarget()
    {
        var candidates = GetTree().GetNodesInGroup("Lockable");
        Node3D best = null;
        float bestScore = float.MaxValue;

        Vector2 screenCenter = GetViewport().GetVisibleRect().Size / 2f;

        foreach (var node in candidates)
        {
            if (node is not Node3D candidate) continue;

            float distance = candidate.GlobalPosition.DistanceTo(_target.GlobalPosition);
            if (distance > LockOnRange || distance < 0.5f) continue;

            // Skip targets behind the camera
            if (_camera.IsPositionBehind(candidate.GlobalPosition)) continue;

            // Prefer targets closer to screen center
            Vector2 screenPos = _camera.UnprojectPosition(candidate.GlobalPosition);
            float screenDist = screenPos.DistanceTo(screenCenter);

            // Score: weighted combination of world distance and screen distance
            float score = distance * 0.4f + screenDist * 0.01f;

            if (score < bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    private void ClampPitch()
    {
        Vector3 rot = _springArm.Rotation;
        rot.X = Mathf.Clamp(rot.X, Mathf.DegToRad(MinPitch), Mathf.DegToRad(MaxPitch));
        _springArm.Rotation = rot;
    }
}
