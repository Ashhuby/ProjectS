using Game.Autoloads;
using Game.Characters;
using Game.Core.Data;
using Game.Core.Interfaces;
using Game.UI;
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
    private VitalSystem _vitalSystem;

    // ── Parriable tracking ────────────────────────────────────────────

    /// <summary>
    /// True when this enemy's current attack can be deflected by
    /// the player's parry. Set automatically from AttackData.IsParriable
    /// when entering Attacking state. Can be overridden per-frame by
    /// animation method call tracks (SetParriable/ClearParriable).
    /// </summary>
    public bool IsCurrentAttackParriable { get; private set; }

    // ── Rotation lock ─────────────────────────────────────────────────

    private bool _rotationLocked;

    // ── AI state tracking ─────────────────────────────────────────────

    private EnemyAI.AIState _prevAiState;

    // ── Visuals ───────────────────────────────────────────────────────

    protected MeshInstance3D _mesh;
    protected StandardMaterial3D _material;
    protected Vector3 _baseScale;

    // Warning indicator
    private MeshInstance3D _warningIndicator;
    private StandardMaterial3D _warningMat;

    // Vital indicator (3D diamond at vital position)
    private MeshInstance3D _vitalIndicator;
    private StandardMaterial3D _vitalMat;
    private float _vitalPulseTimer;
    private VitalSystem.VitalState _prevVitalState;

    // Health bar (world-space, above head)
    private EnemyHealthBar _healthBar;

    // ── ITargetable ───────────────────────────────────────────────────
    public Vector3 TargetPosition => GlobalPosition + new Vector3(0f, 1f, 0f);
    public bool IsValidTarget => !IsDead;

    // ── ILockOnTarget ─────────────────────────────────────────────────
    public string TargetName => Name;

    // ── Public AI accessors ───────────────────────────────────────────

    /// <summary>Expose AI for VitalSystem and other external consumers.</summary>
    public EnemyAI AI => _ai;

    /// <summary>Expose VitalSystem for indicator visuals and HUD compass.</summary>
    public VitalSystem Vitals => _vitalSystem;

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
        _prevAiState = _ai.State;

        _vitalSystem = new VitalSystem(this, Config);

        CreateVisuals();
        CreateWarningIndicator();
        CreateVitalIndicator();
        CreateHealthBar();

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

        // ── Track AI state changes ────────────────────────────────
        var currentState = _ai.State;
        if (currentState != _prevAiState)
        {
            HandleAIStateChange(_prevAiState, currentState);
            _prevAiState = currentState;
        }

        UpdateRotation(dt);
        UpdateVisuals(dt);
        UpdateVitalIndicator(dt);
    }

    protected override void OnDamageTaken(DamageData data)
    {
        // ── Vital check (before AI stun resets state) ─────────────
        // If a vital is active, check if this hit pops it or fails it.
        // Must run before _ai.OnDamageTaken which would change AI state.
        if (_vitalSystem != null && _vitalSystem.IsActive && data.Source != null)
            _vitalSystem.OnEnemyHit(data);

        _ai.OnDamageTaken();
        FlashColor(Colors.White, 0.2f);
    }

    protected override void OnDeath()
    {
        _ai.OnDeath();
        _hitbox.Deactivate();
        _warningIndicator.Visible = false;
        HideVitalIndicator();
        _rotationLocked = false;
        IsCurrentAttackParriable = false;
        _vitalSystem?.Reset();
        SetHurtboxActive(false);
        _healthBar?.OnOwnerDied();

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
    //  AI STATE CHANGE TRACKING
    // ══════════════════════════════════════════════════════════════════

    private void HandleAIStateChange(EnemyAI.AIState from, EnemyAI.AIState to)
    {
        // ── Parriable tracking ────────────────────────────────────
        // Auto-set from AttackData when entering Attacking.
        // Animation method call tracks (SetParriable/ClearParriable)
        // can override this per-frame for finer control.
        if (to == EnemyAI.AIState.Attacking)
            IsCurrentAttackParriable = _ai.CurrentAttack?.IsParriable ?? false;
        else if (from == EnemyAI.AIState.Attacking)
            IsCurrentAttackParriable = false;

        // ── Rotation unlock ───────────────────────────────────────
        // When leaving Stunned, always unlock rotation.
        if (from == EnemyAI.AIState.Stunned && _rotationLocked)
        {
            _rotationLocked = false;
            GD.Print($"[{Name}] Rotation unlocked — stun ended");
        }

        // ── Vital system — stun expired ───────────────────────────
        if (from == EnemyAI.AIState.Stunned)
            _vitalSystem?.OnStunExpired();
    }

    // ══════════════════════════════════════════════════════════════════
    //  PARRIABLE — ANIMATION METHOD CALL TRACK API
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Called by animation method call tracks to mark the current
    /// attack as parriable. Same pattern as the player's
    /// OnParryWindowOpen/OnParryWindowClose.
    /// </summary>
    public void SetParriable() => IsCurrentAttackParriable = true;

    /// <summary>
    /// Called by animation method call tracks to clear parriable status.
    /// </summary>
    public void ClearParriable() => IsCurrentAttackParriable = false;

    // ══════════════════════════════════════════════════════════════════
    //  VITAL SYSTEM — PUBLIC API
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Apply bonus damage from a vital pop. Separate from the normal
    /// TakeDamage pipeline — no knockback, no hit reaction, no re-entry.
    /// Handles death check internally.
    /// </summary>
    public void ApplyVitalDamage(int bonusDamage)
    {
        if (IsDead || bonusDamage <= 0) return;

        _currentHealth = Mathf.Max(_currentHealth - bonusDamage, 0);
        NotifyHealthChanged();
        GD.Print($"[{Name}] Vital bonus damage: {bonusDamage}. HP: {_currentHealth}/{MaxHealth}");

        if (_currentHealth <= 0)
        {
            _isDead = true;
            OnDeath();
            EventBus.Instance?.EmitEntityDied(this);
        }
    }

    /// <summary>
    /// Extend the current parry stun timer. Called by VitalSystem
    /// when the primary vital pops to give the player time for the mini vital.
    /// </summary>
    public void ExtendStun(float extraTime)
    {
        _ai.ExtendStun(extraTime);
    }

    public void CollapseStun(float graceTime)
    {
        _ai.CollapseStun(graceTime);
        _rotationLocked = false;
    }

    // ══════════════════════════════════════════════════════════════════
    //  EVENT HANDLERS
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Fired by EventBus when the player parries any attack.
    /// Check if WE were the attacker — if so, enter parry stagger
    /// and lock rotation so the vital system has a stable reference frame.
    /// </summary>
    private void OnPlayerParried(DamageData data)
    {
        if (IsDead) return;

        // DamageData.Source is the CharacterBody3D that owns the hitbox
        if (data.Source == this)
        {
            _ai.OnParried();
            _rotationLocked = true;
            IsCurrentAttackParriable = false;
            FlashColor(new Color(0.5f, 0.5f, 1f), 0.3f);
            GD.Print($"[{Name}] Parry staggered — rotation locked");

            // Activate vital system — find player position for direction selection
            if (_vitalSystem != null)
            {
                var player = GetTree().GetFirstNodeInGroup("Player") as Node3D;
                if (player != null)
                    _vitalSystem.Activate(player.GlobalPosition);
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  ROTATION
    // ══════════════════════════════════════════════════════════════════

    private void UpdateRotation(float dt)
    {
        // Rotation locked during parry stun — enemy faces the direction
        // they were looking when parried. Vital system relies on this.
        if (_rotationLocked) return;

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
    //  VITAL INDICATOR (3D diamond at vital position)
    // ══════════════════════════════════════════════════════════════════

    private void UpdateVitalIndicator(float dt)
    {
        if (_vitalSystem == null) return;

        var currentState = _vitalSystem.State;

        // ── State transitions ─────────────────────────────────────
        if (currentState != _prevVitalState)
        {
            OnVitalStateChanged(_prevVitalState, currentState);
            _prevVitalState = currentState;
        }

        // ── Pulse animation when active ───────────────────────────
        if (_vitalSystem.IsActive && _vitalIndicator.Visible)
        {
            _vitalPulseTimer += dt;
            float pulse = 1f + 0.25f * Mathf.Sin(_vitalPulseTimer * 8f);
            _vitalIndicator.Scale = Vector3.One * 0.35f * pulse;

            // Slow rotation for visual interest
            _vitalIndicator.RotateY(2f * dt);

            // Keep positioned at vital world position (TopLevel = true)
            _vitalIndicator.GlobalPosition = _vitalSystem.ActiveVitalWorldPosition;
        }
    }

    private void OnVitalStateChanged(VitalSystem.VitalState from, VitalSystem.VitalState to)
    {
        switch (to)
        {
            case VitalSystem.VitalState.PrimaryActive:
                ShowVitalIndicator(isPrimary: true);
                break;

            case VitalSystem.VitalState.MiniActive:
                // Burst at old position, then reposition
                SpawnVitalPopBurst();
                ShowVitalIndicator(isPrimary: false);
                break;

            case VitalSystem.VitalState.Complete:
                SpawnVitalPopBurst();
                HideVitalIndicator();
                break;

            case VitalSystem.VitalState.Failed:
            case VitalSystem.VitalState.Inactive:
                if (from == VitalSystem.VitalState.PrimaryActive
                    || from == VitalSystem.VitalState.MiniActive)
                    HideVitalIndicator();
                break;
        }
    }

    private void ShowVitalIndicator(bool isPrimary)
    {
        _vitalPulseTimer = 0f;

        // Primary: warm orange. Mini: bright yellow-white.
        Color col = isPrimary
            ? new Color(1f, 0.6f, 0.1f)
            : new Color(1f, 0.95f, 0.4f);

        _vitalMat.AlbedoColor = col;
        _vitalMat.Emission = col;
        _vitalMat.EmissionEnergyMultiplier = isPrimary ? 4f : 6f;

        _vitalIndicator.GlobalPosition = _vitalSystem.ActiveVitalWorldPosition;
        _vitalIndicator.Scale = Vector3.One * 0.35f;
        _vitalIndicator.Visible = true;

        string label = isPrimary ? "PRIMARY" : "MINI";
        GD.Print($"[{Name}] Vital indicator shown: {label}");
    }

    private void HideVitalIndicator()
    {
        _vitalIndicator.Visible = false;
    }

    private void SpawnVitalPopBurst()
   {
       HideVitalIndicator();
   }

    private void CreateVitalIndicator()
    {
        _vitalIndicator = new MeshInstance3D();

        // Diamond shape: rotated box mesh (thin, angled = diamond silhouette)
        var box = new BoxMesh();
        box.Size = new Vector3(0.5f, 0.5f, 0.08f);
        _vitalIndicator.Mesh = box;

        // Rotate 45° on Z to create diamond shape
        _vitalIndicator.RotationDegrees = new Vector3(0f, 0f, 45f);

        _vitalMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.6f, 0.1f),
            EmissionEnabled = true,
            Emission = new Color(1f, 0.6f, 0.1f),
            EmissionEnergyMultiplier = 4f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
        };
        _vitalIndicator.MaterialOverride = _vitalMat;

        // TopLevel so it positions in world space, not relative to enemy
        _vitalIndicator.TopLevel = true;
        _vitalIndicator.Visible = false;
        AddChild(_vitalIndicator);
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

    private void CreateHealthBar()
    {
        _healthBar = new EnemyHealthBar();
        _healthBar.Setup(this);
        AddChild(_healthBar);
    }

    // ══════════════════════════════════════════════════════════════════
    //  RESPAWN
    // ══════════════════════════════════════════════════════════════════

    private void Respawn()
    {
        ResetHealth();
        _rotationLocked = false;
        IsCurrentAttackParriable = false;
        _vitalSystem?.Reset();
        _mesh.Scale = _baseScale;
        _material.AlbedoColor = new Color(0.5f, 0.15f, 0.15f);
        _warningIndicator.Visible = false;
        HideVitalIndicator();
        _prevVitalState = VitalSystem.VitalState.Inactive;
        SetHurtboxActive(true);
        _hitbox.Deactivate();
        _healthBar?.OnOwnerRespawned();
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
