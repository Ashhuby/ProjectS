using Godot;
using System;

/// <summary>
/// VFX test bench. Create a new scene with a Node3D root, attach this script, run.
/// Requires GameVFX autoload to be registered.
/// Keys 1-7 trigger effects. Buttons on the left panel do the same.
/// </summary>
public partial class VFXTestScene : Node3D
{
    private readonly Vector3[] _spawnPoints =
    {
        new(-4.5f, 1.2f, 0f),   // 0: Hit Impact
        new(-1.5f, 1.2f, 0f),   // 1: Parry Impact
        new(1.5f, 1.2f, 0f),    // 2: Death Burst
        new(4.5f, 1.2f, 0f),    // 3: Vital Pop (primary)
    };

    private readonly string[] _labels =
    {
        "1 — Hit Impact",
        "2 — Parry Impact",
        "3 — Death Burst",
        "4 — Vital Pop (Primary)",
    };

    public override void _Ready()
    {
        CreateFloor();
        CreateCamera();
        CreateLight();
        CreateTargets();
        CreateLabels();
        CreateUI();

        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            switch (key.Keycode)
            {
                case Key.Key1: TriggerHitImpact(); break;
                case Key.Key2: TriggerParryImpact(); break;
                case Key.Key3: TriggerDeathBurst(); break;
                case Key.Key4: TriggerVitalPopPrimary(); break;
                case Key.Key5: TriggerVitalPopMini(); break;
                case Key.Key6: TriggerVitalFail(); break;
                case Key.Key7: TriggerDamageFlash(); break;
                case Key.Key8: TriggerParryFlash(); break;
            }
        }
    }

    // ── Effect triggers ───────────────────────────────────────────────

    private void TriggerHitImpact()
    {
        var dir = new Vector3(1f, 0.3f, 0f).Normalized();
        Game.VFX.GameVFX.Instance?.SpawnHitImpact(_spawnPoints[0], dir);
    }

    private void TriggerParryImpact()
    {
        Game.VFX.GameVFX.Instance?.SpawnParryImpact(_spawnPoints[1]);
    }

    private void TriggerDeathBurst()
    {
        Game.VFX.GameVFX.Instance?.SpawnDeathBurst(_spawnPoints[2]);
    }

    private void TriggerVitalPopPrimary()
    {
        Game.VFX.GameVFX.Instance?.SpawnVitalPopBurst(_spawnPoints[3], isPrimary: true);
    }

    private void TriggerVitalPopMini()
    {
        Game.VFX.GameVFX.Instance?.SpawnVitalPopBurst(_spawnPoints[3], isPrimary: false);
    }

    private void TriggerVitalFail()
    {
        Game.VFX.GameVFX.Instance?.SpawnVitalFailFizzle(_spawnPoints[3]);
    }

    private void TriggerDamageFlash()
    {
        Game.VFX.GameVFX.Instance?.SpawnScreenFlash(new Color(1f, 0.1f, 0.1f, 0.25f), 0.1f);
    }

    private void TriggerParryFlash()
    {
        Game.VFX.GameVFX.Instance?.SpawnScreenFlash(new Color(1f, 1f, 1f, 0.3f), 0.06f);
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

        var env = new WorldEnvironment();
        var envRes = new Godot.Environment();
        envRes.BackgroundMode = Godot.Environment.BGMode.Color;
        envRes.BackgroundColor = new Color(0.08f, 0.08f, 0.1f);
        envRes.AmbientLightSource = Godot.Environment.AmbientSource.Color;
        envRes.AmbientLightColor = new Color(0.15f, 0.15f, 0.2f);
        env.Environment = envRes;
        AddChild(env);
    }

    private void CreateTargets()
    {
        for (int i = 0; i < _spawnPoints.Length; i++)
        {
            var target = new MeshInstance3D();
            var sphere = new SphereMesh { Radius = 0.15f, Height = 0.3f };
            target.Mesh = sphere;

            var mat = new StandardMaterial3D();
            mat.AlbedoColor = new Color(0.4f, 0.4f, 0.5f);
            target.MaterialOverride = mat;
            target.Position = _spawnPoints[i];

            AddChild(target);
        }
    }

    private void CreateLabels()
    {
        for (int i = 0; i < _labels.Length; i++)
        {
            var label = new Label3D();
            label.Text = _labels[i];
            label.Position = _spawnPoints[i] + new Vector3(0f, -0.6f, 0f);
            label.FontSize = 28;
            label.OutlineSize = 4;
            label.OutlineModulate = new Color(0f, 0f, 0f, 0.7f);
            AddChild(label);
        }
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

        var title = new Label();
        title.Text = "VFX Test Bench";
        title.AddThemeFontSizeOverride("font_size", 16);
        vbox.AddChild(title);

        vbox.AddChild(new HSeparator());

        AddButton(vbox, "[1] Hit Impact", TriggerHitImpact);
        AddButton(vbox, "[2] Parry Impact", TriggerParryImpact);
        AddButton(vbox, "[3] Death Burst", TriggerDeathBurst);
        AddButton(vbox, "[4] Vital Pop (Primary)", TriggerVitalPopPrimary);
        AddButton(vbox, "[5] Vital Pop (Mini)", TriggerVitalPopMini);
        AddButton(vbox, "[6] Vital Fail", TriggerVitalFail);

        vbox.AddChild(new HSeparator());

        var screenLabel = new Label();
        screenLabel.Text = "Screen effects";
        screenLabel.AddThemeFontSizeOverride("font_size", 13);
        vbox.AddChild(screenLabel);

        AddButton(vbox, "[7] Damage Flash", TriggerDamageFlash);
        AddButton(vbox, "[8] Parry Flash", TriggerParryFlash);
    }

    private void AddButton(VBoxContainer parent, string text, Action callback)
    {
        var btn = new Button();
        btn.Text = text;
        btn.Pressed += callback;
        parent.AddChild(btn);
    }
}
