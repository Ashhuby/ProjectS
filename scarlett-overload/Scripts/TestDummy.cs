using Godot;

public partial class TestDummy : CharacterBody3D
{
    [Export] public int MaxHealth = 50;
    [Export] public float RespawnDelay = 3.0f;

    private int _currentHealth;
    private Hurtbox _hurtbox;
    private MeshInstance3D _mesh;
    private StandardMaterial3D _material;
    private Tween _flashTween;
    private bool _isDead = false;

    public override void _Ready()
    {
        _currentHealth = MaxHealth;

        _hurtbox = GetNode<Hurtbox>("Hurtbox");
        _mesh = GetNode<MeshInstance3D>("MeshInstance3D");

        // Create a material we can tween for hit flash
        _material = new StandardMaterial3D();
        _material.AlbedoColor = Colors.White;
        _mesh.MaterialOverride = _material;

        _hurtbox.DamageReceived += OnDamageReceived;
    }

    private void OnDamageReceived(DamageData data)
    {
        if (_isDead) return;

        _currentHealth = Mathf.Max(_currentHealth - data.Amount, 0);
        GD.Print($"Dummy hit for {data.Amount}. HP: {_currentHealth}/{MaxHealth}");

        FlashRed();

        if (_currentHealth <= 0)
        {
            OnDeath();
        }
    }

    private void FlashRed()
    {
        _flashTween?.Kill();
        _flashTween = CreateTween();
        _material.AlbedoColor = new Color(1f, 0.2f, 0.2f);
        _flashTween.TweenProperty(_material, "albedo_color", Colors.White, 0.3f)
            .SetEase(Tween.EaseType.Out);
    }

    private void OnDeath()
    {
        _isDead = true;

        // Disable hurtbox so it can't be hit while dead
        _hurtbox.SetDeferred("monitorable", false);

        GD.Print("Dummy killed.");

        // Shrink and fade out
        var deathTween = CreateTween();
        deathTween.SetParallel(true);
        deathTween.TweenProperty(_mesh, "scale", Vector3.Zero, 0.4f)
            .SetEase(Tween.EaseType.In)
            .SetTrans(Tween.TransitionType.Back);
        deathTween.TweenProperty(_material, "albedo_color", new Color(0.3f, 0.3f, 0.3f), 0.4f);
        deathTween.SetParallel(false);

        // Wait then respawn
        deathTween.TweenInterval(RespawnDelay);
        deathTween.TweenCallback(Callable.From(Respawn));
    }

    private void Respawn()
    {
        _currentHealth = MaxHealth;
        _isDead = false;
        _mesh.Scale = Vector3.One;
        _material.AlbedoColor = Colors.White;
        _hurtbox.SetDeferred("monitorable", true);

        GD.Print("Dummy respawned.");
    }
}
