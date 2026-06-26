using Game.Characters;
using Game.Core.Data;
using Game.Core.Interfaces;
using Godot;

/// <summary>
/// Passive target dummy. Stands still, takes hits, flashes red,
/// dies, respawns. All health/knockback/gravity logic lives in CharacterBase.
/// </summary>
public partial class TestDummy : CharacterBase, ILockOnTarget
{
    private MeshInstance3D _mesh;
    private StandardMaterial3D _material;
    private Tween _flashTween;

    // ── ITargetable ───────────────────────────────────────────────────
    public Vector3 TargetPosition => GlobalPosition + new Vector3(0f, 1f, 0f);
    public bool IsValidTarget => !IsDead;

    // ── ILockOnTarget ─────────────────────────────────────────────────
    public string TargetName => "Test Dummy";

    // ══════════════════════════════════════════════════════════════════
    //  CHARACTERBASE OVERRIDES
    // ══════════════════════════════════════════════════════════════════

    protected override void Initialize()
    {
        // Fallback stats when no .tres is assigned in the inspector.
        // Replace with a proper CharacterStats resource once you've
        // created res://Resources/Characters/TestDummyStats.tres
        Stats ??= new CharacterStats
        {
            MaxHealth = 50,
            KnockbackDecay = 10f,
            RespawnDelay = 3f
        };

        _mesh = GetNode<MeshInstance3D>("MeshInstance3D");
        _material = new StandardMaterial3D { AlbedoColor = Colors.White };
        _mesh.MaterialOverride = _material;
    }

    protected override void OnDamageTaken(DamageData data)
    {
        FlashRed();
    }

    protected override void OnDeath()
    {
        SetHurtboxActive(false);
        GD.Print($"{Name} killed.");

        var deathTween = CreateTween();
        deathTween.SetParallel(true);
        deathTween.TweenProperty(_mesh, "scale", Vector3.Zero, 0.4f)
            .SetEase(Tween.EaseType.In)
            .SetTrans(Tween.TransitionType.Back);
        deathTween.TweenProperty(_material, "albedo_color",
            new Color(0.3f, 0.3f, 0.3f), 0.4f);
        deathTween.SetParallel(false);

        float delay = Stats?.RespawnDelay ?? 3f;
        if (delay > 0f)
        {
            deathTween.TweenInterval(delay);
            deathTween.TweenCallback(Callable.From(Respawn));
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  PRIVATE
    // ══════════════════════════════════════════════════════════════════

    private void Respawn()
    {
        ResetHealth();
        _mesh.Scale = Vector3.One;
        _material.AlbedoColor = Colors.White;
        SetHurtboxActive(true);
        GD.Print($"{Name} respawned.");
    }

    private void FlashRed()
    {
        _flashTween?.Kill();
        _flashTween = CreateTween();
        _material.AlbedoColor = new Color(1f, 0.2f, 0.2f);
        _flashTween.TweenProperty(_material, "albedo_color", Colors.White, 0.3f)
            .SetEase(Tween.EaseType.Out);
    }
}
