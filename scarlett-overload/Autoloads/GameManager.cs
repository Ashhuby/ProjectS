namespace Game.Autoloads;

using Godot;

/// <summary>
/// Manages top-level game state. Register as an autoload in Project Settings.
/// Name: GameManager, Path: res://Autoloads/GameManager.cs
///
/// LOAD ORDER: This must load AFTER EventBus.
///
/// NOTE: You need to add a "pause" input action in Project Settings → Input Map.
///       Map it to Escape (keyboard) and Start button (gamepad).
/// </summary>
public partial class GameManager : Node
{
    public static GameManager Instance { get; private set; }

    public enum State { Playing, Paused, GameOver }

    public State CurrentState { get; private set; } = State.Playing;

    public override void _Ready()
    {
        Instance = this;
        // ProcessMode.Always ensures pause/unpause input works while tree is paused
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("pause"))
            TogglePause();
    }

    /// <summary>
    /// Toggle between Playing and Paused. No-op during GameOver.
    /// </summary>
    public void TogglePause()
    {
        if (CurrentState == State.GameOver) return;

        if (CurrentState == State.Playing)
        {
            GetTree().Paused = true;
            CurrentState = State.Paused;
            EventBus.Instance?.EmitGamePaused();
        }
        else if (CurrentState == State.Paused)
        {
            GetTree().Paused = false;
            CurrentState = State.Playing;
            EventBus.Instance?.EmitGameResumed();
        }
    }

    /// <summary>
    /// Called when the player dies. Does NOT pause — death animation
    /// and VFX should still play. The death screen UI subscribes to
    /// EventBus.EntityDied and shows itself.
    /// </summary>
    public void TriggerGameOver()
    {
        if (CurrentState == State.GameOver) return;
        CurrentState = State.GameOver;
        GD.Print("[GameManager] Game Over");
    }

    /// <summary>
    /// Reload the current scene. Resets all state.
    /// </summary>
    public void RestartLevel()
    {
        CurrentState = State.Playing;
        GetTree().Paused = false;
        GetTree().ReloadCurrentScene();
    }

    /// <summary>
    /// Load a specific scene by path.
    /// </summary>
    public void LoadScene(string scenePath)
    {
        CurrentState = State.Playing;
        GetTree().Paused = false;
        GetTree().ChangeSceneToFile(scenePath);
    }
}
