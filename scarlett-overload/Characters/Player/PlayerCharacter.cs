using Game.Autoloads;
using Game.Characters;
using Game.Core.Data;
using Godot;
using System;

public enum CombatState
{
    Free,
    Attacking,
    Parrying,
    Dashing,
    Stunned,
    Dead
}

/// <summary>
/// Thin coordinator. Extends CharacterBase for health, knockback,
/// gravity, and the damage pipeline. Delegates movement to PlayerMovement,
/// combat to PlayerCombat, and dashing to PlayerDash.
///
/// This script stays on the root CharacterBody3D node.
/// Animation method call tracks still target this class — each callback
/// is a one-line delegate to _combat.
///
/// Requires "dash" input action in Project Settings → Input Map.
/// Map to Shift (keyboard) and B/Circle button (gamepad).
/// </summary>
public partial class PlayerCharacter : CharacterBase
{
    [ExportGroup("Weapon")]
    [Export] public WeaponData Weapon { get; set; }

    /// <summary>
    /// Local-space offset from the Sword node origin to the rapier tip.
    /// Used to position the vital thrust sparkle emitter.
    /// Tune in the inspector until the sparkle sits at the blade tip.
    /// </summary>
    [Export] public Vector3 RapierTipOffset { get; set; } = new(0f, 1.2f, 0f);

    [ExportGroup("Dash")]
    [Export] public DashStats DashConfig { get; set; }

    // ── Components ────────────────────────────────────────────────────

    private PlayerMovement _movement;
    private PlayerCombat _combat;
    private PlayerDash _dash;

    // ══════════════════════════════════════════════════════════════════
    //  CHARACTERBASE LIFECYCLE
    // ══════════════════════════════════════════════════════════════════

    protected override void Initialize()
    {
        Stats ??= new CharacterStats
        {
            MaxHealth = 100,
            MoveSpeed = 5f,
            Acceleration = 25f,
            RotationSpeed = 10f,
            Gravity = 9.8f,
            KnockbackDecay = 12f
        };

        // ── Resolve node references ───────────────────────────────────
        var animTree = GetNode<AnimationTree>("AnimationTree");
        var playback = (AnimationNodeStateMachinePlayback)
            animTree.Get("parameters/playback");

        var hitbox = GetNode<Hitbox>(
            "Player/BodyRig/Skeleton3D/BoneAttachment3D/Sword/Hitbox");
        var swordNode = GetNode<Node3D>(
            "Player/BodyRig/Skeleton3D/BoneAttachment3D/Sword");
        var camera = GetParent().GetNode<CameraController>("CameraRig");

        // ── Create components ─────────────────────────────────────────
        _movement = new PlayerMovement(this, playback, camera, Stats);
        _movement.SubscribeEvents();

        _dash = new PlayerDash(DashConfig ?? new DashStats(), SetHurtboxActive);

        _combat = new PlayerCombat(this, playback, hitbox, camera, Weapon, _dash);
        _combat.SubscribeEvents();
        _combat.SetupVitalSparkle(swordNode, RapierTipOffset);

        // ── Wire events ───────────────────────────────────────────────
        hitbox.HitConnected += _combat.OnHitConnected;

        if (Weapon != null)
            GD.Print($"[Player] Weapon: {Weapon.WeaponName} ({Weapon.MaxComboSteps} combo steps)");
        else
            GD.Print("[Player] No weapon — using fallback values");
    }

    public override void _ExitTree()
    {
        base._ExitTree();
        _combat?.UnsubscribeEvents();
        _movement?.UnsubscribeEvents();
    }

    // ══════════════════════════════════════════════════════════════════
    //  INPUT
    // ══════════════════════════════════════════════════════════════════

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_combat.State == CombatState.Dead) return;

        if (@event.IsActionPressed("attack"))
            _combat.HandleAttackInput();
        else if (@event.IsActionPressed("parry"))
            _combat.HandleParryInput();
        else if (@event.IsActionPressed("dash"))
        {
            Vector3 dashDir = PlayerDash.ComputeDirection(this);
            _combat.HandleDashInput(dashDir);
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  CHARACTERBASE OVERRIDES
    // ══════════════════════════════════════════════════════════════════

    protected override Vector3 ProcessMovement(Vector3 velocity, float dt)
    {
        if (_combat.State == CombatState.Dashing && _dash != null)
        {
            velocity.X = _dash.DashVelocity.X;
            velocity.Z = _dash.DashVelocity.Z;
            return velocity;
        }

        if (_combat.State != CombatState.Free)
        {
            velocity.X = 0f;
            velocity.Z = 0f;
            return velocity;
        }

        return _movement.ComputeVelocity(velocity, dt);
    }

    protected override void ProcessUpdate(float dt)
    {
        _combat.Tick(dt);
        _movement.Tick(dt);

        if (_combat.State != CombatState.Dead)
            _movement.UpdateRotation(dt, isInCombat: _combat.State != CombatState.Free);

        if (_combat.State == CombatState.Free)
            _movement.UpdateLocomotionAnimation();
    }

    protected override bool ShouldTakeDamage(DamageData data)
    {
        return _combat.ShouldTakeDamage(data);
    }

    protected override void OnDamageTaken(DamageData data)
    {
        _combat.OnDamageTaken(data, survived: CurrentHealth > 0);
    }

    protected override void OnDeath()
    {
        _combat.OnDeath();
        GameManager.Instance?.TriggerGameOver();
    }

    // ══════════════════════════════════════════════════════════════════
    //  ANIMATION CALLBACKS
    // ══════════════════════════════════════════════════════════════════

    public void OnAttackHitboxActivate() => _combat.OnAttackHitboxActivate();
    public void OnAttackHitboxDeactivate() => _combat.OnAttackHitboxDeactivate();
    public void OnAttackAnimationFinished() => _combat.OnAttackAnimationFinished();
    public void OnParryWindowOpen() => _combat.OnParryWindowOpen();
    public void OnParryWindowClose() => _combat.OnParryWindowClose();
    public void OnParryAnimationFinished() => _combat.OnParryAnimationFinished();
    public void OnStunAnimationFinished() => _combat.OnStunAnimationFinished();
}
