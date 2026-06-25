using Godot;

public partial class AttackDummy : CharacterBody3D
{
    [Export] public int MaxHealth = 100;
    [Export] public float AttackInterval = 2.5f;
    [Export] public float TelegraphDuration = 1.0f;
    [Export] public float AttackActiveDuration = 0.25f;
    [Export] public float RecoveryDuration = 0.4f;
    [Export] public float RespawnDelay = 3.0f;
    [Export] public float KnockbackDecay = 10f;

    private MeshInstance3D _mesh;
    private StandardMaterial3D _material;
    private Hitbox _attackHitbox;
    private Hurtbox _hurtbox;
    private int _currentHealth;
    private bool _isDead = false;
    private Vector3 _knockbackVelocity = Vector3.Zero;

    // Warning indicator above head
    private MeshInstance3D _warningIndicator;
    private StandardMaterial3D _warningMat;

    // Attack flash light
    private OmniLight3D _attackFlash;

    private enum DummyState { Idle, Telegraph, Attacking, Recovery, Dead }
    private DummyState _state = DummyState.Idle;
    private float _stateTimer;
    private Vector3 _baseScale;

    public override void _Ready()
    {
        _mesh = GetNode<MeshInstance3D>("MeshInstance3D");
        _attackHitbox = GetNode<Hitbox>("AttackHitbox");
        _hurtbox = GetNode<Hurtbox>("Hurtbox");

        _material = new StandardMaterial3D();
        _material.AlbedoColor = new Color(0.6f, 0.15f, 0.15f);
        _mesh.MaterialOverride = _material;
        _baseScale = _mesh.Scale;

        _attackHitbox.Deactivate();
        _hurtbox.DamageReceived += OnDamageReceived;

        // Warning sphere above head — shows during telegraph
        _warningIndicator = new MeshInstance3D();
        var sphere = new SphereMesh();
        sphere.Radius = 0.2f;
        sphere.Height = 0.4f;
        _warningIndicator.Mesh = sphere;
        _warningMat = new StandardMaterial3D();
        _warningMat.AlbedoColor = new Color(1f, 0.8f, 0f);
        _warningMat.EmissionEnabled = true;
        _warningMat.Emission = new Color(1f, 0.8f, 0f);
        _warningMat.EmissionEnergyMultiplier = 3f;
        _warningIndicator.MaterialOverride = _warningMat;
        _warningIndicator.Position = new Vector3(0f, 2.5f, 0f);
        _warningIndicator.Visible = false;
        AddChild(_warningIndicator);

        // Flash light for the attack moment
        _attackFlash = new OmniLight3D();
        _attackFlash.LightColor = new Color(1f, 0.3f, 0.1f);
        _attackFlash.LightEnergy = 0f;
        _attackFlash.OmniRange = 4f;
        _attackFlash.Position = new Vector3(0f, 1f, 0f);
        AddChild(_attackFlash);

        _currentHealth = MaxHealth;
        _stateTimer = AttackInterval;
        _state = DummyState.Idle;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        // Knockback physics
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

        if (!IsOnFloor())
            Velocity = new Vector3(Velocity.X, Velocity.Y - 9.8f * dt, Velocity.Z);

        MoveAndSlide();

        if (_state == DummyState.Dead) return;

        _stateTimer -= dt;

        switch (_state)
        {
            case DummyState.Idle:
                if (_stateTimer <= 0f)
                {
                    _state = DummyState.Telegraph;
                    _stateTimer = TelegraphDuration;
                    _warningIndicator.Visible = true;
                }
                break;

            case DummyState.Telegraph:
                float progress = 1f - (_stateTimer / TelegraphDuration);

                // Warning indicator pulses faster as attack approaches
                float pulseSpeed = Mathf.Lerp(8f, 30f, progress);
                float pulse = Mathf.Sin(_stateTimer * pulseSpeed) * 0.5f + 0.5f;

                // Color shifts from yellow to red as attack nears
                var warningColor = new Color(1f, Mathf.Lerp(0f, 0.8f, 1f - progress), 0f);
                _warningMat.AlbedoColor = warningColor;
                _warningMat.Emission = warningColor;
                _warningMat.EmissionEnergyMultiplier = Mathf.Lerp(2f, 6f, progress);

                // Indicator bobs up and down, faster near the end
                float bob = Mathf.Sin(_stateTimer * pulseSpeed) * 0.1f;
                _warningIndicator.Position = new Vector3(0f, 2.5f + bob, 0f);

                // Dummy body scales up slightly — visual wind-up
                float scaleUp = Mathf.Lerp(1f, 1.15f, progress);
                _mesh.Scale = _baseScale * scaleUp;

                // Body color shifts toward red
                _material.AlbedoColor = new Color(
                    Mathf.Lerp(0.6f, 1f, progress),
                    Mathf.Lerp(0.15f, 0f, progress),
                    Mathf.Lerp(0.15f, 0f, progress)
                );

                if (_stateTimer <= 0f)
                {
                    _state = DummyState.Attacking;
                    _stateTimer = AttackActiveDuration;
                    _material.AlbedoColor = Colors.White;
                    _mesh.Scale = _baseScale * 1.2f;
                    _warningMat.AlbedoColor = new Color(1f, 0f, 0f);
                    _warningMat.Emission = new Color(1f, 0f, 0f);
                    _warningMat.EmissionEnergyMultiplier = 8f;
                    _attackFlash.LightEnergy = 5f;
                    _attackHitbox.Activate();
                    GD.Print("AttackDummy swings!");
                }
                break;

            case DummyState.Attacking:
                // Flash fades during attack window
                _attackFlash.LightEnergy = Mathf.Lerp(0f, 5f, _stateTimer / AttackActiveDuration);

                if (_stateTimer <= 0f)
                {
                    _attackHitbox.Deactivate();
                    _state = DummyState.Recovery;
                    _stateTimer = RecoveryDuration;
                    _material.AlbedoColor = new Color(0.4f, 0.4f, 0.4f);
                    _mesh.Scale = _baseScale;
                    _warningIndicator.Visible = false;
                    _attackFlash.LightEnergy = 0f;
                }
                break;

            case DummyState.Recovery:
                if (_stateTimer <= 0f)
                {
                    _state = DummyState.Idle;
                    _stateTimer = AttackInterval;
                    _material.AlbedoColor = new Color(0.6f, 0.15f, 0.15f);
                }
                break;
        }
    }

    private void OnDamageReceived(DamageData data)
    {
        if (_isDead) return;

        _currentHealth = Mathf.Max(_currentHealth - data.Amount, 0);
        GD.Print($"AttackDummy hit for {data.Amount}. HP: {_currentHealth}/{MaxHealth}");

        _knockbackVelocity = data.KnockbackDirection;
        FlashWhite();

        if (_currentHealth <= 0)
        {
            OnDeath();
        }
    }

    private void FlashWhite()
    {
        var savedColor = _material.AlbedoColor;
        _material.AlbedoColor = Colors.White;
        var tween = CreateTween();
        tween.TweenProperty(_material, "albedo_color", savedColor, 0.2f);
    }

    private void OnDeath()
    {
        _isDead = true;
        _state = DummyState.Dead;
        _attackHitbox.Deactivate();
        _knockbackVelocity = Vector3.Zero;
        _warningIndicator.Visible = false;
        _attackFlash.LightEnergy = 0f;
        _hurtbox.SetDeferred("monitorable", false);

        GD.Print("AttackDummy killed.");

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
        _state = DummyState.Idle;
        _stateTimer = AttackInterval;
        _mesh.Scale = _baseScale;
        _material.AlbedoColor = new Color(0.6f, 0.15f, 0.15f);
        _warningIndicator.Visible = false;
        _attackFlash.LightEnergy = 0f;
        _knockbackVelocity = Vector3.Zero;
        _hurtbox.SetDeferred("monitorable", true);
        _attackHitbox.Deactivate();

        GD.Print("AttackDummy respawned.");
    }
}
