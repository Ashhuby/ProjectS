namespace Game.VFX;

using Game.Autoloads;
using Godot;

/// <summary>
/// VFX manager autoload. Subscribes to EventBus and spawns effects
/// autonomously — callers fire events, this class handles visuals.
///
/// Register as an autoload in Project Settings → Autoload:
///   Name: GameVFX   Path: res://Scripts/GameVFX.cs
///   LOAD ORDER: After EventBus and GameManager.
/// </summary>
public partial class GameVFX : Node
{
    public static GameVFX Instance { get; private set; }

    // ═══════════════════════════════════════════════════════════════════
    //  TEXTURE CACHE — all 6 textures
    // ═══════════════════════════════════════════════════════════════════

    private const string TexPath = "res://Assets/VFX/Textures/";

    private static Texture2D _softGlow;
    private static Texture2D _spark;
    private static Texture2D _crossBurst;
    private static Texture2D _hitStreak;
    private static Texture2D _sparkStreak;
    private static Texture2D _impactRing;

    private static Texture2D SoftGlow    => _softGlow    ??= GD.Load<Texture2D>(TexPath + "SoftGlow.png");
    private static Texture2D Spark       => _spark       ??= GD.Load<Texture2D>(TexPath + "Spark.png");
    private static Texture2D CrossBurst  => _crossBurst  ??= GD.Load<Texture2D>(TexPath + "CrossBurst.png");
    private static Texture2D HitStreak   => _hitStreak   ??= GD.Load<Texture2D>(TexPath + "HitStreak.png");
    private static Texture2D SparkStreak => _sparkStreak ??= GD.Load<Texture2D>(TexPath + "SparkStreak.png");
    private static Texture2D ImpactRing  => _impactRing  ??= GD.Load<Texture2D>(TexPath + "ImpactRing.png");

    // ═══════════════════════════════════════════════════════════════════
    //  LIFECYCLE
    // ═══════════════════════════════════════════════════════════════════

    public override void _Ready()
    {
        Instance = this;
        SubscribeEvents();
    }

    public override void _ExitTree()
    {
        UnsubscribeEvents();
        if (Instance == this)
            Instance = null;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  EVENT SUBSCRIPTIONS
    // ═══════════════════════════════════════════════════════════════════

    private void SubscribeEvents()
    {
        if (EventBus.Instance == null) return;

        EventBus.Instance.HitLanded += OnHitLanded;
        EventBus.Instance.ParrySucceeded += OnParrySucceeded;
        EventBus.Instance.EntityDied += OnEntityDied;
        EventBus.Instance.DamageTaken += OnDamageTaken;
        EventBus.Instance.VitalPopped += OnVitalPopped;
        EventBus.Instance.VitalFailed += OnVitalFailed;
    }

    private void UnsubscribeEvents()
    {
        if (EventBus.Instance == null) return;

        EventBus.Instance.HitLanded -= OnHitLanded;
        EventBus.Instance.ParrySucceeded -= OnParrySucceeded;
        EventBus.Instance.EntityDied -= OnEntityDied;
        EventBus.Instance.DamageTaken -= OnDamageTaken;
        EventBus.Instance.VitalPopped -= OnVitalPopped;
        EventBus.Instance.VitalFailed -= OnVitalFailed;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  EVENT HANDLERS
    // ═══════════════════════════════════════════════════════════════════

    private void OnHitLanded(DamageData data)
    {
        SpawnHitImpact(data.HitPosition, data.KnockbackDirection);
    }

    private void OnParrySucceeded(DamageData data)
    {
        Vector3 parryPos = data.HitPosition;
        if (data.Source != null)
        {
            var defender = GetTree().GetFirstNodeInGroup("Player") as Node3D;
            if (defender != null)
                parryPos = (data.Source.GlobalPosition + defender.GlobalPosition) / 2f
                         + new Vector3(0f, 1.2f, 0f);
        }
        SpawnParryImpact(parryPos);
    }

    private void OnEntityDied(Node3D entity)
    {
        Vector3 pos = entity.GlobalPosition + new Vector3(0f, 1f, 0f);
        SpawnDeathBurst(pos);
    }

    private void OnDamageTaken(DamageData data)
    {
        // Player damage flash is handled directly by PlayerCombat
        // to avoid double-flash (DamageTaken fires for all entities)
    }

    private void OnVitalPopped(Node3D enemy, bool isPrimary)
    {
        Vector3 pos = enemy.GlobalPosition + new Vector3(0f, 1f, 0f);
        SpawnVitalPopBurst(pos, isPrimary);
    }

    private void OnVitalFailed(Node3D enemy)
    {
        Vector3 pos = enemy.GlobalPosition + new Vector3(0f, 1f, 0f);
        SpawnVitalFailFizzle(pos);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  HIT IMPACT — UPGRADED
    //
    //  Sekiro-style contact burst. Four layers:
    //
    //  1. DIRECTIONAL STREAKS (HitStreak.png)
    //     8-10 elongated sparks that fly along the hit direction.
    //     These sell the directionality — you can SEE which way the
    //     hit went. Narrow spread (35°), fast, gravity pulls them
    //     into arcs. 3-stop ramp: white → orange → dark red → gone.
    //
    //  2. RADIAL SCATTER (Spark.png)
    //     12 small sparks that fly in all directions. These fill
    //     the space around the contact point. Wider spread (70°),
    //     shorter lived, smaller. Same 3-stop ramp.
    //
    //  3. FLASH CORONA (CrossBurst.png)
    //     Sharp 4-point star flash at contact. Bigger and harder-edged
    //     than the old SoftGlow version. Scales up fast, fades fast.
    //
    //  4. IMPACT LIGHT (OmniLight3D)
    //     Bright, short, warm. Illuminates nearby geometry.
    // ═══════════════════════════════════════════════════════════════════

    public void SpawnHitImpact(Vector3 position, Vector3 hitDirection)
    {
        Vector3 dir = hitDirection.LengthSquared() > 0.001f
            ? hitDirection.Normalized()
            : Vector3.Up;

        // ── 1. Directional streaks ────────────────────────────────────
        var streaks = new GpuParticles3D();
        streaks.Amount = 8;
        streaks.Lifetime = 0.2f;
        streaks.OneShot = true;
        streaks.Explosiveness = 1.0f;
        streaks.TopLevel = true;

        var streakMat = new ParticleProcessMaterial();
        streakMat.Direction = dir;
        streakMat.Spread = 35f;
        streakMat.InitialVelocityMin = 8f;
        streakMat.InitialVelocityMax = 16f;
        streakMat.Gravity = new Vector3(0f, -14f, 0f);
        streakMat.DampingMin = 3f;
        streakMat.DampingMax = 6f;
        streakMat.AngularVelocityMin = -60f;
        streakMat.AngularVelocityMax = 60f;
        streakMat.ScaleMin = 2.0f;
        streakMat.ScaleMax = 4.5f;
        streakMat.ScaleCurve = MakePopCurve();
        streakMat.ColorRamp = MakeRamp3(
            new Color(1f, 1f, 1f, 1f),        // White-hot core
            new Color(1f, 0.6f, 0.1f, 0.9f),  // Orange mid
            new Color(0.6f, 0.1f, 0f, 0f)     // Dark red fade
        );
        // Stretch particles along velocity for streak look
        streakMat.ParticleFlagAlignY = true;
        streaks.ProcessMaterial = streakMat;

        // Elongated quad — 4:1 ratio matches the HitStreak texture
        streaks.DrawPass1 = MakeQuadRect(0.12f, 0.03f, 10f, HitStreak);

        AddChild(streaks);
        streaks.GlobalPosition = position;
        streaks.Emitting = true;
        ScheduleFree(streaks, 1f);

        // ── 2. Radial scatter sparks ──────────────────────────────────
        var scatter = new GpuParticles3D();
        scatter.Amount = 12;
        scatter.Lifetime = 0.25f;
        scatter.OneShot = true;
        scatter.Explosiveness = 0.95f;
        scatter.TopLevel = true;

        var scatterMat = new ParticleProcessMaterial();
        scatterMat.Direction = dir;
        scatterMat.Spread = 70f;
        scatterMat.InitialVelocityMin = 5f;
        scatterMat.InitialVelocityMax = 12f;
        scatterMat.Gravity = new Vector3(0f, -18f, 0f);
        scatterMat.DampingMin = 2f;
        scatterMat.DampingMax = 4f;
        scatterMat.AngularVelocityMin = -400f;
        scatterMat.AngularVelocityMax = 400f;
        scatterMat.ScaleMin = 1.0f;
        scatterMat.ScaleMax = 2.5f;
        scatterMat.ScaleCurve = MakePopCurve();
        scatterMat.ColorRamp = MakeRamp3(
            new Color(1f, 0.95f, 0.7f, 1f),   // Bright yellow-white
            new Color(1f, 0.5f, 0.05f, 0.8f),  // Orange
            new Color(0.5f, 0.1f, 0f, 0f)      // Dark fade
        );
        scatter.ProcessMaterial = scatterMat;
        scatter.DrawPass1 = MakeQuad(0.06f, 6f, Spark);

        AddChild(scatter);
        scatter.GlobalPosition = position;
        scatter.Emitting = true;
        ScheduleFree(scatter, 1f);

        // ── 3. Flash corona ───────────────────────────────────────────
        SpawnFlashQuad(position, CrossBurst,
            new Color(1f, 0.9f, 0.6f, 1f), 0.8f, 2.0f, 12f, 0.06f);

        // ── 4. Impact light ───────────────────────────────────────────
        SpawnImpactLight(position, new Color(1f, 0.7f, 0.3f), 6f, 5f, 0.08f);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PARRY IMPACT — preserved from last version, will upgrade next
    // ═══════════════════════════════════════════════════════════════════

    public void SpawnParryImpact(Vector3 position)
    {
        // Flash corona
        SpawnFlashQuad(position, CrossBurst,
            new Color(1f, 0.95f, 0.85f, 1f), 2.0f, 3.5f, 12f, 0.09f);

        // Big sparks
        var big = new GpuParticles3D();
        big.Amount = 5;
        big.Lifetime = 0.3f;
        big.OneShot = true;
        big.Explosiveness = 1.0f;
        big.TopLevel = true;

        var bigMat = new ParticleProcessMaterial();
        bigMat.Direction = Vector3.Up;
        bigMat.Spread = 180f;
        bigMat.InitialVelocityMin = 4f;
        bigMat.InitialVelocityMax = 8f;
        bigMat.Gravity = new Vector3(0f, -10f, 0f);
        bigMat.DampingMin = 2f;
        bigMat.DampingMax = 4f;
        bigMat.AngularVelocityMin = -120f;
        bigMat.AngularVelocityMax = 120f;
        bigMat.ScaleMin = 2.0f;
        bigMat.ScaleMax = 4.0f;
        bigMat.ColorRamp = MakeRamp(
            new Color(1f, 0.97f, 0.85f, 1f),
            new Color(1f, 0.6f, 0.15f, 0f)
        );
        big.ProcessMaterial = bigMat;
        big.DrawPass1 = MakeQuad(0.35f, 8f, Spark);

        AddChild(big);
        big.GlobalPosition = position;
        big.Emitting = true;
        ScheduleFree(big, 1f);

        // Small sparks
        var small = new GpuParticles3D();
        small.Amount = 10;
        small.Lifetime = 0.3f;
        small.OneShot = true;
        small.Explosiveness = 0.95f;
        small.TopLevel = true;

        var smallMat = new ParticleProcessMaterial();
        smallMat.Direction = Vector3.Up;
        smallMat.Spread = 180f;
        smallMat.InitialVelocityMin = 5f;
        smallMat.InitialVelocityMax = 14f;
        smallMat.Gravity = new Vector3(0f, -14f, 0f);
        smallMat.DampingMin = 1f;
        smallMat.DampingMax = 3f;
        smallMat.AngularVelocityMin = -300f;
        smallMat.AngularVelocityMax = 300f;
        smallMat.ScaleMin = 0.8f;
        smallMat.ScaleMax = 2.0f;
        smallMat.ColorRamp = MakeRamp(
            new Color(1f, 0.9f, 0.6f, 1f),
            new Color(1f, 0.35f, 0.05f, 0f)
        );
        small.ProcessMaterial = smallMat;
        small.DrawPass1 = MakeQuad(0.1f, 5f, Spark);

        AddChild(small);
        small.GlobalPosition = position;
        small.Emitting = true;
        ScheduleFree(small, 1f);

        // Light + screen flash
        SpawnImpactLight(position, new Color(1f, 0.8f, 0.4f), 6f, 8f, 0.12f);
        SpawnScreenFlash(new Color(1f, 0.95f, 0.85f, 0.25f), 0.06f);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  DEATH BURST — preserved, will upgrade later
    // ═══════════════════════════════════════════════════════════════════

    public void SpawnDeathBurst(Vector3 position)
    {
        var big = new GpuParticles3D();
        big.Amount = 4;
        big.Lifetime = 0.4f;
        big.OneShot = true;
        big.Explosiveness = 1.0f;
        big.TopLevel = true;

        var bigMat = new ParticleProcessMaterial();
        bigMat.Direction = Vector3.Up;
        bigMat.Spread = 180f;
        bigMat.InitialVelocityMin = 3f;
        bigMat.InitialVelocityMax = 6f;
        bigMat.Gravity = new Vector3(0f, -8f, 0f);
        bigMat.AngularVelocityMin = -90f;
        bigMat.AngularVelocityMax = 90f;
        bigMat.ScaleMin = 2.0f;
        bigMat.ScaleMax = 3.5f;
        bigMat.ColorRamp = MakeRamp(
            new Color(1f, 0.9f, 0.7f, 1f),
            new Color(0.7f, 0.3f, 0.1f, 0f)
        );
        big.ProcessMaterial = bigMat;
        big.DrawPass1 = MakeQuad(0.2f, 5f, SoftGlow);

        AddChild(big);
        big.GlobalPosition = position;
        big.Emitting = true;
        ScheduleFree(big, 1.5f);

        var small = new GpuParticles3D();
        small.Amount = 8;
        small.Lifetime = 0.35f;
        small.OneShot = true;
        small.Explosiveness = 0.9f;
        small.TopLevel = true;

        var smallMat = new ParticleProcessMaterial();
        smallMat.Direction = Vector3.Up;
        smallMat.Spread = 180f;
        smallMat.InitialVelocityMin = 4f;
        smallMat.InitialVelocityMax = 9f;
        smallMat.Gravity = new Vector3(0f, -12f, 0f);
        smallMat.AngularVelocityMin = -250f;
        smallMat.AngularVelocityMax = 250f;
        smallMat.ScaleMin = 0.8f;
        smallMat.ScaleMax = 1.5f;
        smallMat.ColorRamp = MakeRamp(
            new Color(1f, 0.85f, 0.5f, 1f),
            new Color(0.6f, 0.2f, 0.05f, 0f)
        );
        small.ProcessMaterial = smallMat;
        small.DrawPass1 = MakeQuad(0.08f, 3f, SoftGlow);

        AddChild(small);
        small.GlobalPosition = position;
        small.Emitting = true;
        ScheduleFree(small, 1.5f);

        SpawnFlashQuad(position, SoftGlow,
            new Color(1f, 0.9f, 0.7f, 0.9f), 1.5f, 2.5f, 6f, 0.1f);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  VITAL POP BURST — preserved, will upgrade later
    // ═══════════════════════════════════════════════════════════════════

    public void SpawnVitalPopBurst(Vector3 position, bool isPrimary)
    {
        Color coreColor = isPrimary
            ? new Color(1f, 0.6f, 0.1f)
            : new Color(1f, 0.95f, 0.4f);

        Color hotColor = isPrimary
            ? new Color(1f, 0.85f, 0.5f, 1f)
            : new Color(1f, 1f, 0.8f, 1f);

        Color fadeColor = isPrimary
            ? new Color(1f, 0.4f, 0.0f, 0f)
            : new Color(1f, 0.7f, 0.1f, 0f);

        float flashScale = isPrimary ? 2.5f : 3.5f;
        float flashEnd = isPrimary ? 4.0f : 5.5f;
        float flashEmission = isPrimary ? 10f : 16f;

        SpawnFlashQuad(position, CrossBurst,
            hotColor, flashScale, flashEnd, flashEmission, 0.12f);

        var burst = new GpuParticles3D();
        burst.Amount = 8;
        burst.Lifetime = 0.35f;
        burst.OneShot = true;
        burst.Explosiveness = 1.0f;
        burst.TopLevel = true;

        var burstMat = new ParticleProcessMaterial();
        burstMat.Direction = Vector3.Up;
        burstMat.Spread = 180f;
        burstMat.InitialVelocityMin = 5f;
        burstMat.InitialVelocityMax = 12f;
        burstMat.Gravity = new Vector3(0f, -6f, 0f);
        burstMat.DampingMin = 3f;
        burstMat.DampingMax = 5f;
        burstMat.ScaleMin = 2.0f;
        burstMat.ScaleMax = isPrimary ? 4.0f : 5.0f;
        burstMat.ColorRamp = MakeRamp(hotColor, fadeColor);
        burst.ProcessMaterial = burstMat;
        burst.DrawPass1 = MakeQuad(0.2f, isPrimary ? 8f : 12f, Spark);

        AddChild(burst);
        burst.GlobalPosition = position;
        burst.Emitting = true;
        ScheduleFree(burst, 1.5f);

        var embers = new GpuParticles3D();
        embers.Amount = isPrimary ? 6 : 10;
        embers.Lifetime = 0.6f;
        embers.OneShot = true;
        embers.Explosiveness = 0.8f;
        embers.TopLevel = true;

        var emberMat = new ParticleProcessMaterial();
        emberMat.Direction = Vector3.Up;
        emberMat.Spread = 120f;
        emberMat.InitialVelocityMin = 1f;
        emberMat.InitialVelocityMax = 3f;
        emberMat.Gravity = new Vector3(0f, 2f, 0f);
        emberMat.DampingMin = 1f;
        emberMat.DampingMax = 2f;
        emberMat.ScaleMin = 0.5f;
        emberMat.ScaleMax = 1.5f;
        emberMat.ColorRamp = MakeRamp(
            coreColor with { A = 0.8f },
            coreColor with { A = 0f }
        );
        embers.ProcessMaterial = emberMat;
        embers.DrawPass1 = MakeQuad(0.06f, 4f, SoftGlow);

        AddChild(embers);
        embers.GlobalPosition = position;
        embers.Emitting = true;
        ScheduleFree(embers, 2f);

        float lightEnergy = isPrimary ? 8f : 12f;
        float lightRange = isPrimary ? 10f : 14f;
        SpawnImpactLight(position, coreColor, lightEnergy, lightRange, 0.15f);

        Color flashColor = isPrimary
            ? new Color(1f, 0.8f, 0.3f, 0.2f)
            : new Color(1f, 1f, 0.7f, 0.3f);
        SpawnScreenFlash(flashColor, 0.08f);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  VITAL FAIL FIZZLE — preserved
    // ═══════════════════════════════════════════════════════════════════

    public void SpawnVitalFailFizzle(Vector3 position)
    {
        var puff = new GpuParticles3D();
        puff.Amount = 4;
        puff.Lifetime = 0.4f;
        puff.OneShot = true;
        puff.Explosiveness = 0.9f;
        puff.TopLevel = true;

        var puffMat = new ParticleProcessMaterial();
        puffMat.Direction = Vector3.Up;
        puffMat.Spread = 180f;
        puffMat.InitialVelocityMin = 1f;
        puffMat.InitialVelocityMax = 3f;
        puffMat.Gravity = new Vector3(0f, -4f, 0f);
        puffMat.DampingMin = 3f;
        puffMat.DampingMax = 5f;
        puffMat.ScaleMin = 1.0f;
        puffMat.ScaleMax = 2.0f;
        puffMat.ColorRamp = MakeRamp(
            new Color(0.6f, 0.3f, 0.2f, 0.6f),
            new Color(0.3f, 0.15f, 0.1f, 0f)
        );
        puff.ProcessMaterial = puffMat;
        puff.DrawPass1 = MakeQuad(0.1f, 2f, SoftGlow);

        AddChild(puff);
        puff.GlobalPosition = position;
        puff.Emitting = true;
        ScheduleFree(puff, 1.5f);

        SpawnImpactLight(position, new Color(0.6f, 0.2f, 0.1f), 2f, 4f, 0.15f);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  SCREEN FLASH
    // ═══════════════════════════════════════════════════════════════════

    public void SpawnScreenFlash(Color color, float duration)
    {
        var canvas = new CanvasLayer();
        canvas.Layer = 100;

        var rect = new ColorRect();
        rect.Color = color;
        rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        canvas.AddChild(rect);

        GetTree().Root.AddChild(canvas);

        var tween = canvas.CreateTween();
        tween.TweenProperty(rect, "color",
            new Color(color.R, color.G, color.B, 0f), duration)
            .SetEase(Tween.EaseType.Out);
        tween.TweenCallback(Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(canvas))
                canvas.QueueFree();
        }));
    }

    // ═══════════════════════════════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════════════════════════════

    private void SpawnFlashQuad(Vector3 position,
        Texture2D texture, Color color, float startScale, float endScale,
        float emissionStrength, float duration)
    {
        var flash = new MeshInstance3D();
        var quad = new QuadMesh();
        quad.Size = new Vector2(1f, 1f);

        var mat = new StandardMaterial3D();
        mat.AlbedoColor = color;
        mat.AlbedoTexture = texture;
        mat.EmissionEnabled = true;
        mat.Emission = new Color(color.R, color.G, color.B, 1f);
        mat.EmissionTexture = texture;
        mat.EmissionEnergyMultiplier = emissionStrength;
        mat.BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled;
        mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        quad.Material = mat;

        flash.Mesh = quad;
        flash.TopLevel = true;
        flash.Scale = Vector3.One * startScale;

        AddChild(flash);
        flash.GlobalPosition = position;

        var tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(flash, "scale", Vector3.One * endScale, duration)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Quad);
        tween.TweenProperty(mat, "albedo_color",
            new Color(color.R, color.G, color.B, 0f), duration)
            .SetEase(Tween.EaseType.In);
        tween.TweenProperty(mat, "emission_energy_multiplier", 0f, duration)
            .SetEase(Tween.EaseType.In);
        tween.SetParallel(false);
        tween.TweenCallback(Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(flash))
                flash.QueueFree();
        }));
    }

    /// <summary>
    /// Square particle quad — used for sparks and glows.
    /// </summary>
    private static QuadMesh MakeQuad(float size, float emissionStrength, Texture2D texture)
    {
        var quad = new QuadMesh();
        quad.Size = new Vector2(size, size);

        var mat = new StandardMaterial3D();
        mat.AlbedoColor = Colors.White;
        mat.AlbedoTexture = texture;
        mat.VertexColorUseAsAlbedo = true;
        mat.BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled;
        mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        mat.EmissionEnabled = true;
        mat.Emission = Colors.White;
        mat.EmissionEnergyMultiplier = emissionStrength;
        quad.Material = mat;

        return quad;
    }

    /// <summary>
    /// Rectangular particle quad — used for directional streaks.
    /// Width and height are separate so the texture stretches along
    /// the velocity direction when AlignYToVelocity is enabled.
    /// Billboard is set to ParticleBillboard so alignment works.
    /// </summary>
    private static QuadMesh MakeQuadRect(float width, float height,
        float emissionStrength, Texture2D texture)
    {
        var quad = new QuadMesh();
        quad.Size = new Vector2(height, width); // Y = forward when aligned

        var mat = new StandardMaterial3D();
        mat.AlbedoColor = Colors.White;
        mat.AlbedoTexture = texture;
        mat.VertexColorUseAsAlbedo = true;
        mat.BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles;
        mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        mat.EmissionEnabled = true;
        mat.Emission = Colors.White;
        mat.EmissionEnergyMultiplier = emissionStrength;
        quad.Material = mat;

        return quad;
    }

    /// <summary>
    /// 2-stop color gradient.
    /// </summary>
    private static GradientTexture1D MakeRamp(Color start, Color end)
    {
        var gradient = new Gradient();
        gradient.Colors = new[] { start, end };
        gradient.Offsets = new[] { 0f, 1f };
        var tex = new GradientTexture1D();
        tex.Gradient = gradient;
        return tex;
    }

    /// <summary>
    /// 3-stop color gradient. Used for the white → orange → dark red
    /// progression that gives sparks their hot-metal look.
    /// </summary>
    private static GradientTexture1D MakeRamp3(Color start, Color mid, Color end)
    {
        var gradient = new Gradient();
        gradient.Colors = new[] { start, mid, end };
        gradient.Offsets = new[] { 0f, 0.35f, 1f };
        var tex = new GradientTexture1D();
        tex.Gradient = gradient;
        return tex;
    }

    /// <summary>
    /// Scale curve: pop in at full size, hold briefly, then shrink
    /// rapidly to nothing. This is the difference between particles
    /// that feel "alive" and particles that feel like shrinking dots.
    ///
    /// Shape: 1.0 → 1.0 (0-15%) → 0.0 (100%)
    /// The hold at the start means the spark is fully visible for a
    /// beat before it starts to die. The sharp falloff after means
    /// it doesn't linger as a tiny speck.
    /// </summary>
    private static CurveTexture MakePopCurve()
    {
        var curve = new Curve();
        curve.AddPoint(new Vector2(0f, 1f));
        curve.AddPoint(new Vector2(0.15f, 1f));
        curve.AddPoint(new Vector2(0.5f, 0.3f));
        curve.AddPoint(new Vector2(1f, 0f));

        var tex = new CurveTexture();
        tex.Curve = curve;
        return tex;
    }

    private void SpawnImpactLight(Vector3 position,
        Color color, float energy, float range, float duration)
    {
        var light = new OmniLight3D();
        light.LightColor = color;
        light.LightEnergy = energy;
        light.OmniRange = range;
        light.TopLevel = true;

        AddChild(light);
        light.GlobalPosition = position;

        var tween = CreateTween();
        tween.TweenProperty(light, "light_energy", 0f, duration)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Quad);
        tween.TweenCallback(Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(light))
                light.QueueFree();
        }));
    }

    private void ScheduleFree(Node target, float delay)
    {
        GetTree().CreateTimer(delay).Timeout += () =>
        {
            if (GodotObject.IsInstanceValid(target))
                target.QueueFree();
        };
    }
}
