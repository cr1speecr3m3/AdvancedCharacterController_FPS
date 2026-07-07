using UnityEngine;
using KinematicCharacterController;
using UnityEditor.Build;
using TMPro;
using System.Threading.Tasks;

public enum CrouchInput
{
    None, Toggle
}

public enum PlayerStance
{
    Standing, Crouching, Sliding
}

public struct PlayerState
{
    public bool Grounded;
    public PlayerStance Stance;
    public Vector3 Velocity;
    public Vector3 Acceleration;
}

public struct CharacterInput
{
    public Quaternion Rotation;
    public Vector2 Move;
    public bool Jump;
    public bool SustainedJump;
    public CrouchInput Crouch;
}

public class PlayerCharacter : MonoBehaviour, ICharacterController
{
    [SerializeField] private KinematicCharacterMotor motor;
    [SerializeField] private Transform cameraTarget;
    [SerializeField] private Transform root;
    [Space]
    [SerializeField] private float walkSpeed = 20f;
    [SerializeField] private float crouchSpeed = 7f;
    [SerializeField] private float walkResponseTime = 25f;
    [SerializeField] private float crouchResponseTime = 20f;
    [Space]
    [SerializeField] private float airSpeed = 15f;
    [SerializeField] private float airAcceleration = 70f;
    [Space]
    [SerializeField] private float jumpSpeed = 20f;
    [SerializeField] private float coyoteTime = 0.2f;
    [Range(0f, 1f)]
    [SerializeField] private float sustainedJumpGravity = 0.4f;
    [SerializeField] private float gravity = -90f;
    [Space]
    [SerializeField] private float slideStartSpeed = 25f;
    [SerializeField] private float slideEndSpeed = 15f;
    [SerializeField] private float slideFriction = 0.8f;
    [SerializeField] private float slideSteerAcceleration = 5f;
    [SerializeField] private float slideGravity = 90f;
    [Space]
    [SerializeField] private float standHeight = 2f;
    [SerializeField] private float crouchHeight = 1f;
    [SerializeField] private float crouchHeightResponseTime = 15f;
    [Range(0f, 1f)]
    [SerializeField] private float standCameraTargetHeight = 0.9f;
    [Range(0f, 1f)]
    [SerializeField] private float crouchCameraTargetHeight = 0.7f;

    private PlayerState _state;
    private PlayerState _lastState;
    private PlayerState _tempState;

    private Quaternion _requestedRotation;
    private Vector3 _requestedMovement;
    private bool _requestedJump;
    private bool _requestedSustainedJump;
    private bool _requestedCrouch;
    private bool _requestedCrouchInAir;

    private float _timeSinceUngrounded;
    private float _timeSinceJumpRequest;
    private bool _ungroundedDueToJump;

    private Collider[] _uncrouchOverlapResults;

    public void Initialize()
    {
        _state.Stance = PlayerStance.Standing;
        _lastState = _state;

        motor.CharacterController = this;

        _uncrouchOverlapResults = new Collider[8];
    }

    public void UpdateInput(CharacterInput input) 
    {
        _requestedRotation = input.Rotation;
        // take the 2D input vector and create a 2D movement vector on the XZ plane
        _requestedMovement = new Vector3(input.Move.x, 0f, input.Move.y);
        // clamp the length of the movement vector to 1 to prevent faster movement when moving diagonally
        _requestedMovement = Vector3.ClampMagnitude(_requestedMovement, 1f);
        // orient the input so it's relative to the direction the player is facing
        _requestedMovement = input.Rotation * _requestedMovement;
        
        var wasRequestingJump = _requestedJump;
        _requestedJump = _requestedJump || input.Jump;
        _requestedSustainedJump = input.SustainedJump;
        if (_requestedJump && !wasRequestingJump)
            _timeSinceJumpRequest = 0f;

        var wasRequestingCrouch = _requestedCrouch;
        _requestedCrouch = input.Crouch switch
        {
            CrouchInput.Toggle => !_requestedCrouch,
            CrouchInput.None => _requestedCrouch,
            _ => _requestedCrouch
        };
        if (_requestedCrouch && !wasRequestingCrouch)
            _requestedCrouchInAir = !_state.Grounded;
        else if (!_requestedCrouch&& wasRequestingCrouch)
            _requestedCrouchInAir = false;
    }

    public void UpdateBody(float deltaTime)
    {
        var currentHeight = motor.Capsule.height;
        var normalizedHeight = currentHeight / standHeight;

        var cameraTargetHeight = currentHeight * (_state.Stance is PlayerStance.Standing 
            ? standCameraTargetHeight
            : crouchCameraTargetHeight
        );
        var rootTargetScale = new Vector3(1f, normalizedHeight, 1f);

        cameraTarget.localPosition = Vector3.Lerp
        (
            a: cameraTarget.localPosition,
            b: new Vector3(0f, cameraTargetHeight, 0f),
            t: 1f - Mathf.Exp(-crouchHeightResponseTime * deltaTime)
        );
        root.localScale = Vector3.Lerp
        (
            a: root.localScale,
            b: rootTargetScale,
            t: 1f - Mathf.Exp(-crouchHeightResponseTime * deltaTime)
        );
    }

    public void UpdateVelocity(ref Vector3 currentVelocity, float deltaTime)
    {
        _state.Acceleration = Vector3.zero;

        // If on the ground...
        if (motor.GroundingStatus.IsStableOnGround)
        {
            _timeSinceUngrounded = 0f;
            _ungroundedDueToJump = false;

            var groundedMovement = motor.GetDirectionTangentToSurface (
                direction: _requestedMovement,
                surfaceNormal: motor.GroundingStatus.GroundNormal
            ) * _requestedMovement.magnitude;

            // Start sliding...
            {
                var moving = groundedMovement.sqrMagnitude > 0f;
                var crouching = _state.Stance is PlayerStance.Crouching;
                var wasStanding = _lastState.Stance is PlayerStance.Standing;
                var wasInAir = !_lastState.Grounded;
                if (moving && crouching && (wasStanding || wasInAir))
                {
                    _state.Stance = PlayerStance.Sliding;

                    if (wasInAir)
                    {
                        currentVelocity = Vector3.ProjectOnPlane 
                        (
                            vector: _lastState.Velocity,
                            planeNormal: motor.GroundingStatus.GroundNormal
                        );
                    }

                    var effectiveSlideStartSpeed = slideStartSpeed;
                    if (!_lastState.Grounded && !_requestedCrouchInAir)
                    {
                        effectiveSlideStartSpeed = 0f;
                        _requestedCrouchInAir = false;
                    }

                    var slideSpeed = Mathf.Max(effectiveSlideStartSpeed, currentVelocity.magnitude);
                    currentVelocity = motor.GetDirectionTangentToSurface
                    (
                        direction: currentVelocity,
                        surfaceNormal: motor.GroundingStatus.GroundNormal
                    ) * slideSpeed;
                }
            }

            // Move.
            if (_state.Stance is PlayerStance.Standing or PlayerStance.Crouching)
            {
                var speed = _state.Stance is PlayerStance.Standing
                    ? walkSpeed
                    : crouchSpeed;
                var responseTime = _state.Stance is PlayerStance.Standing
                    ? walkResponseTime
                    : crouchResponseTime;
                
                var targetVelocity = groundedMovement * speed;
                var moveVelocity = Vector3.Lerp
                (
                    a: currentVelocity,
                    b: targetVelocity,
                    t: 1f - Mathf.Exp(-responseTime * deltaTime)
                );    
                _state.Acceleration = (moveVelocity - currentVelocity) / deltaTime;
                currentVelocity = moveVelocity;
            }

            // ...continue sliding...
            else
            {
                // apply friction...
                currentVelocity -= currentVelocity * (slideFriction * deltaTime);

                // slope acceleration...
                {
                    var force = Vector3.ProjectOnPlane
                    (
                        vector: -motor.CharacterUp,
                        planeNormal: motor.GroundingStatus.GroundNormal
                    ) * slideGravity;

                    currentVelocity += force * deltaTime;
                }

                // steering logic...
                {
                    var clampSpeed = currentVelocity.magnitude;
                    var targetVelocity = groundedMovement * clampSpeed;
                    var steerVelocity = currentVelocity;
                    var steerForce = (targetVelocity - steerVelocity) * slideSteerAcceleration * deltaTime;

                    steerVelocity += steerForce;
                    steerVelocity = Vector3.ClampMagnitude(steerVelocity, clampSpeed);

                    _state.Acceleration = (steerVelocity - currentVelocity) / deltaTime;
                    currentVelocity = steerVelocity;
                }

                // stop the sliding if velocity is not high enough
                if (currentVelocity.magnitude < slideEndSpeed)
                {
                    _state.Stance = PlayerStance.Crouching;
                }
            }
        }

        // ...else, the player is in the air...
        else
        {
            _timeSinceJumpRequest += deltaTime;
            if (_requestedMovement.sqrMagnitude > 0f)
            {
                var planarMovement = Vector3.ProjectOnPlane
                (
                    vector: _requestedMovement,
                    planeNormal: motor.CharacterUp
                ) * _requestedMovement.magnitude;

                var currentPlanarVelocity = Vector3.ProjectOnPlane
                (
                    vector: currentVelocity,
                    planeNormal: motor.CharacterUp
                );

                var movementForce = planarMovement * airAcceleration * deltaTime;

                if (currentPlanarVelocity.magnitude < airSpeed)
                {
                    var targetPlanarVelocity = currentPlanarVelocity + movementForce;

                    targetPlanarVelocity = Vector3.ClampMagnitude(targetPlanarVelocity, airSpeed);   

                    movementForce = targetPlanarVelocity - currentPlanarVelocity;
                }

                else if (Vector3.Dot(currentPlanarVelocity, movementForce) > 0f)
                {
                    var constrainedMovementForce = Vector3.ProjectOnPlane
                    (
                        vector: movementForce,
                        planeNormal: currentPlanarVelocity.normalized
                    );

                    movementForce = constrainedMovementForce;
                }

                if (motor.GroundingStatus.FoundAnyGround)
                {
                    if (Vector3.Dot(movementForce, currentVelocity + movementForce) > 0f)
                    {
                        var obstructionNormal = Vector3.Cross
                        (
                            motor.CharacterUp,
                            Vector3.Cross
                            (
                                motor.CharacterUp,
                                motor.GroundingStatus.GroundNormal
                            )
                        ).normalized;

                        movementForce = Vector3.ProjectOnPlane(movementForce, obstructionNormal);
                    }
                }

                currentVelocity += movementForce;
            }
            var effectiveGravity = gravity;
            var verticalVelocity = Vector3.Dot(currentVelocity, motor.CharacterUp);
            if (_requestedSustainedJump && verticalVelocity > 0f)
            {
                effectiveGravity *= sustainedJumpGravity;
            }
            currentVelocity += motor.CharacterUp * effectiveGravity * deltaTime;
        }

        if (_requestedJump)
        {
            var grounded = motor.GroundingStatus.IsStableOnGround;
            var coyoteJumpAllowed = _timeSinceUngrounded < coyoteTime && !_ungroundedDueToJump;

            if (grounded || coyoteJumpAllowed)
            {
                _requestedJump = false; // stop jump request
                _requestedCrouch = false; // force uncrouch
                _requestedCrouchInAir = false; // force uncrouch in air

                motor.ForceUnground(time: 0f);
                _ungroundedDueToJump = true;

                var currentVerticalSpeed = Vector3.Dot(currentVelocity, motor.CharacterUp);
                var targetVerticalSpeed = Mathf.Max(currentVerticalSpeed, jumpSpeed);

                currentVelocity += motor.CharacterUp * (targetVerticalSpeed - currentVerticalSpeed);
            }
            else
            {
                _timeSinceJumpRequest += deltaTime;

                // defer the jump request until the coyote time has passed.
                var canKeepTryingToJump = _timeSinceJumpRequest < coyoteTime;
                _requestedJump = canKeepTryingToJump; // deny jump request
            }
        }
    }

    public void UpdateRotation(ref Quaternion currentRotation, float deltaTime)
    {
        var forward = Vector3.ProjectOnPlane
        (
            _requestedRotation * Vector3.forward,
            motor.CharacterUp
        );

        currentRotation = Quaternion.LookRotation(forward, motor.CharacterUp);
    }

    public void BeforeCharacterUpdate(float deltaTime)
    {
        _tempState = _state;
        if (_requestedCrouch && _state.Stance is PlayerStance.Standing)
        {
            _state.Stance = PlayerStance.Crouching;
            motor.SetCapsuleDimensions
            (
                radius: motor.Capsule.radius,
                height: crouchHeight,
                yOffset: crouchHeight * 0.5f 
            );
        }
    }
    public void PostGroundingUpdate(float deltaTime)
    {
        if (!motor.GroundingStatus.IsStableOnGround && _state.Stance is PlayerStance.Sliding)
            _state.Stance = PlayerStance.Crouching;
    }
    public void AfterCharacterUpdate(float deltaTime)
    {
        if (!_requestedCrouch && _state.Stance is not PlayerStance.Standing)
        {
            motor.SetCapsuleDimensions
            (
                radius: motor.Capsule.radius,
                height: standHeight,
                yOffset: standHeight * 0.5f 
            );
            
            var position = motor.TransientPosition;
            var rotation = motor.TransientRotation;
            var mask = motor.CollidableLayers;
            if (motor.CharacterOverlap(position, rotation, _uncrouchOverlapResults, mask, QueryTriggerInteraction.Ignore) > 0)
            {
                _requestedCrouch = true;
                motor.SetCapsuleDimensions
                (
                    radius: motor.Capsule.radius,
                    height: crouchHeight,
                    yOffset: crouchHeight * 0.5f 
                );
            }
            else
            {
                _state.Stance = PlayerStance.Standing;
            }
        }
        _state.Grounded = motor.GroundingStatus.IsStableOnGround;
        _state.Velocity = motor.Velocity;
        _lastState = _tempState;
    }

    public void OnGroundHit(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint, ref HitStabilityReport hitStabilityReport) {}
    public void OnMovementHit(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint, ref HitStabilityReport hitStabilityReport) {}
    public bool IsColliderValidForCollisions(Collider coll) => true;
    public void OnDiscreteCollisionDetected(Collider hitCollider) {}
    public void ProcessHitStabilityReport(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint, Vector3 atCharacterPosition, Quaternion atCharacterRotation, ref HitStabilityReport hitStabilityReport) {}

    public Transform GetCameraTarget() => cameraTarget;
    public PlayerState GetState() => _state;
    public PlayerState GetLastState() => _lastState;

    public void SetPosition(Vector3 position, bool killVelocity = true)
    {
        motor.SetPosition(position);
        if (killVelocity)
        {
            motor.BaseVelocity = Vector3.zero;
        }
    }
}
