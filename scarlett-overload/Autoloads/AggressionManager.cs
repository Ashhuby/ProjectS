namespace Game.Autoloads;

using Godot;
using System.Collections.Generic;

/// <summary>
/// Controls enemy attack turns. Register as an autoload:
///   Project Settings → Autoload → Name: AggressionManager
///   Path: res://Autoloads/AggressionManager.cs
///
/// One attack token. Only the token holder may begin an attack.
/// Everyone else circles the player at engagement range.
///
/// After the holder finishes (Recovery → Engaging), the token
/// enters a short cooldown before another enemy can claim it.
/// Parrying force-releases the token and adds a lockout so the
/// player has breathing room for the vital sequence.
/// </summary>
public partial class AggressionManager : Node
{
    public static AggressionManager Instance { get; private set; }

    // ── Config ────────────────────────────────────────────────────────

    /// <summary>Seconds after token release before it can be claimed again.</summary>
    [Export] public float ReassignDelay { get; set; } = 0.4f;

    /// <summary>Extra lockout after a parry — protects the vital window.</summary>
    [Export] public float ParryLockout { get; set; } = 1.5f;

    // ── State ─────────────────────────────────────────────────────────

    private readonly List<Node3D> _combatants = new();
    private Node3D _tokenHolder;
    private float _cooldownTimer;

    /// <summary>Who currently holds the attack token. Null = available.</summary>
    public Node3D TokenHolder => _tokenHolder;

    // ══════════════════════════════════════════════════════════════════
    //  LIFECYCLE
    // ══════════════════════════════════════════════════════════════════

    public override void _Ready()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;

        if (EventBus.Instance != null)
            EventBus.Instance.ParrySucceeded += OnParrySucceeded;
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;

        if (EventBus.Instance != null)
            EventBus.Instance.ParrySucceeded -= OnParrySucceeded;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_cooldownTimer > 0f)
            _cooldownTimer -= (float)delta;
    }

    // ══════════════════════════════════════════════════════════════════
    //  REGISTRATION
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Add an enemy to the combatant pool. Call when the enemy
    /// enters combat range (EnemyBase.Initialize or on detection).
    /// </summary>
    public void Register(Node3D enemy)
    {
        if (!_combatants.Contains(enemy))
        {
            _combatants.Add(enemy);
            GD.Print($"[Aggression] Registered {enemy.Name} ({_combatants.Count} combatants)");
        }
    }

    /// <summary>
    /// Remove an enemy from the pool. Call on death, despawn,
    /// or when leaving combat range.
    /// </summary>
    public void Unregister(Node3D enemy)
    {
        _combatants.Remove(enemy);

        if (_tokenHolder == enemy)
        {
            _tokenHolder = null;
            _cooldownTimer = ReassignDelay;
        }

        GD.Print($"[Aggression] Unregistered {enemy.Name} ({_combatants.Count} combatants)");
    }

    // ══════════════════════════════════════════════════════════════════
    //  TOKEN MANAGEMENT
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Request the attack token. Returns true if granted.
    /// Only one enemy can hold it at a time. Denied during cooldown.
    /// </summary>
    public bool RequestToken(Node3D requester)
    {
        // Token already held
        if (_tokenHolder != null)
            return _tokenHolder == requester; // already yours = ok

        // Cooldown active (post-release delay or parry lockout)
        if (_cooldownTimer > 0f)
            return false;

        // Grant
        _tokenHolder = requester;
        EventBus.Instance?.EmitAttackTokenGranted(requester);
        GD.Print($"[Aggression] Token granted to {requester.Name}");
        return true;
    }

    /// <summary>
    /// Release the attack token. Starts the reassign delay.
    /// Call when the token holder finishes their attack sequence.
    /// </summary>
    public void ReleaseToken(Node3D holder)
    {
        if (_tokenHolder != holder) return;

        GD.Print($"[Aggression] Token released by {holder.Name}");
        _tokenHolder = null;
        _cooldownTimer = ReassignDelay;

        EventBus.Instance?.EmitAttackTokenReleased(holder);
    }

    /// <summary>
    /// Force-release the token and apply parry lockout.
    /// Called when the token holder gets parried.
    /// </summary>
    public void ForceReleaseWithLockout(Node3D holder)
    {
        if (_tokenHolder != holder) return;

        GD.Print($"[Aggression] Token FORCE-RELEASED (parry lockout {ParryLockout}s)");
        _tokenHolder = null;
        _cooldownTimer = ParryLockout;

        EventBus.Instance?.EmitAttackTokenReleased(holder);
    }

    /// <summary>Check if a specific enemy currently holds the token.</summary>
    public bool HoldsToken(Node3D enemy) => _tokenHolder == enemy;

    // ══════════════════════════════════════════════════════════════════
    //  EVENT HANDLERS
    // ══════════════════════════════════════════════════════════════════

    private void OnParrySucceeded(DamageData data)
    {
        // The parried enemy's token is released by EnemyBase.OnPlayerParried
        // via ForceReleaseWithLockout. This handler is a safety net.
        if (_tokenHolder != null && data.Source == _tokenHolder)
            ForceReleaseWithLockout(_tokenHolder);
    }
}
