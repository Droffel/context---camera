using Godot;
using System;

public partial class Player : CharacterBody3D
{
	// Basic movement exports
	[Export] public float moveSpeed = 6.0f;
	[Export] public Node3D pivot;
	[Export] public float turnSpeed = 8.0f;

	// Falling exports
	[Export] public float gravity = 24.0f;
	[Export] public float maxFallAcc = 35.0f;

	// Movement mode shift exports
	[Export] public float accelRate = 4.0f;
	[Export] public float decelRate = 12.0f;
	[Export] public float easeAccelRate = 6.0f;

	// Jump exports
	[Export] public float jumpVelocity = 10.0f;
	[Export] public float jumpStartTime = 0.1f;
	[Export] public float coyoteTime = 0.12f;
	[Export] public float jumpBufferTime = 0.12f;
	[Export] public float hoverTime = 0.10f;
	[Export] public float threshold = 1.5f;
	[Export] public float threshGravMult = 0.35f;
	[Export] public float fallGravMult = 1.35f;

	// Animation / mesh exports
	[Export] public Node3D characterMesh;
	[Export] public float meshLerpScaleSpeed = 12.0f;
	[Export] public Vector3 idleScale = Vector3.One;
	[Export] public Vector3 squashScale = new Vector3(1.5f, 0.5f, 1.5f);
	[Export] public Vector3 stretchScale = new Vector3(0.5f, 1.5f, 0.5f);

	// Crouch exports
	[Export] public float crouchSpeed = 2.5f;
	[Export] public Vector3 crouchScale = new Vector3(1.2f, 0.7f, 1.2f);
	[Export] public CollisionShape3D collisionShape;
	[Export] public float standingColliderHeight = 1.0f;
	[Export] public float crouchingColliderHeight = 0.5f;
	[Export] public float colliderLerpSpeed = 10.0f;

    // Camera exports
    [Export] public Node3D cameraRig;

	private Vector3 desiredDir = Vector3.Zero;
	private Vector3 targetMesh = Vector3.One;

	private float coyoteTimer = 0.0f;
	private float jumpBufferTimer = 0.0f;
	private float jumpStartTimer = 0.0f;
	private float hoverTimer = 0.0f;
	private float airborneStretchTimer = 0.0f;

	private bool isPreparingJump = false;
	private bool isCrouching = false;

	private enum MovementShift
	{
		Direct = 1,
		Linear = 2,
		Ease = 3
	}

	private MovementShift currMove = MovementShift.Direct;

	public override void _Ready()
	{
        
		targetMesh = idleScale;

		if (characterMesh != null)
			characterMesh.Scale = idleScale;
	}

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;

        //input and state
		modeSwitch();
		handleInput();
		handleCrouchInput();
		handleJumpInput(dt);

		Vector3 velocity = Velocity;

		// Horizontal movement
		Vector3 horizontalVelocity = new Vector3(velocity.X, 0, velocity.Z);

		float currentMoveSpeed = isCrouching ? crouchSpeed : moveSpeed;
		Vector3 targetVelocity = desiredDir * currentMoveSpeed;

		switch (currMove)
		{   
            //instant movement
			case MovementShift.Direct:
				horizontalVelocity = targetVelocity;
				break;

            //gradual movement
			case MovementShift.Linear:
				if (desiredDir != Vector3.Zero)
				{
					horizontalVelocity = horizontalVelocity.MoveToward(targetVelocity, accelRate * dt);
				}
				else
				{
					horizontalVelocity = horizontalVelocity.MoveToward(Vector3.Zero, decelRate * dt);
				}
				break;

			case MovementShift.Ease:
				if (desiredDir != Vector3.Zero)
				{
					float t = 1.0f - Mathf.Exp(-easeAccelRate * dt);
					horizontalVelocity = horizontalVelocity.Lerp(targetVelocity, t);
				}
				else
				{
					horizontalVelocity = horizontalVelocity.MoveToward(Vector3.Zero, decelRate * dt);
				}
				break;
		}

		velocity.X = horizontalVelocity.X;
		velocity.Z = horizontalVelocity.Z;

		// Floor / coyote logic
		if (IsOnFloor())
		{
			coyoteTimer = coyoteTime;

			if (!isPreparingJump && velocity.Y < 0.0f)
				velocity.Y = 0.0f;
		}
		else
		{
			coyoteTimer -= dt;
		}

		// Jump startup
		if (isPreparingJump)
		{
			jumpStartTimer -= dt;
			targetMesh = squashScale;

			velocity.X = 0.0f;
			velocity.Z = 0.0f;

			if (jumpStartTimer <= 0.0f)
			{
				isPreparingJump = false;
				velocity.Y = jumpVelocity;
				hoverTimer = hoverTime;
				airborneStretchTimer = 0.12f;
				targetMesh = stretchScale;
			}
		}
		else
		{
			// Air logic
			if (!IsOnFloor())
			{
				float currentGravity = gravity;
				bool nearThresh = Mathf.Abs(velocity.Y) <= threshold;

				if (nearThresh && hoverTimer > 0.0f)
				{
					hoverTimer -= dt;
					velocity.Y = 0.0f;
				}
				else
				{
					if (nearThresh)
						currentGravity *= threshGravMult;
					else if (velocity.Y < 0.0f)
						currentGravity *= fallGravMult;

					velocity.Y -= currentGravity * dt;

					if (velocity.Y < -maxFallAcc)
						velocity.Y = -maxFallAcc;
				}
			}
		}

		if (airborneStretchTimer > 0.0f)
			airborneStretchTimer -= dt;

		Velocity = velocity;

		// Rotation
		if (desiredDir != Vector3.Zero && pivot != null)
		{
			float targetAngle = Mathf.Atan2(desiredDir.X, desiredDir.Z);
			Quaternion targetRotation = Quaternion.FromEuler(new Vector3(0, targetAngle, 0));
			pivot.Quaternion = pivot.Quaternion.Slerp(targetRotation, turnSpeed * dt);
		}

		MoveAndSlide();
		updateMeshScale(dt);
		updateCrouchCollider(dt);
	}

	private void handleInput()
	{
        //collect input from godot
		float inputXaxis = Input.GetAxis("move_left", "move_right");
		float inputZaxis = Input.GetAxis("move_forward", "move_back");
        inputZaxis *= -1.0f;


        Vector2 input = new Vector2(inputXaxis, inputZaxis);

        if(input == Vector2.Zero)
        {
            desiredDir = Vector3.Zero;
            return;
        }

        Vector3 camForward = -cameraRig.GlobalTransform.Basis.Z;
        Vector3 camRight = cameraRig.GlobalTransform.Basis.X;

        camForward.Y = 0;
        camRight.Y = 0;

        camForward = camForward.Normalized();
        camRight = camRight.Normalized();

		desiredDir = (camRight * input.X + camForward * input.Y).Normalized();
	}

	private void handleCrouchInput()
	{
		isCrouching = Input.IsActionPressed("crouch");
	}

	private void modeSwitch()
	{
		if (Input.IsActionJustPressed("mode_1"))
		{
			currMove = MovementShift.Direct;
		}
		else if (Input.IsActionJustPressed("mode_2"))
		{
			currMove = MovementShift.Linear;
		}
		else if (Input.IsActionJustPressed("mode_3"))
		{
			currMove = MovementShift.Ease;
		}
	}

	private void handleJumpInput(float dt)
	{
		if (Input.IsActionJustPressed("jump"))
		{
			jumpBufferTimer = jumpBufferTime;
		}
		else
		{
			jumpBufferTimer -= dt;
		}

		bool canStartJump = coyoteTimer > 0.0f && !isPreparingJump && !isCrouching;

		if (jumpBufferTimer > 0.0f && canStartJump)
		{
			jumpBufferTimer = 0.0f;
			coyoteTimer = 0.0f;

			isPreparingJump = true;
			jumpStartTimer = jumpStartTime;
			targetMesh = squashScale;
		}
	}

	private void updateMeshScale(float dt)
	{
		if (characterMesh == null)
			return;

		Vector3 desiredScale = idleScale;

		if (isPreparingJump)
		{
			desiredScale = squashScale;
		}
		else if (!IsOnFloor() && airborneStretchTimer > 0.0f)
		{
			desiredScale = stretchScale;
		}
		else if (isCrouching && IsOnFloor())
		{
			desiredScale = crouchScale;
		}
		else
		{
			desiredScale = idleScale;
			targetMesh = idleScale;
		}

		characterMesh.Scale = characterMesh.Scale.Lerp(desiredScale, meshLerpScaleSpeed * dt);
	}

	private void updateCrouchCollider(float dt)
	{
		if (collisionShape == null)
			return;

		if (collisionShape.Shape is not CapsuleShape3D capsule)
			return;

		float targetHeight = isCrouching ? crouchingColliderHeight : standingColliderHeight;

		capsule.Height = Mathf.Lerp(capsule.Height, targetHeight, colliderLerpSpeed * dt) + 0.15f;
	}
}


