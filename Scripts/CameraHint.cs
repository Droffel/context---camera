using Godot;
using System;

public partial class CameraHint : Area3D
{
    [Export] public Node3D cameraTarget;
    [Export] public float targetFov = 70.0f;

    private void OnBodyEntered(Node body)
    {
        GD.Print(body.Name);
        if(body is CharacterBody3D)
        {
            var rig = GetNode<CameraRig>("/root/Main/CameraRig");
            rig.SetHint(this);
        }
        GD.Print("Entered");
    }

    private void OnBodyExited(Node body)
    {
        if (body is CharacterBody3D)
        {
            var rig = GetNode<CameraRig>("/root/Main/CameraRig");
            rig.ClearHint(this);
        }
        GD.Print("Exited");
    }
}
