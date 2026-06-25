using Godot;

public partial class TestDummy : CharacterBody3D
{
    [Export] public int MaxHealth = 50;
    [Export] public float RespawnDelay = 3.0f;
    [Export] public float KnockbackDecay = 10f;

    private int _currentHealth;
    private Hurtbox _hurtbox;
    private MeshInstance3D _mesh;
    private StandardMaterial3D _material;
    private Tween _flashTween;
    private bool _isDead = false;
    private Vector3 _knockbackVelocity = Vector3.Zero;

    public override void _Ready()
    {
        _currentHealth = MaxHealth;
        _hurtbox = GetNode<Hurtbox>("Hurtbox");
        _mesh = GetNode<MeshInstance3D>("MeshInstance3D");

        _material = new StandardMaterial3D();
        _material.AlbedoColor = Colors.White;
        _mesh.MaterialOverride = _material;

        _hurtbox.DamageReceived += OnDamageReceived;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        // Apply knockback
        if (_knockbackVelocity.LengthSquared() > 0.01f)
        {
            Velocity = new Vector3(_knockbackVelocity.X, Velocity.Y, _knockbackVelocity.Z);
            _knockbackVelocity = _knockbackVelocity.Lerp(Vector3.Zero, KnockbackDecay * dt);
        }
        else
        {
            Velocity = new Vector3(0f, Velocity.Y, 0f);
            _knockbackVelocity = Vector3.Zero;
        }

        // Gravity
        if (!IsOnFloor())
            Velocity = new Vector3(Velocity.X, Velocity.Y - 9.8f * dt, Velocity.Z);

        MoveAndSlide();
    }

    private void OnDamageReceived(DamageData data)
    {
        if (_isDead) return;

        _currentHealth = Mathf.Max(_currentHealth - data.Amount, 0);
        GD.Print($"Dummy hit for {data.Amount}. HP: {_currentHealth}/{MaxHealth}");

        _knockbackVelocity = data.KnockbackDirection;
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
        _hurtbox.SetDeferred("monitorable", false);
        _knockbackVelocity = Vector3.Zero;

        GD.Print("Dummy killed.");

        var deathTween = CreateTween();
        deathTween.SetParallel(true);
        deathTween.TweenProperty(_mesh, "scale", Vector3.Zero, 0.4f)
            .SetEase(Tween.EaseType.In)
            .SetTrans(Tween.TransitionType.Back);
        deathTween.TweenProperty(_material, "albedo_color", new Color(0.3f, 0.3f, 0.3f), 0.4f);
        deathTween.SetParallel(false);
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
        _knockbackVelocity = Vector3.Zero;

        GD.Print("Dummy respawned.");
    }
}
