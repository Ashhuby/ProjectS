using Game.Autoloads;
using Godot;
using Game.Characters.Enemies;
/// <summary>
/// Fixed HUD compass in the bottom-left showing vital direction.
/// Always visible — dimmed when no vital is active, lights up when
/// a vital is revealed.
///
/// The compass is camera-relative: UP on the compass = camera forward,
/// RIGHT on the compass = camera right. This directly tells the player
/// which way to dash.
///
/// Scene setup:
///   - Add as a child of your HUD CanvasLayer
///   - Assign this script — it draws itself via _Draw()
/// </summary>
public partial class VitalCompass : Control
{
    // ── Config ────────────────────────────────────────────────────────

    /// <summary>Radius of the compass circle in pixels.</summary>
    [Export] public float CompassRadius { get; set; } = 50f;

    /// <summary>Size of the vital diamond marker in pixels.</summary>
    [Export] public float DiamondSize { get; set; } = 12f;

    /// <summary>Margin from the bottom-left corner in pixels.</summary>
    [Export] public float ScreenMargin { get; set; } = 30f;

    // ── State ─────────────────────────────────────────────────────────

    private bool _vitalActive;
    private bool _isPrimary;
    private Node3D _trackedEnemy;
    private float _pulseTimer;

    // ── Colors ────────────────────────────────────────────────────────

    private static readonly Color DimColor = new(0.4f, 0.4f, 0.4f, 0.3f);
    private static readonly Color DimRingColor = new(0.5f, 0.5f, 0.5f, 0.2f);
    private static readonly Color PrimaryColor = new(1f, 0.6f, 0.1f, 1f);
    private static readonly Color MiniColor = new(1f, 0.95f, 0.4f, 1f);
    private static readonly Color ActiveRingColor = new(0.8f, 0.5f, 0.1f, 0.5f);
    private static readonly Color CrosshairColor = new(0.6f, 0.6f, 0.6f, 0.15f);

    // ══════════════════════════════════════════════════════════════════
    //  LIFECYCLE
    // ══════════════════════════════════════════════════════════════════

    public override void _Ready()
    {
        // Position in bottom-left — we'll use fixed coordinates in _Draw
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);

        if (EventBus.Instance != null)
        {
            EventBus.Instance.VitalRevealed += OnVitalRevealed;
            EventBus.Instance.VitalPopped += OnVitalPopped;
            EventBus.Instance.VitalFailed += OnVitalFailed;
            EventBus.Instance.VitalSequenceComplete += OnVitalSequenceComplete;
        }
    }

    public override void _ExitTree()
    {
        if (EventBus.Instance != null)
        {
            EventBus.Instance.VitalRevealed -= OnVitalRevealed;
            EventBus.Instance.VitalPopped -= OnVitalPopped;
            EventBus.Instance.VitalFailed -= OnVitalFailed;
            EventBus.Instance.VitalSequenceComplete -= OnVitalSequenceComplete;
        }
    }

    public override void _Process(double delta)
    {
        _pulseTimer += (float)delta;
        QueueRedraw();
    }

    // ══════════════════════════════════════════════════════════════════
    //  DRAW
    // ══════════════════════════════════════════════════════════════════

    public override void _Draw()
    {
        Vector2 viewport = GetViewportRect().Size;
        Vector2 center = new(
            ScreenMargin + CompassRadius,
            viewport.Y - ScreenMargin - CompassRadius);

        // ── Always draw the compass frame ─────────────────────────
        Color ringColor = _vitalActive ? ActiveRingColor : DimRingColor;
        float ringWidth = _vitalActive ? 2.5f : 1.5f;
        DrawArc(center, CompassRadius, 0f, Mathf.Tau, 48, ringColor, ringWidth);

        // Crosshair lines (subtle orientation reference)
        float cross = CompassRadius * 0.3f;
        DrawLine(center + new Vector2(-cross, 0), center + new Vector2(cross, 0),
            CrosshairColor, 1f);
        DrawLine(center + new Vector2(0, -cross), center + new Vector2(0, cross),
            CrosshairColor, 1f);

        // Center dot (enemy position)
        Color dotColor = _vitalActive ? new Color(1f, 1f, 1f, 0.6f) : DimColor;
        DrawCircle(center, 3f, dotColor);

        // Cardinal labels
        Color labelColor = _vitalActive ? new Color(1f, 1f, 1f, 0.3f) : DimColor;
        float labelOffset = CompassRadius + 12f;

        // ── Draw vital direction if active ────────────────────────
        if (!_vitalActive) return;
        if (_trackedEnemy == null || !GodotObject.IsInstanceValid(_trackedEnemy))
        {
            _vitalActive = false;
            return;
        }

        var enemy = _trackedEnemy as EnemyBase;
        if (enemy == null || enemy.Vitals == null || !enemy.Vitals.IsActive)
            return;

        // Get the vital direction in world space
        Vector3 vitalDir = enemy.Vitals.ActiveVitalDirection;
        if (vitalDir.LengthSquared() < 0.001f) return;

        // Project vital direction into camera-relative 2D
        // Camera right = compass right, camera forward = compass up
        Camera3D cam = GetViewport().GetCamera3D();
        if (cam == null) return;

        Vector3 camRight = cam.GlobalTransform.Basis.X;
        camRight.Y = 0f;
        if (camRight.LengthSquared() > 0.001f)
            camRight = camRight.Normalized();

        Vector3 camForward = -cam.GlobalTransform.Basis.Z;
        camForward.Y = 0f;
        if (camForward.LengthSquared() > 0.001f)
            camForward = camForward.Normalized();

        // Dot product gives us the 2D coordinates on the compass
        float compassX = vitalDir.Dot(camRight);
        float compassY = -vitalDir.Dot(camForward); // negative because screen Y is down

        Vector2 compassDir = new Vector2(compassX, compassY).Normalized();

        // Position the diamond on the compass ring edge
        float pulse = 0.85f + 0.15f * Mathf.Sin(_pulseTimer * 8f);
        Vector2 diamondPos = center + compassDir * CompassRadius * 0.75f;

        // Draw a line from center to diamond (direction indicator)
        Color lineColor = _isPrimary
            ? PrimaryColor with { A = 0.3f }
            : MiniColor with { A = 0.3f };
        DrawLine(center, diamondPos, lineColor, 1.5f);

        // Draw the diamond
        Color diamondColor = _isPrimary ? PrimaryColor : MiniColor;
        diamondColor.A *= pulse;

        float s = DiamondSize * pulse;
        float angle = compassDir.Angle() + Mathf.Pi / 2f;

        Vector2[] diamond = new Vector2[4];
        diamond[0] = diamondPos + new Vector2(0, -s).Rotated(angle);
        diamond[1] = diamondPos + new Vector2(s * 0.6f, 0).Rotated(angle);
        diamond[2] = diamondPos + new Vector2(0, s * 0.6f).Rotated(angle);
        diamond[3] = diamondPos + new Vector2(-s * 0.6f, 0).Rotated(angle);

        DrawColoredPolygon(diamond, diamondColor);

        // White outline
        Color outline = new Color(1f, 1f, 1f, pulse * 0.5f);
        for (int i = 0; i < 4; i++)
            DrawLine(diamond[i], diamond[(i + 1) % 4], outline, 1.5f);

        // Outer ring glow pulse when active
        Color glowRing = (_isPrimary ? PrimaryColor : MiniColor) with { A = 0.15f * pulse };
        DrawArc(center, CompassRadius + 3f, 0f, Mathf.Tau, 48, glowRing, 2f);
    }

    // ══════════════════════════════════════════════════════════════════
    //  EVENT HANDLERS
    // ══════════════════════════════════════════════════════════════════

    private void OnVitalRevealed(Node3D enemy, Vector3 direction)
    {
        _trackedEnemy = enemy;
        _vitalActive = true;
        _isPrimary = true;
        _pulseTimer = 0f;
    }

    private void OnVitalPopped(Node3D enemy, bool isPrimary)
    {
        if (isPrimary)
        {
            // Primary popped → now tracking mini vital
            _isPrimary = false;
            _pulseTimer = 0f;
        }
    }

    private void OnVitalFailed(Node3D enemy)
    {
        _vitalActive = false;
        _trackedEnemy = null;
    }

    private void OnVitalSequenceComplete(Node3D enemy, bool hitBoth)
    {
        _vitalActive = false;
        _trackedEnemy = null;
    }
}
