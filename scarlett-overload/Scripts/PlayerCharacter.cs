using Game.Autoloads;
using Game.Core.Data;
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
    [ExportGroup("Movement")]
    [Export] public float Speed = 5.0f;
    [Export] public float Acceleration = 25.0f;
    [Export] public float RotationSpeed = 10.0f;
    [Export] public float Gravity = 9.8f;

    [ExportGroup("Health")]
    [Export] public int MaxHealth = 100;

    [ExportGroup("Weapon")]
    /// <summary>
    /// Assign a WeaponData .tres resource here. The attack chain defines
    /// damage, knockback, hitstop, and shake per combo step.
    /// Leave null to use the fallback values below.
    /// </summary>
    [Export] public WeaponData Weapon { get; set; }

    [ExportGroup("Combat Fallbacks")]
    [Export] public float ComboWindowDuration = 0.2f;
    [Export] public float HitStopDuration = 0.06f;
    [Export] public float HitStopTimeScale = 0.05f;
    [Export] public float DealHitShakeIntensity = 0.15f;
    [Export] public float TakeHitShakeIntensity = 0.25f;
    [Export] public float KnockbackDecay = 12f;
    [Export] public Vector3 SwordTipOffset = new Vector3(0f, 0f, -0.8f);

    // Node references
    private AnimationTree _animTree;
    private AnimationNodeStateMachinePlayback _stateMachine;
    private Hurtbox _hurtbox;
    private Hitbox _swordHitbox;
    private CameraController _cameraController;
    private Node3D _swordNode;
    private SwordTrail _swordTrail;

    // Combat state
    private CombatState _combatState = CombatState.Free;
    private int _comboStep = 0;
    private bool _attackBuffered = false;
    private bool _isParryActive = false;
    private int _currentHealth;

    // Current attack data — set per combo step from WeaponData
    private AttackData _currentAttack;

    // Combo steps — driven by weapon's attack chain length, fallback to 2
    private int MaxComboSteps => Weapon?.MaxComboSteps ?? 2;

    // Combo window — grace period after attack animation ends
    private bool _inComboWindow = false;
    private float _comboWindowTimer = 0f;

    // Knockback
    private Vector3 _knockbackVelocity = Vector3.Zero;

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

        _swordHitbox = GetNode<Hitbox>("Player/BodyRig/Skeleton3D/BoneAttachment3D/Sword/Hitbox");
        _swordHitbox.HitConnected += OnHitDealt;

        // Sword trail
        _swordNode = GetNode<Node3D>("Player/BodyRig/Skeleton3D/BoneAttachment3D/Sword");
        _swordTrail = new SwordTrail();

        // Trail visuals from weapon data if available
        if (Weapon != null)
        {
            _swordTrail.TipColor = Weapon.TrailTipColor;
            _swordTrail.BaseColor = Weapon.TrailBaseColor;
            _swordTrail.MaxPoints = Weapon.TrailMaxPoints;
            _swordTrail.Jitter = Weapon.TrailJitter;
        }

        _swordTrail.Initialize(_swordNode, SwordTipOffset);
        AddChild(_swordTrail);

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

        if (Weapon != null)
            GD.Print($"[Combat] Weapon loaded: {Weapon.WeaponName} ({MaxComboSteps} combo steps)");
        else
            GD.Print("[Combat] No weapon assigned — using fallback values");
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

        if (!IsOnFloor())
        {
            velocity.Y -= Gravity * dt;
        }

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
            velocity.X = _knockbackVelocity.X;
            velocity.Z = _knockbackVelocity.Z;
            _knockbackVelocity = _knockbackVelocity.Lerp(Vector3.Zero, KnockbackDecay * dt);
            if (_knockbackVelocity.LengthSquared() < 0.01f)
                _knockbackVelocity = Vector3.Zero;

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
                    _inComboWindow = false;
                    StartAttack(_comboStep + 1);
                }
                else if (_comboStep < MaxComboSteps - 1)
                {
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

        // Pull attack data from weapon resource (null = use hitbox defaults)
        _currentAttack = Weapon?.GetAttack(step);

        string animState = step switch
        {
            0 => "Attack1",
            1 => "Attack2",
            _ => "Attack1"
        };
        _stateMachine.Travel(animState);

        string attackName = _currentAttack?.AttackName ?? $"Attack{step + 1}";
        GD.Print($"[Combat] {attackName} (step {step + 1}/{MaxComboSteps})");
    }

    private void EnterParry()
    {
        _combatState = CombatState.Parrying;
        _attackBuffered = false;
        _isParryActive = false;
        _inComboWindow = false;
        _currentAttack = null;
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
        _currentAttack = null;
        _parryIndicator.Visible = false;
        _swordHitbox.Deactivate();
        _swordTrail.StopEmitting();
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
        _currentAttack = null;
        _parryIndicator.Visible = false;
        _swordHitbox.Deactivate();
        _swordTrail.StopEmitting();
        _stateMachine.Travel("Death");
        GD.Print("[Combat] Dead");

        if (_cameraController != null && _cameraController.IsLockedOn)
            _cameraController.DisengageLock();

        EventBus.Instance?.EmitEntityDied(this);
    }

    private void ReturnToFree()
    {
        _combatState = CombatState.Free;
        _comboStep = 0;
        _attackBuffered = false;
        _isParryActive = false;
        _inComboWindow = false;
        _currentAttack = null;
        _knockbackVelocity = Vector3.Zero;
        _parryIndicator.Visible = false;
        _stateMachine.Travel("Idle");
    }

    // ── Damage Handling ───────────────────────────────────────────────

    private void OnHitDealt(DamageData data)
    {
        // Game feel values from AttackData, falling back to [Export] defaults
        float stopDur = _currentAttack?.HitStopDuration ?? HitStopDuration;
        float stopScale = _currentAttack?.HitStopTimeScale ?? HitStopTimeScale;
        float shakeStr = _currentAttack?.CameraShakeIntensity ?? DealHitShakeIntensity;
        float shakeDur = _currentAttack?.CameraShakeDuration ?? 0.15f;

        ApplyHitStop(stopDur, stopScale);
        _cameraController?.Shake(shakeStr, shakeDur);
        GameVFX.SpawnHitImpact(this, data.HitPosition, data.KnockbackDirection);

        EventBus.Instance?.EmitHitLanded(data);
    }

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

        ApplyHitStop(HitStopDuration, HitStopTimeScale);
        _cameraController?.Shake(TakeHitShakeIntensity, 0.2f);

        GameVFX.SpawnScreenFlash(this, new Color(1f, 0.1f, 0.1f, 0.25f), 0.1f);

        _knockbackVelocity = data.KnockbackDirection;

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
        _cameraController?.Shake(DealHitShakeIntensity, 0.1f);
        ApplyHitStop(HitStopDuration, HitStopTimeScale);
        var parryPos = GlobalPosition + new Vector3(0f, 1.2f, 0f);
        GameVFX.SpawnParryImpact(this, parryPos);

        EventBus.Instance?.EmitParrySucceeded(data);
    }

    private void ApplyHitStop(float duration, float timeScale)
    {
        Engine.TimeScale = timeScale;
        GetTree().CreateTimer(duration, true, false, true).Timeout += () =>
        {
            Engine.TimeScale = 1.0;
        };
    }

    // ── Animation Callback Methods ────────────────────────────────────
    // Called by method call tracks on combat animations.

    public void OnAttackHitboxActivate()
    {
        _swordHitbox.Activate(_currentAttack);
        _swordTrail.StartEmitting();
    }

    public void OnAttackHitboxDeactivate()
    {
        _swordHitbox.Deactivate();
        _swordTrail.StopEmitting();
    }

    public void OnAttackAnimationFinished()
    {
        if (_attackBuffered && _comboStep < MaxComboSteps - 1)
        {
            StartAttack(_comboStep + 1);
        }
        else if (_comboStep < MaxComboSteps - 1)
        {
            _inComboWindow = true;
            _comboWindowTimer = _currentAttack?.ComboWindowDuration ?? ComboWindowDuration;
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
