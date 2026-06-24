using Godot;

public partial class Player : Node3D
{
    [Export] public float WalkSpeed = 4.0f;
    [Export] public float MouseSensitivity = 0.002f;
    [Export] public float ControllerSensitivity = 2.0f;
    [Export] public float CameraPitchMin = -60.0f;
    [Export] public float CameraPitchMax = 60.0f;
    [Export] public float BlendTime = 0.2f;

    private AnimationPlayer _animPlayer;
    private SpringArm3D _springArm;
    private bool _wasMoving = false;
    private bool _startedMoving = false;

    public override void _Ready()
    {
        Input.MouseMode = Input.MouseModeEnum.Captured;

        _springArm = GetNode<SpringArm3D>("SpringArm3D");
        if (_springArm == null)
            GD.PrintErr("SpringArm3D not found!");
        else
            _springArm.CollisionMask = 0;

        Node model = GetNode<Node>("Model");
        if (model != null)
            _animPlayer = model.GetNode<AnimationPlayer>("AnimationPlayer");
        else
            GD.PrintErr("Model not found!");
    }

    public override void _PhysicsProcess(double delta)
    {
        float d = (float)delta;

        // --- CAMERA (Mouse + Controller Right Stick) ---
        Vector2 mouse = Input.GetLastMouseVelocity();
        float yaw = -mouse.X * d * MouseSensitivity;   // negative = rotate right
        float pitch = -mouse.Y * d * MouseSensitivity; // negative = look up

        // Controller right stick
        float stickYaw = Input.GetAxis("look_left", "look_right") * d * ControllerSensitivity;
        float stickPitch = Input.GetAxis("look_up", "look_down") * d * ControllerSensitivity;

        _springArm.RotateY(yaw + stickYaw);
        _springArm.RotateX(pitch + stickPitch);

        // Clamp vertical
        Vector3 rot = _springArm.Rotation;
        rot.X = Mathf.Clamp(rot.X, Mathf.DegToRad(CameraPitchMin), Mathf.DegToRad(CameraPitchMax));
        _springArm.Rotation = rot;

        // --- MOVEMENT (WASD + Left Stick) ---
        Vector2 inputDir = Input.GetVector("move_left", "move_right", "move_forward", "move_backward");
        if (inputDir.Length() < 0.2f)
            inputDir = Vector2.Zero;

        // Get camera forward/right (ignore pitch)
        Vector3 forward = -_springArm.GlobalTransform.Basis.Z;
        Vector3 right = _springArm.GlobalTransform.Basis.X;
        forward.Y = 0;
        right.Y = 0;
        forward = forward.Normalized();
        right = right.Normalized();

        // Movement vector relative to camera
        Vector3 move = (forward * inputDir.Y + right * inputDir.X).Normalized();

        if (move.Length() > 0.1f)
        {
            // Move
            Position += move * WalkSpeed * d;

            // Face movement direction (smooth turning)
            float targetAngle = Mathf.Atan2(move.X, move.Z);
            float currentAngle = Rotation.Y;
            float newAngle = Mathf.LerpAngle(currentAngle, targetAngle, 10.0f * d); // 10 = turn speed
            Rotation = new Vector3(0, newAngle, 0);
        }

        // --- ANIMATIONS ---
        if (_animPlayer != null)
        {
            bool moving = inputDir.Length() > 0.1f;
            if (moving)
            {
                if (!_wasMoving)
                {
                    _wasMoving = true;
                    _startedMoving = false;
                    _animPlayer.Play("StartMove", BlendTime);
                    _animPlayer.Queue("MoveForward");
                }
                else if (!_startedMoving && _animPlayer.CurrentAnimation == "MoveForward")
                {
                    _startedMoving = true;
                }
            }
            else
            {
                if (_wasMoving)
                {
                    _wasMoving = false;
                    _startedMoving = false;
                    _animPlayer.Play("Idle", BlendTime);
                }
            }
        }
    }
}