namespace Game.Characters.Enemies.Wolf;

using Game.Debug;
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
/// Method call tracks on HitReaction animation (target: root node ".."):
///   OnHitReactionFinished() — return AI to previous state
///
/// Death animation plays to completion, then base class respawn runs.
///
/// Indicators use GpuParticles3D instead of primitive meshes:
///   Warning: orbiting orange embers that accelerate
///   Parry:   bright blue-white flash ring
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

    // ── Attack indicators (particle-based) ────────────────────────────

    private enum IndicatorPhase { Off, Warning, Parry }

    private GpuParticles3D _warningParticles;
    private OmniLight3D _warningLight;

    private GpuParticles3D _parryParticles;
    private OmniLight3D _parryLight;

    private IndicatorPhase _indicatorPhase = IndicatorPhase.Off;
    private float _indicatorTimer;

    // ── Hit reaction tracking ─────────────────────────────────────────

    private bool _inHitReaction;

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
    /// Particle-based indicators.
    ///
    /// Warning: orbiting orange embers around the wolf's head.
    ///   Starts slow, accelerates as the bite approaches.
    ///
    /// Parry: bright blue-white sparks in a ring that snaps on
    ///   when the hitbox activates. Blue is categorically different
    ///   from every other orange effect — unmistakable "PARRY NOW".
    /// </summary>
    private void CreateAttackIndicators()
    {
        // ── Warning: orbiting embers ──────────────────────────────
        _warningParticles = new GpuParticles3D();
        _warningParticles.Amount = 6;
        _warningParticles.Lifetime = 0.5f;
        _warningParticles.Explosiveness = 0f;
        _warningParticles.Emitting = false;
        _warningParticles.Position = new Vector3(0f, 1.2f, 0f);

        var warnMat = new ParticleProcessMaterial();
        warnMat.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere;
        warnMat.EmissionSphereRadius = 0.4f;
        warnMat.Direction = Vector3.Up;
        warnMat.Spread = 60f;
        warnMat.InitialVelocityMin = 0.5f;
        warnMat.InitialVelocityMax = 1.5f;
        warnMat.Gravity = new Vector3(0f, 1f, 0f);
        warnMat.AngularVelocityMin = -200f;
        warnMat.AngularVelocityMax = 200f;
        warnMat.ScaleMin = 1.0f;
        warnMat.ScaleMax = 2.5f;
        warnMat.ColorRamp = MakeRamp(
            new Color(1f, 0.7f, 0.2f, 0.9f),
            new Color(1f, 0.3f, 0f, 0f)
        );
        _warningParticles.ProcessMaterial = warnMat;
        _warningParticles.DrawPass1 = MakeParticleQuad(0.06f,
            "res://Assets/VFX/Textures/SoftGlow.png",
            new Color(1f, 0.6f, 0.1f), 5f);

        AddChild(_warningParticles);

        _warningLight = new OmniLight3D();
        _warningLight.LightColor = new Color(1f, 0.6f, 0.2f);
        _warningLight.LightEnergy = 0f;
        _warningLight.OmniRange = 3f;
        _warningLight.Position = new Vector3(0f, 1.2f, 0f);
        AddChild(_warningLight);

        // ── Parry: blue-white flash ring ──────────────────────────
        _parryParticles = new GpuParticles3D();
        _parryParticles.Amount = 10;
        _parryParticles.Lifetime = 0.25f;
        _parryParticles.Explosiveness = 0.9f;
        _parryParticles.Emitting = false;
        _parryParticles.Position = new Vector3(0f, 1.2f, 0f);

        var parryMat = new ParticleProcessMaterial();
        parryMat.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Ring;
        parryMat.EmissionRingRadius = 0.6f;
        parryMat.EmissionRingInnerRadius = 0.4f;
        parryMat.EmissionRingHeight = 0.05f;
        parryMat.EmissionRingAxis = Vector3.Up;
        parryMat.Direction = Vector3.Up;
        parryMat.Spread = 30f;
        parryMat.InitialVelocityMin = 0.5f;
        parryMat.InitialVelocityMax = 2f;
        parryMat.Gravity = Vector3.Zero;
        parryMat.ScaleMin = 1.5f;
        parryMat.ScaleMax = 3.0f;
        parryMat.ColorRamp = MakeRamp(
            new Color(0.6f, 0.8f, 1f, 1f),
            new Color(0.2f, 0.5f, 1f, 0f)
        );
        _parryParticles.ProcessMaterial = parryMat;
        _parryParticles.DrawPass1 = MakeParticleQuad(0.05f,
            "res://Assets/VFX/Textures/Spark.png",
            new Color(0.5f, 0.7f, 1f), 10f);

        AddChild(_parryParticles);

        _parryLight = new OmniLight3D();
        _parryLight.LightColor = new Color(0.5f, 0.7f, 1f);
        _parryLight.LightEnergy = 0f;
        _parryLight.OmniRange = 4f;
        _parryLight.Position = new Vector3(0f, 1.2f, 0f);
        AddChild(_parryLight);
    }

    // ══════════════════════════════════════════════════════════════════
    //  PROCESS
    // ══════════════════════════════════════════════════════════════════

    protected override void ProcessUpdate(float dt)
    {
        // ── Respawn reset ─────────────────────────────────────────
        if (_wasDead && !IsDead)
        {
            _modelRoot.Scale = _modelBaseScale;
            _material.AlbedoColor = _originalColor;
            _isLunging = false;
            _inHitReaction = false;
            _indicatorPhase = IndicatorPhase.Off;
            _lastAnimState = (EnemyAI.AIState)(-1);
            _wasDead = false;

            if (_playback != null)
                _playback.Travel("Idle");
        }
        _wasDead = IsDead;

        // ── Base: AI tick, rotation, UpdateVisuals ─────────────────
        base.ProcessUpdate(dt);

        // ── Suppress base visuals ─────────────────────────────────
        if (!IsDead)
        {
            _mesh.Scale = _baseScale;
            if (!_isFlashing)
                _material.AlbedoColor = _originalColor;

            if (_baseWarningIndicator == null)
                _baseWarningIndicator = FindBaseWarningIndicator();

            if (_baseWarningIndicator != null)
                _baseWarningIndicator.Visible = false;

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
        UpdateAnimationState();
        UpdateRunTimeScale();
    }

    // ══════════════════════════════════════════════════════════════════
    //  ANIMATION STATE DRIVER
    // ══════════════════════════════════════════════════════════════════

    private void UpdateAnimationState()
    {
        // Don't interrupt hit reaction
        if (_inHitReaction) return;

        var aiState = AI.State;
        if (aiState != _lastAnimState)
        {
            _playback.Travel(MapAIStateToAnim(aiState));
            _lastAnimState = aiState;
        }
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

        // White flash
        _isFlashing = true;
        _material.AlbedoColor = Colors.White;
        var tween = CreateTween();
        tween.TweenProperty(_material, "albedo_color", _originalColor, 0.15f);
        tween.TweenCallback(Callable.From(() => _isFlashing = false));

        // Play hit reaction if not attacking or dead
        if (AI.State != EnemyAI.AIState.Attacking && !IsDead && _playback != null)
        {
            _inHitReaction = true;
            _playback.Travel("HitReaction");
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  DEATH
    // ══════════════════════════════════════════════════════════════════

    protected override void OnDeath()
    {
        _isLunging = false;
        _inHitReaction = false;
        SetIndicatorPhase(IndicatorPhase.Off);

        // Let base handle AI death, hitbox, hurtbox, vital reset, health bar
        base.OnDeath();

        // Play death animation instead of base class scale-to-zero tween
        if (_playback != null)
            _playback.Travel("Death");
    }

    // ══════════════════════════════════════════════════════════════════
    //  ANIMATION CALLBACKS (method call tracks)
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

    /// <summary>
    /// Called by method call track on HitReaction animation's last frame.
    /// </summary>
    public void OnHitReactionFinished()
    {
        _inHitReaction = false;
        _lastAnimState = (EnemyAI.AIState)(-1); // Force re-evaluation
        GameLog.AILog($"[{Name}] Hit reaction finished");
    }

    // ══════════════════════════════════════════════════════════════════
    //  INDICATOR — particle-based two-phase telegraph
    // ══════════════════════════════════════════════════════════════════

    private void SetIndicatorPhase(IndicatorPhase phase)
    {
        _indicatorPhase = phase;
        _indicatorTimer = 0f;

        _warningParticles.Emitting = phase == IndicatorPhase.Warning;
        _warningLight.LightEnergy = phase == IndicatorPhase.Warning ? 2f : 0f;

        _parryParticles.Emitting = phase == IndicatorPhase.Parry;
        _parryLight.LightEnergy = phase == IndicatorPhase.Parry ? 6f : 0f;
    }

    private void UpdateIndicator(float dt)
    {
        if (_indicatorPhase == IndicatorPhase.Off) return;

        _indicatorTimer += dt;

        if (_indicatorPhase == IndicatorPhase.Warning)
        {
            // Embers accelerate as the bite approaches
            float urgency = Mathf.Min(_indicatorTimer * 2f, 1f);

            var mat = _warningParticles.ProcessMaterial as ParticleProcessMaterial;
            if (mat != null)
            {
                mat.InitialVelocityMin = Mathf.Lerp(0.5f, 3f, urgency);
                mat.InitialVelocityMax = Mathf.Lerp(1.5f, 5f, urgency);
                mat.ScaleMin = Mathf.Lerp(1f, 2f, urgency);
                mat.ScaleMax = Mathf.Lerp(2.5f, 4f, urgency);
            }

            float pulse = (Mathf.Sin(_indicatorTimer * Mathf.Lerp(6f, 25f, urgency)) + 1f) * 0.5f;
            _warningLight.LightEnergy = Mathf.Lerp(1f, 5f, urgency) * Mathf.Lerp(0.7f, 1f, pulse);
        }
        else if (_indicatorPhase == IndicatorPhase.Parry)
        {
            float throb = (Mathf.Sin(_indicatorTimer * 20f) + 1f) * 0.5f;
            _parryLight.LightEnergy = Mathf.Lerp(4f, 8f, throb);
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
                && mi.Position.Y > 2.0f)
            {
                return mi;
            }
        }
        return null;
    }

    private static GradientTexture1D MakeRamp(Color start, Color end)
    {
        var gradient = new Gradient();
        gradient.Colors = new[] { start, end };
        gradient.Offsets = new[] { 0f, 1f };
        var tex = new GradientTexture1D();
        tex.Gradient = gradient;
        return tex;
    }

    private static QuadMesh MakeParticleQuad(float size, string texturePath,
        Color emission, float emissionStrength)
    {
        var quad = new QuadMesh();
        quad.Size = new Vector2(size, size);

        var mat = new StandardMaterial3D();
        mat.AlbedoColor = Colors.White;
        mat.AlbedoTexture = GD.Load<Texture2D>(texturePath);
        mat.VertexColorUseAsAlbedo = true;
        mat.BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled;
        mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        mat.EmissionEnabled = true;
        mat.Emission = emission;
        mat.EmissionEnergyMultiplier = emissionStrength;
        quad.Material = mat;

        return quad;
    }
}
