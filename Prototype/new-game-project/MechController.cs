using Godot;
using System;

public partial class MechController : Node3D
{
    public override void _UnhandledInput(InputEvent @event)
    {
        // Check for right-click mouse press
        if (@event is InputEventMouseButton mouseEvent && 
            mouseEvent.ButtonIndex == MouseButton.Right && 
            mouseEvent.Pressed)
        {
            ParryAllArms();
        }
    }

    private void ParryAllArms()
    {
        // Calls the 'PlayParry' method on every node assigned to the "spider_arms" group
        GetTree().CallGroup("spider_arms", nameof(SpiderArm.PlayParry));
    }
}