using Godot;

public static class GameVFX
{
    // ═══════════════════════════════════════════════════════════════════
    //  TEXTURE CACHE
    // ═══════════════════════════════════════════════════════════════════

    private const string TexPath = "res://Assets/VFX/Textures/";

    private static Texture2D _softGlow;
    private static Texture2D _spark;
    private static Texture2D _crossBurst;

    private static Texture2D SoftGlow   => _softGlow   ??= GD.Load<Texture2D>(TexPath + "SoftGlow.png");
    private static Texture2D Spark      => _spark      ??= GD.Load<Texture2D>(TexPath + "Spark.png");
    private static Texture2D CrossBurst => _crossBurst ??= GD.Load<Texture2D>(TexPath + "CrossBurst.png");

    // ═══════════════════════════════════════════════════════════════════
    //  HIT IMPACT — Sekiro-style: orange sparks, two size layers
    //  - Flash quad (SoftGlow) — contact corona
    //  - Big sparks — few, large, slow, heavy
    //  - Small sparks — many, small, fast, scatter
    //  - Impact light
    // ═══════════════════════════════════════════════════════════════════

    public static void SpawnHitImpact(Node parent, Vector3 position, Vector3 hitDirection)
    {
        Vector3 dir = hitDirection.LengthSquared() > 0.001f
            ? hitDirection.Normalized()
            : Vector3.Up;

        // ── Flash corona ──────────────────────────────────────────────
        SpawnFlashQuad(parent, position, SoftGlow,
            new Color(1f, 0.85f, 0.5f, 0.95f), 1.0f, 1.6f, 8f, 0.07f);

        // ── Big sparks — 3 large, slow, heavy ─────────────────────────
        var big = new GpuParticles3D();
        big.Amount = 3;
        big.Lifetime = 0.25f;
        big.OneShot = true;
        big.Explosiveness = 1.0f;
        big.TopLevel = true;

        var bigMat = new ParticleProcessMaterial();
        bigMat.Direction = dir;
        bigMat.Spread = 40f;
        bigMat.InitialVelocityMin = 3f;
        bigMat.InitialVelocityMax = 6f;
        bigMat.Gravity = new Vector3(0f, -10f, 0f);
        bigMat.DampingMin = 2f;
        bigMat.DampingMax = 4f;
        bigMat.AngularVelocityMin = -120f;
        bigMat.AngularVelocityMax = 120f;
        bigMat.ScaleMin = 1.5f;
        bigMat.ScaleMax = 3.0f;
        bigMat.ColorRamp = MakeRamp(
            new Color(1f, 0.95f, 0.7f, 1f),
            new Color(1f, 0.5f, 0.1f, 0f)
        );
        big.ProcessMaterial = bigMat;
        big.DrawPass1 = MakeQuad(0.3f, 6f, Spark);

        parent.AddChild(big);
        big.GlobalPosition = position;
        big.Emitting = true;
        ScheduleFree(parent, big, 1f);

        // ── Small sparks — 6 small, fast, scatter ─────────────────────
        var small = new GpuParticles3D();
        small.Amount = 6;
        small.Lifetime = 0.3f;
        small.OneShot = true;
        small.Explosiveness = 0.95f;
        small.TopLevel = true;

        var smallMat = new ParticleProcessMaterial();
        smallMat.Direction = dir;
        smallMat.Spread = 50f;
        smallMat.InitialVelocityMin = 6f;
        smallMat.InitialVelocityMax = 13f;
        smallMat.Gravity = new Vector3(0f, -16f, 0f);
        smallMat.DampingMin = 1f;
        smallMat.DampingMax = 2f;
        smallMat.AngularVelocityMin = -300f;
        smallMat.AngularVelocityMax = 300f;
        smallMat.ScaleMin = 0.8f;
        smallMat.ScaleMax = 1.5f;
        smallMat.ColorRamp = MakeRamp(
            new Color(1f, 0.9f, 0.5f, 1f),
            new Color(1f, 0.3f, 0.0f, 0f)
        );
        small.ProcessMaterial = smallMat;
        small.DrawPass1 = MakeQuad(0.08f, 5f, Spark);

        parent.AddChild(small);
        small.GlobalPosition = position;
        small.Emitting = true;
        ScheduleFree(parent, small, 1f);

        // ── Impact light ──────────────────────────────────────────────
        SpawnImpactLight(parent, position, new Color(1f, 0.7f, 0.3f), 4f, 6f, 0.1f);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PARRY IMPACT — Sekiro deflect: brighter, bigger, more sparks
    //  Same orange palette but white-hot center, larger corona,
    //  more particles, wider spread. This is THE moment.
    //  - Flash quad (CrossBurst) — big white-hot corona
    //  - Big sparks — more and larger than hit
    //  - Small sparks — fast scatter cloud
    //  - Impact light (brighter)
    //  - Screen flash
    // ═══════════════════════════════════════════════════════════════════

    public static void SpawnParryImpact(Node parent, Vector3 position)
    {
        // ── Flash corona — large, white-hot ───────────────────────────
        SpawnFlashQuad(parent, position, CrossBurst,
            new Color(1f, 0.95f, 0.85f, 1f), 2.0f, 3.5f, 12f, 0.09f);

        // ── Big sparks — 5 large, dramatic ────────────────────────────
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

        parent.AddChild(big);
        big.GlobalPosition = position;
        big.Emitting = true;
        ScheduleFree(parent, big, 1f);

        // ── Small sparks — 10, fast scatter ───────────────────────────
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

        parent.AddChild(small);
        small.GlobalPosition = position;
        small.Emitting = true;
        ScheduleFree(parent, small, 1f);

        // ── Impact light — brighter ───────────────────────────────────
        SpawnImpactLight(parent, position, new Color(1f, 0.8f, 0.4f), 6f, 8f, 0.12f);

        // ── Screen flash — warm white ─────────────────────────────────
        SpawnScreenFlash(parent, new Color(1f, 0.95f, 0.85f, 0.25f), 0.06f);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  SCREEN FLASH — CanvasLayer + ColorRect, fades out
    // ═══════════════════════════════════════════════════════════════════

    public static void SpawnScreenFlash(Node parent, Color color, float duration)
    {
        var canvas = new CanvasLayer();
        canvas.Layer = 100;

        var rect = new ColorRect();
        rect.Color = color;
        rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        canvas.AddChild(rect);

        parent.GetTree().Root.AddChild(canvas);

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
    //  DEATH BURST — two size layers
    // ═══════════════════════════════════════════════════════════════════

    public static void SpawnDeathBurst(Node parent, Vector3 position)
    {
        // Big chunks
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

        parent.AddChild(big);
        big.GlobalPosition = position;
        big.Emitting = true;
        ScheduleFree(parent, big, 1.5f);

        // Small scatter
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

        parent.AddChild(small);
        small.GlobalPosition = position;
        small.Emitting = true;
        ScheduleFree(parent, small, 1.5f);

        // Flash
        SpawnFlashQuad(parent, position, SoftGlow,
            new Color(1f, 0.9f, 0.7f, 0.9f), 1.5f, 2.5f, 6f, 0.1f);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════════════════════════════

    private static void SpawnFlashQuad(Node parent, Vector3 position,
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

        parent.AddChild(flash);
        flash.GlobalPosition = position;

        var tween = parent.CreateTween();
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
        quad.Material = mat;

        return quad;
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

    private static GradientTexture1D MakeRamp(Color start, Color mid, Color end)
    {
        var gradient = new Gradient();
        gradient.Colors = new[] { start, mid, end };
        gradient.Offsets = new[] { 0f, 0.4f, 1f };
        var tex = new GradientTexture1D();
        tex.Gradient = gradient;
        return tex;
    }

    private static void SpawnImpactLight(Node parent, Vector3 position,
        Color color, float energy, float range, float duration)
    {
        var light = new OmniLight3D();
        light.LightColor = color;
        light.LightEnergy = energy;
        light.OmniRange = range;
        light.TopLevel = true;

        parent.AddChild(light);
        light.GlobalPosition = position;

        var tween = parent.CreateTween();
        tween.TweenProperty(light, "light_energy", 0f, duration)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Quad);
        tween.TweenCallback(Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(light))
                light.QueueFree();
        }));
    }

    private static void ScheduleFree(Node parent, Node target, float delay)
    {
        parent.GetTree().CreateTimer(delay).Timeout += () =>
        {
            if (GodotObject.IsInstanceValid(target))
                target.QueueFree();
        };
    }
}
