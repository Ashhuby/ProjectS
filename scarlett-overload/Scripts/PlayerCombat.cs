using Game.Autoloads;
using Game.Core.Data;
using Godot;

/// <summary>
/// Manages the player's combat state machine, combo system,
/// parry logic, and game feel effects (hit stop, camera shake, VFX).
///
/// Plain C# class — not a Node. PlayerCharacter creates it and calls:
///   - HandleAttackInput / HandleParryInput (from _UnhandledInput)
///   - Tick (every physics frame for combo window)
///   - ShouldTakeDamage (from CharacterBase damage pipeline)
///   - OnDamageTaken / OnDeath (from CharacterBase callbacks)
///   - Animation callbacks (routed from PlayerCharacter)
/// </summary>
public class PlayerCombat
{
    private readonly CharacterBody3D _owner;
    private readonly AnimationNodeStateMachinePlayback _playback;
    private readonly Hitbox _hitbox;
    private readonly CameraController _camera;
    private readonly WeaponData _weapon;

    private SwordTrail _trail;
    private MeshInstance3D _parryIndicator;

    // ── State ─────────────────────────────────────────────────────────

    public CombatState State { get; private set; } = CombatState.Free;

    private int _comboStep;
    private bool _attackBuffered;
    private bool _isParryActive;
    private AttackData _currentAttack;

    // Combo window — grace period after attack ends
    private bool _inComboWindow;
    private float _comboWindowTimer;

    private int MaxComboSteps => _weapon?.MaxComboSteps ?? 2;

    // ── Game feel fallbacks (used when no AttackData) ─────────────────

    public float FallbackComboWindow { get; set; } = 0.2f;
    public float FallbackHitStopDuration { get; set; } = 0.06f;
    public float FallbackHitStopTimeScale { get; set; } = 0.05f;
    public float DealHitShakeIntensity { get; set; } = 0.15f;
    public float DealHitShakeDuration { get; set; } = 0.15f;
    public float TakeHitShakeIntensity { get; set; } = 0.25f;
    public float TakeHitShakeDuration { get; set; } = 0.2f;

    // ══════════════════════════════════════════════════════════════════
    //  CONSTRUCTION
    // ══════════════════════════════════════════════════════════════════

    public PlayerCombat(
        CharacterBody3D owner,
        AnimationNodeStateMachinePlayback playback,
        Hitbox hitbox,
        CameraController camera,
        WeaponData weapon)
    {
        _owner = owner;
        _playback = playback;
        _hitbox = hitbox;
        _camera = camera;
        _weapon = weapon;
    }

    /// <summary>
    /// Wire up the sword trail after it's created by the coordinator.
    /// </summary>
    public void SetTrail(SwordTrail trail) => _trail = trail;

    /// <summary>
    /// Create the debug parry indicator (green sphere above head).
    /// Called once during initialization.
    /// </summary>
    public void CreateParryIndicator()
    {
        _parryIndicator = new MeshInstance3D();
        var sphere = new SphereMesh { Radius = 0.15f, Height = 0.3f };
        _parryIndicator.Mesh = sphere;

        var mat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0f, 1f, 0.5f),
            EmissionEnabled = true,
            Emission = new Color(0f, 1f, 0.5f),
            EmissionEnergyMultiplier = 2f
        };
        _parryIndicator.MaterialOverride = mat;
        _parryIndicator.Position = new Vector3(0f, 2.2f, 0f);
        _parryIndicator.Visible = false;
        _owner.AddChild(_parryIndicator);
    }

    // ══════════════════════════════════════════════════════════════════
    //  INPUT
    // ══════════════════════════════════════════════════════════════════

    public void HandleAttackInput()
    {
        switch (State)
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

    public void HandleParryInput()
    {
        if (State == CombatState.Free)
            EnterParry();
    }

    // ══════════════════════════════════════════════════════════════════
    //  STATE TRANSITIONS
    // ══════════════════════════════════════════════════════════════════

    private void StartAttack(int step)
    {
        State = CombatState.Attacking;
        _comboStep = step;
        _attackBuffered = false;
        _inComboWindow = false;

        _currentAttack = _weapon?.GetAttack(step);

        string animState = step switch
        {
            0 => "Attack1",
            1 => "Attack2",
            _ => "Attack1"
        };
        _playback.Travel(animState);

        string name = _currentAttack?.AttackName ?? $"Attack{step + 1}";
        GD.Print($"[Combat] {name} (step {step + 1}/{MaxComboSteps})");
    }

    private void EnterParry()
    {
        State = CombatState.Parrying;
        _attackBuffered = false;
        _isParryActive = false;
        _inComboWindow = false;
        _currentAttack = null;
        _playback.Travel("Parry");
        GD.Print("[Combat] Parry started");
    }

    private void EnterStunned()
    {
        State = CombatState.Stunned;
        _attackBuffered = false;
        _comboStep = 0;
        _isParryActive = false;
        _inComboWindow = false;
        _currentAttack = null;
        _parryIndicator.Visible = false;
        _hitbox.Deactivate();
        _trail?.StopEmitting();
        _playback.Travel("HitReaction");
        GD.Print("[Combat] Stunned");
    }

    private void ReturnToFree()
    {
        State = CombatState.Free;
        _comboStep = 0;
        _attackBuffered = false;
        _isParryActive = false;
        _inComboWindow = false;
        _currentAttack = null;
        _parryIndicator.Visible = false;
        _playback.Travel("Idle");
    }

    // ══════════════════════════════════════════════════════════════════
    //  TICK (called every physics frame)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Count down the combo grace window. Called from ProcessUpdate.
    /// </summary>
    public void Tick(float dt)
    {
        if (!_inComboWindow) return;

        _comboWindowTimer -= dt;
        if (_comboWindowTimer <= 0f)
        {
            _inComboWindow = false;
            ReturnToFree();
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  DAMAGE PIPELINE (called by CharacterBase via PlayerCharacter)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Called by CharacterBase.ShouldTakeDamage. Returns false if the
    /// parry window is active (and triggers parry success).
    /// </summary>
    public bool ShouldTakeDamage(DamageData data)
    {
        if (State == CombatState.Parrying && _isParryActive)
        {
            OnParrySuccess(data);
            return false;
        }
        return true;
    }

    /// <summary>
    /// Called after damage is applied. Triggers game feel and enters
    /// stunned state if the hit didn't kill us.
    /// </summary>
    public void OnDamageTaken(DamageData data, bool survived)
    {
        ApplyHitStop(FallbackHitStopDuration, FallbackHitStopTimeScale);
        _camera?.Shake(TakeHitShakeIntensity, TakeHitShakeDuration);
        GameVFX.SpawnScreenFlash(_owner, new Color(1f, 0.1f, 0.1f, 0.25f), 0.1f);

        GD.Print($"[Combat] Took {data.Amount} damage");

        if (survived)
            EnterStunned();
    }

    /// <summary>
    /// Called when health reaches zero. Plays death, disengages lock-on.
    /// EventBus.EntityDied is fired by CharacterBase — not here.
    /// </summary>
    public void OnDeath()
    {
        State = CombatState.Dead;
        _attackBuffered = false;
        _comboStep = 0;
        _isParryActive = false;
        _inComboWindow = false;
        _currentAttack = null;
        _parryIndicator.Visible = false;
        _hitbox.Deactivate();
        _trail?.StopEmitting();
        _playback.Travel("Death");
        GD.Print("[Combat] Dead");

        if (_camera != null && _camera.IsLockedOn)
            _camera.DisengageLock();
    }

    private void OnParrySuccess(DamageData data)
    {
        GD.Print($"[Combat] PARRY SUCCESS against {data.Source?.Name}");

        ApplyHitStop(FallbackHitStopDuration, FallbackHitStopTimeScale);
        _camera?.Shake(DealHitShakeIntensity, DealHitShakeDuration);

        var parryPos = _owner.GlobalPosition + new Vector3(0f, 1.2f, 0f);
        GameVFX.SpawnParryImpact(_owner, parryPos);

        EventBus.Instance?.EmitParrySucceeded(data);
    }

    // ══════════════════════════════════════════════════════════════════
    //  HIT CONNECTED (wired to Hitbox.HitConnected event)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Called when the player's sword hitbox connects with a target.
    /// Game feel values come from the current AttackData, falling back
    /// to the flat defaults when no weapon is assigned.
    /// </summary>
    public void OnHitConnected(DamageData data)
    {
        float stopDur = _currentAttack?.HitStopDuration ?? FallbackHitStopDuration;
        float stopScale = _currentAttack?.HitStopTimeScale ?? FallbackHitStopTimeScale;
        float shakeStr = _currentAttack?.CameraShakeIntensity ?? DealHitShakeIntensity;
        float shakeDur = _currentAttack?.CameraShakeDuration ?? DealHitShakeDuration;

        ApplyHitStop(stopDur, stopScale);
        _camera?.Shake(shakeStr, shakeDur);
        GameVFX.SpawnHitImpact(_owner, data.HitPosition, data.KnockbackDirection);

        EventBus.Instance?.EmitHitLanded(data);
    }

    // ══════════════════════════════════════════════════════════════════
    //  ANIMATION CALLBACKS
    //  (called by method call tracks via PlayerCharacter routing)
    // ══════════════════════════════════════════════════════════════════

    public void OnAttackHitboxActivate()
    {
        _hitbox.Activate(_currentAttack);
        _trail?.StartEmitting();
    }

    public void OnAttackHitboxDeactivate()
    {
        _hitbox.Deactivate();
        _trail?.StopEmitting();
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
            _comboWindowTimer = _currentAttack?.ComboWindowDuration ?? FallbackComboWindow;
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

    public void OnParryAnimationFinished() => ReturnToFree();

    public void OnStunAnimationFinished() => ReturnToFree();

    // ══════════════════════════════════════════════════════════════════
    //  GAME FEEL
    // ══════════════════════════════════════════════════════════════════

    private void ApplyHitStop(float duration, float timeScale)
    {
        Engine.TimeScale = timeScale;
        _owner.GetTree().CreateTimer(duration, true, false, true).Timeout += () =>
        {
            Engine.TimeScale = 1.0;
        };
    }
}
