namespace Game.UI;

using Game.Characters;
using Godot;

/// <summary>
/// World-space health bar displayed above enemies via SubViewport + Sprite3D.
///
/// Visibility rules (Sekiro-style):
///   1. Locked-on target — always visible while lock is held
///   2. Recently damaged — visible for ShowDuration seconds, then fades
///   3. Dead — immediately hidden
///
/// Created by EnemyBase in Initialize(). Not a scene — builds itself
/// entirely in code. Subscribes to the owner's HealthChanged event
/// for bar updates and damage-triggered visibility.
///
/// The SubViewport renders a flat health bar with ghost-bar drain effect.
/// A billboard Sprite3D displays the viewport texture in world space
/// above the enemy's head.
/// </summary>
public partial class EnemyHealthBar : Node3D
{
    // ── Configuration ─────────────────────────────────────────────────

    [Export] public float GhostBarDelay = 0.3f;
    [Export] public float GhostBarDrainSpeed = 0.4f;

    /// <summary>How long the bar stays visible after taking damage.</summary>
    [Export] public float ShowDuration = 3.0f;

    /// <summary>Alpha lerp speed for fade in/out.</summary>
    [Export] public float FadeSpeed = 5.0f;

    /// <summary>Offset from owner's GlobalPosition where the bar sits.</summary>
    [Export] public Vector3 Offset = new(0f, 2.4f, 0f);

    // ── Viewport dimensions ───────────────────────────────────────────

    private const int ViewportWidth = 160;
    private const int ViewportHeight = 14;

    /// <summary>
    /// World-space size per pixel. 160px * 0.008 = 1.28 world units wide.
    /// </summary>
    private const float SpritePixelSize = 0.008f;

    // ── Bar colors ────────────────────────────────────────────────────

    private static readonly Color BgColor = new(0.08f, 0.08f, 0.08f, 0.9f);
    private static readonly Color BorderColor = new(0.35f, 0.35f, 0.35f, 0.6f);
    private static readonly Color HealthColor = new(0.75f, 0.12f, 0.12f);
    private static readonly Color GhostColor = new(0.85f, 0.45f, 0.08f);

    // ── Nodes ─────────────────────────────────────────────────────────

    private SubViewport _viewport;
    private Sprite3D _sprite;
    private ProgressBar _healthFill;
    private ProgressBar _ghostFill;

    // ── State ─────────────────────────────────────────────────────────

    private CharacterBase _owner;
    private CameraController _cameraController;
    private Tween _ghostTween;

    private float _showTimer;
    private float _currentAlpha;
    private float _targetAlpha;
    private bool _ownerDead;

    // ══════════════════════════════════════════════════════════════════
    //  PUBLIC API — called by EnemyBase
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Bind this health bar to a CharacterBase owner. Must be called
    /// before the bar is added to the tree (right after construction).
    /// </summary>
    public void Setup(CharacterBase owner)
    {
        _owner = owner;
    }

    /// <summary>
    /// Called by EnemyBase.OnDeath(). Immediately hides the bar
    /// and stops reacting to HealthChanged until respawn.
    /// </summary>
    public void OnOwnerDied()
    {
        _ownerDead = true;
        _showTimer = 0f;
        _targetAlpha = 0f;
        _currentAlpha = 0f;
        _sprite.Modulate = new Color(1f, 1f, 1f, 0f);
    }

    /// <summary>
    /// Called by EnemyBase.Respawn(). Re-enables the bar and
    /// resets both fills to 100%.
    /// </summary>
    public void OnOwnerRespawned()
    {
        _ownerDead = false;
        _healthFill.Value = 100;
        _ghostFill.Value = 100;
    }

    // ══════════════════════════════════════════════════════════════════
    //  LIFECYCLE
    // ══════════════════════════════════════════════════════════════════

    public override void _Ready()
    {
        TopLevel = true;

        BuildViewport();
        BuildSprite();

        // Start invisible
        _currentAlpha = 0f;
        _targetAlpha = 0f;
        _sprite.Modulate = new Color(1f, 1f, 1f, 0f);

        if (_owner != null)
            _owner.HealthChanged += OnHealthChanged;

        CallDeferred(nameof(FindCameraController));
    }

    public override void _ExitTree()
    {
        if (_owner != null)
            _owner.HealthChanged -= OnHealthChanged;
    }

    public override void _Process(double delta)
    {
        if (_owner == null || _ownerDead) return;

        float dt = (float)delta;
        UpdatePosition();
        UpdateVisibility(dt);
    }

    // ══════════════════════════════════════════════════════════════════
    //  VIEWPORT CONSTRUCTION
    // ══════════════════════════════════════════════════════════════════

    private void BuildViewport()
    {
        _viewport = new SubViewport
        {
            Size = new Vector2I(ViewportWidth, ViewportHeight),
            TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            Disable3D = true,
            GuiDisableInput = true
        };
        AddChild(_viewport);

        // Border — full-size rect behind everything for a 1px border
        var border = new ColorRect
        {
            Color = BorderColor,
            Position = Vector2.Zero,
            Size = new Vector2(ViewportWidth, ViewportHeight)
        };
        _viewport.AddChild(border);

        // Dark inner background
        var bg = new ColorRect
        {
            Color = BgColor,
            Position = new Vector2(1, 1),
            Size = new Vector2(ViewportWidth - 2, ViewportHeight - 2)
        };
        _viewport.AddChild(bg);

        // Ghost bar — trails behind health to show recent damage
        _ghostFill = CreateBar(GhostColor);
        _viewport.AddChild(_ghostFill);

        // Health bar — snaps to current health immediately
        _healthFill = CreateBar(HealthColor);
        _viewport.AddChild(_healthFill);
    }

    private ProgressBar CreateBar(Color fillColor)
    {
        var bar = new ProgressBar
        {
            Position = new Vector2(2, 2),
            Size = new Vector2(ViewportWidth - 4, ViewportHeight - 4),
            MinValue = 0,
            MaxValue = 100,
            Value = 100,
            ShowPercentage = false
        };

        var bgStyle = new StyleBoxFlat { BgColor = Colors.Transparent };
        bar.AddThemeStyleboxOverride("background", bgStyle);

        var fillStyle = new StyleBoxFlat
        {
            BgColor = fillColor,
            CornerRadiusTopLeft = 1,
            CornerRadiusTopRight = 1,
            CornerRadiusBottomLeft = 1,
            CornerRadiusBottomRight = 1
        };
        bar.AddThemeStyleboxOverride("fill", fillStyle);

        return bar;
    }

    // ══════════════════════════════════════════════════════════════════
    //  SPRITE CONSTRUCTION
    // ══════════════════════════════════════════════════════════════════

    private void BuildSprite()
    {
        _sprite = new Sprite3D
        {
            Texture = _viewport.GetTexture(),
            PixelSize = SpritePixelSize,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            AlphaCut = SpriteBase3D.AlphaCutMode.Disabled,
            Shaded = false,
            DoubleSided = false,
            NoDepthTest = true,
            RenderPriority = 10
        };
        AddChild(_sprite);
    }

    // ══════════════════════════════════════════════════════════════════
    //  POSITION TRACKING
    // ══════════════════════════════════════════════════════════════════

    private void UpdatePosition()
    {
        GlobalPosition = _owner.GlobalPosition + Offset;
    }

    // ══════════════════════════════════════════════════════════════════
    //  VISIBILITY
    // ══════════════════════════════════════════════════════════════════

    private void UpdateVisibility(float dt)
    {
        bool isLockTarget = _cameraController != null
                         && _cameraController.IsLockedOn
                         && _cameraController.LockTarget == _owner;

        if (isLockTarget)
        {
            _targetAlpha = 1f;
        }
        else if (_showTimer > 0f)
        {
            _showTimer -= dt;
            _targetAlpha = _showTimer > 0f ? 1f : 0f;
        }
        else
        {
            _targetAlpha = 0f;
        }

        _currentAlpha = Mathf.MoveToward(_currentAlpha, _targetAlpha, FadeSpeed * dt);
        _sprite.Modulate = new Color(1f, 1f, 1f, _currentAlpha);
    }

    // ══════════════════════════════════════════════════════════════════
    //  HEALTH CHANGED CALLBACK
    // ══════════════════════════════════════════════════════════════════

    private void OnHealthChanged(int current, int max)
    {
        if (_ownerDead) return;

        float percent = max > 0 ? (float)current / max * 100f : 0f;

        _healthFill.Value = percent;

        _ghostTween?.Kill();
        _ghostTween = CreateTween();
        _ghostTween.TweenInterval(GhostBarDelay);
        _ghostTween.TweenProperty(_ghostFill, "value", percent, GhostBarDrainSpeed)
            .SetEase(Tween.EaseType.In)
            .SetTrans(Tween.TransitionType.Cubic);

        _showTimer = ShowDuration;
    }

    // ══════════════════════════════════════════════════════════════════
    //  CAMERA CONTROLLER LOOKUP
    // ══════════════════════════════════════════════════════════════════

    private void FindCameraController()
    {
        var root = GetTree()?.CurrentScene;
        if (root == null) return;

        _cameraController = FindCameraRecursive(root);

        if (_cameraController == null)
            GD.PrintErr("[EnemyHealthBar] CameraController not found — lock-on visibility disabled.");
    }

    private static CameraController FindCameraRecursive(Node node)
    {
        if (node is CameraController cam) return cam;

        foreach (var child in node.GetChildren())
        {
            var found = FindCameraRecursive(child);
            if (found != null) return found;
        }

        return null;
    }
}
