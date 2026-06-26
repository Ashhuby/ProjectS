using Game.Autoloads;
using Game.Characters;
using Game.Core.Data;
using Game.Core.Interfaces;
using Godot;

/// <summary>
/// Working enemy with AI. Extends CharacterBase for health/knockback,
/// composes EnemyAI for behavior. Creates capsule visuals by default —
/// subclass and override CreateVisuals() for proper enemy models.
///
/// Scene requirements:
///   - Root: CharacterBody3D with this script, in group "Lockable"
///   - Child: Hurtbox (Area3D) — layer EnemyHurtbox (16), mask 0
///   - Child: Hitbox (Area3D) named "Hitbox" — layer EnemyHitbox (8), mask PlayerHurtbox (4)
///   - Assign EnemyConfig resource to the Config export
///   - Assign CharacterStats resource to the Stats export (inherited)
/// </summary>
public partial class EnemyBase : CharacterBase, ILockOnTarget
{
    [ExportGroup("AI")]
    [Export] public EnemyConfig Config { get; set; }

    // ── Components ────────────────────────────────────────────────────

    private EnemyAI _ai;
    private Hitbox _hitbox;

    // ── Visuals ───────────────────────────────────────────────────────

    private MeshInstance3D _mesh;
    private StandardMaterial3D _material;
    private Vector3 _baseScale;

    // Warning indicator
    private MeshInstance3D _warningIndicator;
    private StandardMaterial3D _warningMat;

    // ── ITargetable ───────────────────────────────────────────────────
    public Vector3 TargetPosition => GlobalPosition + new Vector3(0f, 1f, 0f);
    public bool IsValidTarget => !IsDead;

    // ── ILockOnTarget ─────────────────────────────────────────────────
    public string TargetName => Name;

    // ══════════════════════════════════════════════════════════════════
    //  CHARACTERBASE LIFECYCLE
    // ══════════════════════════════════════════════════════════════════

    protected override void Initialize()
    {
        Stats ??= new CharacterStats
        {
            MaxHealth = 80,
            MoveSpeed = 4f,
            Gravity = 9.8f,
            KnockbackDecay = 10f,
            RespawnDelay = 5f
        };

        Config ??= new EnemyConfig();

        _hitbox = GetNode<Hitbox>("Hitbox");
        _hitbox.Deactivate();

        _ai = new EnemyAI(this, _hitbox, Config);

        CreateVisuals();
        CreateWarningIndicator();

        // Subscribe to parry events — react when OUR attack gets deflected
        if (EventBus.Instance != null)
            EventBus.Instance.ParrySucceeded += OnPlayerParried;
    }

    public override void _ExitTree()
    {
        base._ExitTree();
        if (EventBus.Instance != null)
            EventBus.Instance.ParrySucceeded -= OnPlayerParried;
    }

    // ══════════════════════════════════════════════════════════════════
    //  CHARACTERBASE OVERRIDES
    // ══════════════════════════════════════════════════════════════════

    protected override Vector3 ProcessMovement(Vector3 velocity, float dt)
    {
        return _ai.ComputeVelocity(velocity, dt);
    }

    protected override void ProcessUpdate(float dt)
    {
        if (IsDead) return;

        _ai.Tick(dt);
        UpdateRotation(dt);
        UpdateVisuals(dt);
    }

    protected override void OnDamageTaken(DamageData data)
    {
        _ai.OnDamageTaken();
        FlashColor(Colors.White, 0.2f);
    }

    protected override void OnDeath()
    {
        _ai.OnDeath();
        _hitbox.Deactivate();
        _warningIndicator.Visible = false;
        SetHurtboxActive(false);

        GD.Print($"[{Name}] Killed.");

        var tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(_mesh, "scale", Vector3.Zero, 0.4f)
            .SetEase(Tween.EaseType.In)
            .SetTrans(Tween.TransitionType.Back);
        tween.TweenProperty(_material, "albedo_color",
            new Color(0.3f, 0.3f, 0.3f), 0.4f);
        tween.SetParallel(false);

        float delay = Stats?.RespawnDelay ?? 5f;
        if (delay > 0f)
        {
            tween.TweenInterval(delay);
            tween.TweenCallback(Callable.From(Respawn));
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  EVENT HANDLERS
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Fired by EventBus when the player parries any attack.
    /// Check if WE were the attacker — if so, enter parry stagger.
    /// </summary>
    private void OnPlayerParried(DamageData data)
    {
        if (IsDead) return;

        // DamageData.Source is the CharacterBody3D that owns the hitbox
        if (data.Source == this)
        {
            _ai.OnParried();
            FlashColor(new Color(0.5f, 0.5f, 1f), 0.3f);
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  ROTATION
    // ══════════════════════════════════════════════════════════════════

    private void UpdateRotation(float dt)
    {
        Vector3 facing = _ai.GetFacingDirection();
        if (facing == Vector3.Zero) return;

        float speed = Config?.RotationSpeed ?? 8f;
        float angle = Mathf.Atan2(facing.X, facing.Z);
        Rotation = new Vector3(
            Rotation.X,
            Mathf.LerpAngle(Rotation.Y, angle, speed * dt),
            Rotation.Z);
    }

    // ══════════════════════════════════════════════════════════════════
    //  VISUAL FEEDBACK
    // ══════════════════════════════════════════════════════════════════

    private void UpdateVisuals(float dt)
    {
        var aiState = _ai.State;

        // Warning indicator — visible during telegraph and attack
        bool showWarning = aiState == EnemyAI.AIState.Telegraphing
                        || aiState == EnemyAI.AIState.Attacking;
        _warningIndicator.Visible = showWarning;

        if (aiState == EnemyAI.AIState.Telegraphing)
        {
            float p = _ai.TelegraphProgress;

            // Warning pulses faster as attack approaches
            float pulseSpeed = Mathf.Lerp(8f, 30f, p);
            float bob = Mathf.Sin(p * pulseSpeed * Mathf.Pi) * 0.1f;
            _warningIndicator.Position = new Vector3(0f, 2.5f + bob, 0f);

            var col = new Color(1f, Mathf.Lerp(0.8f, 0f, p), 0f);
            _warningMat.AlbedoColor = col;
            _warningMat.Emission = col;
            _warningMat.EmissionEnergyMultiplier = Mathf.Lerp(2f, 6f, p);

            // Body scales up during wind-up
            _mesh.Scale = _baseScale * Mathf.Lerp(1f, 1.15f, p);
            _material.AlbedoColor = new Color(
                Mathf.Lerp(0.7f, 1f, p),
                Mathf.Lerp(0.2f, 0f, p),
                Mathf.Lerp(0.2f, 0f, p));
        }
        else if (aiState == EnemyAI.AIState.Attacking)
        {
            _material.AlbedoColor = Colors.White;
            _mesh.Scale = _baseScale * 1.2f;
            _warningMat.AlbedoColor = new Color(1f, 0f, 0f);
            _warningMat.Emission = new Color(1f, 0f, 0f);
            _warningMat.EmissionEnergyMultiplier = 8f;
        }
        else if (aiState == EnemyAI.AIState.Recovering)
        {
            _material.AlbedoColor = new Color(0.4f, 0.4f, 0.5f);
            _mesh.Scale = _baseScale;
        }
        else if (aiState == EnemyAI.AIState.Stunned)
        {
            // Stunned color stays from the flash — no override needed
            _mesh.Scale = _baseScale;
        }
        else if (aiState == EnemyAI.AIState.Chasing)
        {
            _material.AlbedoColor = new Color(0.8f, 0.25f, 0.15f);
            _mesh.Scale = _baseScale;
        }
        else if (aiState == EnemyAI.AIState.Engaging)
        {
            _material.AlbedoColor = new Color(0.7f, 0.2f, 0.2f);
            _mesh.Scale = _baseScale;
        }
        else
        {
            // Idle
            _material.AlbedoColor = new Color(0.5f, 0.15f, 0.15f);
            _mesh.Scale = _baseScale;
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  VISUALS SETUP (override for real models)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates default capsule visuals. Override in subclasses
    /// that use actual enemy models — set _mesh and _material
    /// to your model's MeshInstance3D and material.
    /// </summary>
    protected virtual void CreateVisuals()
    {
        _mesh = GetNodeOrNull<MeshInstance3D>("MeshInstance3D");

        if (_mesh == null)
        {
            _mesh = new MeshInstance3D();
            var capsule = new CapsuleMesh { Radius = 0.4f, Height = 1.8f };
            _mesh.Mesh = capsule;
            _mesh.Position = new Vector3(0f, 0.9f, 0f);
            AddChild(_mesh);
        }

        _material = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.5f, 0.15f, 0.15f)
        };
        _mesh.MaterialOverride = _material;
        _baseScale = _mesh.Scale;
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

    private void Respawn()
    {
        ResetHealth();
        _mesh.Scale = _baseScale;
        _material.AlbedoColor = new Color(0.5f, 0.15f, 0.15f);
        _warningIndicator.Visible = false;
        SetHurtboxActive(true);
        _hitbox.Deactivate();
        GD.Print($"[{Name}] Respawned.");
    }

    private void FlashColor(Color flashColor, float duration)
    {
        var savedColor = _material.AlbedoColor;
        _material.AlbedoColor = flashColor;
        var tween = CreateTween();
        tween.TweenProperty(_material, "albedo_color", savedColor, duration);
    }
}
