using Godot;
using System;





public partial class CameraRig : Node3D
{
	[Export] public Node3D player;
	[Export] public Node3D pitchPivot;

	[Export] public Node3D playerPivot;

	[Export] public float followSpeed = 5.0f;
	[Export] public float rotateSpeed = 2.5f;

	[Export] public float minPitchDeg = -30.0f;
	[Export] public float maxPitchDeg = 60.0f;

	[Export] public float autoAlignSpeed = 3.0f;
	[Export] public float autoAlignDelay = 0.6f;
	[Export] public float movementThresh = 0.1f;
	[Export] public float autoAlignAngleThresholdDeg = 5.0f;

	//raycasting exports
	[Export] public Camera3D camera3D;
	[Export] public float cameraDistance = 6.0f;
	[Export] public float cameraHeight = 1.5f;
	[Export] public float collisionRadius = 0.2f;
	[Export] public uint cameraCollisionMask = 1;
	[Export] public float cameraReturnSpeed = 8.0f;

	//whisker exports
	[Export] public float whiskerOffset = 2.0f;
	[Export] public float whiskerHeightOffset = 0.6f;
	
	private float autoAlignTimer = 0.0f;
	private float autoAlignTargetYaw = 0.0f;

	private float yaw = 0.0f;
	private float pitch = 0.0f;

	private CameraHint currentHint = null;
	[Export] public float hintBlendSpeed = 3.0f;

	public void SetHint(CameraHint hint)
	{
		currentHint = hint;
	}

	public void ClearHint(CameraHint hint)
	{
		if(currentHint == hint)
			currentHint = null;
	}

    public override void _Ready()
    {
        Vector3 startRot = Rotation;
		yaw = startRot.Y;

		if(pitchPivot != null)
		{
			pitch = pitchPivot.Rotation.X;
		}
    }

    
	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;

		if (player == null || pitchPivot == null)
			return;

		float t = 1.0f - Mathf.Exp(-followSpeed * dt);
		GlobalPosition = GlobalPosition.Lerp(player.GlobalPosition, t);

		float camX = Input.GetAxis("camera_left", "camera_right");
		float camY = Input.GetAxis("camera_up", "camera_down");

		yaw -= camX * rotateSpeed * dt;
		pitch -= camY * rotateSpeed * dt;

		// Delay efter att spelaren själv styrt kameran
		if (Mathf.Abs(camX) > 0.01f || Mathf.Abs(camY) > 0.01f)
		{
			autoAlignTimer = autoAlignDelay;
		}
		else
		{
			autoAlignTimer -= dt;
			if (autoAlignTimer < 0.0f)
				autoAlignTimer = 0.0f;
		}

		if (autoAlignTimer <= 0.0f && playerPivot != null)
		{
			float targetYaw = playerPivot.GlobalRotation.Y + Mathf.Pi;

			// Om kameran lägger sig framför istället för bakom:
			// targetYaw += Mathf.Pi;

			float angleDiff = Mathf.AngleDifference(yaw, targetYaw);
			float angleThreshold = Mathf.DegToRad(autoAlignAngleThresholdDeg);

			if (Mathf.Abs(angleDiff) > angleThreshold)
			{
				yaw = Mathf.LerpAngle(yaw, targetYaw, autoAlignSpeed * dt);
			}
		}

		float minPitchRad = Mathf.DegToRad(minPitchDeg);
		float maxPitchRad = Mathf.DegToRad(maxPitchDeg);
		pitch = Mathf.Clamp(pitch, minPitchRad, maxPitchRad);

		if(currentHint != null && currentHint.cameraTarget != null)
		{
			float hintT = 1.0f - Mathf.Exp(-hintBlendSpeed * dt);

			GlobalPosition = GlobalPosition.Lerp(currentHint.cameraTarget.GlobalPosition, hintT);

			Vector3 targetRot = currentHint.cameraTarget.GlobalRotation;
			Rotation = Rotation.Lerp(targetRot, hintT);

			if(camera3D != null)
			{
				camera3D.Fov = Mathf.Lerp(camera3D.Fov, currentHint.targetFov, hintT);
			}

			return;
		}

		Rotation = new Vector3(0, yaw, 0);
		pitchPivot.Rotation = new Vector3(pitch, 0, 0);

		UpdateCameraCollision(delta);
	}

	private void UpdateCameraCollision(double delta)
	{
		float dt = (float)delta;

		Vector3 origin = pitchPivot.GlobalPosition;

		Vector3 baseLocalPos = new Vector3(0, cameraHeight, cameraDistance);

		Vector3 leftLocalPos = baseLocalPos + new Vector3(-whiskerOffset, whiskerHeightOffset, 0);
		Vector3 rightLocalPos = baseLocalPos + new Vector3(whiskerOffset, whiskerHeightOffset, 0);

		Vector3 baseGlobalPos = pitchPivot.ToGlobal(baseLocalPos);
		Vector3 leftGlobalPos = pitchPivot.ToGlobal(leftLocalPos);
		Vector3 rightGlobalPos = pitchPivot.ToGlobal(rightLocalPos);

		Vector3 targetLocalPos = baseLocalPos;

		bool baseBlocked = isCameraPathBlocked(origin, baseGlobalPos, out Vector3 baseHit);
		bool leftBlocked = isCameraPathBlocked(origin, leftGlobalPos, out Vector3 _);
		bool rightBlocked = isCameraPathBlocked(origin, rightGlobalPos, out Vector3 _);

		if(!baseBlocked)
		{
			targetLocalPos = baseLocalPos;
		}

		else if(!leftBlocked)
		{
			targetLocalPos = leftLocalPos;
		}
		else if(!rightBlocked)
		{
			targetLocalPos = rightLocalPos;
		}
		else
		{
			Vector3 dir = (baseHit - origin).Normalized();
			Vector3 safeGlobalPos = baseHit - dir*collisionRadius;
			targetLocalPos = pitchPivot.ToLocal(safeGlobalPos);
		}

		float t = 1.0f - Mathf.Exp(-cameraReturnSpeed * dt);
		camera3D.Position = camera3D.Position.Lerp(targetLocalPos, t);
	}

	private bool isCameraPathBlocked(Vector3 from, Vector3 to, out Vector3 hitPosition)
	{
		hitPosition = Vector3.Zero;

		var spaceState = GetWorld3D().DirectSpaceState;
		var query = PhysicsRayQueryParameters3D.Create(from, to);
		query.CollisionMask = cameraCollisionMask;

		if(player is CollisionObject3D body)
		{
			query.Exclude = new Godot.Collections.Array<Rid>{body.GetRid()};
		}

		var result = spaceState.IntersectRay(query);

		if(result.Count > 0)
		{
			hitPosition = (Vector3)result["position"];
			return true;
		}
		return false;
	}

}
