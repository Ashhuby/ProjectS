using Game.Core.Data;
using Godot;

/// <summary>
/// Enemy AI state machine. Plain C# class — same composition pattern
/// as PlayerCombat. EnemyBase creates it and calls:
///   - Tick(dt) every physics frame
///   - ComputeVelocity for movement
///   - OnDamageTaken / OnParried / OnDeath for reactions
///
/// The AI finds the player automatically via the "Player" group.
/// No NavigationAgent — uses direct movement. Good for open arenas.
/// Add nav mesh pathfinding later for complex levels.
/// </summary>
public class EnemyAI
{
    public enum AIState
    {
        Idle,          // No target detected
        Chasing,       // Moving toward player
        Engaging,      // In range, waiting for cooldown
        Telegraphing,  // Wind-up, visual warning
        Attacking,     // Hitbox active, committed
        Recovering,    // Post-attack, vulnerable
        Stunned        // Hit reaction or parry stagger
    }

    public AIState State { get; private set; } = AIState.Idle;

    private readonly CharacterBody3D _owner;
    private readonly Hitbox _hitbox;
    private readonly EnemyConfig _config;

    // ── Target tracking ──────────────────────────────────────────────

    private Node3D _target;
    private float _distToTarget;
    private Vector3 _dirToTarget;

    public bool HasTarget => _target != null && GodotObject.IsInstanceValid(_target);
    public float DistanceToTarget => _distToTarget;

    // ── Timers ────────────────────────────────────────────────────────

    private float _stateTimer;
    private float _attackCooldown;
    private bool _isParryStunned;

    // ── Current attack ────────────────────────────────────────────────

    private AttackData _currentAttack;
    public AttackData CurrentAttack => _currentAttack;

    /// <summary>
    /// Normalized telegraph progress (0 = just started, 1 = about to attack).
    /// Used by EnemyBase for visual feedback.
    /// </summary>
    public float TelegraphProgress { get; private set; }

    /// <summary>
    /// Remaining stun time in seconds. Used by EnemyBase/VitalSystem
    /// to read how much stun window is left.
    /// </summary>
    public float StunRemaining => State == AIState.Stunned ? _stateTimer : 0f;

    // ══════════════════════════════════════════════════════════════════
    //  CONSTRUCTION
    // ══════════════════════════════════════════════════════════════════

    public EnemyAI(CharacterBody3D owner, Hitbox hitbox, EnemyConfig config)
    {
        _owner = owner;
        _hitbox = hitbox;
        _config = config;
    }

    // ══════════════════════════════════════════════════════════════════
    //  TICK — called every physics frame from ProcessUpdate
    // ══════════════════════════════════════════════════════════════════

    public void Tick(float dt)
    {
        UpdateTargetTracking();

        switch (State)
        {
            case AIState.Idle:        TickIdle(dt); break;
            case AIState.Chasing:     TickChasing(dt); break;
            case AIState.Engaging:    TickEngaging(dt); break;
            case AIState.Telegraphing: TickTelegraphing(dt); break;
            case AIState.Attacking:   TickAttacking(dt); break;
            case AIState.Recovering:  TickRecovering(dt); break;
            case AIState.Stunned:     TickStunned(dt); break;
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  MOVEMENT — called from ProcessMovement
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Set horizontal velocity based on AI state.
    /// Only Chasing produces movement — all other states hold position
    /// (knockback is handled by CharacterBase separately).
    /// </summary>
    public Vector3 ComputeVelocity(Vector3 velocity, float dt)
    {
        if (State == AIState.Chasing && HasTarget)
        {
            float speed = _config?.ChaseSpeed ?? 4f;
            velocity.X = _dirToTarget.X * speed;
            velocity.Z = _dirToTarget.Z * speed;
        }
        else
        {
            velocity.X = Mathf.MoveToward(velocity.X, 0f, 20f * dt);
            velocity.Z = Mathf.MoveToward(velocity.Z, 0f, 20f * dt);
        }

        return velocity;
    }

    /// <summary>
    /// Direction the enemy should face. Points toward the target
    /// when in any combat state, zero when idle.
    /// </summary>
    public Vector3 GetFacingDirection()
    {
        if (State == AIState.Idle || !HasTarget)
            return Vector3.Zero;
        return _dirToTarget;
    }

    // ══════════════════════════════════════════════════════════════════
    //  REACTIONS — called by EnemyBase
    // ══════════════════════════════════════════════════════════════════

    public void OnDamageTaken()
    {
        if (State == AIState.Attacking)
            _hitbox.Deactivate();

        _currentAttack = null;
        TelegraphProgress = 0f;

        // Don't override parry stun — the vital system manages the timer.
        // Hitting a parry-stunned enemy should NOT reset the stun duration.
        if (_isParryStunned)
        {
            GD.Print($"[{_owner.Name} AI] Hit during parry stun — stun timer preserved ({_stateTimer:F1}s remaining)");
            return;
        }

        EnterState(AIState.Stunned, _config?.StunDuration ?? 0.4f);
    }

    public void OnParried()
    {
        _hitbox.Deactivate();
        _currentAttack = null;
        TelegraphProgress = 0f;
        _isParryStunned = true;

        float duration = _config?.ParryStaggerDuration ?? 10.5f;
        EnterState(AIState.Stunned, duration);
        GD.Print($"[{_owner.Name} AI] PARRIED — parry stun for {duration}s (config value: {_config?.ParryStaggerDuration})");
    }

    public void OnDeath()
    {
        _hitbox.Deactivate();
        _currentAttack = null;
        TelegraphProgress = 0f;
        _isParryStunned = false;
    }

    /// <summary>
    /// Add extra time to the current stun. Called by VitalSystem
    /// when the player pops the primary vital — extends the window
    /// for the mini vital attack.
    /// No-op if not currently stunned.
    /// </summary>
    public void ExtendStun(float extraTime)
    {
        if (State != AIState.Stunned) return;
        _stateTimer += extraTime;
        GD.Print($"[{_owner.Name} AI] Stun extended by {extraTime:F1}s (remaining: {_stateTimer:F1}s)");
    }

    // ══════════════════════════════════════════════════════════════════
    //  STATE TICKS
    // ══════════════════════════════════════════════════════════════════

    private void TickIdle(float dt)
    {
        if (HasTarget && _distToTarget <= (_config?.DetectionRange ?? 15f))
        {
            EnterState(AIState.Chasing);
            GD.Print($"[{_owner.Name} AI] Player detected — chasing");
        }
    }

    private void TickChasing(float dt)
    {
        if (!HasTarget || _distToTarget > (_config?.LoseAggroRange ?? 25f))
        {
            EnterState(AIState.Idle);
            GD.Print($"[{_owner.Name} AI] Lost target — returning to idle");
            return;
        }

        if (_distToTarget <= (_config?.AttackRange ?? 2.5f))
        {
            _attackCooldown = _config?.RollCooldown() ?? 1.5f;
            EnterState(AIState.Engaging);
            GD.Print($"[{_owner.Name} AI] In range — engaging");
        }
    }

    private void TickEngaging(float dt)
    {
        if (!HasTarget)
        {
            EnterState(AIState.Idle);
            return;
        }

        // Player moved out of range — chase again
        if (_distToTarget > (_config?.AttackRange ?? 2.5f) * 1.5f)
        {
            EnterState(AIState.Chasing);
            return;
        }

        _attackCooldown -= dt;
        if (_attackCooldown <= 0f)
        {
            _currentAttack = _config?.PickAttack();
            float telegraphDur = _config?.TelegraphDuration ?? 0.7f;
            EnterState(AIState.Telegraphing, telegraphDur);

            string name = _currentAttack?.AttackName ?? "Attack";
            GD.Print($"[{_owner.Name} AI] Telegraphing: {name}");
        }
    }

    private void TickTelegraphing(float dt)
    {
        _stateTimer -= dt;
        float totalDur = _config?.TelegraphDuration ?? 0.7f;
        TelegraphProgress = 1f - Mathf.Max(_stateTimer / totalDur, 0f);

        if (_stateTimer <= 0f)
        {
            // Commit to attack
            float attackDur = _config?.AttackActiveDuration ?? 0.25f;
            EnterState(AIState.Attacking, attackDur);
            _hitbox.Activate(_currentAttack);
            TelegraphProgress = 1f;
            GD.Print($"[{_owner.Name} AI] Attacking!");
        }
    }

    private void TickAttacking(float dt)
    {
        _stateTimer -= dt;
        if (_stateTimer <= 0f)
        {
            _hitbox.Deactivate();
            float recoveryDur = _config?.RecoveryDuration ?? 0.6f;
            EnterState(AIState.Recovering, recoveryDur);
            TelegraphProgress = 0f;
            GD.Print($"[{_owner.Name} AI] Recovering");
        }
    }

    private void TickRecovering(float dt)
    {
        _stateTimer -= dt;
        if (_stateTimer <= 0f)
        {
            if (HasTarget && _distToTarget <= (_config?.AttackRange ?? 2.5f) * 1.5f)
            {
                _attackCooldown = _config?.RollCooldown() ?? 1.5f;
                EnterState(AIState.Engaging);
            }
            else if (HasTarget)
            {
                EnterState(AIState.Chasing);
            }
            else
            {
                EnterState(AIState.Idle);
            }
        }
    }

    private void TickStunned(float dt)
    {
        _stateTimer -= dt;
        if (_stateTimer <= 0f)
        {
            _isParryStunned = false;

            if (HasTarget && _distToTarget <= (_config?.AttackRange ?? 2.5f) * 1.5f)
            {
                _attackCooldown = _config?.RollCooldown() ?? 1.5f;
                EnterState(AIState.Engaging);
            }
            else if (HasTarget)
            {
                EnterState(AIState.Chasing);
            }
            else
            {
                EnterState(AIState.Idle);
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  HELPERS
    // ══════════════════════════════════════════════════════════════════

    private void EnterState(AIState newState, float timer = 0f)
    {
        State = newState;
        _stateTimer = timer;
    }

    private void UpdateTargetTracking()
    {
        if (_target == null || !GodotObject.IsInstanceValid(_target))
        {
            var playerNode = _owner.GetTree().GetFirstNodeInGroup("Player");
            _target = playerNode as Node3D;
        }

        if (HasTarget)
        {
            Vector3 toTarget = _target.GlobalPosition - _owner.GlobalPosition;
            toTarget.Y = 0f;
            _distToTarget = toTarget.Length();
            _dirToTarget = _distToTarget > 0.01f
                ? toTarget / _distToTarget
                : Vector3.Zero;
        }
        else
        {
            _distToTarget = float.MaxValue;
            _dirToTarget = Vector3.Zero;
        }
    }
}
