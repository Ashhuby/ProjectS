using Godot;

public partial class HealthBar : Control
{
    [Export] public float GhostBarDelay = 0.4f;
    [Export] public float GhostBarDrainSpeed = 0.5f;

    private ProgressBar _healthFill;
    private ProgressBar _ghostFill;
    private PlayerCharacter _player;
    private Tween _ghostTween;

    public override void _Ready()
    {
        // Position and size
        SetAnchorsPreset(LayoutPreset.TopLeft);
        CustomMinimumSize = new Vector2(250, 22);
        Size = new Vector2(250, 22);
        Position = new Vector2(20, 20);

        // Dark background
        var bg = new ColorRect();
        bg.Color = new Color(0.1f, 0.1f, 0.1f, 0.8f);
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        // Ghost bar — trails behind the health bar to show recent damage
        _ghostFill = CreateBar(new Color(0.9f, 0.6f, 0.1f));
        AddChild(_ghostFill);

        // Health bar — snaps to current health immediately
        _healthFill = CreateBar(new Color(0.8f, 0.15f, 0.15f));
        AddChild(_healthFill);

        // Find player after all _Ready() calls have finished
        CallDeferred(nameof(ConnectToPlayer));
    }

    private void ConnectToPlayer()
    {
        _player = GetTree().GetFirstNodeInGroup("Player") as PlayerCharacter;
        if (_player == null)
        {
            GD.PrintErr("HealthBar: No node found in 'Player' group. Add PlayerCharacter to the 'Player' group.");
            return;
        }

        _player.HealthChanged += OnHealthChanged;

        // Initialize both bars to full
        _healthFill.Value = 100;
        _ghostFill.Value = 100;
    }

    private ProgressBar CreateBar(Color fillColor)
    {
        var bar = new ProgressBar();
        bar.SetAnchorsPreset(LayoutPreset.FullRect);
        bar.MinValue = 0;
        bar.MaxValue = 100;
        bar.Value = 100;
        bar.ShowPercentage = false;

        // Transparent background so bars layer correctly
        var bgStyle = new StyleBoxFlat();
        bgStyle.BgColor = Colors.Transparent;
        bar.AddThemeStyleboxOverride("background", bgStyle);

        // Colored fill
        var fillStyle = new StyleBoxFlat();
        fillStyle.BgColor = fillColor;
        fillStyle.CornerRadiusTopLeft = 2;
        fillStyle.CornerRadiusTopRight = 2;
        fillStyle.CornerRadiusBottomLeft = 2;
        fillStyle.CornerRadiusBottomRight = 2;
        bar.AddThemeStyleboxOverride("fill", fillStyle);

        return bar;
    }

    private void OnHealthChanged(int current, int max)
    {
        float percent = (float)current / max * 100f;

        // Health bar snaps immediately
        _healthFill.Value = percent;

        // Ghost bar drains after a short delay
        _ghostTween?.Kill();
        _ghostTween = CreateTween();
        _ghostTween.TweenInterval(GhostBarDelay);
        _ghostTween.TweenProperty(_ghostFill, "value", percent, GhostBarDrainSpeed)
            .SetEase(Tween.EaseType.In)
            .SetTrans(Tween.TransitionType.Cubic);
    }

    public override void _ExitTree()
    {
        if (_player != null)
        {
            _player.HealthChanged -= OnHealthChanged;
        }
    }
}
