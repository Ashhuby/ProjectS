using Godot;
using System;
using System.Collections.Generic;

public enum CombatState
{
    Free,
    Attacking,
    Parrying,
    Stunned,
    Dead
}

public partial class PlayerCharacter : CharacterBody3D
{
    [Export] public float Speed = 5.0f;
    [Export] public float Acceleration = 25.0f;
    [Export] public float RotationSpeed = 10.0f;
    [Export] public float Gravity = 9.8f;
    [Export] public int MaxHealth = 100;
    [Export] public float ComboWindowDuration = 0.2f;

    // Node references
    private AnimationTree _animTree;
    private AnimationNodeStateMachinePlayback _stateMachine;
    private Hurtbox _hurtbox;
    private Hitbox _swordHitbox;
    private CameraController _cameraController;

    // Combat state
    private CombatState _combatState = CombatState.Free;
    private int _comboStep = 0;
    private const int MaxComboSteps = 2;
    private bool _attackBuffered = false;
    private bool _isParryActive = false;
    private int _currentHealth;

    // Combo window — grace period after attack animation ends
    private bool _inComboWindow = false;
    private float _comboWindowTimer = 0f;

    // Debug parry indicator
    private MeshInstance3D _parryIndicator;

    // UI event — fires (currentHealth, maxHealth)
    public event Action<int, int> HealthChanged;

    public override void _Ready()
    {
        _currentHealth = MaxHealth;

        _animTree = GetNode<AnimationTree>("AnimationTree");
        _stateMachine = (AnimationNodeStateMachinePlayback)_animTree.Get("parameters/playback");

        _hurtbox = GetNode<Hurtbox>("Hurtbox");
        _hurtbox.DamageReceived += OnDamageReceived;

        // Adjust this path to match your actual scene tree.
        // Player → BodyRig → Skeleton3D → BoneAttachment3D → Sword → Hitbox
        _swordHitbox = GetNode<Hitbox>("Player/BodyRig/Skeleton3D/BoneAttachment3D/Sword/Hitbox");

        // Camera controller for lock-on
        _cameraController = GetParent().GetNode<CameraController>("CameraRig");

        // Debug: green sphere above head shows when parry window is active
        _parryIndicator = new MeshInstance3D();
        var sphere = new SphereMesh();
        sphere.Radius = 0.15f;
        sphere.Height = 0.3f;
        _parryIndicator.Mesh = sphere;
        var indicatorMat = new StandardMaterial3D();
        indicatorMat.AlbedoColor = new Color(0f, 1f, 0.5f);
        indicatorMat.EmissionEnabled = true;
        indicatorMat.Emission = new Color(0f, 1f, 0.5f);
        indicatorMat.EmissionEnergyMultiplier = 2f;
        _parryIndicator.MaterialOverride = indicatorMat;
        _parryIndicator.Position = new Vector3(0f, 2.2f, 0f);
        _parryIndicator.Visible = false;
        AddChild(_parryIndicator);

        HealthChanged?.Invoke(_currentHealth, MaxHealth);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_combatState == CombatState.Dead) return;

        if (@event.IsActionPressed("attack"))
        {
            HandleAttackInput();
        }
        else if (@event.IsActionPressed("parry"))
        {
            HandleParryInput();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        Vector3 velocity = Velocity;

        // Gravity always applies
        if (!IsOnFloor())
        {
            velocity.Y -= Gravity * dt;
        }

        // Movement only in Free state
        if (_combatState == CombatState.Free)
        {
            Vector2 inputDir = Input.GetVector("move_left", "move_right", "move_forward", "move_back");

            Vector3 moveDir = Vector3.Zero;

            if (inputDir != Vector2.Zero)
            {
                Camera3D camera = GetViewport().GetCamera3D();

                Vector3 camForward = -camera.GlobalTransform.Basis.Z;
                camForward.Y = 0f;
                camForward = camForward.Normalized();

                Vector3 camRight = camera.GlobalTransform.Basis.X;
                camRight.Y = 0f;
                camRight = camRight.Normalized();

                moveDir = (camRight * inputDir.X - camForward * inputDir.Y).Normalized();
            }

            if (moveDir != Vector3.Zero)
            {
                velocity.X = Mathf.MoveToward(velocity.X, moveDir.X * Speed, Acceleration * dt);
                velocity.Z = Mathf.MoveToward(velocity.Z, moveDir.Z * Speed, Acceleration * dt);

                float targetAngle = Mathf.Atan2(moveDir.X, moveDir.Z);
                Rotation = new Vector3(
                    Rotation.X,
                    Mathf.LerpAngle(Rotation.Y, targetAngle, RotationSpeed * dt),
                    Rotation.Z
                );

                string current = _stateMachine.GetCurrentNode();
                if (current == "Idle")
                {
                    _stateMachine.Travel("StartMove");
                }
            }
            else
            {
                velocity.X = Mathf.MoveToward(velocity.X, 0f, Acceleration * dt);
                velocity.Z = Mathf.MoveToward(velocity.Z, 0f, Acceleration * dt);

                string current = _stateMachine.GetCurrentNode();
                if (current != "Idle")
                {
                    _stateMachine.Travel("Idle");
                }
            }
        }
        else
        {
            // Kill horizontal movement during combat states
            velocity.X = Mathf.MoveToward(velocity.X, 0f, Acceleration * dt);
            velocity.Z = Mathf.MoveToward(velocity.Z, 0f, Acceleration * dt);

            // Combo window countdown
            if (_inComboWindow)
            {
                _comboWindowTimer -= dt;
                if (_comboWindowTimer <= 0f)
                {
                    _inComboWindow = false;
                    ReturnToFree();
                }
            }
        }

        // Lock-on: face the target regardless of combat state
        if (_combatState != CombatState.Dead && _cameraController != null
            && _cameraController.IsLockedOn && _cameraController.LockTarget != null)
        {
            Vector3 toTarget = (_cameraController.LockTarget.GlobalPosition - GlobalPosition);
            toTarget.Y = 0f;
            if (toTarget.LengthSquared() > 0.01f)
            {
                toTarget = toTarget.Normalized();
                float lockAngle = Mathf.Atan2(toTarget.X, toTarget.Z);
                Rotation = new Vector3(
                    Rotation.X,
                    Mathf.LerpAngle(Rotation.Y, lockAngle, RotationSpeed * dt),
                    Rotation.Z
                );
            }
        }

        Velocity = velocity;
        MoveAndSlide();
    }

    // ── Combat State Transitions ──────────────────────────────────────

    private void HandleAttackInput()
    {
        switch (_combatState)
        {
            case CombatState.Free:
                StartAttack(0);
                break;
            case CombatState.Attacking:
                if (_inComboWindow && _comboStep < MaxComboSteps - 1)
                {
                    // Late input during grace period — chain immediately
                    _inComboWindow = false;
                    StartAttack(_comboStep + 1);
                }
                else if (_comboStep < MaxComboSteps - 1)
                {
                    // Buffer during the animation
                    _attackBuffered = true;
                }
                break;
        }
    }

    private void HandleParryInput()
    {
        if (_combatState == CombatState.Free)
        {
            EnterParry();
        }
    }

    private void StartAttack(int step)
    {
        _combatState = CombatState.Attacking;
        _comboStep = step;
        _attackBuffered = false;
        _inComboWindow = false;

        string animState = step switch
        {
            0 => "Attack1",
            1 => "Attack2",
            _ => "Attack1"
        };
        _stateMachine.Travel(animState);
        GD.Print($"[Combat] Attack{step + 1}");
    }

    private void EnterParry()
    {
        _combatState = CombatState.Parrying;
        _attackBuffered = false;
        _isParryActive = false;
        _inComboWindow = false;
        _stateMachine.Travel("Parry");
        GD.Print("[Combat] Parry started");
    }

    private void EnterStunned()
    {
        _combatState = CombatState.Stunned;
        _attackBuffered = false;
        _comboStep = 0;
        _isParryActive = false;
        _inComboWindow = false;
        _parryIndicator.Visible = false;
        _swordHitbox.Deactivate();
        _stateMachine.Travel("HitReaction");
        GD.Print("[Combat] Stunned");
    }

    private void Die()
    {
        _combatState = CombatState.Dead;
        _attackBuffered = false;
        _comboStep = 0;
        _isParryActive = false;
        _inComboWindow = false;
        _parryIndicator.Visible = false;
        _swordHitbox.Deactivate();
        _stateMachine.Travel("Death");
        GD.Print("[Combat] Dead");

        // Release lock-on on death
        if (_cameraController != null && _cameraController.IsLockedOn)
            _cameraController.DisengageLock();
    }

    private void ReturnToFree()
    {
        _combatState = CombatState.Free;
        _comboStep = 0;
        _attackBuffered = false;
        _isParryActive = false;
        _inComboWindow = false;
        _parryIndicator.Visible = false;
        _stateMachine.Travel("Idle");
    }

    // ── Damage Handling ───────────────────────────────────────────────

    private void OnDamageReceived(DamageData data)
    {
        if (_combatState == CombatState.Dead) return;

        GD.Print($"[Combat] Damage received. State: {_combatState}, ParryActive: {_isParryActive}");

        if (_combatState == CombatState.Parrying && _isParryActive)
        {
            OnParrySuccess(data);
            return;
        }

        _currentHealth = Mathf.Max(_currentHealth - data.Amount, 0);
        HealthChanged?.Invoke(_currentHealth, MaxHealth);

        GD.Print($"[Combat] HP: {_currentHealth}/{MaxHealth}");

        if (_currentHealth <= 0)
        {
            Die();
        }
        else
        {
            EnterStunned();
        }
    }

    private void OnParrySuccess(DamageData data)
    {
        GD.Print($"[Combat] PARRY SUCCESS against {data.Source?.Name}");
        // TODO: notify the attacker to enter their Stunned state
    }

    // ── Animation Callback Methods ────────────────────────────────────
    // Called by method call tracks on combat animations.

    public void OnAttackHitboxActivate()
    {
        _swordHitbox.Activate();
    }

    public void OnAttackHitboxDeactivate()
    {
        _swordHitbox.Deactivate();
    }

    public void OnAttackAnimationFinished()
    {
        if (_attackBuffered && _comboStep < MaxComboSteps - 1)
        {
            // Input was buffered during the swing — chain immediately
            StartAttack(_comboStep + 1);
        }
        else if (_comboStep < MaxComboSteps - 1)
        {
            // No buffer yet — open a grace window for late input
            _inComboWindow = true;
            _comboWindowTimer = ComboWindowDuration;
            GD.Print("[Combat] Combo window open");
        }
        else
        {
            ReturnToFree();
        }
    }

    public void OnParryWindowOpen()
    {
        _isParryActive = true;
        _parryIndicator.Visible = true;
        GD.Print("[Combat] Parry window OPEN");
    }

    public void OnParryWindowClose()
    {
        _isParryActive = false;
        _parryIndicator.Visible = false;
        GD.Print("[Combat] Parry window CLOSED");
    }

    public void OnParryAnimationFinished()
    {
        ReturnToFree();
    }

    public void OnStunAnimationFinished()
    {
        ReturnToFree();
    }
}
