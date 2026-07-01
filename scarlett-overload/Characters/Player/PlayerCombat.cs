namespace Game.Characters.Player;

using Game.Debug;
using Game.Core.Data;
using Godot;
using Game.Camera;
using Game.Autoloads;
using Game.Characters.Enemies;

/// <summary>
/// Manages the player's combat state machine, combo system,
/// parry logic, and game feel effects (hit stop, camera shake).
///
/// Plain C# class — not a Node. PlayerCharacter creates it and calls:
///   - HandleAttackInput / HandleParryInput / HandleDashInput (from _UnhandledInput)
///   - Tick (every physics frame for combo window + dash)
///   - ShouldTakeDamage (from CharacterBase damage pipeline)
///   - OnDamageTaken / OnDeath (from CharacterBase callbacks)
///   - Animation callbacks (routed from PlayerCharacter)
///
/// VFX are NOT called directly here. GameVFX autoload subscribes to
/// EventBus and spawns effects autonomously. This class fires events
/// (HitLanded, ParrySucceeded, etc.) and GameVFX reacts.
///
/// The one exception is the vital thrust sparkle — a persistent
/// GpuParticles3D attached to the rapier tip that emits during the
/// vital thrust animation. This is weapon-specific and lives on the
/// sword node, not in the global VFX manager.
/// </summary>
public class PlayerCombat
{
    private readonly CharacterBody3D _owner;
    private readonly AnimationNodeStateMachinePlayback _playback;
    private readonly Hitbox _hitbox;
    private readonly CameraController _camera;
    private readonly WeaponData _weapon;
    private readonly PlayerDash _dash;

    // ── Vital thrust sparkle ──────────────────────────────────────────

    private GpuParticles3D _vitalSparkle;
    private OmniLight3D _vitalSparkleLight;

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

    // Vital thrust — special attack gated by the vital system
    private bool _vitalThrustReady;
    private bool _isVitalThrusting;

    // ── Game feel fallbacks (used when no AttackData) ─────────────────
    // Tuned for Sekiro-style impact: short sharp jolts, not prolonged wobble.

    public float FallbackComboWindow { get; set; } = 0.25f;
    public float FallbackHitStopDuration { get; set; } = 0.08f;
    public float FallbackHitStopTimeScale { get; set; } = 0.05f;
    public float DealHitShakeIntensity { get; set; } = 0.2f;
    public float DealHitShakeDuration { get; set; } = 0.1f;
    public float TakeHitShakeIntensity { get; set; } = 0.35f;
    public float TakeHitShakeDuration { get; set; } = 0.12f;

    // ══════════════════════════════════════════════════════════════════
    //  CONSTRUCTION
    // ══════════════════════════════════════════════════════════════════

    public PlayerCombat(
        CharacterBody3D owner,
        AnimationNodeStateMachinePlayback playback,
        Hitbox hitbox,
        CameraController camera,
        WeaponData weapon,
        PlayerDash dash)
    {
        _owner = owner;
        _playback = playback;
        _hitbox = hitbox;
        _camera = camera;
        _weapon = weapon;
        _dash = dash;
    }

    /// <summary>
    /// Create the anime star sparkle emitter at the rapier tip.
    /// Called once during initialization by PlayerCharacter.
    ///
    /// The emitter is a child of the sword node so it follows the
    /// blade through all animations. It starts disabled and is
    /// toggled on only during vital thrust.
    ///
    /// Effect: 4-point star bursts that shimmer white-gold with
    /// a subtle blue-white core. PS1 aesthetic = small, hard-edged,
    /// high-emission, no soft blur.
    /// </summary>
    public void SetupVitalSparkle(Node3D swordNode, Vector3 tipOffset)
    {
        if (swordNode == null) return;

        _vitalSparkle = new GpuParticles3D();
        _vitalSparkle.Amount = 6;
        _vitalSparkle.Lifetime = 0.3f;
        _vitalSparkle.Explosiveness = 0.2f;
        _vitalSparkle.Emitting = false;
        _vitalSparkle.Position = tipOffset;

        var mat = new ParticleProcessMaterial();

        // Tight cluster around the tip — not a spray, a shimmer
        mat.Direction = Vector3.Zero;
        mat.Spread = 180f;
        mat.InitialVelocityMin = 0.3f;
        mat.InitialVelocityMax = 1.0f;
        mat.Gravity = Vector3.Zero; // Stars float, they don't fall

        // Scale flicker — stars pop in large then shrink to nothing
        mat.ScaleMin = 1.5f;
        mat.ScaleMax = 3.5f;
        mat.ScaleCurve = MakeScaleCurve();

        // Spin so the star shape rotates — anime sparkle hallmark
        mat.AngularVelocityMin = -400f;
        mat.AngularVelocityMax = 400f;

        // Color: white-gold core → transparent
        // The stars should feel luminous and precious
        mat.ColorRamp = MakeSparkleRamp();

        _vitalSparkle.ProcessMaterial = mat;

        // Draw pass: small quad with the CrossBurst texture (4-point star)
        // CrossBurst.png is already a hand-drawn 4-point star shape
        var quad = new QuadMesh();
        quad.Size = new Vector2(0.08f, 0.08f);

        var quadMat = new StandardMaterial3D();
        quadMat.AlbedoColor = Colors.White;
        quadMat.AlbedoTexture = GD.Load<Texture2D>("res://Assets/VFX/Textures/CrossBurst.png");
        quadMat.VertexColorUseAsAlbedo = true;
        quadMat.BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled;
        quadMat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        quadMat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        quadMat.EmissionEnabled = true;
        quadMat.Emission = new Color(1f, 0.95f, 0.8f);
        quadMat.EmissionEnergyMultiplier = 10f;
        quad.Material = quadMat;

        _vitalSparkle.DrawPass1 = quad;
        swordNode.AddChild(_vitalSparkle);

        // Small point light at the tip — makes the sparkle cast glow
        _vitalSparkleLight = new OmniLight3D();
        _vitalSparkleLight.LightColor = new Color(1f, 0.9f, 0.7f);
        _vitalSparkleLight.LightEnergy = 0f; // Off by default
        _vitalSparkleLight.OmniRange = 2f;
        _vitalSparkleLight.Position = tipOffset;
        swordNode.AddChild(_vitalSparkleLight);
    }

    /// <summary>
    /// Subscribe to EventBus events. Called by PlayerCharacter during Initialize.
    /// Must be paired with UnsubscribeEvents in _ExitTree.
    /// </summary>
    public void SubscribeEvents()
    {
        if (EventBus.Instance == null) return;
        EventBus.Instance.VitalThrustLoaded += OnVitalThrustLoaded;
        EventBus.Instance.VitalThrustUnloaded += OnVitalThrustUnloaded;
    }

    /// <summary>
    /// Unsubscribe from EventBus events. Called by PlayerCharacter in _ExitTree.
    /// </summary>
    public void UnsubscribeEvents()
    {
        if (EventBus.Instance == null) return;
        EventBus.Instance.VitalThrustLoaded -= OnVitalThrustLoaded;
        EventBus.Instance.VitalThrustUnloaded -= OnVitalThrustUnloaded;
    }

    private void OnVitalThrustLoaded()
    {
        _vitalThrustReady = true;
        StartSparkle();
        GameLog.CombatLog("[Combat] Vital thrust LOADED — sparkle active, press attack to fire");
    }

    private void OnVitalThrustUnloaded()
    {
        _vitalThrustReady = false;
        StopSparkle();
        GameLog.CombatLog("[Combat] Vital thrust unloaded");
    }

    // ══════════════════════════════════════════════════════════════════
    //  INPUT HANDLERS
    // ══════════════════════════════════════════════════════════════════

    public void HandleAttackInput()
    {
        if (State == CombatState.Dead) return;

        // Vital thrust overrides normal attacks when loaded
        if (_vitalThrustReady && State == CombatState.Free)
        {
            StartVitalThrust();
            return;
        }

        switch (State)
        {
            case CombatState.Free:
                StartAttack(0);
                break;

            case CombatState.Attacking:
                _attackBuffered = true;
                break;
        }

        // Also buffer during combo window
        if (_inComboWindow)
        {
            _inComboWindow = false;
            if (_comboStep < MaxComboSteps - 1)
                StartAttack(_comboStep + 1);
        }
    }

    public void HandleParryInput()
    {
        if (State == CombatState.Dead) return;

        switch (State)
        {
            case CombatState.Free:
                EnterParry();
                break;

            case CombatState.Attacking:
                CancelAttackIntoParry();
                break;
        }
    }

    public void HandleDashInput(Vector3 direction)
    {
        if (State == CombatState.Dead) return;
        if (State == CombatState.Dashing) return;
        if (_dash == null || !_dash.CanDash) return;

        if (State == CombatState.Attacking)
        {
            _hitbox.Deactivate();
            _comboStep = 0;
            _attackBuffered = false;
            _inComboWindow = false;
            _currentAttack = null;
            _isVitalThrusting = false;
        }

        State = CombatState.Dashing;
        _dash.Start(direction);
        _playback.Travel("Idle");
        GameLog.CombatLog("[Combat] Dash started");
    }

    // ══════════════════════════════════════════════════════════════════
    //  ATTACK EXECUTION
    // ══════════════════════════════════════════════════════════════════

    private void StartAttack(int step)
    {
        State = CombatState.Attacking;
        _comboStep = step;
        _attackBuffered = false;
        _inComboWindow = false;
        _isParryActive = false;

        _currentAttack = _weapon?.GetAttack(step);

        string animName = step switch
        {
            0 => "Attack1",
            1 => "Attack2",
            2 => "Attack3",
            _ => "Attack1"
        };

        _playback.Travel(animName);
        GameLog.CombatLog($"[Combat] Attack step {step + 1} ({_currentAttack?.AttackName ?? "fallback"})");
    }

    private void StartVitalThrust()
    {
        State = CombatState.Attacking;
        _comboStep = 0;
        _attackBuffered = false;
        _inComboWindow = false;
        _isParryActive = false;
        _isVitalThrusting = true;
        _vitalThrustReady = false;

        _currentAttack = _weapon?.VitalThrustAttack;
        _playback.Travel("VitalThrust");

        // Sparkle stays on during the thrust — it's the visual payoff
        // It will be turned off in OnAttackAnimationFinished or if
        // VitalThrustUnloaded fires

        GameLog.CombatLog("[Combat] VITAL THRUST fired!");
    }

    // ══════════════════════════════════════════════════════════════════
    //  STATE TRANSITIONS
    // ══════════════════════════════════════════════════════════════════

    private void EnterParry()
    {
        State = CombatState.Parrying;
        _attackBuffered = false;
        _isParryActive = false;
        _inComboWindow = false;
        _currentAttack = null;
        _playback.Travel("Parry");
        GameLog.CombatLog("[Combat] Parry started");
    }

    private void CancelAttackIntoParry()
    {
        _hitbox.Deactivate();
        _comboStep = 0;
        _attackBuffered = false;
        _inComboWindow = false;
        _currentAttack = null;

        if (_isVitalThrusting)
        {
            _isVitalThrusting = false;
            StopSparkle();
        }

        State = CombatState.Parrying;
        _isParryActive = false;
        _playback.Travel("Parry");
        GameLog.CombatLog("[Combat] Attack cancelled → Parry");
    }

    private void EnterStunned()
    {
        State = CombatState.Stunned;
        _attackBuffered = false;
        _comboStep = 0;
        _isParryActive = false;
        _inComboWindow = false;
        _currentAttack = null;

        if (_isVitalThrusting)
        {
            _isVitalThrusting = false;
            StopSparkle();
        }

        _hitbox.Deactivate();
        _playback.Travel("HitReaction");
        GameLog.CombatLog("[Combat] Stunned");
    }

    private void ReturnToFree()
    {
        State = CombatState.Free;
        _comboStep = 0;
        _attackBuffered = false;
        _isParryActive = false;
        _inComboWindow = false;
        _currentAttack = null;
        _playback.Travel("Idle");
    }

    // ══════════════════════════════════════════════════════════════════
    //  TICK (called every physics frame)
    // ══════════════════════════════════════════════════════════════════

    public void Tick(float dt)
    {
        _dash?.Tick(dt);

        if (State == CombatState.Dashing && (_dash == null || !_dash.IsDashing))
        {
            ReturnToFree();
            GameLog.CombatLog("[Combat] Dash → Free");
        }

        if (!_inComboWindow) return;

        _comboWindowTimer -= dt;
        if (_comboWindowTimer <= 0f)
        {
            _inComboWindow = false;
            ReturnToFree();
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  DAMAGE PIPELINE
    // ══════════════════════════════════════════════════════════════════

    public bool ShouldTakeDamage(DamageData data)
    {
        if (State == CombatState.Parrying && _isParryActive)
        {
            bool attackIsParriable = true;
            if (data.Source is EnemyBase enemy)
                attackIsParriable = enemy.IsCurrentAttackParriable;

            if (attackIsParriable)
            {
                OnParrySuccess(data);
                return false;
            }

            GameLog.CombatLog($"[Combat] Parry FAILED — {data.Source?.Name}'s attack is not parriable");
        }
        return true;
    }

    public void OnDamageTaken(DamageData data, bool survived)
    {
        _dash?.ForceCancel();

        ApplyHitStop(FallbackHitStopDuration, FallbackHitStopTimeScale);
        _camera?.Shake(TakeHitShakeIntensity, TakeHitShakeDuration);

        Game.VFX.GameVFX.Instance?.SpawnScreenFlash(
            new Color(1f, 0.1f, 0.1f, 0.25f), 0.1f);

        GameLog.CombatLog($"[Combat] Took {data.Amount} damage");

        if (survived)
            EnterStunned();
    }

    public void OnDeath()
    {
        _dash?.ForceCancel();

        State = CombatState.Dead;
        _attackBuffered = false;
        _comboStep = 0;
        _isParryActive = false;
        _inComboWindow = false;
        _currentAttack = null;
        _vitalThrustReady = false;

        if (_isVitalThrusting)
        {
            _isVitalThrusting = false;
            StopSparkle();
        }

        _hitbox.Deactivate();
        _playback.Travel("Death");
        GameLog.CombatLog("[Combat] Dead");

        if (_camera != null && _camera.IsLockedOn)
            _camera.DisengageLock();
    }

    private void OnParrySuccess(DamageData data)
    {
        GameLog.CombatLog($"[Combat] PARRY SUCCESS against {data.Source?.Name}");

        ApplyHitStop(FallbackHitStopDuration, FallbackHitStopTimeScale);
        _camera?.Shake(DealHitShakeIntensity, DealHitShakeDuration);

        EventBus.Instance?.EmitParrySucceeded(data);
    }

    // ══════════════════════════════════════════════════════════════════
    //  HIT CONNECTED
    // ══════════════════════════════════════════════════════════════════

    public void OnHitConnected(DamageData data)
    {
        float stopDur = _currentAttack?.HitStopDuration ?? FallbackHitStopDuration;
        float stopScale = _currentAttack?.HitStopTimeScale ?? FallbackHitStopTimeScale;
        float shakeStr = _currentAttack?.CameraShakeIntensity ?? DealHitShakeIntensity;
        float shakeDur = _currentAttack?.CameraShakeDuration ?? DealHitShakeDuration;

        ApplyHitStop(stopDur, stopScale);
        _camera?.Shake(shakeStr, shakeDur);

        EventBus.Instance?.EmitHitLanded(data);
    }

    // ══════════════════════════════════════════════════════════════════
    //  ANIMATION CALLBACKS
    // ══════════════════════════════════════════════════════════════════

    public void OnAttackHitboxActivate()
    {
        _hitbox.Activate(_currentAttack);
    }

    public void OnAttackHitboxDeactivate()
    {
        _hitbox.Deactivate();
    }

    public void OnAttackAnimationFinished()
    {
        if (_isVitalThrusting)
        {
            _isVitalThrusting = false;
            StopSparkle();
            ReturnToFree();
            GameLog.CombatLog("[Combat] Vital thrust complete → Free");
            return;
        }

        if (_attackBuffered && _comboStep < MaxComboSteps - 1)
        {
            StartAttack(_comboStep + 1);
        }
        else if (_comboStep < MaxComboSteps - 1)
        {
            _inComboWindow = true;
            _comboWindowTimer = _currentAttack?.ComboWindowDuration ?? FallbackComboWindow;
            GameLog.CombatLog("[Combat] Combo window open");
        }
        else
        {
            ReturnToFree();
        }
    }

    public void OnParryWindowOpen()
    {
        _isParryActive = true;
        GameLog.CombatLog("[Combat] Parry window OPEN");
    }

    public void OnParryWindowClose()
    {
        _isParryActive = false;
        GameLog.CombatLog("[Combat] Parry window CLOSED");
    }

    public void OnParryAnimationFinished() => ReturnToFree();

    public void OnStunAnimationFinished() => ReturnToFree();

    // ══════════════════════════════════════════════════════════════════
    //  VITAL SPARKLE CONTROL
    // ══════════════════════════════════════════════════════════════════

    private void StartSparkle()
    {
        if (_vitalSparkle != null)
            _vitalSparkle.Emitting = true;

        if (_vitalSparkleLight != null)
            _vitalSparkleLight.LightEnergy = 3f;
    }

    private void StopSparkle()
    {
        if (_vitalSparkle != null)
            _vitalSparkle.Emitting = false;

        if (_vitalSparkleLight != null)
            _vitalSparkleLight.LightEnergy = 0f;
    }

    // ══════════════════════════════════════════════════════════════════
    //  SPARKLE HELPERS
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Scale curve: stars pop in at full size then shrink to nothing.
    /// Creates the classic anime sparkle "flash and fade" feel.
    /// </summary>
    private static CurveTexture MakeScaleCurve()
    {
        var curve = new Curve();
        // Pop in at full scale
        curve.AddPoint(new Vector2(0f, 1f));
        // Hold briefly
        curve.AddPoint(new Vector2(0.2f, 1f));
        // Shrink to nothing
        curve.AddPoint(new Vector2(1f, 0f));

        var tex = new CurveTexture();
        tex.Curve = curve;
        return tex;
    }

    /// <summary>
    /// Color ramp: white-gold core → golden → transparent.
    /// The blue-white start gives the "hot" anime sparkle look,
    /// fading through gold to match the vital system's color language.
    /// </summary>
    private static GradientTexture1D MakeSparkleRamp()
    {
        var gradient = new Gradient();
        gradient.Colors = new[]
        {
            new Color(1f, 1f, 1f, 1f),         // White-hot core
            new Color(1f, 0.9f, 0.5f, 0.9f),   // Golden midlife
            new Color(1f, 0.7f, 0.2f, 0f)      // Fade out warm
        };
        gradient.Offsets = new[] { 0f, 0.3f, 1f };

        var tex = new GradientTexture1D();
        tex.Gradient = gradient;
        return tex;
    }

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
