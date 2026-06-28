using Game.Autoloads;
using Game.Characters;
using Game.Core.Data;
using Game.Core.Interfaces;
using Game.UI;
using Godot;

/// <summary>
/// Working enemy with AI. Extends CharacterBase for health/knockback,
/// composes EnemyAI for behavior. Finds the first MeshInstance3D in the
/// scene tree for visual feedback (state colors, scale pulses).
///
/// Scene requirements:
///   - Root: CharacterBody3D with this script, in group "Lockable"
///   - Child: imported Blender model with at least one MeshInstance3D
///   - Child: Hurtbox (Area3D) — layer EnemyHurtbox (16), mask 0
///   - Child: Hitbox (Area3D) named "Hitbox" — layer EnemyHitbox (8), mask PlayerHurtbox (4)
///   - Assign EnemyConfig resource to the Config export
///   - Assign CharacterStats resource to the Stats export (inherited)
///
/// Above-head indicators:
///   - Parriable attack: orange diamond (Sekiro-style "deflect me" signal)
///   - Non-parriable attack: red sphere (danger — dodge, don't parry)
///   - Both pulse during telegraph and go solid during active attack
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

    private MeshInstance3D _mesh;
    private StandardMaterial3D _material;
    private Vector3 _baseScale;

    // Warning indicators — two meshes, one for parriable (diamond), one for unparriable (sphere)
    private MeshInstance3D _parryIndicator;     // orange diamond — "deflect this"
    private StandardMaterial3D _parryIndicatorMat;
    private MeshInstance3D _dangerIndicator;    // red sphere — "don't parry, dodge"
    private StandardMaterial3D _dangerIndicatorMat;

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
        CreateAttackIndicators();
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
        _parryIndicator.Visible = false;
        _dangerIndicator.Visible = false;
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

        // ── Attack indicators ─────────────────────────────────────
        // During telegraph: determine parriability from the queued attack.
        // During attack: use the live IsCurrentAttackParriable flag
        // (which animation tracks may have overridden).
        bool showAttackIndicator = aiState == EnemyAI.AIState.Telegraphing
                                || aiState == EnemyAI.AIState.Attacking;

        if (showAttackIndicator)
        {
            // Determine parriability — telegraph uses queued attack data,
            // active attack uses the live flag
            bool isParriable;
            if (aiState == EnemyAI.AIState.Telegraphing)
                isParriable = _ai.CurrentAttack?.IsParriable ?? false;
            else
                isParriable = IsCurrentAttackParriable;

            // Show the correct indicator
            _parryIndicator.Visible = isParriable;
            _dangerIndicator.Visible = !isParriable;

            if (aiState == EnemyAI.AIState.Telegraphing)
            {
                float p = _ai.TelegraphProgress;

                // Pulse faster as attack approaches
                float pulseSpeed = Mathf.Lerp(8f, 30f, p);
                float bob = Mathf.Sin(p * pulseSpeed * Mathf.Pi) * 0.1f;
                float indicatorY = 2.5f + bob;

                if (isParriable)
                {
                    _parryIndicator.Position = new Vector3(0f, indicatorY, 0f);

                    // Orange → bright orange as telegraph progresses (Sekiro parry color)
                    var col = new Color(1f, Mathf.Lerp(0.65f, 0.4f, p), 0f);
                    _parryIndicatorMat.AlbedoColor = col;
                    _parryIndicatorMat.Emission = col;
                    _parryIndicatorMat.EmissionEnergyMultiplier = Mathf.Lerp(3f, 8f, p);
                }
                else
                {
                    _dangerIndicator.Position = new Vector3(0f, indicatorY, 0f);

                    // Red pulses brighter
                    var col = new Color(1f, Mathf.Lerp(0.2f, 0f, p), 0f);
                    _dangerIndicatorMat.AlbedoColor = col;
                    _dangerIndicatorMat.Emission = col;
                    _dangerIndicatorMat.EmissionEnergyMultiplier = Mathf.Lerp(2f, 6f, p);
                }

                // Body scales up during wind-up
                _mesh.Scale = _baseScale * Mathf.Lerp(1f, 1.15f, p);
                _material.AlbedoColor = new Color(
                    Mathf.Lerp(0.7f, 1f, p),
                    Mathf.Lerp(0.2f, 0f, p),
                    Mathf.Lerp(0.2f, 0f, p));
            }
            else // Attacking — solid, no pulse
            {
                if (isParriable)
                {
                    _parryIndicator.Position = new Vector3(0f, 2.5f, 0f);
                    _parryIndicatorMat.AlbedoColor = new Color(1f, 0.5f, 0f);
                    _parryIndicatorMat.Emission = new Color(1f, 0.5f, 0f);
                    _parryIndicatorMat.EmissionEnergyMultiplier = 10f;
                }
                else
                {
                    _dangerIndicator.Position = new Vector3(0f, 2.5f, 0f);
                    _dangerIndicatorMat.AlbedoColor = new Color(1f, 0f, 0f);
                    _dangerIndicatorMat.Emission = new Color(1f, 0f, 0f);
                    _dangerIndicatorMat.EmissionEnergyMultiplier = 8f;
                }

                _material.AlbedoColor = Colors.White;
                _mesh.Scale = _baseScale * 1.2f;
            }
        }
        else
        {
            _parryIndicator.Visible = false;
            _dangerIndicator.Visible = false;
        }

        // ── Body color by state (non-attack states) ──────────────
        if (!showAttackIndicator)
        {
            if (aiState == EnemyAI.AIState.Recovering)
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

        // ── Active vital — update position and pulse ──────────────
        if (_vitalSystem.IsActive && _vitalIndicator.Visible)
        {
            _vitalIndicator.GlobalPosition = _vitalSystem.ActiveVitalWorldPosition;

            _vitalPulseTimer += dt;
            float pulse = 0.5f + 0.5f * Mathf.Sin(_vitalPulseTimer * 8f);

            bool isMini = currentState == VitalSystem.VitalState.MiniActive;
            float baseEnergy = isMini ? 4f : 3f;
            _vitalMat.EmissionEnergyMultiplier = baseEnergy + pulse * 3f;

            float scale = isMini ? 0.15f : 0.25f;
            float scaleBoost = scale + pulse * 0.05f;
            _vitalIndicator.Scale = new Vector3(scaleBoost, scaleBoost, scaleBoost);
        }
    }

    private void OnVitalStateChanged(VitalSystem.VitalState from, VitalSystem.VitalState to)
    {
        switch (to)
        {
            case VitalSystem.VitalState.PrimaryActive:
                ShowVitalIndicator(new Color(0.2f, 1f, 0.2f), 0.25f);
                break;

            case VitalSystem.VitalState.MiniActive:
                ShowVitalIndicator(new Color(0.2f, 0.6f, 1f), 0.15f);
                break;

            case VitalSystem.VitalState.Complete:
            case VitalSystem.VitalState.Failed:
            case VitalSystem.VitalState.Inactive:
                HideVitalIndicator();
                break;
        }
    }

    private void ShowVitalIndicator(Color color, float scale)
    {
        _vitalMat.AlbedoColor = color;
        _vitalMat.Emission = color;
        _vitalIndicator.Scale = new Vector3(scale, scale, scale);
        _vitalIndicator.Visible = true;
        _vitalPulseTimer = 0f;
    }

    private void HideVitalIndicator()
    {
        _vitalIndicator.Visible = false;
    }

    // ══════════════════════════════════════════════════════════════════
    //  VISUAL CONSTRUCTION
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Find the enemy's body mesh from the imported Blender model.
    /// Searches children recursively for the first MeshInstance3D.
    /// Assigns a StandardMaterial3D override for state-based color changes.
    /// </summary>
    protected virtual void CreateVisuals()
    {
        _mesh = FindMeshRecursive(this);

        if (_mesh == null)
        {
            GD.PrintErr($"[{Name}] No MeshInstance3D found in scene tree — enemy will have no visuals.");
            // Create an invisible placeholder so null checks don't explode
            _mesh = new MeshInstance3D();
            _mesh.Visible = false;
            AddChild(_mesh);
        }

        _material = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.5f, 0.15f, 0.15f)
        };
        _mesh.MaterialOverride = _material;
        _baseScale = _mesh.Scale;
    }

    /// <summary>
    /// Recursively find the first MeshInstance3D child in the scene tree.
    /// Skips indicator meshes we create ourselves (parry/danger/vital).
    /// </summary>
    private static MeshInstance3D FindMeshRecursive(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is MeshInstance3D mesh)
                return mesh;

            var found = FindMeshRecursive(child);
            if (found != null)
                return found;
        }
        return null;
    }

    /// <summary>
    /// Two attack indicators above the enemy's head:
    ///   - Parry indicator: orange diamond — signals "this attack is parriable"
    ///   - Danger indicator: red sphere — signals "unblockable, dodge this"
    /// Only one is visible at a time during telegraph/attack phases.
    /// </summary>
    private void CreateAttackIndicators()
    {
        // ── Parry indicator (orange diamond) ──────────────────────
        _parryIndicator = new MeshInstance3D();
        var diamond = new PrismMesh
        {
            Size = new Vector3(0.25f, 0.35f, 0.25f)
        };
        _parryIndicator.Mesh = diamond;
        // Rotate 180° on X so the prism points upward like a diamond
        _parryIndicator.RotationDegrees = new Vector3(180f, 0f, 0f);

        _parryIndicatorMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.55f, 0f),
            EmissionEnabled = true,
            Emission = new Color(1f, 0.55f, 0f),
            EmissionEnergyMultiplier = 4f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            NoDepthTest = true
        };
        _parryIndicator.MaterialOverride = _parryIndicatorMat;
        _parryIndicator.Position = new Vector3(0f, 2.5f, 0f);
        _parryIndicator.Visible = false;
        AddChild(_parryIndicator);

        // ── Danger indicator (red sphere) ─────────────────────────
        _dangerIndicator = new MeshInstance3D();
        var sphere = new SphereMesh { Radius = 0.15f, Height = 0.3f };
        _dangerIndicator.Mesh = sphere;

        _dangerIndicatorMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.1f, 0f),
            EmissionEnabled = true,
            Emission = new Color(1f, 0.1f, 0f),
            EmissionEnergyMultiplier = 3f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            NoDepthTest = true
        };
        _dangerIndicator.MaterialOverride = _dangerIndicatorMat;
        _dangerIndicator.Position = new Vector3(0f, 2.5f, 0f);
        _dangerIndicator.Visible = false;
        AddChild(_dangerIndicator);
    }

    private void CreateVitalIndicator()
    {
        _vitalIndicator = new MeshInstance3D();
        var diamond = new PrismMesh
        {
            Size = new Vector3(0.25f, 0.35f, 0.25f)
        };
        _vitalIndicator.Mesh = diamond;
        _vitalIndicator.RotationDegrees = new Vector3(180f, 0f, 0f);

        _vitalMat = new StandardMaterial3D
        {
            EmissionEnabled = true,
            EmissionEnergyMultiplier = 3f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            NoDepthTest = true
        };
        _vitalIndicator.MaterialOverride = _vitalMat;
        _vitalIndicator.Visible = false;
        _vitalIndicator.TopLevel = true;
        AddChild(_vitalIndicator);
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
        _parryIndicator.Visible = false;
        _dangerIndicator.Visible = false;
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
