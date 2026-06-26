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
    Stunned,
    Dead
}

/// <summary>
/// Thin coordinator. Extends CharacterBase for health, knockback,
/// gravity, and the damage pipeline. Delegates movement to PlayerMovement
/// and combat to PlayerCombat.
///
/// This script stays on the root CharacterBody3D node.
/// Animation method call tracks still target this class — each callback
/// is a one-line delegate to _combat.
/// </summary>
public partial class PlayerCharacter : CharacterBase
{
    [ExportGroup("Weapon")]
    [Export] public WeaponData Weapon { get; set; }
    [Export] public Vector3 SwordTipOffset { get; set; } = new(0f, 0f, -0.8f);

    // ── Components ────────────────────────────────────────────────────

    private PlayerMovement _movement;
    private PlayerCombat _combat;

    // ══════════════════════════════════════════════════════════════════
    //  CHARACTERBASE LIFECYCLE
    // ══════════════════════════════════════════════════════════════════

    protected override void Initialize()
    {
        // Fallback stats when no .tres assigned in the inspector
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

        _combat = new PlayerCombat(this, playback, hitbox, camera, Weapon);
        _combat.CreateParryIndicator();

        // ── Sword trail ───────────────────────────────────────────────
        var trail = new SwordTrail();
        if (Weapon != null)
        {
            trail.TipColor = Weapon.TrailTipColor;
            trail.BaseColor = Weapon.TrailBaseColor;
            trail.MaxPoints = Weapon.TrailMaxPoints;
            trail.Jitter = Weapon.TrailJitter;
        }
        trail.Initialize(swordNode, SwordTipOffset);
        AddChild(trail);
        _combat.SetTrail(trail);

        // ── Wire events ───────────────────────────────────────────────
        hitbox.HitConnected += _combat.OnHitConnected;

        if (Weapon != null)
            GD.Print($"[Player] Weapon: {Weapon.WeaponName} ({Weapon.MaxComboSteps} combo steps)");
        else
            GD.Print("[Player] No weapon — using fallback values");
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
    }

    // ══════════════════════════════════════════════════════════════════
    //  CHARACTERBASE OVERRIDES
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Drive horizontal velocity. Only applies input movement when
    /// in Free state — combat states return zero horizontal velocity
    /// (CharacterBase handles knockback separately).
    /// </summary>
    protected override Vector3 ProcessMovement(Vector3 velocity, float dt)
    {
        if (_combat.State != CombatState.Free)
        {
            velocity.X = 0f;
            velocity.Z = 0f;
            return velocity;
        }
        return _movement.ComputeVelocity(velocity, dt);
    }

    /// <summary>
    /// Called every physics frame after MoveAndSlide.
    /// Ticks the combat FSM and updates rotation/animation.
    /// </summary>
    protected override void ProcessUpdate(float dt)
    {
        _combat.Tick(dt);

        if (_combat.State != CombatState.Dead)
            _movement.UpdateRotation(dt, isInCombat: _combat.State != CombatState.Free);

        if (_combat.State == CombatState.Free)
            _movement.UpdateLocomotionAnimation();
    }

    /// <summary>
    /// Parry check. If the parry window is active, PlayerCombat blocks
    /// the damage and fires parry effects.
    /// </summary>
    protected override bool ShouldTakeDamage(DamageData data)
    {
        return _combat.ShouldTakeDamage(data);
    }

    /// <summary>
    /// Called after damage is applied (health reduced, knockback set).
    /// Delegates to combat for hit reaction + game feel.
    /// </summary>
    protected override void OnDamageTaken(DamageData data)
    {
        _combat.OnDamageTaken(data, survived: CurrentHealth > 0);
    }

    /// <summary>
    /// Called when health reaches zero. Combat component handles the
    /// death state; GameManager gets notified for game-over flow.
    /// EventBus.EntityDied is fired by CharacterBase automatically.
    /// </summary>
    protected override void OnDeath()
    {
        _combat.OnDeath();
        GameManager.Instance?.TriggerGameOver();
    }

    // ══════════════════════════════════════════════════════════════════
    //  ANIMATION CALLBACKS
    //  Method call tracks on combat animations target this node.
    //  Each is a one-line delegate to the combat component.
    // ══════════════════════════════════════════════════════════════════

    public void OnAttackHitboxActivate() => _combat.OnAttackHitboxActivate();
    public void OnAttackHitboxDeactivate() => _combat.OnAttackHitboxDeactivate();
    public void OnAttackAnimationFinished() => _combat.OnAttackAnimationFinished();
    public void OnParryWindowOpen() => _combat.OnParryWindowOpen();
    public void OnParryWindowClose() => _combat.OnParryWindowClose();
    public void OnParryAnimationFinished() => _combat.OnParryAnimationFinished();
    public void OnStunAnimationFinished() => _combat.OnStunAnimationFinished();
}
