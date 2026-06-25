using Godot;
using System;

/// <summary>
/// VFX test bench. Create a new scene with a Node3D root, attach this script, run.
/// Keys 1-5 trigger effects. Buttons on the left panel do the same.
/// </summary>
public partial class VFXTestScene : Node3D
{
    // Spawn positions for 3D effects
    private readonly Vector3[] _spawnPoints =
    {
        new(-4.5f, 1.2f, 0f),   // 0: Hit Impact
        new(-1.5f, 1.2f, 0f),   // 1: Parry Impact
        new(1.5f, 1.2f, 0f),    // 2: Death Burst
        new(4.5f, 1.2f, 0f),    // 3: Sword Trail
    };

    private readonly string[] _labels =
    {
        "1 — Hit Impact",
        "2 — Parry Impact",
        "3 — Death Burst",
        "4 — Sword Trail",
    };

    // Sword trail test rig
    private Node3D _trailPivot;
    private SwordTrail _swordTrail;
    private bool _trailActive;

    public override void _Ready()
    {
        CreateFloor();
        CreateCamera();
        CreateLight();
        CreateTargets();
        CreateLabels();
        CreateTrailRig();
        CreateUI();

        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public override void _Process(double delta)
    {
        // Spin the trail arm when active
        if (_trailActive && _trailPivot != null)
        {
            _trailPivot.RotateY(6f * (float)delta);
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            switch (key.Keycode)
            {
                case Key.Key1: SpawnHitImpact(); break;
                case Key.Key2: SpawnParryImpact(); break;
                case Key.Key3: SpawnDeathBurst(); break;
                case Key.Key4: ToggleTrail(); break;
                case Key.Key5: SpawnDamageFlash(); break;
                case Key.Key6: SpawnParryFlash(); break;
            }
        }
    }

    // ── Effect triggers ───────────────────────────────────────────────

    private void SpawnHitImpact()
    {
        var dir = new Vector3(1f, 0.3f, 0f).Normalized();
        GameVFX.SpawnHitImpact(this, _spawnPoints[0], dir);
    }

    private void SpawnParryImpact()
    {
        GameVFX.SpawnParryImpact(this, _spawnPoints[1]);
    }

    private void SpawnDeathBurst()
    {
        GameVFX.SpawnDeathBurst(this, _spawnPoints[2]);
    }

    private void ToggleTrail()
    {
        _trailActive = !_trailActive;
        if (_trailActive)
            _swordTrail.StartEmitting();
        else
            _swordTrail.StopEmitting();

        GD.Print($"[VFXTest] Sword trail: {(_trailActive ? "ON" : "OFF")}");
    }

    private void SpawnDamageFlash()
    {
        GameVFX.SpawnScreenFlash(this, new Color(1f, 0.1f, 0.1f, 0.25f), 0.1f);
    }

    private void SpawnParryFlash()
    {
        GameVFX.SpawnScreenFlash(this, new Color(1f, 1f, 1f, 0.3f), 0.06f);
    }

    // ── Scene construction ────────────────────────────────────────────

    private void CreateFloor()
    {
        var floor = new StaticBody3D();

        var meshInst = new MeshInstance3D();
        var plane = new PlaneMesh();
        plane.Size = new Vector2(20f, 10f);
        meshInst.Mesh = plane;

        var mat = new StandardMaterial3D();
        mat.AlbedoColor = new Color(0.15f, 0.15f, 0.18f);
        meshInst.MaterialOverride = mat;

        var col = new CollisionShape3D();
        var shape = new WorldBoundaryShape3D();
        col.Shape = shape;

        floor.AddChild(meshInst);
        floor.AddChild(col);
        AddChild(floor);
    }

    private void CreateCamera()
    {
        var cam = new Camera3D();
        cam.Position = new Vector3(0f, 4f, 10f);
        cam.LookAtFromPosition(cam.Position, new Vector3(0f, 1f, 0f), Vector3.Up);
        cam.Fov = 50f;
        AddChild(cam);
    }

    private void CreateLight()
    {
        var light = new DirectionalLight3D();
        light.RotationDegrees = new Vector3(-45f, -30f, 0f);
        light.LightEnergy = 0.6f;
        light.ShadowEnabled = true;
        AddChild(light);

        // Ambient fill so effects are visible against dark surfaces
        var env = new WorldEnvironment();
        var envRes = new Godot.Environment();
        envRes.BackgroundMode = Godot.Environment.BGMode.Color;
        envRes.BackgroundColor = new Color(0.08f, 0.08f, 0.1f);
        envRes.AmbientLightSource = Godot.Environment.AmbientSource.Color;
        envRes.AmbientLightColor = new Color(0.2f, 0.2f, 0.25f);
        envRes.AmbientLightEnergy = 0.5f;
        env.Environment = envRes;
        AddChild(env);
    }

    private void CreateTargets()
    {
        // Grey capsules at each spawn point so impact lights have geometry to illuminate
        for (int i = 0; i < _spawnPoints.Length; i++)
        {
            var meshInst = new MeshInstance3D();
            var capsule = new CapsuleMesh();
            capsule.Radius = 0.25f;
            capsule.Height = 1.4f;
            meshInst.Mesh = capsule;

            var mat = new StandardMaterial3D();
            mat.AlbedoColor = new Color(0.45f, 0.45f, 0.5f);
            meshInst.MaterialOverride = mat;

            meshInst.Position = _spawnPoints[i];
            AddChild(meshInst);

            // Ground marker ring
            var ring = new MeshInstance3D();
            var torus = new TorusMesh();
            torus.InnerRadius = 0.6f;
            torus.OuterRadius = 0.75f;
            ring.Mesh = torus;

            var ringMat = new StandardMaterial3D();
            ringMat.AlbedoColor = new Color(0.3f, 0.3f, 0.35f);
            ring.MaterialOverride = ringMat;
            ring.Position = new Vector3(_spawnPoints[i].X, 0.01f, _spawnPoints[i].Z);
            AddChild(ring);
        }
    }

    private void CreateLabels()
    {
        for (int i = 0; i < _spawnPoints.Length; i++)
        {
            var label = new Label3D();
            label.Text = _labels[i];
            label.FontSize = 48;
            label.Position = _spawnPoints[i] + new Vector3(0f, 1.6f, 0f);
            label.Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
            label.NoDepthTest = true;
            label.Modulate = new Color(0.9f, 0.9f, 0.9f);
            label.OutlineSize = 8;
            label.OutlineModulate = new Color(0f, 0f, 0f, 0.7f);
            AddChild(label);
        }
    }

    private void CreateTrailRig()
    {
        // Spinning arm that simulates sword movement
        _trailPivot = new Node3D();
        _trailPivot.Position = _spawnPoints[3];
        AddChild(_trailPivot);

        // Visual arm so you can see what the trail is tracking
        var armMesh = new MeshInstance3D();
        var box = new BoxMesh();
        box.Size = new Vector3(0.05f, 0.05f, 1.5f);
        armMesh.Mesh = box;
        armMesh.Position = new Vector3(0f, 0f, -0.75f);

        var armMat = new StandardMaterial3D();
        armMat.AlbedoColor = new Color(0.7f, 0.7f, 0.8f);
        armMesh.MaterialOverride = armMat;
        _trailPivot.AddChild(armMesh);

        // Tip marker
        var tipMesh = new MeshInstance3D();
        var sphere = new SphereMesh();
        sphere.Radius = 0.06f;
        sphere.Height = 0.12f;
        tipMesh.Mesh = sphere;
        tipMesh.Position = new Vector3(0f, 0f, -1.5f);

        var tipMat = new StandardMaterial3D();
        tipMat.AlbedoColor = Colors.White;
        tipMat.EmissionEnabled = true;
        tipMat.Emission = Colors.White;
        tipMat.EmissionEnergyMultiplier = 2f;
        tipMesh.MaterialOverride = tipMat;
        _trailPivot.AddChild(tipMesh);

        // Sword trail
        _swordTrail = new SwordTrail();
        _swordTrail.Initialize(_trailPivot, new Vector3(0f, 0f, -1.5f));
        AddChild(_swordTrail);
    }

    private void CreateUI()
    {
        var canvas = new CanvasLayer();
        canvas.Layer = 10;
        AddChild(canvas);

        var panel = new PanelContainer();
        panel.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        panel.Position = new Vector2(16, -16);
        panel.GrowVertical = Control.GrowDirection.Begin;

        var panelStyle = new StyleBoxFlat();
        panelStyle.BgColor = new Color(0.1f, 0.1f, 0.12f, 0.85f);
        panelStyle.CornerRadiusTopLeft = 6;
        panelStyle.CornerRadiusTopRight = 6;
        panelStyle.CornerRadiusBottomLeft = 6;
        panelStyle.CornerRadiusBottomRight = 6;
        panelStyle.ContentMarginLeft = 12;
        panelStyle.ContentMarginRight = 12;
        panelStyle.ContentMarginTop = 12;
        panelStyle.ContentMarginBottom = 12;
        panel.AddThemeStyleboxOverride("panel", panelStyle);
        canvas.AddChild(panel);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 6);
        panel.AddChild(vbox);

        // Title
        var title = new Label();
        title.Text = "VFX Test Bench";
        title.AddThemeFontSizeOverride("font_size", 16);
        vbox.AddChild(title);

        var sep = new HSeparator();
        vbox.AddChild(sep);

        // Buttons
        AddButton(vbox, "[1] Hit Impact", SpawnHitImpact);
        AddButton(vbox, "[2] Parry Impact", SpawnParryImpact);
        AddButton(vbox, "[3] Death Burst", SpawnDeathBurst);
        AddButton(vbox, "[4] Sword Trail", ToggleTrail);

        var sep2 = new HSeparator();
        vbox.AddChild(sep2);

        var screenLabel = new Label();
        screenLabel.Text = "Screen effects";
        screenLabel.AddThemeFontSizeOverride("font_size", 13);
        vbox.AddChild(screenLabel);

        AddButton(vbox, "[5] Damage Flash", SpawnDamageFlash);
        AddButton(vbox, "[6] Parry Flash", SpawnParryFlash);
    }

    private void AddButton(VBoxContainer parent, string text, Action callback)
    {
        var btn = new Button();
        btn.Text = text;
        btn.Pressed += callback;
        parent.AddChild(btn);
    }
}
