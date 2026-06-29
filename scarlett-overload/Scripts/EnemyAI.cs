using Game.Autoloads;
using Game.Core.Data;
using Godot;

/// <summary>
/// Enemy AI state machine — composition pattern, plain C# class.
///
/// Lunge: enemies with LungeSpeed > 0 close gap during attack.
/// Hitbox delay: enemies with HitboxDelay > 0 activate hitbox late
/// in the attack so the lunge connects at the right moment.
/// </summary>
public class EnemyAI
{
    public enum AIState
    {
        Idle,
        Chasing,
        Engaging,
        Telegraphing,
        Attacking,
        Recovering,
        Stunned
    }

    public AIState State { get; private set; } = AIState.Idle;

    private readonly CharacterBody3D _owner;
    private readonly Hitbox _hitbox;
    private readonly EnemyConfig _config;

    private Node3D _target;
    private float _distToTarget;
    private Vector3 _dirToTarget;

    public bool HasTarget => _target != null && GodotObject.IsInstanceValid(_target);
    public float DistanceToTarget => _distToTarget;

    private float _stateTimer;
    private float _attackCooldown;
    private bool _isParryStunned;

    private AttackData _currentAttack;
    public AttackData CurrentAttack => _currentAttack;

    public float TelegraphProgress { get; private set; }
    public float StunRemaining => State == AIState.Stunned ? _stateTimer : 0f;
    public bool IsParryStunned => _isParryStunned;
    public float ParryStunTotalDuration { get; private set; }

    // ── Hitbox delay ──────────────────────────────────────────────────
    private float _attackElapsed;
    private bool _hitboxActivatedThisAttack;

    // ── Aggression / circling ─────────────────────────────────────────
    private bool _holdsToken;
    private float _circleDirection = 1f;
    private float _circleTimer;
    private const float CircleSpeedFraction = 0.6f;
    private const float CircleSwitchMin = 2f;
    private const float CircleSwitchMax = 4f;

    public EnemyAI(CharacterBody3D owner, Hitbox hitbox, EnemyConfig config)
    {
        _owner = owner;
        _hitbox = hitbox;
        _config = config;
        RollCircleTimer();
    }

    // ══════════════════════════════════════════════════════════════════
    //  TICK
    // ══════════════════════════════════════════════════════════════════

    public void Tick(float dt)
    {
        UpdateTargetTracking();
        switch (State)
        {
            case AIState.Idle:         TickIdle(dt); break;
            case AIState.Chasing:      TickChasing(dt); break;
            case AIState.Engaging:     TickEngaging(dt); break;
            case AIState.Telegraphing: TickTelegraphing(dt); break;
            case AIState.Attacking:    TickAttacking(dt); break;
            case AIState.Recovering:   TickRecovering(dt); break;
            case AIState.Stunned:      TickStunned(dt); break;
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  MOVEMENT
    // ══════════════════════════════════════════════════════════════════

    public Vector3 ComputeVelocity(Vector3 velocity, float dt)
    {
        if (State == AIState.Chasing && HasTarget)
        {
            float speed = _config?.ChaseSpeed ?? 4f;
            velocity.X = _dirToTarget.X * speed;
            velocity.Z = _dirToTarget.Z * speed;
        }
        else if (State == AIState.Engaging && !_holdsToken && HasTarget)
        {
            float speed = (_config?.ChaseSpeed ?? 4f) * CircleSpeedFraction;
            Vector3 perp = new Vector3(-_dirToTarget.Z, 0f, _dirToTarget.X) * _circleDirection;
            velocity.X = perp.X * speed;
            velocity.Z = perp.Z * speed;

            float idealRange = (_config?.AttackRange ?? 2.5f) * 2f;
            float rangeDiff = _distToTarget - idealRange;
            if (Mathf.Abs(rangeDiff) > 0.5f)
            {
                float correction = Mathf.Sign(rangeDiff) * speed * 0.4f;
                velocity.X += _dirToTarget.X * correction;
                velocity.Z += _dirToTarget.Z * correction;
            }
        }
        else if (State == AIState.Telegraphing
                 && HasTarget
                 && (_config?.LungeDuringTelegraph ?? false)
                 && (_config?.LungeSpeed ?? 0f) > 0f)
        {
            float lungeSpeed = _config.LungeSpeed * _config.TelegraphLungeFraction;
            velocity.X = _dirToTarget.X * lungeSpeed;
            velocity.Z = _dirToTarget.Z * lungeSpeed;
        }
        else if (State == AIState.Attacking
                 && HasTarget
                 && (_config?.LungeSpeed ?? 0f) > 0f)
        {
            float lungeSpeed = _config.LungeSpeed;
            velocity.X = _dirToTarget.X * lungeSpeed;
            velocity.Z = _dirToTarget.Z * lungeSpeed;
        }
        else
        {
            velocity.X = Mathf.MoveToward(velocity.X, 0f, 20f * dt);
            velocity.Z = Mathf.MoveToward(velocity.Z, 0f, 20f * dt);
        }

        return velocity;
    }

    public Vector3 GetFacingDirection()
    {
        if (State == AIState.Idle || !HasTarget)
            return Vector3.Zero;
        return _dirToTarget;
    }

    // ══════════════════════════════════════════════════════════════════
    //  REACTIONS
    // ══════════════════════════════════════════════════════════════════

    public void OnDamageTaken()
    {
        if (State == AIState.Attacking)
            _hitbox.Deactivate();

        _currentAttack = null;
        _hitboxActivatedThisAttack = false;
        TelegraphProgress = 0f;

        if (_isParryStunned)
        {
            GD.Print($"[{_owner.Name} AI] Hit during parry stun — timer preserved ({_stateTimer:F1}s)");
            return;
        }

        EnterState(AIState.Stunned, _config?.StunDuration ?? 0.4f);
    }

    public void OnParried()
    {
        _hitbox.Deactivate();
        _currentAttack = null;
        _hitboxActivatedThisAttack = false;
        TelegraphProgress = 0f;
        _isParryStunned = true;

        if (_holdsToken)
        {
            _holdsToken = false;
            AggressionManager.Instance?.ForceReleaseWithLockout(_owner);
        }

        float duration = _config?.ParryStaggerDuration ?? 2.5f;
        ParryStunTotalDuration = duration;
        EnterState(AIState.Stunned, duration);
        GD.Print($"[{_owner.Name} AI] PARRIED — parry stun for {duration}s");
    }

    public void OnDeath()
    {
        _hitbox.Deactivate();
        _currentAttack = null;
        _hitboxActivatedThisAttack = false;
        TelegraphProgress = 0f;
        _isParryStunned = false;
        ParryStunTotalDuration = 0f;

        if (_holdsToken)
        {
            _holdsToken = false;
            AggressionManager.Instance?.ReleaseToken(_owner);
        }

        AggressionManager.Instance?.Unregister(_owner);
    }

    public void ExtendStun(float extraTime)
    {
        if (State != AIState.Stunned) return;
        _stateTimer += extraTime;
        ParryStunTotalDuration += extraTime;
        GD.Print($"[{_owner.Name} AI] Stun extended by {extraTime:F1}s (remaining: {_stateTimer:F1}s)");
    }

    public void CollapseStun(float graceTime)
    {
        if (State != AIState.Stunned) return;
        if (_stateTimer <= graceTime) return;
        GD.Print($"[{_owner.Name} AI] Stun collapsed: {_stateTimer:F1}s → {graceTime:F1}s");
        _stateTimer = graceTime;
    }

    // ══════════════════════════════════════════════════════════════════
    //  STATE TICKS
    // ══════════════════════════════════════════════════════════════════

    private void TickIdle(float dt)
    {
        if (HasTarget && _distToTarget <= (_config?.DetectionRange ?? 15f))
        {
            EnterState(AIState.Chasing);
            AggressionManager.Instance?.Register(_owner);
            GD.Print($"[{_owner.Name} AI] Player detected — chasing");
        }
    }

    private void TickChasing(float dt)
    {
        if (!HasTarget || _distToTarget > (_config?.LoseAggroRange ?? 25f))
        {
            AggressionManager.Instance?.Unregister(_owner);
            EnterState(AIState.Idle);
            return;
        }

        if (_distToTarget <= (_config?.AttackRange ?? 2.5f))
        {
            _attackCooldown = _config?.RollCooldown() ?? 1.5f;
            EnterState(AIState.Engaging);
        }
    }

    private void TickEngaging(float dt)
    {
        if (!HasTarget) { EnterState(AIState.Idle); return; }

        if (_distToTarget > (_config?.AttackRange ?? 2.5f) * 1.5f)
        {
            EnterState(AIState.Chasing);
            return;
        }

        _circleTimer -= dt;
        if (_circleTimer <= 0f)
        {
            _circleDirection *= -1f;
            RollCircleTimer();
        }

        _attackCooldown -= dt;
        if (_attackCooldown <= 0f)
        {
            if (AggressionManager.Instance == null)
                GD.PushWarning($"[{_owner.Name} AI] AggressionManager.Instance is NULL!");

            bool granted = AggressionManager.Instance == null
                        || AggressionManager.Instance.RequestToken(_owner);

            if (granted)
            {
                _holdsToken = true;
                _currentAttack = _config?.PickAttack();
                float telegraphDur = _config?.TelegraphDuration ?? 0.7f;
                EnterState(AIState.Telegraphing, telegraphDur);

                if (_currentAttack != null)
                    GD.Print($"[{_owner.Name} AI] Telegraphing: {_currentAttack.AttackName}");
                else
                    GD.PushWarning($"[{_owner.Name} AI] Telegraphing with NULL attack!");
            }
            else
            {
                _attackCooldown = 0.5f;
            }
        }
    }

    private void TickTelegraphing(float dt)
    {
        _stateTimer -= dt;
        float totalDur = _config?.TelegraphDuration ?? 0.7f;
        TelegraphProgress = 1f - Mathf.Max(_stateTimer / totalDur, 0f);

        if (_stateTimer <= 0f)
        {
            float attackDur = _config?.AttackActiveDuration ?? 0.25f;
            EnterState(AIState.Attacking, attackDur);
            _attackElapsed = 0f;
            _hitboxActivatedThisAttack = false;

            float delay = _config?.HitboxDelay ?? 0f;
            if (delay <= 0f)
            {
                _hitbox.Activate(_currentAttack);
                _hitboxActivatedThisAttack = true;
            }

            TelegraphProgress = 1f;
            GD.Print($"[{_owner.Name} AI] Attacking!");
        }
    }

    private void TickAttacking(float dt)
    {
        _stateTimer -= dt;
        _attackElapsed += dt;

        // Delayed hitbox — lets lunge close distance first
        if (!_hitboxActivatedThisAttack)
        {
            float delay = _config?.HitboxDelay ?? 0f;
            if (_attackElapsed >= delay)
            {
                _hitbox.Activate(_currentAttack);
                _hitboxActivatedThisAttack = true;
            }
        }

        if (_stateTimer <= 0f)
        {
            _hitbox.Deactivate();
            _hitboxActivatedThisAttack = false;
            float recoveryDur = _config?.RecoveryDuration ?? 0.6f;
            EnterState(AIState.Recovering, recoveryDur);
            TelegraphProgress = 0f;
        }
    }

    private void TickRecovering(float dt)
    {
        _stateTimer -= dt;
        if (_stateTimer <= 0f)
        {
            if (_holdsToken)
            {
                _holdsToken = false;
                AggressionManager.Instance?.ReleaseToken(_owner);
            }

            if (HasTarget && _distToTarget <= (_config?.AttackRange ?? 2.5f) * 1.5f)
            {
                _attackCooldown = _config?.RollCooldown() ?? 1.5f;
                EnterState(AIState.Engaging);
            }
            else if (HasTarget)
                EnterState(AIState.Chasing);
            else
                EnterState(AIState.Idle);
        }
    }

    private void TickStunned(float dt)
    {
        _stateTimer -= dt;
        if (_stateTimer <= 0f)
        {
            _isParryStunned = false;
            ParryStunTotalDuration = 0f;

            if (HasTarget && _distToTarget <= (_config?.AttackRange ?? 2.5f) * 1.5f)
            {
                _attackCooldown = _config?.RollCooldown() ?? 1.5f;
                EnterState(AIState.Engaging);
            }
            else if (HasTarget)
                EnterState(AIState.Chasing);
            else
                EnterState(AIState.Idle);
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

    private void RollCircleTimer()
    {
        _circleTimer = (float)GD.RandRange(CircleSwitchMin, CircleSwitchMax);
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
