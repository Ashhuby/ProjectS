using Game.Autoloads;
using Game.Core.Data;
using Godot;
using System.Collections.Generic;

/// <summary>
/// Fiora-style vital system. Composed into EnemyBase.
///
/// Flow:
///   1. Player parries → EnemyBase calls Activate(playerPosition)
///   2. Primary vital spawns at a cardinal direction around the enemy
///   3. Player attacks from the correct angle → primary pops, mini vital spawns
///   4. Player fires vital thrust from correct angle → mini pops, speed boost
///
/// Any attack from the wrong direction (or wrong attack type for mini)
/// immediately fails the entire sequence. Stun expiry also fails it.
///
/// Cardinal directions are computed in world space using the enemy's
/// locked rotation at the moment of parry.
/// </summary>
public class VitalSystem
{
    public enum VitalState
    {
        Inactive,       // No vital sequence active
        PrimaryActive,  // Primary vital showing, waiting for correct-angle hit
        MiniActive,     // Mini vital showing, vital thrust loaded
        Complete,       // Both vitals hit — sequence succeeded
        Failed          // Missed, wrong angle, wrong attack, or stun expired
    }

    public VitalState State { get; private set; } = VitalState.Inactive;

    /// <summary>World-space unit vector pointing toward the active vital.</summary>
    public Vector3 ActiveVitalDirection { get; private set; }

    /// <summary>World position where the vital indicator should appear.</summary>
    public Vector3 ActiveVitalWorldPosition { get; private set; }

    /// <summary>True when a vital is active and waiting for a hit.</summary>
    public bool IsActive => State == VitalState.PrimaryActive
                         || State == VitalState.MiniActive;

    private readonly EnemyBase _owner;
    private readonly EnemyConfig _config;

    // Cardinal directions in world space, computed at activation time
    // from the enemy's locked rotation
    private Vector3[] _worldCardinals;
    private int _primaryIndex = -1;

    // ══════════════════════════════════════════════════════════════════
    //  CONSTRUCTION
    // ══════════════════════════════════════════════════════════════════

    public VitalSystem(EnemyBase owner, EnemyConfig config)
    {
        _owner = owner;
        _config = config ?? new EnemyConfig();
    }

    // ══════════════════════════════════════════════════════════════════
    //  ACTIVATION — called by EnemyBase when parry stun begins
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Start the vital sequence. Picks a cardinal direction around
    /// the enemy (relative to their locked facing direction) and
    /// reveals the primary vital indicator.
    /// </summary>
    /// <param name="playerWorldPos">
    /// Player's position at the moment of parry. Used to exclude
    /// the opposite-side cardinal when AllowOppositeVitals is false.
    /// </param>
    public void Activate(Vector3 playerWorldPos)
    {
        if (State != VitalState.Inactive) return;

        ComputeWorldCardinals();

        int chosen = PickDirection(playerWorldPos, excludeIndex: -1);
        if (chosen < 0)
        {
            GD.PrintErr($"[VitalSystem] No valid direction — aborting");
            return;
        }

        _primaryIndex = chosen;
        SetActiveVital(chosen);
        State = VitalState.PrimaryActive;

        EventBus.Instance?.EmitVitalRevealed(_owner, ActiveVitalDirection);
        GD.Print($"[VitalSystem] Primary vital revealed — direction: {DirectionLabel(chosen)}");
    }

    // ══════════════════════════════════════════════════════════════════
    //  HIT DETECTION — called by EnemyBase.OnDamageTaken
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Check if the incoming hit pops the active vital.
    /// Call this whenever the enemy takes damage while a vital is active.
    ///
    /// For primary: any attack type from correct angle → pop.
    /// For mini: only Thrust type from correct angle → pop.
    /// Any other hit → fail.
    /// </summary>
    public void OnEnemyHit(DamageData data)
    {
        if (!IsActive || data.Source == null) return;

        Vector3 playerPos = data.Source.GlobalPosition;
        bool fromCorrectAngle = CheckAngle(playerPos);

        switch (State)
        {
            case VitalState.PrimaryActive:
                if (fromCorrectAngle)
                    PopPrimary(data, playerPos);
                else
                    Fail("wrong angle on primary");
                break;

            case VitalState.MiniActive:
                if (data.Type == AttackType.Thrust && fromCorrectAngle)
                    PopMini(data);
                else
                    Fail(data.Type != AttackType.Thrust
                        ? "wrong attack type on mini (need Thrust)"
                        : "wrong angle on mini");
                break;
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  STUN EXPIRY — called by EnemyBase when AI leaves Stunned
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Called when the parry stun timer runs out. If the vital
    /// sequence is still active, it fails.
    /// </summary>
    public void OnStunExpired()
    {
        if (!IsActive) return;
        Fail("stun expired");
    }

    // ══════════════════════════════════════════════════════════════════
    //  RESET — called on death/respawn
    // ══════════════════════════════════════════════════════════════════

    public void Reset()
    {
        if (IsActive)
        {
            EventBus.Instance?.EmitVitalFailed(_owner);
            if (State == VitalState.MiniActive)
                EventBus.Instance?.EmitVitalThrustUnloaded();
        }

        State = VitalState.Inactive;
        ActiveVitalDirection = Vector3.Zero;
        ActiveVitalWorldPosition = Vector3.Zero;
        _primaryIndex = -1;
    }

    // ══════════════════════════════════════════════════════════════════
    //  INTERNAL — POP / FAIL
    // ══════════════════════════════════════════════════════════════════

    private void PopPrimary(DamageData data, Vector3 playerWorldPos)
    {
        // ── Burst damage ──────────────────────────────────────────
        float multiplier = _config.VitalBurstMultiplier;
        int bonusDamage = Mathf.RoundToInt(data.Amount * (multiplier - 1f));
        if (bonusDamage > 0)
            _owner.ApplyVitalDamage(bonusDamage);

        GD.Print($"[VitalSystem] PRIMARY POPPED — burst damage: {bonusDamage}");
        EventBus.Instance?.EmitVitalPopped(_owner, isPrimary: true);

        // ── Extend stun ───────────────────────────────────────────
        _owner.ExtendStun(_config.VitalStunExtension);

        // ── Pick mini vital direction ─────────────────────────────
        int miniIndex = PickDirection(playerWorldPos, excludeIndex: _primaryIndex);
        if (miniIndex < 0)
        {
            // Edge case: no valid direction left — complete early
            GD.Print("[VitalSystem] No valid mini direction — completing");
            State = VitalState.Complete;
            EventBus.Instance?.EmitVitalSequenceComplete(_owner, hitBoth: false);
            return;
        }

        SetActiveVital(miniIndex);
        State = VitalState.MiniActive;

        // Load the vital thrust for the player
        EventBus.Instance?.EmitVitalThrustLoaded();
        EventBus.Instance?.EmitVitalRevealed(_owner, ActiveVitalDirection);
        GD.Print($"[VitalSystem] Mini vital revealed — direction: {DirectionLabel(miniIndex)}");
    }

    private void PopMini(DamageData data)
    {
        // ── Bonus damage ──────────────────────────────────────────
        float multiplier = _config.VitalBonusMultiplier;
        int bonusDamage = Mathf.RoundToInt(data.Amount * (multiplier - 1f));
        if (bonusDamage > 0)
            _owner.ApplyVitalDamage(bonusDamage);

        GD.Print($"[VitalSystem] MINI POPPED — bonus damage: {bonusDamage}");

        State = VitalState.Complete;
        ActiveVitalDirection = Vector3.Zero;
        ActiveVitalWorldPosition = Vector3.Zero;

        // Unload vital thrust (it was already consumed, but clean up)
        EventBus.Instance?.EmitVitalThrustUnloaded();
        EventBus.Instance?.EmitVitalPopped(_owner, isPrimary: false);
        EventBus.Instance?.EmitVitalSequenceComplete(_owner, hitBoth: true);

        // ── Speed boost ───────────────────────────────────────────
        EventBus.Instance?.EmitSpeedBoostApplied(
            _config.SpeedBoostMultiplier,
            _config.SpeedBoostDuration);

        GD.Print($"[VitalSystem] SEQUENCE COMPLETE — speed boost: " +
                 $"{_config.SpeedBoostMultiplier}x for {_config.SpeedBoostDuration}s");
    }

    private void Fail(string reason)
    {
        GD.Print($"[VitalSystem] FAILED — {reason}");

        bool wasMini = State == VitalState.MiniActive;
        State = VitalState.Failed;
        ActiveVitalDirection = Vector3.Zero;
        ActiveVitalWorldPosition = Vector3.Zero;
        _primaryIndex = -1;

        EventBus.Instance?.EmitVitalFailed(_owner);

        if (wasMini)
            EventBus.Instance?.EmitVitalThrustUnloaded();

        EventBus.Instance?.EmitVitalSequenceComplete(_owner, hitBoth: false);

        // Reset to Inactive so the next parry can start a new sequence
        State = VitalState.Inactive;
    }

    // ══════════════════════════════════════════════════════════════════
    //  DIRECTION MATH
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Compute the four cardinal directions in world space using
    /// the enemy's current facing direction (locked during stun).
    /// </summary>
    private void ComputeWorldCardinals()
    {
        // Enemy's world-space forward (their -Z axis, projected to XZ)
        Vector3 forward = -_owner.GlobalTransform.Basis.Z;
        forward.Y = 0f;
        forward = forward.Normalized();

        Vector3 right = _owner.GlobalTransform.Basis.X;
        right.Y = 0f;
        right = right.Normalized();

        _worldCardinals = new Vector3[]
        {
            forward,    // 0: front
            right,      // 1: right
            -forward,   // 2: back
            -right      // 3: left
        };
    }

    /// <summary>
    /// Pick a random cardinal direction, optionally excluding one index
    /// and optionally excluding the direction opposite to the player.
    /// </summary>
    private int PickDirection(Vector3 playerWorldPos, int excludeIndex)
    {
        Vector3 enemyToPlayer = playerWorldPos - _owner.GlobalPosition;
        enemyToPlayer.Y = 0f;
        if (enemyToPlayer.LengthSquared() > 0.001f)
            enemyToPlayer = enemyToPlayer.Normalized();

        var validIndices = new List<int>();
        for (int i = 0; i < 4; i++)
        {
            if (i == excludeIndex) continue;
            validIndices.Add(i);
        }

        // Exclude the direction most opposite to the player if configured
        if (!_config.AllowOppositeVitals && validIndices.Count > 1)
        {
            float minDot = float.MaxValue;
            int excludeOpposite = -1;

            foreach (int i in validIndices)
            {
                float dot = _worldCardinals[i].Dot(enemyToPlayer);
                if (dot < minDot)
                {
                    minDot = dot;
                    excludeOpposite = i;
                }
            }

            if (excludeOpposite >= 0)
                validIndices.Remove(excludeOpposite);
        }

        if (validIndices.Count == 0) return -1;

        return validIndices[GD.RandRange(0, validIndices.Count - 1)];
    }

    /// <summary>
    /// Check if the player's position relative to the enemy is within
    /// the angle tolerance of the active vital direction.
    /// </summary>
    private bool CheckAngle(Vector3 playerWorldPos)
    {
        Vector3 enemyToPlayer = playerWorldPos - _owner.GlobalPosition;
        enemyToPlayer.Y = 0f;

        if (enemyToPlayer.LengthSquared() < 0.001f)
            return false;

        enemyToPlayer = enemyToPlayer.Normalized();
        float dot = enemyToPlayer.Dot(ActiveVitalDirection);
        float angleDeg = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(dot, -1f, 1f)));

        return angleDeg <= _config.VitalAngleTolerance;
    }

    /// <summary>
    /// Set the active vital direction and world position from a cardinal index.
    /// </summary>
    private void SetActiveVital(int index)
    {
        ActiveVitalDirection = _worldCardinals[index];
        float radius = _config.VitalSpawnRadius;
        ActiveVitalWorldPosition = _owner.GlobalPosition
            + ActiveVitalDirection * radius
            + new Vector3(0f, 1f, 0f); // center height offset
    }

    private static string DirectionLabel(int index)
    {
        return index switch
        {
            0 => "FRONT",
            1 => "RIGHT",
            2 => "BACK",
            3 => "LEFT",
            _ => "UNKNOWN"
        };
    }
}
