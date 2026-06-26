namespace Game.Autoloads;

using Godot;
using System;

/// <summary>
/// Global event bus. Register as an autoload in Project Settings → Autoload.
/// Name: EventBus, Path: res://Autoloads/EventBus.cs
///
/// LOAD ORDER: This must load BEFORE GameManager.
///
/// Usage — firing:
///   EventBus.Instance.EmitHitLanded(data);
///
/// Usage — subscribing:
///   EventBus.Instance.HitLanded += OnHitLanded;
///
/// Usage — unsubscribing (do this in _ExitTree):
///   EventBus.Instance.HitLanded -= OnHitLanded;
///
/// If EventBus isn't loaded (e.g. running a test scene without autoloads),
/// Instance is null. Always use the null-conditional: EventBus.Instance?.Emit...
/// </summary>
public partial class EventBus : Node
{
    public static EventBus Instance { get; private set; }

    // ── Combat ────────────────────────────────────────────────────────

    /// <summary>
    /// A hitbox connected with a hurtbox. Fired by the attacker's hitbox.
    /// Subscribers: VFX (spawn sparks), Audio (play impact SFX),
    /// Camera (screen shake), UI (hit marker).
    /// </summary>
    public event Action<DamageData> HitLanded;

    /// <summary>
    /// Damage was applied to a character (after parry/armor checks).
    /// Subscribers: UI (update health bar), Analytics.
    /// </summary>
    public event Action<DamageData> DamageTaken;

    /// <summary>
    /// A parry successfully deflected an attack.
    /// Subscribers: VFX (parry sparks), Audio (parry clang),
    /// Camera (shake), the attacker (enter stunned).
    /// </summary>
    public event Action<DamageData> ParrySucceeded;

    /// <summary>
    /// An entity's health reached zero.
    /// Subscribers: VFX (death burst), Audio (death SFX),
    /// GameManager (check win/lose), UI (death screen if player).
    /// </summary>
    public event Action<Node3D> EntityDied;

    /// <summary>
    /// An entity respawned after death.
    /// Subscribers: UI (reset health bar), AI (reset aggro).
    /// </summary>
    public event Action<Node3D> EntityRespawned;

    // ── Game State ────────────────────────────────────────────────────

    public event Action GamePaused;
    public event Action GameResumed;

    // ── Lifecycle ─────────────────────────────────────────────────────

    public override void _Ready()
    {
        Instance = this;
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    // ── Emit Methods ──────────────────────────────────────────────────
    // C# events can only be invoked by the declaring class.
    // These methods are the public API for firing events.

    public void EmitHitLanded(DamageData data) => HitLanded?.Invoke(data);
    public void EmitDamageTaken(DamageData data) => DamageTaken?.Invoke(data);
    public void EmitParrySucceeded(DamageData data) => ParrySucceeded?.Invoke(data);
    public void EmitEntityDied(Node3D entity) => EntityDied?.Invoke(entity);
    public void EmitEntityRespawned(Node3D entity) => EntityRespawned?.Invoke(entity);
    public void EmitGamePaused() => GamePaused?.Invoke();
    public void EmitGameResumed() => GameResumed?.Invoke();
}
