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

    // ── Vital System ──────────────────────────────────────────────────

    /// <summary>
    /// A vital indicator appeared on an enemy after a successful parry.
    /// Args: the enemy node, the vital's world-space direction vector.
    /// Subscribers: HUD (compass indicator), Audio (reveal SFX).
    /// </summary>
    public event Action<Node3D, Vector3> VitalRevealed;

    /// <summary>
    /// The player popped a vital by attacking from the correct direction.
    /// Args: the enemy node, true if primary vital / false if mini vital.
    /// Subscribers: VFX (burst effect), Audio (pop SFX), Camera (shake).
    /// </summary>
    public event Action<Node3D, bool> VitalPopped;

    /// <summary>
    /// The vital sequence failed — player attacked from the wrong
    /// direction, or the stun timer expired before completion.
    /// Subscribers: VFX (fizzle), Audio (fail SFX), HUD (clear compass).
    /// </summary>
    public event Action<Node3D> VitalFailed;

    /// <summary>
    /// The vital thrust attack is now available. Fired when the primary
    /// vital pops and the mini vital becomes active.
    /// Subscribers: PlayerCombat (enable vital thrust input).
    /// </summary>
    public event Action VitalThrustLoaded;

    /// <summary>
    /// The vital thrust is no longer available — either it was used,
    /// the mini vital failed, or the stun expired.
    /// Subscribers: PlayerCombat (disable vital thrust, reset combo).
    /// </summary>
    public event Action VitalThrustUnloaded;

    /// <summary>
    /// The full vital sequence completed (both vitals resolved).
    /// Args: the enemy node, true if the player hit both vitals.
    /// Subscribers: Analytics, Achievement tracking.
    /// </summary>
    public event Action<Node3D, bool> VitalSequenceComplete;

    // ── Dash ──────────────────────────────────────────────────────────

    /// <summary>
    /// Player started a dash. Subscribers: VFX (dash trail),
    /// Audio (dash woosh).
    /// </summary>
    public event Action DashStarted;

    /// <summary>
    /// Player dash completed. Subscribers: VFX (stop trail).
    /// </summary>
    public event Action DashEnded;

    // ── Aggression Management ─────────────────────────────────────────

    /// <summary>
    /// An enemy was granted the attack token — only this enemy
    /// may transition from Engaging to Telegraphing.
    /// Subscribers: EnemyAI (check if it's this enemy).
    /// </summary>
    public event Action<Node3D> AttackTokenGranted;

    /// <summary>
    /// The attack token was released. Another enemy may now request it.
    /// Subscribers: AggressionManager internal, debug UI.
    /// </summary>
    public event Action<Node3D> AttackTokenReleased;

    // ── Speed Boost ───────────────────────────────────────────────────

    /// <summary>
    /// A speed boost was applied to the player (from mini vital pop).
    /// Args: multiplier, duration in seconds.
    /// Subscribers: PlayerMovement, VFX (speed lines).
    /// </summary>
    public event Action<float, float> SpeedBoostApplied;

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

    // Combat
    public void EmitHitLanded(DamageData data) => HitLanded?.Invoke(data);
    public void EmitDamageTaken(DamageData data) => DamageTaken?.Invoke(data);
    public void EmitParrySucceeded(DamageData data) => ParrySucceeded?.Invoke(data);
    public void EmitEntityDied(Node3D entity) => EntityDied?.Invoke(entity);
    public void EmitEntityRespawned(Node3D entity) => EntityRespawned?.Invoke(entity);

    // Vital System
    public void EmitVitalRevealed(Node3D enemy, Vector3 direction) => VitalRevealed?.Invoke(enemy, direction);
    public void EmitVitalPopped(Node3D enemy, bool isPrimary) => VitalPopped?.Invoke(enemy, isPrimary);
    public void EmitVitalFailed(Node3D enemy) => VitalFailed?.Invoke(enemy);
    public void EmitVitalThrustLoaded() => VitalThrustLoaded?.Invoke();
    public void EmitVitalThrustUnloaded() => VitalThrustUnloaded?.Invoke();
    public void EmitVitalSequenceComplete(Node3D enemy, bool hitBoth) => VitalSequenceComplete?.Invoke(enemy, hitBoth);

    // Dash
    public void EmitDashStarted() => DashStarted?.Invoke();
    public void EmitDashEnded() => DashEnded?.Invoke();

    // Aggression
    public void EmitAttackTokenGranted(Node3D enemy) => AttackTokenGranted?.Invoke(enemy);
    public void EmitAttackTokenReleased(Node3D enemy) => AttackTokenReleased?.Invoke(enemy);

    // Speed Boost
    public void EmitSpeedBoostApplied(float multiplier, float duration) => SpeedBoostApplied?.Invoke(multiplier, duration);

    // Game State
    public void EmitGamePaused() => GamePaused?.Invoke();
    public void EmitGameResumed() => GameResumed?.Invoke();
}
