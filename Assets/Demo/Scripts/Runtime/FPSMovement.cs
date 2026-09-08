// Designed by KINEMATION, 2023
// Patched with scripted-input hooks for teaser automation.

using Kinemation.FPSFramework.Runtime.FPSAnimator;
using UnityEngine;
using UnityEngine.Events;

namespace Demo.Scripts.Runtime
{
    public enum FPSMovementState
    {
        Idle,
        Walking,
        Sprinting,
        InAir,
        Sliding
    }

    public enum FPSPoseState
    {
        Standing,
        Crouching,
        Prone
    }

    // =========================================================================
    // SCRIPTED INPUT SUPPORT
    // =========================================================================
    /// <summary>
    /// Synthetic movement input fed by the teaser runner.
    /// Axes are in the same convention as Input.GetAxisRaw("Horizontal"/"Vertical"): -1..+1.
    /// Button-level flags are "held" states; the component synthesizes GetKeyDown via
    /// comparison with the previous frame.
    /// </summary>
    [System.Serializable]
    public struct FPSMovementScriptedInput
    {
        public float moveX;  // strafe: -1 left, +1 right
        public float moveY;  // forward: -1 back, +1 forward
        public bool sprint; // held
        public bool slide;  // held (only acts when combined with sprint + speed)
        public bool jump;   // held (edge-detected)
        public bool crouch; // held (edge-detected: toggles crouch)
        public bool prone;  // held (edge-detected: toggles prone)
    }

    public class FPSMovement : MonoBehaviour
    {
        public delegate bool ConditionDelegate();

        [SerializeField] private FPSMovementSettings movementSettings;
        [SerializeField] public Transform rootBone;

        [SerializeField] public UnityEvent onStartMoving;
        [SerializeField] public UnityEvent onStopMoving;

        [SerializeField] public UnityEvent onSprintStarted;
        [SerializeField] public UnityEvent onSprintEnded;

        [SerializeField] public UnityEvent onCrouch;
        [SerializeField] public UnityEvent onUncrouch;

        [SerializeField] public UnityEvent onProneStarted;
        [SerializeField] public UnityEvent onProneEnded;

        [SerializeField] public UnityEvent onJump;
        [SerializeField] public UnityEvent onLanded;

        [SerializeField] public UnityEvent onSlideStarted;
        [SerializeField] public UnityEvent onSlideEnded;

        public ConditionDelegate slideCondition;
        public ConditionDelegate proneCondition;
        public ConditionDelegate sprintCondition;

        public FPSMovementState MovementState { get; private set; }
        public FPSPoseState PoseState { get; private set; }

        public Vector2 AnimatorVelocity { get; private set; }

        private CharacterController _controller;
        private Animator _animator;
        private Vector2 _inputDirection;

        public Vector3 MoveVector { get; private set; }

        private Vector3 _velocity;

        private float _originalHeight;
        private Vector3 _originalCenter;

        private GaitSettings _desiredGait;
        private float _slideProgress = 0f;

        private Vector3 _prevPosition;
        private Vector3 _velocityVector;

        private bool _wantsToJump = false;

        private static readonly int InAir = Animator.StringToHash("InAir");
        private static readonly int MoveX = Animator.StringToHash("MoveX");
        private static readonly int MoveY = Animator.StringToHash("MoveY");
        private static readonly int Velocity = Animator.StringToHash("Velocity");
        private static readonly int Moving = Animator.StringToHash("Moving");
        private static readonly int Crouching = Animator.StringToHash("Crouching");
        private static readonly int Sliding = Animator.StringToHash("Sliding");
        private static readonly int Sprinting = Animator.StringToHash("Sprinting");
        private static readonly int Proning = Animator.StringToHash("Proning");

        private float _sprintAnimatorInterp = 8f;

        private bool _wasMoving = false;

        public FPSController playerController;

        // ========== SCRIPTED INPUT HOOKS ==========
        [Header("Scripted Input (Teaser)")]
        [Tooltip("When ON, WASD / sprint / crouch / prone / jump inputs come from 'scriptedInput' instead of real player input.")]
        public bool useScriptedInput = false;

        [Tooltip("Synthetic movement input written by the teaser runner each frame.")]
        public FPSMovementScriptedInput scriptedInput;

        private bool _prevJumpHeld;
        private bool _prevCrouchHeld;
        private bool _prevProneHeld;
        // ==========================================

        public bool IsInAir()
        {
            return !_controller.isGrounded;
        }

        private bool IsMoving()
        {
            return !Mathf.Approximately(_inputDirection.normalized.magnitude, 0f);
        }

        private float GetSpeedRatio()
        {
            return _velocity.magnitude / _desiredGait.velocity;
        }

        private bool CanSlide()
        {
            return slideCondition == null || slideCondition.Invoke();
        }

        private bool CanSprint()
        {
            return sprintCondition == null || sprintCondition.Invoke();
        }

        private bool CanProne()
        {
            return proneCondition == null || proneCondition.Invoke();
        }

        // --- Scripted-input helpers ---
        private bool JumpPressed()
        {
            if (useScriptedInput) return scriptedInput.jump && !_prevJumpHeld;
            return Input.GetKeyDown(movementSettings.jumpKey);
        }

        private bool SprintHeld()
        {
            if (useScriptedInput) return scriptedInput.sprint;
            return Input.GetKey(movementSettings.sprintKey);
        }

        private bool SlideHeld()
        {
            if (useScriptedInput) return scriptedInput.slide;
            return Input.GetKey(movementSettings.slideKey);
        }

        private bool CrouchPressed()
        {
            if (useScriptedInput) return scriptedInput.crouch && !_prevCrouchHeld;
            return Input.GetKeyDown(movementSettings.crouchKey);
        }

        private bool PronePressed()
        {
            if (useScriptedInput) return scriptedInput.prone && !_prevProneHeld;
            return Input.GetKeyDown(movementSettings.proneKey);
        }

        private bool TryJump()
        {
            if (!JumpPressed() || PoseState == FPSPoseState.Crouching)
            {
                return false;
            }

            if (PoseState == FPSPoseState.Prone)
            {
                CancelProne();
                return false;
            }

            _wantsToJump = true;
            MovementState = FPSMovementState.InAir;
            return true;
        }

        private bool TrySprint()
        {
            if (PoseState is FPSPoseState.Crouching or FPSPoseState.Prone)
            {
                return false;
            }

            if (_inputDirection.y <= 0f || !SprintHeld())
            {
                return false;
            }

            if (SlideHeld() && GetSpeedRatio() > 0.5f)
            {
                if (!CanSlide()) return false;

                MovementState = FPSMovementState.Sliding;
                return true;
            }

            if (!CanSprint()) return false;

            MovementState = FPSMovementState.Sprinting;
            return true;
        }

        private bool CanUnCrouch()
        {
            float height = _originalHeight - _controller.radius * 2f;
            Vector3 position = rootBone.TransformPoint(_originalCenter + Vector3.up * height / 2f);
            return !Physics.CheckSphere(position, _controller.radius);
        }

        private void EnableProne()
        {
            Crouch();
            PoseState = FPSPoseState.Prone;
            _animator.SetBool(Crouching, false);
            _animator.SetBool(Proning, true);

            onProneStarted?.Invoke();
            _desiredGait = movementSettings.prone;
        }

        private void CancelProne()
        {
            if (!CanUnCrouch()) return;
            UnCrouch();
            PoseState = FPSPoseState.Standing;
            _animator.SetBool(Proning, false);

            onProneEnded?.Invoke();
            _desiredGait = movementSettings.walking;
        }

        private void Crouch()
        {
            float crouchedHeight = _originalHeight * movementSettings.crouchRatio;
            float heightDifference = _originalHeight - crouchedHeight;

            _controller.height = crouchedHeight;

            // Adjust the center position so the bottom of the capsule remains at the same position
            Vector3 crouchedCenter = _originalCenter;
            crouchedCenter.y -= heightDifference / 2;
            _controller.center = crouchedCenter;

            PoseState = FPSPoseState.Crouching;

            _animator.SetBool(Crouching, true);
            onCrouch.Invoke();
        }

        private void UnCrouch()
        {
            _controller.height = _originalHeight;
            _controller.center = _originalCenter;

            PoseState = FPSPoseState.Standing;

            _animator.SetBool(Crouching, false);
            onUncrouch.Invoke();
        }

        private void UpdatePoseState()
        {
            if (MovementState is FPSMovementState.Sprinting or FPSMovementState.InAir)
            {
                return;
            }

            if (PronePressed())
            {
                if (!CanProne())
                {
                    return;
                }

                if (PoseState == FPSPoseState.Prone)
                {
                    CancelProne();
                }
                else
                {
                    EnableProne();
                }

                return;
            }

            if (!CrouchPressed())
            {
                return;
            }

            if (PoseState == FPSPoseState.Standing)
            {
                Crouch();

                _desiredGait = movementSettings.crouching;
                return;
            }

            if (!CanUnCrouch()) return;

            UnCrouch();
            _desiredGait = movementSettings.walking;
        }

        private void UpdateMovementState()
        {
            if (MovementState == FPSMovementState.InAir && IsInAir())
            {
                // Do not update player movement while jumping or falling
                return;
            }

            if (MovementState != FPSMovementState.InAir && IsInAir())
            {
                MovementState = FPSMovementState.InAir;
                _wantsToJump = false;
                return;
            }


            // Get the current player input (real or scripted)
            float moveX, moveY;
            if (useScriptedInput)
            {
                moveX = Mathf.Clamp(scriptedInput.moveX, -1f, 1f);
                moveY = Mathf.Clamp(scriptedInput.moveY, -1f, 1f);
            }
            else
            {
                moveX = Input.GetAxisRaw("Horizontal");
                moveY = Input.GetAxisRaw("Vertical");
            }

            _inputDirection.x = moveX;
            _inputDirection.y = moveY;

            if (!playerController.isPlayerReady || !playerController.isQoeDisabled || !playerController.isAcceptabilityDisabled)
            {
                MovementState = FPSMovementState.Idle;
                _inputDirection.x = 0;
                _inputDirection.y = 0;
            }

            if (MovementState == FPSMovementState.Sliding && !Mathf.Approximately(_slideProgress, 1f))
            {
                // Consume input, but do not allow cancelling sliding.
                return;
            }

            // Jump action overrides any other input
            if (TryJump())
            {
                return;
            }

            if (TrySprint())
            {
                return;
            }

            if (!IsMoving())
            {
                MovementState = FPSMovementState.Idle;
                return;
            }

            MovementState = FPSMovementState.Walking;
        }

        private void OnMovementStateChanged(FPSMovementState prevState)
        {
            if (prevState == FPSMovementState.InAir)
            {
                onLanded.Invoke();
            }

            if (prevState == FPSMovementState.Sprinting)
            {
                _sprintAnimatorInterp = 7f;
                onSprintEnded.Invoke();
            }

            if (prevState == FPSMovementState.Sliding)
            {
                _sprintAnimatorInterp = 15f;
                onSlideEnded.Invoke();

                if (CanUnCrouch())
                {
                    UnCrouch();
                }
            }

            if (MovementState == FPSMovementState.Idle)
            {
                float prevVelocity = _desiredGait.velocity;
                _desiredGait = movementSettings.idle;
                _desiredGait.velocity = prevVelocity;
                return;
            }

            if (MovementState == FPSMovementState.InAir)
            {
                if (_wantsToJump)
                {
                    _velocity.y = movementSettings.jumpHeight;
                    onJump.Invoke();
                }

                return;
            }

            if (MovementState == FPSMovementState.Sprinting)
            {
                _desiredGait = movementSettings.sprinting;
                onSprintStarted.Invoke();
                return;
            }

            if (MovementState == FPSMovementState.Sliding)
            {
                _desiredGait.velocitySmoothing = movementSettings.slideDirectionSmoothing;
                _slideProgress = 0f;
                onSlideStarted.Invoke();
                Crouch();
                return;
            }

            if (PoseState == FPSPoseState.Crouching)
            {
                _desiredGait = movementSettings.crouching;
                return;
            }

            if (PoseState == FPSPoseState.Prone)
            {
                _desiredGait = movementSettings.prone;
                return;
            }

            // Walking state
            _desiredGait = movementSettings.walking;
        }

        private void UpdateSliding()
        {
            // 1. Extract the slide animation.
            float slideAmount = movementSettings.slideCurve.Evaluate(_slideProgress);

            // 2. Apply sliding to both current and desired velocity vectors.
            // Here we just want to interpolate between the same velocities, but different directions.

            _velocity *= slideAmount;

            Vector3 desiredVelocity = _velocity;
            desiredVelocity.y = -movementSettings.gravity;
            MoveVector = desiredVelocity;

            _slideProgress = Mathf.Clamp01(_slideProgress + Time.deltaTime * movementSettings.slideSpeed);
        }

        private void UpdateGrounded()
        {
            var normInput = _inputDirection.normalized;
            var desiredVelocity = rootBone.right * normInput.x + rootBone.forward * normInput.y;

            desiredVelocity *= _desiredGait.velocity;

            desiredVelocity = Vector3.Lerp(_velocity, desiredVelocity,
                FPSAnimLib.ExpDecayAlpha(_desiredGait.velocitySmoothing, Time.deltaTime));

            _velocity = desiredVelocity;

            desiredVelocity.y = -movementSettings.gravity;
            MoveVector = desiredVelocity;
        }

        private void UpdateInAir()
        {
            var normInput = _inputDirection.normalized;
            _velocity.y -= movementSettings.gravity * Time.deltaTime;
            _velocity.y = Mathf.Max(-movementSettings.maxFallVelocity, _velocity.y);

            var desiredVelocity = rootBone.right * normInput.x + rootBone.forward * normInput.y;
            desiredVelocity *= _desiredGait.velocity;

            desiredVelocity = Vector3.Lerp(_velocity, desiredVelocity * movementSettings.airFriction,
                FPSAnimLib.ExpDecayAlpha(movementSettings.airVelocity, Time.deltaTime));

            desiredVelocity.y = _velocity.y;
            _velocity = desiredVelocity;

            MoveVector = desiredVelocity;
        }

        private void UpdateMovement()
        {
            _controller.Move(MoveVector * Time.deltaTime);
        }

        private void UpdateAnimatorParams()
        {
            var animatorVelocity = _inputDirection;
            animatorVelocity *= MovementState == FPSMovementState.InAir ? 0f : 1f;

            AnimatorVelocity = Vector2.Lerp(AnimatorVelocity, animatorVelocity,
                FPSAnimLib.ExpDecayAlpha(_desiredGait.velocitySmoothing, Time.deltaTime));

            _animator.SetFloat(MoveX, AnimatorVelocity.x);
            _animator.SetFloat(MoveY, AnimatorVelocity.y);
            _animator.SetFloat(Velocity, AnimatorVelocity.magnitude);
            _animator.SetBool(InAir, IsInAir());
            _animator.SetBool(Moving, IsMoving());

            // Sprinting needs to be blended manually
            float a = _animator.GetFloat(Sprinting);
            float b = MovementState == FPSMovementState.Sprinting ? 1f : 0f;

            a = Mathf.Lerp(a, b, FPSAnimLib.ExpDecayAlpha(_sprintAnimatorInterp, Time.deltaTime));
            _animator.SetFloat(Sprinting, a);

            if (!playerController.isPlayerReady || !playerController.isQoeDisabled || !playerController.isAcceptabilityDisabled)
            {
                _animator.SetFloat(Sprinting, 0);
            }
        }

        private void Start()
        {
            _controller = GetComponent<CharacterController>();
            _animator = GetComponentInChildren<Animator>();

            _originalHeight = _controller.height;
            _originalCenter = _controller.center;

            MovementState = FPSMovementState.Idle;
            PoseState = FPSPoseState.Standing;

            _desiredGait = movementSettings.walking;

            playerController = GetComponent<FPSController>();
        }

        private void Update()
        {
            bool isMoving;
            if (!playerController.isPlayerReady || !playerController.isQoeDisabled || !playerController.isAcceptabilityDisabled)
            {
                MovementState = FPSMovementState.Idle;
                PoseState = FPSPoseState.Standing;
                UpdateMovementState();
                UpdatePoseState();
                isMoving = false;
                onStopMoving?.Invoke();
                UpdateAnimatorParams();
                OnMovementStateChanged(MovementState);
                _sprintAnimatorInterp = 7f;
                _desiredGait = movementSettings.idle;
                onSprintEnded.Invoke();

                // Still update edge-detection history so first real frame doesn't mis-fire.
                UpdatePrevHeldFlags();
                return;
            }


            var prevState = MovementState;
            UpdateMovementState();

            UpdatePoseState();

            if (prevState != MovementState)
            {
                OnMovementStateChanged(prevState);
            }

            isMoving = IsMoving();

            if (_wasMoving != isMoving)
            {
                if (isMoving)
                {
                    onStartMoving?.Invoke();
                }
                else
                {
                    onStopMoving?.Invoke();
                }
            }

            _wasMoving = isMoving;

            if (MovementState == FPSMovementState.InAir)
            {
                UpdateInAir();
            }
            else if (MovementState == FPSMovementState.Sliding)
            {
                UpdateSliding();
            }
            else
            {
                UpdateGrounded();
            }

            UpdateMovement();
            UpdateAnimatorParams();

            UpdatePrevHeldFlags();
        }

        private void UpdatePrevHeldFlags()
        {
            if (useScriptedInput)
            {
                _prevJumpHeld = scriptedInput.jump;
                _prevCrouchHeld = scriptedInput.crouch;
                _prevProneHeld = scriptedInput.prone;
            }
            else
            {
                _prevJumpHeld = Input.GetKey(movementSettings.jumpKey);
                _prevCrouchHeld = Input.GetKey(movementSettings.crouchKey);
                _prevProneHeld = Input.GetKey(movementSettings.proneKey);
            }
        }
    }
}