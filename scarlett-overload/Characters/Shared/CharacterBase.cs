namespace Game.Characters;

using Game.Debug;

using Game.Autoloads;
using Game.Core.Data;
using Game.Core.Interfaces;
using Godot;
using System;

/// <summary>
/// Abstract base for anything with health that takes hits and dies:
/// player, enemies, destructible props.
///
/// Handles the damage pipeline, gravity, knockback decay, and
/// health bookkeeping so subclasses never duplicate that logic.
///
/// Subclass contract:
///   - Initialize()       → set up nodes, visuals, state machines
///   - OnDeath()          → play death animation, disable colliders
///   - ProcessMovement()  → drive horizontal velocity (input, AI, nothing)
///   - ProcessUpdate()    → tick FSM, timers, rotation
///   - ShouldTakeDamage() → return false to block (parry, iframe, armor)
///   - OnDamageTaken()    → hit reaction, flash, screen shake
///
/// Scene requirements:
///   - A child Hurtbox node named "Hurtbox" (or override hurtbox wiring)
///   - A CharacterStats resource assigned to the Stats export
/// </summary>
public abstract partial class CharacterBase : CharacterBody3D, IDamageable
{
    [Export] public CharacterStats Stats { get; set; }

    // ── Health ────────────────────────────────────────────────────────

    protected int _currentHealth;
    protected bool _isDead;

    public int CurrentHealth => _currentHealth;
    public int MaxHealth => Stats?.MaxHealth ?? 100;
    public bool IsDead => _isDead;

    public event Action<int, int> HealthChanged;

    // ── Physics ───────────────────────────────────────────────────────

    protected Vector3 _knockbackVelocity;
    protected float GravityForce => Stats?.Gravity ?? 9.8f;
    protected float KnockbackDecayRate => Stats?.KnockbackDecay ?? 12f;

    // ── References ────────────────────────────────────────────────────

    protected Hurtbox _hurtbox;

    // ══════════════════════════════════════════════════════════════════
    //  LIFECYCLE
    // ══════════════════════════════════════════════════════════════════

    public override void _Ready()
    {
        _hurtbox = GetNodeOrNull<Hurtbox>("Hurtbox");
        if (_hurtbox != null)
            _hurtbox.DamageReceived += TakeDamage;

        Initialize();

        // Set health AFTER Initialize — subclasses may assign Stats there
        _currentHealth = MaxHealth;
        HealthChanged?.Invoke(_currentHealth, MaxHealth);
    }

    public override void _ExitTree()
    {
        if (_hurtbox != null)
            _hurtbox.DamageReceived -= TakeDamage;
    }

    /// <summary>
    /// Called after CharacterBase._Ready finishes.
    /// Set up subclass-specific nodes, materials, state machines here.
    /// Do NOT call base._Ready() — it's handled for you.
    /// </summary>
    protected abstract void Initialize();

    // ══════════════════════════════════════════════════════════════════
    //  PHYSICS
    // ══════════════════════════════════════════════════════════════════

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        Vector3 vel = Velocity;

        // Gravity — always applies
        if (!IsOnFloor())
            vel.Y -= GravityForce * dt;

        // Knockback takes priority over voluntary movement
        if (_knockbackVelocity.LengthSquared() > 0.01f)
        {
            vel.X = _knockbackVelocity.X;
            vel.Z = _knockbackVelocity.Z;
            _knockbackVelocity = _knockbackVelocity.Lerp(
                Vector3.Zero, KnockbackDecayRate * dt);
        }
        else
        {
            _knockbackVelocity = Vector3.Zero;
            vel = ProcessMovement(vel, dt);
        }

        Velocity = vel;
        MoveAndSlide();

        ProcessUpdate(dt);
    }

    /// <summary>
    /// Set horizontal velocity when no knockback is active.
    /// Player: apply input. AI enemy: pathfind. Dummy: zero out.
    /// Default zeroes X/Z — override to add movement.
    /// </summary>
    protected virtual Vector3 ProcessMovement(Vector3 velocity, float dt)
    {
        velocity.X = 0f;
        velocity.Z = 0f;
        return velocity;
    }

    /// <summary>
    /// Called every physics frame after MoveAndSlide.
    /// Use for state machine ticks, timers, rotation, animation updates.
    /// </summary>
    protected virtual void ProcessUpdate(float dt) { }

    // ══════════════════════════════════════════════════════════════════
    //  DAMAGE PIPELINE
    // ══════════════════════════════════════════════════════════════════
    //
    //  TakeDamage (entry)
    //    → ShouldTakeDamage? (parry/iframe check)
    //    → apply damage, fire HealthChanged
    //    → set knockback
    //    → OnDamageTaken (hit reaction)
    //    → if dead → OnDeath
    //

    /// <summary>
    /// Single entry point for all damage. Called by the Hurtbox event
    /// and also satisfies the IDamageable interface, so external systems
    /// (fall damage, hazards) can call it directly.
    /// </summary>
    public void TakeDamage(DamageData data)
    {
        if (_isDead) return;

        if (!ShouldTakeDamage(data)) return;

        _currentHealth = Mathf.Max(_currentHealth - data.Amount, 0);
        HealthChanged?.Invoke(_currentHealth, MaxHealth);
        _knockbackVelocity = data.KnockbackDirection;

        OnDamageTaken(data);
        EventBus.Instance?.EmitDamageTaken(data);

        GameLog.CombatLog($"[{Name}] Took {data.Amount} damage. HP: {_currentHealth}/{MaxHealth}");

        if (_currentHealth <= 0)
        {
            _isDead = true;
            OnDeath();
            EventBus.Instance?.EmitEntityDied(this);
        }
    }

    /// <summary>
    /// Return false to prevent damage entirely. Override for parry
    /// windows, invincibility frames, shields, armor-that-absorbs, etc.
    /// If you block damage here, you're responsible for handling the
    /// interaction (e.g. fire ParrySucceeded, stagger the attacker).
    /// </summary>
    protected virtual bool ShouldTakeDamage(DamageData data) => true;

    /// <summary>
    /// Called after damage is applied, before death check.
    /// Use for hit flash, screen shake, hit reaction animation.
    /// </summary>
    protected virtual void OnDamageTaken(DamageData data) { }

    /// <summary>
    /// Called when health reaches zero. Play death animation,
    /// disable colliders, trigger respawn timer if applicable.
    /// </summary>
    protected abstract void OnDeath();

    // ══════════════════════════════════════════════════════════════════
    //  UTILITIES
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Reset health to max, clear death flag and knockback.
    /// Call this in respawn logic.
    /// </summary>
    protected void ResetHealth()
    {
        _currentHealth = MaxHealth;
        _isDead = false;
        _knockbackVelocity = Vector3.Zero;
        HealthChanged?.Invoke(_currentHealth, MaxHealth);
    }

    /// <summary>
    /// Fire the HealthChanged event from a subclass.
    /// C# events can only be invoked by the declaring class, so
    /// subclasses that modify _currentHealth directly (e.g. vital
    /// bonus damage) must call this to notify the UI.
    /// </summary>
    protected void NotifyHealthChanged()
    {
        HealthChanged?.Invoke(_currentHealth, MaxHealth);
    }

    /// <summary>
    /// Safely toggle the hurtbox on/off (e.g. disable on death).
    /// Uses SetDeferred to avoid physics callback errors.
    /// </summary>
    protected void SetHurtboxActive(bool active)
    {
        _hurtbox?.SetDeferred("monitorable", active);
    }

    /// <summary>
    /// Force-set knockback from an external source
    /// (explosions, environmental pushes).
    /// </summary>
    public void ApplyKnockback(Vector3 force)
    {
        _knockbackVelocity = force;
    }
}
