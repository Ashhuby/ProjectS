namespace Game.Debug;

using Godot;

/// <summary>
/// Category-gated debug logging. Replaces raw GD.Print calls scattered
/// through gameplay code so debug output can be toggled per-system
/// instead of all-or-nothing.
///
/// All categories default to OFF. Flip the ones you're actively
/// debugging — either by editing the defaults below for a session,
/// or by setting them at runtime (e.g. from a debug menu later).
///
/// Usage:
///   GameLog.Combat($"Combo window open");
///   GameLog.AI($"[{_owner.Name}] Player detected — chasing");
///   GameLog.VFX($"Hit impact spawned at {position}");
/// </summary>
public static class GameLog
{
    // ── Category toggles ─────────────────────────────────────────────
    // Flip individual flags to true while working on that system.
    // Leave everything off for normal play/testing sessions.

    public static bool Combat { get; set; } = false;
    public static bool AI { get; set; } = false;
    public static bool VFX { get; set; } = false;
    public static bool Aggression { get; set; } = false;
    public static bool Vital { get; set; } = false;
    public static bool Camera { get; set; } = false;
    public static bool Movement { get; set; } = false;
    public static bool GameState { get; set; } = false;

    // ── Category loggers ─────────────────────────────────────────────

    public static void CombatLog(string message)
    {
        if (Combat) GD.Print($"[Combat] {message}");
    }

    public static void AILog(string message)
    {
        if (AI) GD.Print($"[AI] {message}");
    }

    public static void VFXLog(string message)
    {
        if (VFX) GD.Print($"[VFX] {message}");
    }

    public static void AggressionLog(string message)
    {
        if (Aggression) GD.Print($"[Aggression] {message}");
    }

    public static void VitalLog(string message)
    {
        if (Vital) GD.Print($"[Vital] {message}");
    }

    public static void CameraLog(string message)
    {
        if (Camera) GD.Print($"[Camera] {message}");
    }

    public static void MovementLog(string message)
    {
        if (Movement) GD.Print($"[Movement] {message}");
    }

    public static void GameStateLog(string message)
    {
        if (GameState) GD.Print($"[GameState] {message}");
    }

    /// <summary>
    /// Errors always print regardless of category toggles — these
    /// indicate a real problem, not routine trace output.
    /// </summary>
    public static void Error(string message)
    {
        GD.PrintErr($"[ERROR] {message}");
    }
}
