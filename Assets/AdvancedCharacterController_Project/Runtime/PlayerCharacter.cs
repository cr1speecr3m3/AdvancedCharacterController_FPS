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

        _requestedMovement = new Vector3(input.Move.x, 0f, input.Move.y);
        _requestedMovement = Vector3.ClampMagnitude(_requestedMovement, 1f);
        _requestedMovement = input.Rotation * _requestedMovement;
        _requestedJump = _requestedJump || input.Jump;
        _requestedSustainedJump = input.SustainedJump;
        _requestedCrouch = input.Crouch switch
        {
            CrouchInput.Toggle => !_requestedCrouch,
            CrouchInput.None => _requestedCrouch,
            _ => _requestedCrouch
        };
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
        // If on the ground...
        if (motor.GroundingStatus.IsStableOnGround)
        {
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

                    var slideSpeed = Mathf.Max(slideStartSpeed, currentVelocity.magnitude);
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
                currentVelocity = Vector3.Lerp
                (
                    a: currentVelocity,
                    b: targetVelocity,
                    t: 1f - Mathf.Exp(-responseTime * deltaTime)
                );    
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
                    var steerForce = (targetVelocity - currentVelocity) * slideSteerAcceleration * deltaTime;
                    currentVelocity += steerForce;
                    currentVelocity = Vector3.ClampMagnitude(currentVelocity, clampSpeed);
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

                var targetPlanarVelocity = currentPlanarVelocity + movementForce;

                targetPlanarVelocity = Vector3.ClampMagnitude(targetPlanarVelocity, airSpeed);

                currentVelocity += targetPlanarVelocity - currentPlanarVelocity;
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
            _requestedJump = false; // stop jump request
            _requestedCrouch = false; // force uncrouch

            motor.ForceUnground(time: 0f);

            var currentVerticalSpeed = Vector3.Dot(currentVelocity, motor.CharacterUp);
            var targetVerticalSpeed = Mathf.Max(currentVerticalSpeed, jumpSpeed);

            currentVelocity += motor.CharacterUp * (targetVerticalSpeed - currentVerticalSpeed);
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
        _lastState = _tempState;
    }

    public void OnGroundHit(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint, ref HitStabilityReport hitStabilityReport) {}
    public void OnMovementHit(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint, ref HitStabilityReport hitStabilityReport) {}
    public bool IsColliderValidForCollisions(Collider coll) => true;
    public void OnDiscreteCollisionDetected(Collider hitCollider) {}
    public void ProcessHitStabilityReport(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint, Vector3 atCharacterPosition, Quaternion atCharacterRotation, ref HitStabilityReport hitStabilityReport) {}

    public Transform GetCameraTarget() => cameraTarget;

    public void SetPosition(Vector3 position, bool killVelocity = true)
    {
        motor.SetPosition(position);
        if (killVelocity)
        {
            motor.BaseVelocity = Vector3.zero;
        }
    }
}
