using Godot;
using System;

public partial class SpiderArm : Node3D
{
    private AnimationPlayer _animPlayer;

    public override void _Ready()
    {
        // Automatically add this instance to the "spider_arms" group
        AddToGroup("spider_arms");

        // Fetch the AnimationPlayer inside the GLB/Scene hierarchy
        _animPlayer = GetNode<AnimationPlayer>("AnimationPlayer");
    }

    public void PlayParry()
    {
        if (_animPlayer != null && _animPlayer.HasAnimation("Parry"))
        {
            _animPlayer.Play("Parry");
        }
    }
}