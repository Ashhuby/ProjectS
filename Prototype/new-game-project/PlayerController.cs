using Godot;
using System;

public partial class PlayerController : CharacterBody3D
{
    [ExportCategory("Movement Speeds")]
    [Export] public float WalkSpeed = 5.0f;
    [Export] public float SlideSpeed = 12.0f; // Bumped up slightly for a better feel
    [Export] public float DashSpeed = 25.0f;
    [Export] public float JumpVelocity = 4.5f;

    [ExportCategory("Camera Settings")]
    [Export] public float MouseSensitivity = 0.002f;
    [Export] public float NormalHeight = 0.6f;
    [Export] public float SlideHeight = 0.0f; // How low the camera drops when sliding

    // Node References
    private Node3D _neck;

    // State tracking
    private bool _isSliding = false;
    private bool _isDashing = false;
    
    // Timers
    private float _dashDuration = 0.2f;
    private float _dashTimer = 0.0f;
    private float _dashCooldown = 1.0f;
    private float _dashCooldownTimer = 0.0f;

    // Gravity
    private float gravity = ProjectSettings.GetSetting("physics/3d/default_gravity").AsSingle();

    public override void _Ready()
    {
        _neck = GetNode<Node3D>("Neck");
        // Ensure neck starts at normal height
        _neck.Position = new Vector3(_neck.Position.X, NormalHeight, _neck.Position.Z);
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // 1. Handle Mouse Look
        if (@event is InputEventMouseMotion mouseMotion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            RotateY(-mouseMotion.Relative.X * MouseSensitivity);
            _neck.RotateX(-mouseMotion.Relative.Y * MouseSensitivity);
            
            Vector3 neckRot = _neck.Rotation;
            neckRot.X = Mathf.Clamp(neckRot.X, Mathf.DegToRad(-90), Mathf.DegToRad(90));
            _neck.Rotation = neckRot;
        }

        // 2. Handle Mech Parry
        if (@event is InputEventMouseButton mouseEvent && 
            mouseEvent.ButtonIndex == MouseButton.Right && 
            mouseEvent.Pressed && 
            Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            ParryAllArms();
        }

        // 3. UI Cancel
        if (@event.IsActionPressed("ui_cancel"))
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        Vector3 velocity = Velocity;
        float floatDelta = (float)delta;

        // Handle Gravity
        if (!IsOnFloor())
        {
            velocity.Y -= gravity * floatDelta;
        }

        // Handle Jump
        if (Input.IsActionJustPressed("jump") && IsOnFloor() && !_isDashing)
        {
            velocity.Y = JumpVelocity;
        }

        // Handle Dash Cooldowns
        if (_dashCooldownTimer > 0)
            _dashCooldownTimer -= floatDelta;

        // Get Input Direction
        Vector2 inputDir = Input.GetVector("move_left", "move_right", "move_forward", "move_backward");
        Vector3 direction = (Transform.Basis * new Vector3(inputDir.X, 0, inputDir.Y)).Normalized();

        // Handle Dash Activation
        if (Input.IsActionJustPressed("dash") && _dashCooldownTimer <= 0 && direction != Vector3.Zero)
        {
            _isDashing = true;
            _dashTimer = _dashDuration;
            _dashCooldownTimer = _dashCooldown;
        }

        // Handle Slide Activation
        _isSliding = Input.IsActionPressed("slide") && IsOnFloor() && !_isDashing;

        // --- NEW: Smooth Camera Height Adjustment for Sliding ---
        float targetHeight = _isSliding ? SlideHeight : NormalHeight;
        Vector3 currentNeckPos = _neck.Position;
        // Smoothly interpolate (Lerp) the camera Y position
        currentNeckPos.Y = Mathf.Lerp(currentNeckPos.Y, targetHeight, 10.0f * floatDelta);
        _neck.Position = currentNeckPos;
        // --------------------------------------------------------

        // Calculate Movement
        if (_isDashing)
        {
            velocity.X = direction.X * DashSpeed;
            velocity.Z = direction.Z * DashSpeed;

            _dashTimer -= floatDelta;
            if (_dashTimer <= 0)
            {
                _isDashing = false;
            }
        }
        else
        {
            float currentSpeed = _isSliding ? SlideSpeed : WalkSpeed;

            if (direction != Vector3.Zero)
            {
                velocity.X = direction.X * currentSpeed;
                velocity.Z = direction.Z * currentSpeed;
            }
            else
            {
                velocity.X = Mathf.MoveToward(Velocity.X, 0, currentSpeed);
                velocity.Z = Mathf.MoveToward(Velocity.Z, 0, currentSpeed);
            }
        }

        Velocity = velocity;
        MoveAndSlide();
    }

    private void ParryAllArms()
    {
        GetTree().CallGroup("spider_arms", "PlayParry");
    }
}