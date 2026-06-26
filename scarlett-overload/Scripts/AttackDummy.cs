using Game.Characters;
using Game.Core.Data;
using Game.Core.Interfaces;
using Godot;

/// <summary>
/// Aggressive target dummy with a timed attack cycle.
/// Telegraph → Attack → Recovery → Idle → repeat.
///
/// All health, knockback, gravity, and damage pipeline logic
/// lives in CharacterBase. This class only owns:
///   - The attack FSM
///   - Telegraph visuals (warning indicator, body pulse)
///   - Visual feedback (flash, attack light)
/// </summary>
public partial class AttackDummy : CharacterBase, ILockOnTarget
{
    [ExportGroup("Attack Cycle")]
    [Export] public float AttackInterval { get; set; } = 2.5f;
    [Export] public float TelegraphDuration { get; set; } = 1.0f;
    [Export] public float AttackActiveDuration { get; set; } = 0.25f;
    [Export] public float RecoveryDuration { get; set; } = 0.4f;

    private MeshInstance3D _mesh;
    private StandardMaterial3D _material;
    private Hitbox _attackHitbox;
    private Vector3 _baseScale;

    // Warning indicator
    private MeshInstance3D _warningIndicator;
    private StandardMaterial3D _warningMat;
    private OmniLight3D _attackFlash;

    // FSM
    private enum DummyState { Idle, Telegraph, Attacking, Recovery }
    private DummyState _state = DummyState.Idle;
    private float _stateTimer;

    // ── ITargetable ───────────────────────────────────────────────────
    public Vector3 TargetPosition => GlobalPosition + new Vector3(0f, 1f, 0f);
    public bool IsValidTarget => !IsDead;

    // ── ILockOnTarget ─────────────────────────────────────────────────
    public string TargetName => "Attack Dummy";

    // ══════════════════════════════════════════════════════════════════
    //  CHARACTERBASE OVERRIDES
    // ══════════════════════════════════════════════════════════════════

    protected override void Initialize()
    {
        Stats ??= new CharacterStats
        {
            MaxHealth = 100,
            KnockbackDecay = 10f,
            RespawnDelay = 3f
        };

        _mesh = GetNode<MeshInstance3D>("MeshInstance3D");
        _attackHitbox = GetNode<Hitbox>("AttackHitbox");
        _attackHitbox.Deactivate();

        _material = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.6f, 0.15f, 0.15f)
        };
        _mesh.MaterialOverride = _material;
        _baseScale = _mesh.Scale;

        CreateWarningIndicator();
        CreateAttackFlash();

        _stateTimer = AttackInterval;
        _state = DummyState.Idle;
    }

    /// <summary>
    /// Tick the attack FSM every physics frame (after MoveAndSlide).
    /// </summary>
    protected override void ProcessUpdate(float dt)
    {
        if (IsDead) return;
        TickAttackFSM(dt);
    }

    protected override void OnDamageTaken(DamageData data)
    {
        FlashWhite();
    }

    protected override void OnDeath()
    {
        _attackHitbox.Deactivate();
        _warningIndicator.Visible = false;
        _attackFlash.LightEnergy = 0f;
        SetHurtboxActive(false);
        GD.Print($"{Name} killed.");

        var deathTween = CreateTween();
        deathTween.SetParallel(true);
        deathTween.TweenProperty(_mesh, "scale", Vector3.Zero, 0.4f)
            .SetEase(Tween.EaseType.In)
            .SetTrans(Tween.TransitionType.Back);
        deathTween.TweenProperty(_material, "albedo_color",
            new Color(0.3f, 0.3f, 0.3f), 0.4f);
        deathTween.SetParallel(false);

        float delay = Stats?.RespawnDelay ?? 3f;
        if (delay > 0f)
        {
            deathTween.TweenInterval(delay);
            deathTween.TweenCallback(Callable.From(Respawn));
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  ATTACK FSM
    // ══════════════════════════════════════════════════════════════════

    private void TickAttackFSM(float dt)
    {
        _stateTimer -= dt;

        switch (_state)
        {
            case DummyState.Idle:
                if (_stateTimer <= 0f)
                {
                    _state = DummyState.Telegraph;
                    _stateTimer = TelegraphDuration;
                    _warningIndicator.Visible = true;
                }
                break;

            case DummyState.Telegraph:
                TickTelegraph();
                if (_stateTimer <= 0f)
                    EnterAttacking();
                break;

            case DummyState.Attacking:
                _attackFlash.LightEnergy = Mathf.Lerp(
                    0f, 5f, _stateTimer / AttackActiveDuration);
                if (_stateTimer <= 0f)
                    EnterRecovery();
                break;

            case DummyState.Recovery:
                if (_stateTimer <= 0f)
                {
                    _state = DummyState.Idle;
                    _stateTimer = AttackInterval;
                    _material.AlbedoColor = new Color(0.6f, 0.15f, 0.15f);
                }
                break;
        }
    }

    private void TickTelegraph()
    {
        float progress = 1f - (_stateTimer / TelegraphDuration);

        // Warning indicator pulses faster as attack approaches
        float pulseSpeed = Mathf.Lerp(8f, 30f, progress);
        float bob = Mathf.Sin(_stateTimer * pulseSpeed) * 0.1f;

        var warningColor = new Color(
            1f, Mathf.Lerp(0f, 0.8f, 1f - progress), 0f);
        _warningMat.AlbedoColor = warningColor;
        _warningMat.Emission = warningColor;
        _warningMat.EmissionEnergyMultiplier = Mathf.Lerp(2f, 6f, progress);
        _warningIndicator.Position = new Vector3(0f, 2.5f + bob, 0f);

        // Body wind-up: scale and color shift
        _mesh.Scale = _baseScale * Mathf.Lerp(1f, 1.15f, progress);
        _material.AlbedoColor = new Color(
            Mathf.Lerp(0.6f, 1f, progress),
            Mathf.Lerp(0.15f, 0f, progress),
            Mathf.Lerp(0.15f, 0f, progress));
    }

    private void EnterAttacking()
    {
        _state = DummyState.Attacking;
        _stateTimer = AttackActiveDuration;
        _material.AlbedoColor = Colors.White;
        _mesh.Scale = _baseScale * 1.2f;
        _warningMat.AlbedoColor = new Color(1f, 0f, 0f);
        _warningMat.Emission = new Color(1f, 0f, 0f);
        _warningMat.EmissionEnergyMultiplier = 8f;
        _attackFlash.LightEnergy = 5f;
        _attackHitbox.Activate();
        GD.Print($"{Name} swings!");
    }

    private void EnterRecovery()
    {
        _attackHitbox.Deactivate();
        _state = DummyState.Recovery;
        _stateTimer = RecoveryDuration;
        _material.AlbedoColor = new Color(0.4f, 0.4f, 0.4f);
        _mesh.Scale = _baseScale;
        _warningIndicator.Visible = false;
        _attackFlash.LightEnergy = 0f;
    }

    // ══════════════════════════════════════════════════════════════════
    //  PRIVATE
    // ══════════════════════════════════════════════════════════════════

    private void Respawn()
    {
        ResetHealth();
        _state = DummyState.Idle;
        _stateTimer = AttackInterval;
        _mesh.Scale = _baseScale;
        _material.AlbedoColor = new Color(0.6f, 0.15f, 0.15f);
        _warningIndicator.Visible = false;
        _attackFlash.LightEnergy = 0f;
        SetHurtboxActive(true);
        _attackHitbox.Deactivate();
        GD.Print($"{Name} respawned.");
    }

    private void FlashWhite()
    {
        var savedColor = _material.AlbedoColor;
        _material.AlbedoColor = Colors.White;
        var tween = CreateTween();
        tween.TweenProperty(_material, "albedo_color", savedColor, 0.2f);
    }

    private void CreateWarningIndicator()
    {
        _warningIndicator = new MeshInstance3D();
        var sphere = new SphereMesh { Radius = 0.2f, Height = 0.4f };
        _warningIndicator.Mesh = sphere;
        _warningMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.8f, 0f),
            EmissionEnabled = true,
            Emission = new Color(1f, 0.8f, 0f),
            EmissionEnergyMultiplier = 3f
        };
        _warningIndicator.MaterialOverride = _warningMat;
        _warningIndicator.Position = new Vector3(0f, 2.5f, 0f);
        _warningIndicator.Visible = false;
        AddChild(_warningIndicator);
    }

    private void CreateAttackFlash()
    {
        _attackFlash = new OmniLight3D
        {
            LightColor = new Color(1f, 0.3f, 0.1f),
            LightEnergy = 0f,
            OmniRange = 4f,
            Position = new Vector3(0f, 1f, 0f)
        };
        AddChild(_attackFlash);
    }
}
