// Designed by KINEMATION, 2023
// Patched with scripted-input hooks for teaser automation.

using Kinemation.FPSFramework.Runtime.FPSAnimator;
using Kinemation.FPSFramework.Runtime.Layers;
using Kinemation.FPSFramework.Runtime.Recoil;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;

namespace Demo.Scripts.Runtime
{
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
    public class TabAttribute : PropertyAttribute
    {
        public readonly string tabName;

        public TabAttribute(string tabName)
        {
            this.tabName = tabName;
        }
    }

    public enum FPSAimState
    {
        None,
        Ready,
        Aiming,
        PointAiming
    }

    public enum FPSActionState
    {
        None,
        Reloading,
        WeaponChange
    }

    // =========================================================================
    // SCRIPTED INPUT SUPPORT
    // =========================================================================
    /// <summary>
    /// Synthetic input fed to FPSController by the teaser runner.
    /// All mouse values are raw per-frame deltas in SCREEN UNITS (same semantics
    /// as Input.GetAxis("Mouse X") * sensitivity multiplier on the controller
    /// side � but we bypass sensitivity by setting mouseX/Y already-scaled, see
    /// FPSController.UpdateLookInput).
    /// </summary>
    [Serializable]
    public struct FPSScriptedInput
    {
        // Mouse deltas (already-scaled degrees-ish values; see UpdateLookInput)
        public float mouseX;
        public float mouseY;

        // Button-level state (held flags). The controller synthesizes GetKeyDown/Up
        // by comparing the current frame's held flag to the previous frame.
        public bool fire;
        public bool aim;
        public bool reload;
        public bool grenade;
    }

    public class FPSController : FPSAnimController
    {
        [Tab("Animation")]
        [Header("General")]
        [SerializeField] private Animator animator;

        [Header("Turn In Place")]
        [SerializeField] private float turnInPlaceAngle;
        [SerializeField] private AnimationCurve turnCurve = new AnimationCurve(new Keyframe(0f, 0f));
        [SerializeField] private float turnSpeed = 1f;

        [Header("Leaning")]
        [SerializeField] private float smoothLeanStep = 1f;
        [SerializeField, Range(0f, 1f)] private float startLean = 1f;

        [Header("Dynamic Motions")]
        [SerializeField] private IKAnimation aimMotionAsset;
        [SerializeField] private IKAnimation leanMotionAsset;
        [SerializeField] private IKAnimation crouchMotionAsset;
        [SerializeField] private IKAnimation unCrouchMotionAsset;
        [SerializeField] private IKAnimation onJumpMotionAsset;
        [SerializeField] private IKAnimation onLandedMotionAsset;
        [SerializeField] private IKAnimation onStartStopMoving;

        [SerializeField] private IKPose sprintPose;
        [SerializeField] private IKPose pronePose;

        // Animation Layers
        [SerializeField][HideInInspector] private LookLayer lookLayer;
        [SerializeField][HideInInspector] private AdsLayer adsLayer;
        [SerializeField][HideInInspector] private SwayLayer swayLayer;
        [SerializeField][HideInInspector] private LocomotionLayer locoLayer;
        [SerializeField][HideInInspector] private SlotLayer slotLayer;
        [SerializeField][HideInInspector] public WeaponCollision collisionLayer;
        // Animation Layers

        [Header("General")]
        [Tab("Controller")]
        [SerializeField] private float timeScale = 1f;
        [SerializeField, Min(0f)] private float equipDelay = 0f;

        [Header("Camera")]
        [SerializeField] public Transform mainCamera;
        [SerializeField] private Transform cameraHolder;
        [SerializeField] private Transform firstPersonCamera;
        [SerializeField] private float sensitivity;
        [SerializeField] private Vector2 freeLookAngle;

        [Header("Movement")]
        [SerializeField] private FPSMovement movementComponent;

        [SerializeField]
        [Tab("Weapon")]
        private List<Weapon> weapons;
        private Vector2 _playerInput;

        // Used for free-look
        private Vector2 _freeLookInput;

        private int _currentWeaponIndex;
        private int _lastIndex;

        private int _bursts;
        private bool _freeLook;

        private FPSAimState aimState;
        private FPSActionState actionState;

        private static readonly int Crouching = Animator.StringToHash("Crouching");
        private static readonly int OverlayType = Animator.StringToHash("OverlayType");
        private static readonly int TurnRight = Animator.StringToHash("TurnRight");
        private static readonly int TurnLeft = Animator.StringToHash("TurnLeft");
        private static readonly int UnEquip = Animator.StringToHash("UnEquip");

        private Vector2 _controllerRecoil;
        private float _recoilStep;
        private bool _isFiring;

        private bool _isUnarmed;
        private float _lastRecoilTime;


        public GameObject reticleObject;
        private float fireTimer;

        RaycastHit hit;

        public float regularHitCooldown;
        public float killCooldown;
        public float headshotCooldown;

        //Audio
        public AudioClip weaponChangeSFX;
        public AudioClip ADSInSFX;
        public AudioClip ADSOutSFX;
        public AudioClip hitRegSFX;
        public AudioClip killSFX;
        public AudioClip headshotSFX;
        public AudioClip deathSFX;

        public AudioSource playerAudioSource;

        public long score;
        public int roundKills;
        public int roundDeaths;

        public int shotsFiredPerRound;
        public int shotsHitPerRound;
        public int headshotsHitPerRound;

        public Transform playerSpawnPoint;

        public EnemyManager enemyManager;

        public float deathTimeOut;

        public bool isPlayerReady = false;

        Vector3 oldPosition;
        public float distanceTravelledPerRound;

        public float delXCumilative = 0;
        public float delYCumilative = 0;

        public ParticleSystem muzzleFlash;

        public ParticleSystem bulletHitPE;

        public int realoadCountPerRound = 0;

        public bool isQoeDisabled;
        public bool isAcceptabilityDisabled;

        public GameManager gameManager;

        public bool isAimSpikeEnabled;
        public bool isReloadSpikeEnabled;
        public bool isMouseMovementSpikeEnabled;
        public bool isEnemySpawnSpikeEnabled;

        float mouseSpikeCooldown;
        public float mouseSpikeDelay;
        public float mouseSpikeDegreeOfMovement;
        public float aimSpikeDelay;
        float aimSpikeCooldown;

        bool enemyAimedFuse;

        float deltaMouseX;
        float deltaMouseY;

        public LayerMask enemyLargeColliderLayer;
        public int perRoundAimSpikeCount;
        public int perRoundReloadSpikeCount;
        public int perRoundMouseMovementSpikeCount;
        public int perRoundEnemySpawnSpikeCount;

        public PlayerTickLog playerTickLog;

        public Image clickToPhotonIMG;

        public RoundManager roundManager;

        public float degreeToTargetX;
        public float degreeToTargetY;

        public bool targetMarked;

        public float degreeToShootX;
        public float degreeToShootY;

        public bool targetShot;

        public float degreeToTargetXCumulative;
        public float degreeToShootXCumulative;

        public float timeToTargetEnemy;
        public float timeToHitEnemy;
        public float timeToKillEnemy;

        public float timeToTargetEnemyCumulative;
        public float timeToHitEnemyCumulative;
        public float timeToKillEnemyCumulative;

        public float enemySizeCumulative;

        public float minAnlgeToEnemyCumulative;

        public int tacticalReloadCountPerRound = 0;

        public float aimDurationPerRound;
        public float isFiringDurationPerRound;

        public float enemySpeedGlobal;

        public float reticleSizeMultiplier;

        public float enemyHealthGlobal;

        public float liveAccuracy;

        public int onHitScore;
        public int onKillScore;
        public int onMissScore;
        public int onDeathScore;

        public double spikeDurationCumulative;

        // ========== SCRIPTED INPUT HOOKS ==========
        [Header("Scripted Input (Teaser)")]
        [Tooltip("When ON, mouse/fire/aim/reload input comes from 'scriptedInput' instead of real player input. See HDRPTeaserRunner.")]
        public bool useScriptedInput = false;

        [Tooltip("Synthetic input written by the teaser runner each frame. Ignored unless useScriptedInput is ON.")]
        public FPSScriptedInput scriptedInput;

        // Previous-frame held flags so we can synthesize GetKeyDown/GetKeyUp.
        private bool _prevFireHeld;
        private bool _prevAimHeld;
        private bool _prevReloadHeld;
        private bool _prevGrenadeHeld;
        // ==========================================

        private void InitLayers()
        {
            InitAnimController();

            animator = GetComponentInChildren<Animator>();
            lookLayer = GetComponentInChildren<LookLayer>();
            adsLayer = GetComponentInChildren<AdsLayer>();
            locoLayer = GetComponentInChildren<LocomotionLayer>();
            swayLayer = GetComponentInChildren<SwayLayer>();
            slotLayer = GetComponentInChildren<SlotLayer>();
            collisionLayer = GetComponentInChildren<WeaponCollision>();

        }

        private bool HasActiveAction()
        {
            return actionState != FPSActionState.None;
        }

        private bool IsAiming()
        {
            return aimState is FPSAimState.Aiming or FPSAimState.PointAiming;
        }

        private void Start()
        {
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;

            moveRotation = transform.rotation;

            movementComponent = GetComponent<FPSMovement>();

            movementComponent.onStartMoving.AddListener(OnMoveStarted);
            movementComponent.onStopMoving.AddListener(OnMoveEnded);

            movementComponent.onCrouch.AddListener(OnCrouch);
            movementComponent.onUncrouch.AddListener(OnUncrouch);

            movementComponent.onJump.AddListener(OnJump);
            movementComponent.onLanded.AddListener(OnLand);

            movementComponent.onSprintStarted.AddListener(OnSprintStarted);
            movementComponent.onSprintEnded.AddListener(OnSprintEnded);

            movementComponent.onSlideStarted.AddListener(OnSlideStarted);
            movementComponent.onSlideEnded.AddListener(OnSlideEnded);

            movementComponent.onProneStarted.AddListener(() => collisionLayer.SetLayerAlpha(0f));
            movementComponent.onProneEnded.AddListener(() => collisionLayer.SetLayerAlpha(1f));

            movementComponent.slideCondition += () => !HasActiveAction();
            movementComponent.sprintCondition += () => !HasActiveAction();
            movementComponent.proneCondition += () => !HasActiveAction();

            actionState = FPSActionState.None;

            InitLayers();
            EquipWeapon();

            playerAudioSource = GetComponent<AudioSource>();
            enemyAimedFuse = false;

            playerTickLog = new PlayerTickLog();

            playerTickLog.time = new List<string>();
            playerTickLog.mouseX = new List<float>();
            playerTickLog.mouseY = new List<float>();

            playerTickLog.playerX = new List<float>();
            playerTickLog.playerY = new List<float>();
            playerTickLog.playerZ = new List<float>();

            playerTickLog.roundTimer = new List<float>();

            playerTickLog.scorePerSec = new List<float>();

            playerTickLog.playerRot = new List<Quaternion>();

            playerTickLog.isADS = new List<bool>();

            playerTickLog.frameTimeMS = new List<double>();

            playerTickLog.enemyCount = new List<int>();
            playerTickLog.closestEnemyId = new List<int>();
            playerTickLog.closestEnemyX = new List<float>();
            playerTickLog.closestEnemyY = new List<float>();
            playerTickLog.closestEnemyZ = new List<float>();
            playerTickLog.closestEnemyDist = new List<float>();
            playerTickLog.allEnemies = new List<string>();

            playerTickLog.time.Clear();
            playerTickLog.mouseX.Clear();
            playerTickLog.mouseY.Clear();

            targetMarked = false;
            targetShot = false;


        }

        private void UnequipWeapon()
        {
            DisableAim();

            actionState = FPSActionState.WeaponChange;
            GetAnimGraph().GetFirstPersonAnimator().CrossFade(UnEquip, 0.1f);
        }

        public void ResetActionState()
        {
            actionState = FPSActionState.None;
        }

        public void RefreshStagedState()
        {
        }

        public void ResetStagedState()
        {
        }

        private void EquipWeapon()
        {
            if (weapons.Count == 0) return;

            weapons[_lastIndex].gameObject.SetActive(false);
            var gun = weapons[_currentWeaponIndex];

            _bursts = gun.burstAmount;

            InitWeapon(gun);
            gun.gameObject.SetActive(true);

            animator.SetFloat(OverlayType, (float)gun.overlayType);
            actionState = FPSActionState.None;
        }

        private void EnableUnarmedState()
        {
            if (weapons.Count == 0) return;

            weapons[_currentWeaponIndex].gameObject.SetActive(false);
            animator.SetFloat(OverlayType, 0);
        }

        private void DisableAim()
        {
            if (!GetGun().canAim) return;

            aimState = FPSAimState.None;
            OnInputAim(false);

            adsLayer.SetAds(false);
            adsLayer.SetPointAim(false);
            swayLayer.SetFreeAimEnable(true);
            swayLayer.SetLayerAlpha(1f);
        }

        public void ToggleAim()
        {
            if (!GetGun().canAim) return;

            slotLayer.PlayMotion(aimMotionAsset);

            if (!IsAiming())
            {
                aimState = FPSAimState.Aiming;
                OnInputAim(true);

                adsLayer.SetAds(true);
                swayLayer.SetFreeAimEnable(false);
                swayLayer.SetLayerAlpha(0.5f);
            }
            else
            {
                DisableAim();
            }

            recoilComponent.isAiming = IsAiming();
        }

        public void StartAiming()
        {
            if (!GetGun().canAim) return;

            slotLayer.PlayMotion(aimMotionAsset);

            if (!IsAiming())
            {
                aimState = FPSAimState.Aiming;
                OnInputAim(true);

                adsLayer.SetAds(true);
                swayLayer.SetFreeAimEnable(false);
                swayLayer.SetLayerAlpha(0.5f);
            }

            recoilComponent.isAiming = IsAiming();
        }

        public void StopAiming()
        {
            slotLayer.PlayMotion(aimMotionAsset);
            DisableAim();
            recoilComponent.isAiming = IsAiming();
        }

        public void ChangeScope()
        {
            InitAimPoint(GetGun());
        }

        private void Fire()
        {
            if (HasActiveAction()) return;

            if (GetGun().currentAmmoCount <= 0)
            {
                OnFireReleased();
                return;
            }

            GetGun().currentAmmoCount--;



            GetGun().weaponAudioSource.PlayOneShot(GetGun().fireSFX);
            fireTimer = 60 / GetGun().fireRate;

            shotsFiredPerRound++;

            if (muzzleFlash != null && roundManager.isPlayerVFXEnabled)
            {
                ParticleSystem PE = Instantiate(muzzleFlash, GetGun().shootPoint.transform.position, GetGun().shootPoint.transform.rotation);
                PE.transform.SetParent(GetGun().shootPoint.transform);
            }


            if (hit.collider != null)
            {
                if (bulletHitPE != null && roundManager.isPlayerVFXEnabled)
                    Instantiate(bulletHitPE, hit.point, Quaternion.LookRotation(hit.normal));
                /*if (hit.collider.gameObject.name == "Enemy(Clone)")
                {
                    hit.collider.gameObject.GetComponent<Enemy>().TakeDamage(GetGun().bulletDamage);
                    regularHitCooldown = .1f;
                    PlayHitRegSFX();
                    shotsHitPerRound++;
                    score++;
                }
                else*/
                if (hit.collider.gameObject.name == "Head")
                {
                    if (!targetShot)
                    {
                        targetShot = true;
                    }
                    hit.collider.gameObject.GetComponentInParent<Enemy>().TakeDamage(GetGun().bulletDamage * 5);
                    headshotCooldown = .2f;
                    PlayHeadshotSFX();
                    shotsHitPerRound++;
                    headshotsHitPerRound++;
                    score += onHitScore;
                }
                else
                {
                    score += onMissScore;
                }
            }


            GetGun().OnFire();
            PlayAnimation(GetGun().fireClip);

            PlayCameraShake(GetGun().cameraShake);

            if (GetGun().recoilPattern != null)
            {
                float aimRatio = IsAiming() ? GetGun().recoilPattern.aimRatio : 1f;
                float hRecoil = Random.Range(GetGun().recoilPattern.horizontalVariation.x,
                    GetGun().recoilPattern.horizontalVariation.y);
                _controllerRecoil += new Vector2(hRecoil, _recoilStep) * aimRatio;
            }

            if (recoilComponent == null || GetGun().weaponAsset.recoilData == null)
            {
                return;
            }

            recoilComponent.Play();

            if (recoilComponent.fireMode == FireMode.Burst)
            {
                if (_bursts == 0)
                {
                    OnFireReleased();
                    return;
                }

                _bursts--;
            }

            if (recoilComponent.fireMode == FireMode.Semi)
            {
                _isFiring = false;
                return;
            }

            _recoilStep += GetGun().recoilPattern.acceleration;


        }

        private void OnFirePressed()
        {
            if (weapons.Count == 0 || HasActiveAction()) return;

            if (Mathf.Approximately(GetGun().fireRate, 0f))
            {
                return;
            }

            if (Time.unscaledTime - _lastRecoilTime < 60f / GetGun().fireRate)
            {
                return;
            }

            _lastRecoilTime = Time.unscaledTime;
            _bursts = GetGun().burstAmount - 1;

            if (GetGun().recoilPattern != null)
            {
                _recoilStep = GetGun().recoilPattern.step;
            }

            _isFiring = true;
            Fire();
        }

        public Weapon GetGun()
        {
            if (weapons.Count == 0) return null;

            return weapons[_currentWeaponIndex];
        }

        private void OnFireReleased()
        {
            if (weapons.Count == 0) return;

            if (recoilComponent != null)
            {
                recoilComponent.Stop();
            }

            _recoilStep = 0f;
            _isFiring = false;
            //CancelInvoke(nameof(Fire));
        }

        private void OnMoveStarted()
        {
            if (slotLayer != null)
            {
                slotLayer.PlayMotion(onStartStopMoving);
            }

            if (movementComponent.PoseState == FPSPoseState.Prone)
            {
                locoLayer.BlendInIkPose(pronePose);
            }
        }

        private void OnMoveEnded()
        {
            if (slotLayer != null)
            {
                slotLayer.PlayMotion(onStartStopMoving);
            }

            if (movementComponent.PoseState == FPSPoseState.Prone)
            {
                locoLayer.BlendOutIkPose();
            }
        }

        private void OnSlideStarted()
        {
            lookLayer.SetLayerAlpha(0.3f);
            GetAnimGraph().GetBaseAnimator().CrossFade("Sliding", 0.1f);
        }

        private void OnSlideEnded()
        {
            lookLayer.SetLayerAlpha(1f);
        }

        private void OnSprintStarted()
        {
            OnFireReleased();
            DisableAim();

            lookLayer.SetLayerAlpha(0.5f);
            collisionLayer.SetLayerAlpha(0f);

            if (GetGun().overlayType == Runtime.OverlayType.Rifle)
            {
                locoLayer.BlendInIkPose(sprintPose);

            }

            aimState = FPSAimState.None;

            if (recoilComponent != null)
            {
                recoilComponent.Stop();
            }
        }

        private void OnSprintEnded()
        {
            lookLayer.SetLayerAlpha(1f);
            adsLayer.SetLayerAlpha(1f);
            locoLayer.BlendOutIkPose();
            collisionLayer.SetLayerAlpha(1f);
        }

        private void OnCrouch()
        {
            lookLayer.SetPelvisWeight(0f);
            animator.SetBool(Crouching, true);
            slotLayer.PlayMotion(crouchMotionAsset);

            GetAnimGraph().GetFirstPersonAnimator().SetFloat("MovementPlayRate", .7f);
        }

        private void OnUncrouch()
        {
            lookLayer.SetPelvisWeight(1f);
            animator.SetBool(Crouching, false);
            slotLayer.PlayMotion(unCrouchMotionAsset);

            GetAnimGraph().GetFirstPersonAnimator().SetFloat("MovementPlayRate", 1f);
        }

        private void OnJump()
        {
            slotLayer.PlayMotion(onJumpMotionAsset);
        }

        private void OnLand()
        {
            slotLayer.PlayMotion(onLandedMotionAsset);
        }

        private void TryReload(bool isAuto)
        {
            if (HasActiveAction()) return;

            if (GetGun().currentAmmoCount >= GetGun().magSize)
                return;

            GetGun().currentAmmoCount = GetGun().magSize;
            realoadCountPerRound++;
            if (!isAuto)
                tacticalReloadCountPerRound++;
            if (isReloadSpikeEnabled)
            {
                gameManager.isEventBasedDelay = true;
                perRoundReloadSpikeCount++;
            }

            var reloadClip = GetGun().reloadClip;

            if (reloadClip == null) return;

            OnFireReleased();

            PlayAnimation(reloadClip);
            GetGun().Reload();
            actionState = FPSActionState.Reloading;
        }

        private void TryGrenadeThrow()
        {
            if (HasActiveAction()) return;
            if (GetGun().grenadeClip == null) return;

            OnFireReleased();
            DisableAim();
            PlayAnimation(GetGun().grenadeClip);
            actionState = FPSActionState.Reloading;
        }

        private bool _isLeaning;

        private void ChangeWeapon_Internal(int newIndex)
        {
            if (newIndex == _currentWeaponIndex || newIndex > weapons.Count - 1)
            {
                return;
            }

            _lastIndex = _currentWeaponIndex;
            _currentWeaponIndex = newIndex;

            OnFireReleased();

            UnequipWeapon();
            Invoke(nameof(EquipWeapon), equipDelay);
        }

        private void HandleWeaponChangeInput()
        {
            if (movementComponent.PoseState == FPSPoseState.Prone) return;
            if (HasActiveAction() || weapons.Count == 0) return;

            if (Input.GetKeyDown(KeyCode.F))
            {
                //ChangeWeapon_Internal(_currentWeaponIndex + 1 > weapons.Count - 1 ? 0 : _currentWeaponIndex + 1);
                return;
            }

            for (int i = (int)KeyCode.Alpha1; i <= (int)KeyCode.Alpha9; i++)
            {
                if (Input.GetKeyDown((KeyCode)i))
                {
                    //ChangeWeapon_Internal(i - (int)KeyCode.Alpha1); //weapon change blocked
                }
            }
        }

        // --- Scripted-input helpers (synthesize Down/Up from held flags) ---
        private bool FireDown() => useScriptedInput ? (scriptedInput.fire && !_prevFireHeld) : Input.GetKeyDown(KeyCode.Mouse0);
        private bool FireUp() => useScriptedInput ? (!scriptedInput.fire && _prevFireHeld) : Input.GetKeyUp(KeyCode.Mouse0);
        private bool AimDown() => useScriptedInput ? (scriptedInput.aim && !_prevAimHeld) : Input.GetKeyDown(KeyCode.Mouse1);
        private bool AimUp() => useScriptedInput ? (!scriptedInput.aim && _prevAimHeld) : Input.GetKeyUp(KeyCode.Mouse1);
        private bool ReloadDown() => useScriptedInput ? (scriptedInput.reload && !_prevReloadHeld) : Input.GetKeyDown(KeyCode.R);
        private bool GrenadeDown() => useScriptedInput ? (scriptedInput.grenade && !_prevGrenadeHeld) : Input.GetKeyDown(KeyCode.G);

        private void UpdateActionInput()
        {
            if (movementComponent.MovementState == FPSMovementState.Sprinting)
            {
                return;
            }

            if (!isPlayerReady || !isQoeDisabled || !isAcceptabilityDisabled) return;

            if (ReloadDown())
            {
                TryReload(false);

            }

            if (GrenadeDown())
            {
                TryGrenadeThrow();
            }

            HandleWeaponChangeInput();

            if (aimState != FPSAimState.Ready)
            {
                bool wasLeaning = _isLeaning;



                bool rightLean = !useScriptedInput && Input.GetKey(KeyCode.E);
                bool leftLean = !useScriptedInput && Input.GetKey(KeyCode.Q);

                //_isLeaning = rightLean || leftLean; // LEANING OFF

                if (_isLeaning != wasLeaning)
                {
                    slotLayer.PlayMotion(leanMotionAsset);
                    charAnimData.SetLeanInput(wasLeaning ? 0f : rightLean ? -startLean : startLean);
                }

                if (_isLeaning)
                {
                    float leanValue = Input.GetAxis("Mouse ScrollWheel") * smoothLeanStep;
                    charAnimData.AddLeanInput(leanValue);
                }

                if (FireDown())
                {
                    OnFirePressed();
                    if (clickToPhotonIMG.isActiveAndEnabled)
                        clickToPhotonIMG.color = new UnityEngine.Color(.8f, .8f, .8f, 1f);
                }

                if (FireUp())
                {
                    OnFireReleased();
                    if (clickToPhotonIMG.isActiveAndEnabled)
                        clickToPhotonIMG.color = new UnityEngine.Color(.2f, .2f, .2f, 1f);
                }

                if (AimDown())
                {
                    StartAiming();
                }
                if (AimUp())
                {
                    StopAiming();
                }

                if (!useScriptedInput && Input.GetKeyDown(KeyCode.V))
                {
                    //ChangeScope(); // SIDESCOPING OFF
                }

                if (!useScriptedInput && Input.GetKeyDown(KeyCode.B) && IsAiming())
                {
                    if (aimState == FPSAimState.PointAiming)
                    {
                        adsLayer.SetPointAim(false);
                        aimState = FPSAimState.Aiming;
                    }
                    else
                    {
                        adsLayer.SetPointAim(true);
                        aimState = FPSAimState.PointAiming;
                    }
                }
            }

            if (!useScriptedInput && Input.GetKeyDown(KeyCode.H))
            {
                if (aimState == FPSAimState.Ready)
                {
                    aimState = FPSAimState.None;
                    lookLayer.SetLayerAlpha(1f);
                }
                else
                {
                    aimState = FPSAimState.Ready;
                    lookLayer.SetLayerAlpha(.5f);
                    OnFireReleased();
                }
            }

            // Update previous-frame held flags for next frame's edge detection.
            if (useScriptedInput)
            {
                _prevFireHeld = scriptedInput.fire;
                _prevAimHeld = scriptedInput.aim;
                _prevReloadHeld = scriptedInput.reload;
                _prevGrenadeHeld = scriptedInput.grenade;
            }
            else
            {
                _prevFireHeld = Input.GetKey(KeyCode.Mouse0);
                _prevAimHeld = Input.GetKey(KeyCode.Mouse1);
                _prevReloadHeld = Input.GetKey(KeyCode.R);
                _prevGrenadeHeld = Input.GetKey(KeyCode.G);
            }
        }

        private Quaternion desiredRotation;
        private Quaternion moveRotation;
        private float turnProgress = 1f;
        private bool isTurning = false;

        private void TurnInPlace()
        {
            float turnInput = _playerInput.x;
            _playerInput.x = Mathf.Clamp(_playerInput.x, -90f, 90f);
            turnInput -= _playerInput.x;

            //Debug.Log("Player input x:" + _playerInput.x);

            float sign = Mathf.Sign(_playerInput.x);
            if (Mathf.Abs(_playerInput.x) > turnInPlaceAngle)
            {
                if (!isTurning)
                {
                    turnProgress = 0f;

                    animator.ResetTrigger(TurnRight);
                    animator.ResetTrigger(TurnLeft);

                    animator.SetTrigger(sign > 0f ? TurnRight : TurnLeft);
                }

                isTurning = true;
            }
            isTurning = true;
            transform.rotation *= Quaternion.Euler(0f, turnInput, 0f);

            float lastProgress = turnCurve.Evaluate(turnProgress);
            turnProgress += Time.deltaTime * turnSpeed;
            turnProgress = Mathf.Min(turnProgress, 1f);

            float deltaProgress = turnCurve.Evaluate(turnProgress) - lastProgress;

            _playerInput.x -= sign * turnInPlaceAngle * deltaProgress;

            transform.rotation *= Quaternion.Slerp(Quaternion.identity,
                Quaternion.Euler(0f, sign * turnInPlaceAngle, 0f), deltaProgress);

            if (Mathf.Approximately(turnProgress, 1f) && isTurning)
            {
                isTurning = false;
            }
        }

        private float _jumpState = 0f;

        private void UpdateLookInput()
        {
            deltaMouseX = 0;
            deltaMouseY = 0;
            //UpdateReticle();
            if (isPlayerReady && isQoeDisabled && isAcceptabilityDisabled)
            {
                _freeLook = !useScriptedInput && Input.GetKey(KeyCode.X);

                if (useScriptedInput)
                {
                    // Scripted input: already-scaled deltas (we skip the sensitivity multiply so
                    // the teaser has direct control in deg/sec units).
                    deltaMouseX = scriptedInput.mouseX;
                    deltaMouseY = -scriptedInput.mouseY;
                }
                else
                {
                    deltaMouseX = Input.GetAxis("Mouse X") * sensitivity;
                    deltaMouseY = -Input.GetAxis("Mouse Y") * sensitivity;
                }

                delXCumilative += Mathf.Abs(deltaMouseX);
                delYCumilative += Mathf.Abs(deltaMouseY);

                if (!targetMarked)
                {
                    degreeToTargetX += Mathf.Abs(deltaMouseX);
                    degreeToTargetY += Mathf.Abs(deltaMouseY);
                    timeToTargetEnemy += Time.deltaTime;
                }
                if (!targetShot)
                {
                    degreeToShootX += Mathf.Abs(deltaMouseX);
                    degreeToShootY += Mathf.Abs(deltaMouseY);
                    timeToHitEnemy += Time.deltaTime;
                }
                timeToKillEnemy += Time.deltaTime;

            }

            if (Mathf.Abs(deltaMouseX) > mouseSpikeDegreeOfMovement && isMouseMovementSpikeEnabled)
            {
                if (mouseSpikeCooldown <= 0)
                {
                    gameManager.isEventBasedDelay = true;
                    mouseSpikeCooldown = mouseSpikeDelay;
                    perRoundMouseMovementSpikeCount++;
                }
            }

            if (_freeLook)
            {
                // No input for both controller and animation component. We only want to rotate the camera
                _freeLookInput.x += deltaMouseX;
                _freeLookInput.y += deltaMouseY;

                _freeLookInput.x = Mathf.Clamp(_freeLookInput.x, -freeLookAngle.x, freeLookAngle.x);
                _freeLookInput.y = Mathf.Clamp(_freeLookInput.y, -freeLookAngle.y, freeLookAngle.y);

                return;
            }

            _freeLookInput = Vector2.Lerp(_freeLookInput, Vector2.zero,
                FPSAnimLib.ExpDecayAlpha(15f, Time.deltaTime));

            _playerInput.x += deltaMouseX;
            _playerInput.y += deltaMouseY;

            float proneWeight = animator.GetFloat("ProneWeight");
            Vector2 pitchClamp = Vector2.Lerp(new Vector2(-90f, 90f), new Vector2(-30, 0f), proneWeight);

            _playerInput.y = Mathf.Clamp(_playerInput.y, pitchClamp.x, pitchClamp.y);
            moveRotation *= Quaternion.Euler(0f, deltaMouseX, 0f);

            TurnInPlace();

            _jumpState = Mathf.Lerp(_jumpState, movementComponent.IsInAir() ? 1f : 0f,
                FPSAnimLib.ExpDecayAlpha(10f, Time.deltaTime));

            float moveWeight = Mathf.Clamp01(movementComponent.AnimatorVelocity.magnitude);
            transform.rotation = Quaternion.Slerp(transform.rotation, moveRotation, moveWeight);
            transform.rotation = Quaternion.Slerp(transform.rotation, moveRotation, _jumpState);
            _playerInput.x *= 1f - moveWeight;
            _playerInput.x *= 1f - _jumpState;

            charAnimData.SetAimInput(_playerInput);
            charAnimData.AddDeltaInput(new Vector2(deltaMouseX, charAnimData.deltaAimInput.y));
        }

        private Quaternion lastRotation;
        private Vector2 _cameraRecoilOffset;

        private void UpdateRecoil()
        {
            if (Mathf.Approximately(_controllerRecoil.magnitude, 0f)
                && Mathf.Approximately(_cameraRecoilOffset.magnitude, 0f))
            {
                return;
            }

            float smoothing = 8f;
            float restoreSpeed = 8f;
            float cameraWeight = 0f;

            if (GetGun().recoilPattern != null)
            {
                smoothing = GetGun().recoilPattern.smoothing;
                restoreSpeed = GetGun().recoilPattern.cameraRestoreSpeed;
                cameraWeight = GetGun().recoilPattern.cameraWeight;
            }

            _controllerRecoil = Vector2.Lerp(_controllerRecoil, Vector2.zero,
                FPSAnimLib.ExpDecayAlpha(smoothing, Time.deltaTime));

            _playerInput += _controllerRecoil * Time.deltaTime;

            Vector2 clamp = Vector2.Lerp(Vector2.zero, new Vector2(90f, 90f), cameraWeight);
            _cameraRecoilOffset -= _controllerRecoil * Time.deltaTime;
            _cameraRecoilOffset = Vector2.ClampMagnitude(_cameraRecoilOffset, clamp.magnitude);

            if (_isFiring) return;

            _cameraRecoilOffset = Vector2.Lerp(_cameraRecoilOffset, Vector2.zero,
                FPSAnimLib.ExpDecayAlpha(restoreSpeed, Time.deltaTime));
        }

        public void OnWarpStarted()
        {
            movementComponent.enabled = false;
            GetComponent<CharacterController>().enabled = false;
        }

        public void OnWarpEnded()
        {
            movementComponent.enabled = true;
            GetComponent<CharacterController>().enabled = true;
        }

        private void Update()
        {

            if (!useScriptedInput && Input.GetKeyDown(KeyCode.Tab))
            {
                isPlayerReady = true;
            }

            if (!isQoeDisabled || !isAcceptabilityDisabled)
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }
            else
            {
                Cursor.visible = false;
                Cursor.lockState = CursorLockMode.Locked;
            }

            if (!isPlayerReady || !isQoeDisabled || !isAcceptabilityDisabled)
            {
                //OnSprintEnded();
                UpdateAnimController();
                OnFireReleased();
                StopAiming();
                UpdateCooldowns();
                UpdateActionInput();
                UpdateLookInput();
                OnSprintStarted();

                //Time.timeScale = 0.0f;
                return;
            }
            //Time.timeScale = 1.0f;
            UpdateReticle();
            UpdateActionInput();
            UpdateLookInput();
            UpdateRecoil();


            if (_isFiring)
            {
                fireTimer -= Time.deltaTime;

                isFiringDurationPerRound += Time.deltaTime;

                if (fireTimer < 0)
                {
                    Fire();
                }
            }
            else if (fireTimer > 0)
            {
                fireTimer -= Time.deltaTime;
            }

            charAnimData.moveInput = movementComponent.AnimatorVelocity;
            UpdateAnimController();
            UpdateCooldowns();

            if (deathTimeOut > 0)
            {
                deathTimeOut -= Time.deltaTime;
            }

            Vector3 distanceVector = transform.position - oldPosition;

            if (distanceVector.magnitude < 1)
                distanceTravelledPerRound += distanceVector.magnitude;
            oldPosition = transform.position;

            if (aimState == FPSAimState.Aiming)
            {
                aimDurationPerRound += Time.deltaTime;
            }
        }

        // Reused every tick so the per-frame log does not allocate a new builder.
        private readonly System.Text.StringBuilder _enemyListBuilder = new System.Text.StringBuilder(128);

        public void UpdatePlayerLog(double deltaTime)
        {
            playerTickLog.time.Add(System.DateTime.Now.ToString());
            playerTickLog.mouseX.Add(deltaMouseX);
            playerTickLog.mouseY.Add(deltaMouseY);

            playerTickLog.playerX.Add(transform.position.x);
            playerTickLog.playerY.Add(transform.position.y);
            playerTickLog.playerZ.Add(transform.position.z);

            float elapsed = roundManager.roundDuration - roundManager.roundTimer;
            playerTickLog.scorePerSec.Add(elapsed > 0f ? score / elapsed : 0f);

            playerTickLog.roundTimer.Add(elapsed);

            playerTickLog.playerRot.Add(this.transform.rotation);
            playerTickLog.isADS.Add(IsAiming());

            playerTickLog.frameTimeMS.Add(deltaTime);

            LogEnemyTickState();

            //Debug.Log("TICK: " + Time.deltaTime * 1000.0);

            //Debug.Log("Aim: " + degreeToTargetX + "  " +targetMarked);
            //Debug.Log("Shoot: " + degreeToShootX + "  " + targetShot);
        }

        /// <summary>
        /// Records the whole live enemy set for this tick: a count, the closest
        /// enemy broken out into its own columns, and every enemy packed into one
        /// quoted cell as "id:x|y|z;id:x|y|z".
        /// </summary>
        private void LogEnemyTickState()
        {
            List<Enemy> enemies = enemyManager != null ? enemyManager.GetAllEnemies() : null;

            int count = 0;
            int closestId = -1;
            Vector3 closestPos = Vector3.zero;
            float closestDist = -1f;
            float closestSqr = float.MaxValue;

            _enemyListBuilder.Length = 0;

            if (enemies != null)
            {
                Vector3 me = transform.position;
                for (int i = 0; i < enemies.Count; i++)
                {
                    Enemy e = enemies[i];
                    if (e == null) continue;

                    Vector3 p = e.transform.position;
                    count++;

                    if (_enemyListBuilder.Length > 0) _enemyListBuilder.Append(';');
                    _enemyListBuilder.Append(e.enemyId).Append(':')
                        .Append(p.x.ToString("F2", CsvUtil.Inv)).Append('|')
                        .Append(p.y.ToString("F2", CsvUtil.Inv)).Append('|')
                        .Append(p.z.ToString("F2", CsvUtil.Inv));

                    float d = (p - me).sqrMagnitude;
                    if (d < closestSqr)
                    {
                        closestSqr = d;
                        closestId = e.enemyId;
                        closestPos = p;
                    }
                }
                if (closestId >= 0) closestDist = Mathf.Sqrt(closestSqr);
            }

            playerTickLog.enemyCount.Add(count);
            playerTickLog.closestEnemyId.Add(closestId);
            playerTickLog.closestEnemyX.Add(closestPos.x);
            playerTickLog.closestEnemyY.Add(closestPos.y);
            playerTickLog.closestEnemyZ.Add(closestPos.z);
            playerTickLog.closestEnemyDist.Add(closestDist);
            playerTickLog.allEnemies.Add(_enemyListBuilder.ToString());
        }

        void UpdateCooldowns()
        {
            if (killCooldown > 0)
                killCooldown -= Time.deltaTime;

            if (regularHitCooldown > 0)
                regularHitCooldown -= Time.deltaTime;

            if (headshotCooldown > 0)
                headshotCooldown -= Time.deltaTime;

            if (GetGun().currentAmmoCount <= 0)
                TryReload(true);

            if (mouseSpikeCooldown > 0)
                mouseSpikeCooldown -= Time.deltaTime;

            if (aimSpikeCooldown > 0)
                aimSpikeCooldown -= Time.deltaTime;

            if (shotsFiredPerRound > 0)
                liveAccuracy = (float)shotsHitPerRound / (float)shotsFiredPerRound;
            else
                liveAccuracy = 0;
        }

        public void UpdateCameraRotation()
        {
            Vector2 input = _playerInput;
            input += _cameraRecoilOffset;

            (Quaternion, Vector3) cameraTransform =
                (transform.rotation * Quaternion.Euler(input.y, input.x, 0f),
                    firstPersonCamera.position);

            cameraHolder.rotation = cameraTransform.Item1;
            cameraHolder.position = cameraTransform.Item2;

            mainCamera.rotation = cameraHolder.rotation * Quaternion.Euler(_freeLookInput.y, _freeLookInput.x, 0f);
        }

        public void UpdateReticle()
        {

            Transform muzzlePointTransform = GetGun().shootPoint.transform;
            Vector3 targetPoint = muzzlePointTransform.position + muzzlePointTransform.TransformDirection(Vector3.forward);
            Vector3 directionWithoutSpread = targetPoint - muzzlePointTransform.position;
            // Does the ray intersect any objects excluding the player layer
            if (Physics.Raycast(muzzlePointTransform.position, directionWithoutSpread, out hit, Mathf.Infinity, ~enemyLargeColliderLayer))
            {
                reticleObject.gameObject.SetActive(true);
                reticleObject.transform.position = hit.point;

                float distanceToReticle = Vector3.Distance(muzzlePointTransform.position, hit.point);

                reticleObject.transform.localScale = new Vector3(reticleSizeMultiplier, reticleSizeMultiplier, reticleSizeMultiplier) * (0.007f + distanceToReticle / 200);


            }
            else
            {
                reticleObject.gameObject.SetActive(false);
                enemyAimedFuse = false;
            }
            RaycastHit largeColliderHit;
            if (isAimSpikeEnabled && Physics.Raycast(muzzlePointTransform.position, directionWithoutSpread, out largeColliderHit, Mathf.Infinity, enemyLargeColliderLayer))
            {
                if (enemyAimedFuse == false && aimSpikeCooldown <= 0)
                {
                    gameManager.isEventBasedDelay = true;
                    perRoundAimSpikeCount++;
                    aimSpikeCooldown = aimSpikeDelay;
                }
                enemyAimedFuse = true;
            }
            else
            {
                enemyAimedFuse = false;
            }

            if (hit.collider != null && hit.collider.gameObject.name == "Head")
            {
                targetMarked = true;
            }
        }

        void PlayWeaponChangeSFX()
        {
            playerAudioSource.volume = UnityEngine.Random.Range(0.1f, 0.3f);
            playerAudioSource.pitch = UnityEngine.Random.Range(0.9f, 1.1f);

            playerAudioSource.PlayOneShot(weaponChangeSFX);
        }

        void PlayADSInSFX()
        {
            playerAudioSource.volume = UnityEngine.Random.Range(0.15f, 0.2f);
            playerAudioSource.pitch = UnityEngine.Random.Range(0.9f, 1.05f);

            playerAudioSource.PlayOneShot(ADSInSFX);
        }

        void PlayADSOutSFX()
        {
            playerAudioSource.volume = UnityEngine.Random.Range(0.15f, 0.2f);
            playerAudioSource.pitch = UnityEngine.Random.Range(0.95f, 1.05f);

            playerAudioSource.PlayOneShot(ADSOutSFX);
        }

        void PlayHitRegSFX()
        {
            playerAudioSource.volume = UnityEngine.Random.Range(1f, 1.2f);
            playerAudioSource.pitch = UnityEngine.Random.Range(0.95f, 1.05f);

            playerAudioSource.PlayOneShot(hitRegSFX);
        }

        public void PlayKillSFX()
        {
            playerAudioSource.volume = UnityEngine.Random.Range(1f, 1.2f);
            playerAudioSource.pitch = UnityEngine.Random.Range(0.95f, 1.05f);

            playerAudioSource.PlayOneShot(killSFX);
            targetShot = false;
        }

        void PlayHeadshotSFX()
        {
            playerAudioSource.volume = UnityEngine.Random.Range(1f, 1.2f);
            playerAudioSource.pitch = UnityEngine.Random.Range(0.95f, 1.05f);

            playerAudioSource.PlayOneShot(headshotSFX);
        }

        public void PlayDeathSFX()
        {
            playerAudioSource.pitch = UnityEngine.Random.Range(0.95f, 1.05f);
            playerAudioSource.PlayOneShot(deathSFX);
        }

        public void RespawnPlayer()
        {
            RespawnPlayer(Enemy.DespawnReason.PlayerDeath);
        }

        public void RespawnPlayer(Enemy.DespawnReason reason)
        {
            roundDeaths++;
            score += onDeathScore;

            movementComponent.enabled = false;
            GetComponent<CharacterController>().enabled = false;
            this.transform.position = playerSpawnPoint.position;
            GetComponent<CharacterController>().enabled = true;
            movementComponent.enabled = true;

            enemyManager.DestroyAllEnemy(reason);
            GetGun().currentAmmoCount = GetGun().magSize;

            deathTimeOut = 1.5f;
            targetMarked = false;
            targetShot = false;
        }

        public void ResetPlayerAndDestroyEnemy()
        {
            movementComponent.enabled = false;
            GetComponent<CharacterController>().enabled = false;
            this.transform.position = playerSpawnPoint.position;
            GetComponent<CharacterController>().enabled = true;
            movementComponent.enabled = true;

            enemyManager.DestroyAllEnemy(Enemy.DespawnReason.RoundReset);
        }

        public void ResetRound()
        {
            GetGun().currentAmmoCount = GetGun().magSize;

            score = 0;
            roundKills = 0;
            roundDeaths = 0;
            distanceTravelledPerRound = 0;
            shotsFiredPerRound = 0;
            shotsHitPerRound = 0;
            headshotsHitPerRound = 0;
            delXCumilative = 0;
            delYCumilative = 0;
            realoadCountPerRound = 0;
            tacticalReloadCountPerRound = 0;
            spikeDurationCumulative = 0;
            perRoundMouseMovementSpikeCount = 0;
            perRoundAimSpikeCount = 0;
            perRoundReloadSpikeCount = 0;
            perRoundEnemySpawnSpikeCount = 0;

            degreeToTargetXCumulative = 0;
            degreeToShootXCumulative = 0;
            minAnlgeToEnemyCumulative = 0;

            timeToTargetEnemyCumulative = 0;
            timeToHitEnemyCumulative = 0;
            timeToKillEnemyCumulative = 0;

            enemySizeCumulative = 0;
            degreeToShootX = 0;
            degreeToShootY = 0;
            degreeToTargetX = 0;
            degreeToTargetY = 0;
            aimDurationPerRound = 0;
            isFiringDurationPerRound = 0;

            targetMarked = false;
            targetShot = false;

            playerTickLog.time.Clear();
            playerTickLog.mouseX.Clear();
            playerTickLog.mouseY.Clear();

            playerTickLog.playerX.Clear();
            playerTickLog.playerY.Clear();
            playerTickLog.playerZ.Clear();

            playerTickLog.scorePerSec.Clear();
            playerTickLog.roundTimer.Clear();

            playerTickLog.playerRot.Clear();
            playerTickLog.isADS.Clear();

            playerTickLog.frameTimeMS.Clear();

            playerTickLog.enemyCount.Clear();
            playerTickLog.closestEnemyId.Clear();
            playerTickLog.closestEnemyX.Clear();
            playerTickLog.closestEnemyY.Clear();
            playerTickLog.closestEnemyZ.Clear();
            playerTickLog.closestEnemyDist.Clear();
            playerTickLog.allEnemies.Clear();
        }

        // This function calculates the angular size (in degrees) of a sphere as seen from a reference point.
        public float CalculateAngularSize(GameObject sphere, Vector3 referencePoint)
        {
            // Get the sphere's position
            Vector3 spherePosition = sphere.transform.position;

            // Calculate the sphere's diameter using its scale (assuming it's a uniform scale sphere)
            float sphereDiameter = sphere.transform.localScale.x; // Assuming the sphere is uniformly scaled

            // Calculate the horizontal distance from the reference point to the sphere's center
            Vector3 horizontalVector = new Vector3(spherePosition.x - referencePoint.x, 0, spherePosition.z - referencePoint.z);
            float horizontalDistanceToSphere = horizontalVector.magnitude;

            // Calculate the angular size in radians using horizontal distance
            float angularSizeInRadians = 2 * Mathf.Atan(sphereDiameter / (2 * horizontalDistanceToSphere));

            // Convert the angular size to degrees
            float angularSizeInDegrees = angularSizeInRadians * Mathf.Rad2Deg;

            return angularSizeInDegrees;
        }
    }
}