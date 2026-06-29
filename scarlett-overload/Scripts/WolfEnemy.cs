using Game.Core.Data;
using Godot;

/// <summary>
/// Wolf enemy — extends EnemyBase with fully animation-driven combat.
///
/// The AI state machine handles detection, chasing, engaging, and
/// cooldowns. Everything else — lunge, hitbox, parry window, and
/// indicators — is driven by method call tracks on the Bite animation.
///
/// Method call tracks on Bite animation (target: root node ".."):
///   OnLungeStart()          — wolf leaps toward player
///   OnLungeStop()           — wolf plants feet
///   OnBiteHitboxActivate()  — jaw connects, hitbox on, parry window open
///   OnBiteHitboxDeactivate()— bite ends, hitbox off, parry window closed
///
/// WolfConfig.tres:
///   LungeSpeed = 0          — AI lunge disabled (animation owns it)
///   HitboxDelay = 999       — AI hitbox disabled (animation owns it)
///   AttackActiveDuration    — match Bite animation length (1.8)
///   RecoveryDuration = 0.1  — animation has its own recovery frames
///
/// Scene:
///   - WolfModel (.glb with Idle/Run/Bite)
///   - AnimationTree (Idle, Run, Bite states, Active = true)
///   - AnimationTree.anim_player → WolfModel/AnimationPlayer
///   - AnimationTree.root_node  → "../WolfModel"
/// </summary>
public partial class WolfEnemy : EnemyBase
{
    // ── Animation ─────────────────────────────────────────────────────

    private AnimationTree _animTree;
    private AnimationNodeStateMachinePlayback _playback;
    private EnemyAI.AIState _lastAnimState = (EnemyAI.AIState)(-1);

    // ── Animation-driven lunge ────────────────────────────────────────

    [Export] public float BiteLungeSpeed { get; set; } = 12f;

    private bool _isLunging;
    private Vector3 _lungeDirection;

    // ── Model ─────────────────────────────────────────────────────────

    private Node3D _modelRoot;
    private Vector3 _modelBaseScale;

    // ── Material ──────────────────────────────────────────────────────

    private Color _originalColor;
    private bool _isFlashing;

    // ── Base indicator suppression ────────────────────────────────────

    private MeshInstance3D _baseWarningIndicator;

    // ── Respawn ───────────────────────────────────────────────────────

    private bool _wasDead;

    // ── Attack indicator (two-phase) ──────────────────────────────────

    private enum IndicatorPhase { Off, Warning, Parry }

    private MeshInstance3D _warningMeshInst;
    private StandardMaterial3D _warningMeshMat;

    private MeshInstance3D _parryMeshInst;
    private StandardMaterial3D _parryMeshMat;

    private IndicatorPhase _indicatorPhase = IndicatorPhase.Off;
    private float _indicatorTimer;

    // ══════════════════════════════════════════════════════════════════
    //  VISUALS
    // ══════════════════════════════════════════════════════════════════

    protected override void CreateVisuals()
    {
        _modelRoot = GetNode<Node3D>("WolfModel");
        _modelBaseScale = _modelRoot.Scale;

        _mesh = FindFirstMesh(_modelRoot);
        if (_mesh == null)
        {
            _mesh = new MeshInstance3D { Mesh = new BoxMesh() };
            _modelRoot.AddChild(_mesh);
        }
        _baseScale = _mesh.Scale;

        var surfaceMat = _mesh.GetActiveMaterial(0);
        if (surfaceMat is StandardMaterial3D stdMat)
        {
            _material = stdMat;
            _originalColor = stdMat.AlbedoColor;
        }
        else
        {
            _material = new StandardMaterial3D { AlbedoColor = Colors.White };
            _mesh.MaterialOverride = _material;
            _originalColor = Colors.White;
        }

        _animTree = GetNodeOrNull<AnimationTree>("AnimationTree");
        if (_animTree != null)
            _playback = (AnimationNodeStateMachinePlayback)
                _animTree.Get("parameters/playback");

        CreateAttackIndicators();
    }

    /// <summary>
    /// Two separate meshes — one for warning, one for parry.
    /// Different shape, color, size, and behavior so the player
    /// can instantly tell them apart at a glance.
    ///
    /// Warning: small orange prism, pulses and bobs with increasing speed.
    /// Parry:   large red diamond, bright steady glow, snaps into existence.
    /// </summary>
    private void CreateAttackIndicators()
    {
        // ── Warning indicator (orange prism) ──────────────────────
        _warningMeshInst = new MeshInstance3D
        {
            Mesh = new PrismMesh { Size = new Vector3(0.25f, 0.25f, 0.1f) }
        };
        _warningMeshMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.6f, 0f),
            EmissionEnabled = true,
            Emission = new Color(1f, 0.6f, 0f),
            EmissionEnergyMultiplier = 3f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            RenderPriority = 5
        };
        _warningMeshInst.MaterialOverride = _warningMeshMat;
        _warningMeshInst.Position = new Vector3(0f, 1.8f, 0f);
        _warningMeshInst.Visible = false;
        AddChild(_warningMeshInst);

        // ── Parry indicator (red diamond) ─────────────────────────
        _parryMeshInst = new MeshInstance3D();
        var box = new BoxMesh { Size = new Vector3(0.35f, 0.35f, 0.06f) };
        _parryMeshInst.Mesh = box;
        _parryMeshInst.RotationDegrees = new Vector3(0f, 0f, 45f);
        _parryMeshMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0f, 0.4f, 1f),
            EmissionEnabled = true,
            Emission = new Color(0f, 0.4f, 1f),
            EmissionEnergyMultiplier = 8f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            RenderPriority = 6
        };
        _parryMeshInst.MaterialOverride = _parryMeshMat;
        _parryMeshInst.Position = new Vector3(0f, 1.8f, 0f);
        _parryMeshInst.Visible = false;
        AddChild(_parryMeshInst);
    }

    // ══════════════════════════════════════════════════════════════════
    //  PROCESS
    // ══════════════════════════════════════════════════════════════════

    protected override void ProcessUpdate(float dt)
    {
        // ── Respawn ───────────────────────────────────────────────
        if (_wasDead && !IsDead)
        {
            _modelRoot.Scale = _modelBaseScale;
            _material.AlbedoColor = _originalColor;
            _isLunging = false;
            _indicatorPhase = IndicatorPhase.Off;
            _lastAnimState = (EnemyAI.AIState)(-1);
            _wasDead = false;
        }
        _wasDead = IsDead;

        // ── Base: AI tick, rotation, UpdateVisuals ─────────────────
        base.ProcessUpdate(dt);

        // ── Suppress base visual damage ───────────────────────────
        if (!IsDead)
        {
            _mesh.Scale = _baseScale;
            if (!_isFlashing)
                _material.AlbedoColor = _originalColor;

            // Kill base warning indicator every frame
            if (_baseWarningIndicator == null)
                _baseWarningIndicator = FindBaseWarningIndicator();
            if (_baseWarningIndicator != null)
                _baseWarningIndicator.Visible = false;

            // Suppress base auto-parriable (HandleAIStateChange sets
            // it on Attacking entry — we only want animation tracks
            // to control parriable timing)
            if (AI.State == EnemyAI.AIState.Attacking
                && _indicatorPhase != IndicatorPhase.Parry)
            {
                ClearParriable();
            }
        }

        if (IsDead || _playback == null) return;

        // ── Indicator animation ───────────────────────────────────
        UpdateIndicator(dt);

        // ── Drive animation from AI state ─────────────────────────
        var aiState = AI.State;
        if (aiState != _lastAnimState)
        {
            _playback.Travel(MapAIStateToAnim(aiState));
            _lastAnimState = aiState;
        }

        UpdateRunTimeScale();
    }

    // ══════════════════════════════════════════════════════════════════
    //  MOVEMENT
    // ══════════════════════════════════════════════════════════════════

    protected override Vector3 ProcessMovement(Vector3 velocity, float dt)
    {
        velocity = base.ProcessMovement(velocity, dt);

        if (_isLunging)
        {
            velocity.X = _lungeDirection.X * BiteLungeSpeed;
            velocity.Z = _lungeDirection.Z * BiteLungeSpeed;
        }

        return velocity;
    }

    // ══════════════════════════════════════════════════════════════════
    //  DAMAGE
    // ══════════════════════════════════════════════════════════════════

    protected override void OnDamageTaken(DamageData data)
    {
        base.OnDamageTaken(data);

        _isFlashing = true;
        _material.AlbedoColor = Colors.White;
        var tween = CreateTween();
        tween.TweenProperty(_material, "albedo_color", _originalColor, 0.15f);
        tween.TweenCallback(Callable.From(() => _isFlashing = false));
    }

    // ══════════════════════════════════════════════════════════════════
    //  DEATH
    // ══════════════════════════════════════════════════════════════════

    protected override void OnDeath()
    {
        _isLunging = false;
        SetIndicatorPhase(IndicatorPhase.Off);
        base.OnDeath();

        var tween = CreateTween();
        tween.TweenProperty(_modelRoot, "scale", Vector3.Zero, 0.4f)
            .SetEase(Tween.EaseType.In)
            .SetTrans(Tween.TransitionType.Back);
    }

    // ══════════════════════════════════════════════════════════════════
    //  ANIMATION CALLBACKS (method call tracks on Bite)
    // ══════════════════════════════════════════════════════════════════

    public void OnLungeStart()
    {
        if (!AI.HasTarget) return;

        var player = GetTree().GetFirstNodeInGroup("Player") as Node3D;
        if (player == null) return;

        Vector3 toTarget = player.GlobalPosition - GlobalPosition;
        toTarget.Y = 0f;
        _lungeDirection = toTarget.LengthSquared() > 0.001f
            ? toTarget.Normalized()
            : -GlobalTransform.Basis.Z;

        _isLunging = true;
        SetIndicatorPhase(IndicatorPhase.Warning);
    }

    public void OnLungeStop()
    {
        _isLunging = false;
    }

    public void OnBiteHitboxActivate()
    {
        GetNode<Hitbox>("Hitbox").Activate(AI.CurrentAttack);
        SetParriable();
        SetIndicatorPhase(IndicatorPhase.Parry);
    }

    public void OnBiteHitboxDeactivate()
    {
        GetNode<Hitbox>("Hitbox").Deactivate();
        ClearParriable();
        SetIndicatorPhase(IndicatorPhase.Off);
    }

    // ══════════════════════════════════════════════════════════════════
    //  INDICATOR — two-phase visual telegraph
    // ══════════════════════════════════════════════════════════════════

    private void SetIndicatorPhase(IndicatorPhase phase)
    {
        _indicatorPhase = phase;
        _indicatorTimer = 0f;

        _warningMeshInst.Visible = phase == IndicatorPhase.Warning;
        _parryMeshInst.Visible = phase == IndicatorPhase.Parry;

        // Reset transforms
        _warningMeshInst.Scale = Vector3.One;
        _warningMeshInst.Position = new Vector3(0f, 1.8f, 0f);
        _parryMeshInst.Scale = Vector3.One * 1.3f;
        _parryMeshInst.Position = new Vector3(0f, 1.8f, 0f);
    }

    private void UpdateIndicator(float dt)
    {
        if (_indicatorPhase == IndicatorPhase.Off) return;

        _indicatorTimer += dt;

        if (_indicatorPhase == IndicatorPhase.Warning)
        {
            // Orange prism — pulses faster as the bite approaches.
            // Acceleration creates urgency: slow blink → frantic flash.
            float speed = Mathf.Lerp(6f, 30f, Mathf.Min(_indicatorTimer * 2f, 1f));
            float pulse = (Mathf.Sin(_indicatorTimer * speed) + 1f) * 0.5f;

            // Bob up/down
            float bob = Mathf.Sin(_indicatorTimer * speed * 0.5f) * 0.08f;
            _warningMeshInst.Position = new Vector3(0f, 1.8f + bob, 0f);

            // Orange → deep orange as pulse peaks
            var col = new Color(1f, Mathf.Lerp(0.6f, 0.15f, pulse), 0f);
            _warningMeshMat.AlbedoColor = col;
            _warningMeshMat.Emission = col;
            _warningMeshMat.EmissionEnergyMultiplier = Mathf.Lerp(2f, 5f, pulse);

            // Scale pulse
            _warningMeshInst.Scale = Vector3.One * Mathf.Lerp(0.8f, 1.15f, pulse);
        }
        else if (_indicatorPhase == IndicatorPhase.Parry)
        {
            // Red diamond — large, bright, steady with a slight throb.
            // Visually distinct from warning: different shape, color,
            // size, and behavior. Unmistakable "PARRY NOW" signal.
            float throb = (Mathf.Sin(_indicatorTimer * 20f) + 1f) * 0.5f;

            _parryMeshMat.EmissionEnergyMultiplier = Mathf.Lerp(6f, 12f, throb);
            _parryMeshInst.Scale = Vector3.One * Mathf.Lerp(1.3f, 1.5f, throb);
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  RUN SPEED SCALING
    // ══════════════════════════════════════════════════════════════════

    private AnimationPlayer _animPlayer;

    private void UpdateRunTimeScale()
    {
        if (_animPlayer == null)
        {
            _animPlayer = GetNodeOrNull<AnimationPlayer>("WolfModel/AnimationPlayer");
            if (_animPlayer == null) return;
        }

        string current = _playback.GetCurrentNode();
        if (current != "Run")
        {
            _animPlayer.SpeedScale = 1.0f;
            return;
        }

        var vel = Velocity;
        float hSpeed = new Vector2(vel.X, vel.Z).Length();
        float chaseSpeed = Config?.ChaseSpeed ?? 6f;
        _animPlayer.SpeedScale = Mathf.Clamp(hSpeed / chaseSpeed, 0.3f, 1.8f);
    }

    // ══════════════════════════════════════════════════════════════════
    //  HELPERS
    // ══════════════════════════════════════════════════════════════════

    private static string MapAIStateToAnim(EnemyAI.AIState state) => state switch
    {
        EnemyAI.AIState.Chasing      => "Run",
        EnemyAI.AIState.Engaging     => "Run",
        EnemyAI.AIState.Attacking    => "Bite",
        EnemyAI.AIState.Telegraphing => "Idle",
        EnemyAI.AIState.Recovering   => "Idle",
        EnemyAI.AIState.Stunned      => "Idle",
        EnemyAI.AIState.Idle         => "Idle",
        _                            => "Idle"
    };

    private static MeshInstance3D FindFirstMesh(Node root)
    {
        if (root is MeshInstance3D mesh)
            return mesh;

        foreach (var child in root.GetChildren())
        {
            var found = FindFirstMesh(child);
            if (found != null) return found;
        }

        return null;
    }

    private MeshInstance3D FindBaseWarningIndicator()
    {
        foreach (var child in GetChildren())
        {
            if (child is MeshInstance3D mi
                && mi.Mesh is SphereMesh
                && mi.Position.Y > 2.0f
                && mi != _warningMeshInst
                && mi != _parryMeshInst)
            {
                return mi;
            }
        }
        return null;
    }
}
