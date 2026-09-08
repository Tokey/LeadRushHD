using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Demo.Scripts.Runtime;

public class HDRPTeaserRunner : MonoBehaviour
{
    public enum PlayerControlMode
    {
        Auto,    // Bot drives the player: lock-on aim, tap-fire, deterministic spawns.
        Manual   // You drive the player normally. EnemyManager spawns its own enemies.
    }

    public enum RunMode { Cycle, Single }

    public enum GfxPreset
    {
        LeaveAsIs,
        AllOff,
        TexOnly,
        VfxOnly,
        LightOnly,
        TexVfx,
        TexLight,
        VfxLight,
        AllOn,
        Custom
    }

    [Header("Control Mode")]
    [Tooltip("Auto = bot plays; deterministic spawns; GFX advances on kills.\nManual = you play; EnemyManager spawns; GFX advances on a fixed clock.")]
    public PlayerControlMode playerControlMode = PlayerControlMode.Auto;

    [Header("Mode & Auto-Start")]
    public RunMode mode = RunMode.Cycle;
    public float autoStartDelay = 3.0f;
    public bool autoStart = true;
    public KeyCode startKey = KeyCode.Space;

    [Tooltip("Press this any time to toggle between Auto and Manual modes mid-run.")]
    public KeyCode toggleControlKey = KeyCode.M;

    [Header("Manual Mode � GFX Clock")]
    public float manualSecondsPerCombo = 1.0f;

    [Header("Auto Mode � Behavior Settings")]
    public int enemiesPerPhase = 2;
    public float waitBetweenKills = 1.0f;
    public float waitAfterPhase = 2.0f;

    [Header("Auto Mode � Deterministic Spawn")]
    public float spawnDistance = 12f;
    public float spawnAngleFromForward = 25f;
    public float spawnHeightOffset = 0f;
    public bool alternateFirstSideEachCycle = true;
    public Transform[] explicitSpawnPoints;

    [Header("Auto Mode � Lock-On Steering")]
    public float maxTurnSpeed = 720f;
    public float turnGain = 30f;
    public float adsAngle = 8f;

    [Header("Auto Mode � Tap-Fire")]
    public float muzzleFireAngle = 1.2f;
    public float settleTime = 0.10f;
    public int fireHoldFrames = 2;
    public float postShotCooldown = 0.18f;
    public float maxSettleWait = 0.6f;
    public float perEnemyTimeout = 25f;

    [Header("Stutter Compensation")]
    [Tooltip("Cap on the dt used by the steering controller. If a frame stutter blows past this, we treat it as a normal frame so the bot doesn't snap wildly. Recommended: 0.033 (~30 fps). Lower = harder cap.")]
    public float maxControlDt = 0.033f;
    [Tooltip("Hard cap on the angular delta the bot can request in a single frame, regardless of dt. Prevents 'going overboard' on stutters. Recommended: 6\u00b0\u201310\u00b0.")]
    public float maxDegPerFrame = 8f;
    [Tooltip("If the actual frame's dt exceeds this, freeze input for that frame (no movement, no fire). Useful for severe hitches where any input would look bad. Set to 0 to disable.")]
    public float freezeFrameDtThreshold = 0.10f;
    [Tooltip("Pause the settle-time accumulator on stuttery frames so the bot doesn't think it's settled when it's actually just frozen. ON = robust, OFF = legacy.")]
    public bool pauseSettleOnStutter = true;

    [Header("References")]
    public RoundManager roundManager;
    public FPSController fpsController;
    public FPSMovement fpsMovement;
    public EnemyManager enemyManager;

    [Header("Single-Mode GFX Choice")]
    public GfxPreset singlePreset = GfxPreset.AllOn;
    public bool customTex = true;
    public bool customVfx = true;
    public bool customLight = true;

    [Header("On-Screen Label")]
    public bool showLabel = true;
    public Vector2 labelPosition = new Vector2(20, 20);
    public Vector2 labelSize = new Vector2(900, 90);
    public int labelFontSize = 44;
    public Color labelColor = Color.white;
    public Color labelBackground = new Color(0f, 0f, 0f, 0.6f);

    // --- Internal ---
    private string currentLabel = "";
    private bool running = false;
    private int currentComboIndex = -1;
    private int spawnCounter = 0;
    private Coroutine activeMainLoop;

    static readonly bool[][] CycleCombos = new bool[][]
    {
        new bool[]{false, false, false},
        new bool[]{true,  false, false},
        new bool[]{false, true,  false},
        new bool[]{false, false, true },
        new bool[]{true,  true,  false},
        new bool[]{true,  false, true },
        new bool[]{false, true,  true },
        new bool[]{true,  true,  true },
    };
    static readonly string[] CycleNames = {
        "AllOff","Texures Only","VFX Only","Lighting Only","Texures + VFX","Texures + Lighting","VFX + Lighting","AllOn"
    };

    /// <summary>
    /// True if the current frame's dt looks like a stutter (above freezeFrameDtThreshold).
    /// Used to skip input writes and pause settle accumulation.
    /// </summary>
    bool IsStutterFrame()
    {
        return freezeFrameDtThreshold > 0f && Time.deltaTime > freezeFrameDtThreshold;
    }

    /// <summary>
    /// Clamped dt for the steering controller. Even on a 200ms stutter we'll
    /// only act as if maxControlDt seconds passed.
    /// </summary>
    float ControlDt()
    {
        return Mathf.Min(Time.deltaTime, maxControlDt);
    }

    void Start()
    {
        if (roundManager == null) roundManager = FindObjectOfType<RoundManager>();
        if (fpsController == null) fpsController = FindObjectOfType<FPSController>();
        if (fpsMovement == null && fpsController != null)
            fpsMovement = fpsController.GetComponent<FPSMovement>();
        if (enemyManager == null) enemyManager = FindObjectOfType<EnemyManager>();

        if (autoStart) StartCoroutine(AutoStartRoutine());
    }

    void Update()
    {
        if (!running && !autoStart && Input.GetKeyDown(startKey))
            BeginRunner();

        if (running && Input.GetKeyDown(toggleControlKey))
        {
            playerControlMode = playerControlMode == PlayerControlMode.Auto
                ? PlayerControlMode.Manual : PlayerControlMode.Auto;
            RestartMainLoop();
        }
    }

    IEnumerator AutoStartRoutine()
    {
        yield return new WaitForSeconds(autoStartDelay);
        BeginRunner();
    }

    void BeginRunner()
    {
        if (running) return;
        running = true;
        spawnCounter = 0;

        ApplyControlModeSetup();

        if (playerControlMode == PlayerControlMode.Auto)
            CleanInitialScene();

        RestartMainLoop();
    }

    void ApplyControlModeSetup()
    {
        if (playerControlMode == PlayerControlMode.Auto)
        {
            if (fpsController != null)
            {
                fpsController.isPlayerReady = true;
                fpsController.useScriptedInput = true;
            }
            if (fpsMovement != null) fpsMovement.useScriptedInput = true;
            if (enemyManager != null) enemyManager.enabled = false;
            WriteNeutralInput();
        }
        else
        {
            if (fpsController != null) fpsController.useScriptedInput = false;
            if (fpsMovement != null) fpsMovement.useScriptedInput = false;
            WriteNeutralInput();
            if (enemyManager != null) enemyManager.enabled = true;
        }
    }

    void RestartMainLoop()
    {
        if (activeMainLoop != null) StopCoroutine(activeMainLoop);
        ApplyControlModeSetup();
        activeMainLoop = StartCoroutine(
            playerControlMode == PlayerControlMode.Auto ? MainHuntLoop() : ManualGfxClockLoop()
        );
    }

    void CleanInitialScene()
    {
        // Go through the registry rather than the "Enemy" tag: the prefab tags
        // both its root and its EnemyLarge child, so a tag sweep can destroy the
        // child collider and leave a half-alive enemy behind.
        var live = new List<Enemy>(EnemyManager.Live);
        foreach (var e in live)
            if (e != null) Destroy(e.gameObject);
    }

    // ============================================================
    //  MANUAL MODE
    // ============================================================
    IEnumerator ManualGfxClockLoop()
    {
        int idx = 0;
        while (running && playerControlMode == PlayerControlMode.Manual)
        {
            if (mode == RunMode.Cycle)
            {
                ApplyComboByIndex(idx);
                idx = (idx + 1) % CycleCombos.Length;
            }
            else
            {
                ApplySinglePreset();
                currentLabel = BuildSingleLabel();
            }
            yield return new WaitForSeconds(Mathf.Max(0.01f, manualSecondsPerCombo));
        }
    }

    // ============================================================
    //  AUTO MODE
    // ============================================================
    IEnumerator MainHuntLoop()
    {
        yield return new WaitForSeconds(1f);
        int idx = 0;
        while (running && playerControlMode == PlayerControlMode.Auto)
        {
            if (mode == RunMode.Cycle)
            {
                ApplyComboByIndex(idx);
                idx = (idx + 1) % CycleCombos.Length;
            }
            else
            {
                ApplySinglePreset();
                currentLabel = BuildSingleLabel();
            }

            yield return new WaitForSeconds(0.5f);
            yield return HuntEnemiesPhase(enemiesPerPhase);
            yield return new WaitForSeconds(waitAfterPhase);
        }
    }

    IEnumerator HuntEnemiesPhase(int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (playerControlMode != PlayerControlMode.Auto) yield break;

            SpawnEnemyDeterministic(i);
            yield return new WaitForSeconds(1.0f);

            GameObject target = GetTargetEnemy();
            if (target != null)
                yield return EngageTarget(target);

            yield return new WaitForSeconds(waitBetweenKills);
        }
    }

    void SpawnEnemyDeterministic(int indexInPhase)
    {
        if (enemyManager == null || enemyManager.enemy == null) return;

        Vector3 spawnPos;
        if (explicitSpawnPoints != null && explicitSpawnPoints.Length > 0)
        {
            int idx = spawnCounter % explicitSpawnPoints.Length;
            spawnPos = explicitSpawnPoints[idx].position;
        }
        else
        {
            int slot = indexInPhase % 2;
            if (alternateFirstSideEachCycle)
            {
                int phaseNumber = spawnCounter / Mathf.Max(1, enemiesPerPhase);
                slot = (slot + phaseNumber) % 2;
            }
            float sign = slot == 0 ? -1f : +1f;
            float angleDeg = sign * spawnAngleFromForward;

            Transform p = enemyManager.player != null
                ? enemyManager.player.transform
                : (fpsController != null ? fpsController.transform : transform);

            Vector3 fwd = p.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            fwd.Normalize();

            Vector3 dir = Quaternion.AngleAxis(angleDeg, Vector3.up) * fwd;
            spawnPos = p.position + dir * spawnDistance;
            spawnPos.y += spawnHeightOffset;
        }

        Vector3 finalPos = enemyManager.GetRandomWalkablePositionNear(spawnPos, enemyManager.walkwableAreaRadius);
        Object.Instantiate(enemyManager.enemy, finalPos, Quaternion.identity);
        spawnCounter++;
    }

    GameObject GetTargetEnemy()
    {
        if (enemyManager != null) return enemyManager.GetClosestEnemy();
        return GameObject.FindGameObjectWithTag("Enemy");
    }

    IEnumerator EngageTarget(GameObject target)
    {
        Transform head = FindHead(target != null ? target.transform : null);
        float engagementStart = Time.time;

        while (target != null && target.activeInHierarchy && running
               && playerControlMode == PlayerControlMode.Auto)
        {
            if (Time.time - engagementStart > perEnemyTimeout) break;
            if (head == null) head = FindHead(target.transform);

            yield return AcquireUntilAds(target, () => head);
            if (target == null || !target.activeInHierarchy) break;

            yield return SettleUntilStable(target, () => head);
            if (target == null || !target.activeInHierarchy) break;

            yield return TapFire(target, () => head);
            yield return PostShotCooldown(target, () => head);
        }

        WriteNeutralInput();
    }

    IEnumerator AcquireUntilAds(GameObject target, System.Func<Transform> headFn)
    {
        while (target != null && target.activeInHierarchy && running
               && playerControlMode == PlayerControlMode.Auto)
        {
            // Skip the frame entirely if it's a severe stutter � better to do
            // nothing than overshoot the target.
            if (IsStutterFrame()) { WriteNeutralInput(); yield return null; continue; }

            Vector3 aimPoint;
            if (!TryGetAimPoint(target, headFn, out aimPoint)) { yield return null; continue; }

            float yawErr, pitchErr;
            if (!ComputeCameraErrorDeg(aimPoint, out yawErr, out pitchErr)) { yield return null; continue; }
            float absErr = Mathf.Sqrt(yawErr * yawErr + pitchErr * pitchErr);

            ApplySteering(yawErr, pitchErr, ads: true, fire: false);
            if (absErr <= adsAngle) yield break;
            yield return null;
        }
    }

    IEnumerator SettleUntilStable(GameObject target, System.Func<Transform> headFn)
    {
        float settleAccum = 0f;
        float waitStart = Time.time;

        while (target != null && target.activeInHierarchy && running
               && playerControlMode == PlayerControlMode.Auto)
        {
            bool stutter = IsStutterFrame();

            if (stutter) { WriteNeutralInput(); }
            else
            {
                Vector3 aimPoint;
                if (!TryGetAimPoint(target, headFn, out aimPoint)) { yield return null; continue; }

                float yawErr, pitchErr;
                if (!ComputeCameraErrorDeg(aimPoint, out yawErr, out pitchErr)) { yield return null; continue; }

                ApplySteering(yawErr, pitchErr, ads: true, fire: false);

                float muzzleErr = ComputeMuzzleErrorDeg(aimPoint);
                if (muzzleErr <= muzzleFireAngle)
                {
                    // Use ControlDt so a small overshoot stutter doesn't cheat the timer.
                    settleAccum += ControlDt();
                    if (settleAccum >= settleTime) yield break;
                }
                else settleAccum = 0f;
            }

            // Pause the wall-clock waitStart on stutter frames if requested,
            // so we don't time out during a hitch.
            if (stutter && pauseSettleOnStutter) waitStart += Time.deltaTime;

            if (Time.time - waitStart > maxSettleWait) yield break;
            yield return null;
        }
    }

    IEnumerator TapFire(GameObject target, System.Func<Transform> headFn)
    {
        int frames = Mathf.Max(1, fireHoldFrames);
        int fired = 0;
        while (fired < frames)
        {
            if (target == null || !target.activeInHierarchy || !running
                || playerControlMode != PlayerControlMode.Auto) break;

            // On a stutter, don't fire � the muzzle could be anywhere.
            if (IsStutterFrame())
            {
                WriteNeutralInput();
                yield return null;
                continue;
            }

            Vector3 aimPoint;
            if (!TryGetAimPoint(target, headFn, out aimPoint)) { yield return null; continue; }
            float yawErr, pitchErr;
            ComputeCameraErrorDeg(aimPoint, out yawErr, out pitchErr);
            ApplySteering(yawErr, pitchErr, ads: true, fire: true);
            fired++;
            yield return null;
        }
        ApplySteering(0f, 0f, ads: true, fire: false);
    }

    IEnumerator PostShotCooldown(GameObject target, System.Func<Transform> headFn)
    {
        float t = 0f;
        while (t < postShotCooldown && running && playerControlMode == PlayerControlMode.Auto)
        {
            if (target == null || !target.activeInHierarchy) yield break;

            if (IsStutterFrame()) { WriteNeutralInput(); yield return null; continue; }

            Vector3 aimPoint;
            if (!TryGetAimPoint(target, headFn, out aimPoint)) { yield return null; continue; }
            float yawErr, pitchErr;
            ComputeCameraErrorDeg(aimPoint, out yawErr, out pitchErr);
            ApplySteering(yawErr, pitchErr, ads: true, fire: false);
            t += ControlDt();
            yield return null;
        }
    }

    bool TryGetAimPoint(GameObject target, System.Func<Transform> headFn, out Vector3 aimPoint)
    {
        aimPoint = default;
        if (target == null) return false;
        var head = headFn();
        aimPoint = head != null ? head.position : target.transform.position + Vector3.up * 1.5f;
        return true;
    }

    bool ComputeCameraErrorDeg(Vector3 aimPoint, out float yawErr, out float pitchErr)
    {
        yawErr = pitchErr = 0f;
        if (fpsController == null || fpsController.mainCamera == null) return false;
        Transform cam = fpsController.mainCamera;
        Vector3 toTarget = aimPoint - cam.position;
        if (toTarget.sqrMagnitude < 1e-6f) return false;

        Vector3 fwd = cam.forward;
        Vector3 fwdH = new Vector3(fwd.x, 0f, fwd.z);
        Vector3 toH = new Vector3(toTarget.x, 0f, toTarget.z);
        if (fwdH.sqrMagnitude < 1e-6f || toH.sqrMagnitude < 1e-6f) return false;
        fwdH.Normalize(); toH.Normalize();

        yawErr = Vector3.SignedAngle(fwdH, toH, Vector3.up);
        float curPitch = -Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f)) * Mathf.Rad2Deg;
        float desPitch = -Mathf.Asin(Mathf.Clamp(toTarget.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
        pitchErr = desPitch - curPitch;
        return true;
    }

    float ComputeMuzzleErrorDeg(Vector3 aimPoint)
    {
        if (fpsController == null) return 999f;
        var gun = fpsController.GetGun();
        if (gun == null || gun.shootPoint == null) return 999f;
        Transform sp = gun.shootPoint.transform;
        Vector3 to = aimPoint - sp.position;
        if (to.sqrMagnitude < 1e-6f) return 0f;
        return Vector3.Angle(sp.forward, to);
    }

    void ApplySteering(float yawErr, float pitchErr, bool ads, bool fire)
    {
        float yawRate = Mathf.Clamp(yawErr * turnGain, -maxTurnSpeed, maxTurnSpeed);
        float pitchRate = Mathf.Clamp(pitchErr * turnGain, -maxTurnSpeed, maxTurnSpeed);
        WriteRaw(yawRate, -pitchRate, fire, ads);
    }

    /// <summary>
    /// Writes scripted input. Two stutter safeguards:
    ///   1. dt is clamped to maxControlDt before being multiplied with the rate.
    ///   2. The resulting per-frame angular delta is hard-capped at maxDegPerFrame.
    /// </summary>
    void WriteRaw(float yawDegPerSec, float pitchDegPerSec, bool fire, bool aim)
    {
        if (playerControlMode != PlayerControlMode.Auto) return;

        float dt = ControlDt();
        float yawDelta = yawDegPerSec * dt;
        float pitchDelta = pitchDegPerSec * dt;

        // Hard per-frame cap, regardless of dt or rate.
        yawDelta = Mathf.Clamp(yawDelta, -maxDegPerFrame, maxDegPerFrame);
        pitchDelta = Mathf.Clamp(pitchDelta, -maxDegPerFrame, maxDegPerFrame);

        if (fpsMovement != null) fpsMovement.scriptedInput = default;
        if (fpsController != null)
        {
            FPSScriptedInput c = default;
            c.mouseX = yawDelta;
            c.mouseY = pitchDelta;
            c.fire = fire;
            c.aim = aim;
            // c.reload intentionally false � never script reload.
            fpsController.scriptedInput = c;
        }
    }

    void WriteNeutralInput()
    {
        if (fpsMovement != null) fpsMovement.scriptedInput = default;
        if (fpsController != null) fpsController.scriptedInput = default;
    }

    Transform FindHead(Transform root)
    {
        if (root == null) return null;
        var direct = root.Find("Head");
        if (direct != null) return direct;
        return FindChildRecursive(root, "Head");
    }

    Transform FindChildRecursive(Transform t, string name)
    {
        for (int i = 0; i < t.childCount; i++)
        {
            var c = t.GetChild(i);
            if (c.name == name) return c;
            var hit = FindChildRecursive(c, name);
            if (hit != null) return hit;
        }
        return null;
    }

    void ApplyComboByIndex(int i)
    {
        currentComboIndex = i;
        ApplyConfig(CycleCombos[i][0], CycleCombos[i][1], CycleCombos[i][2]);
        currentLabel = BuildLabel(CycleNames[i], CycleCombos[i][0], CycleCombos[i][1], CycleCombos[i][2]);
    }

    void ApplyConfig(bool tex, bool vfx, bool light)
    {
        if (roundManager == null) return;
        roundManager.isHighResTexturesEnabled = tex;
        roundManager.isSkyboxEnabled = tex;
        roundManager.isPlayerVFXEnabled = vfx;
        roundManager.isEnemyVFXEnabled = vfx;
        roundManager.isEnvironmentVFXEnabled = vfx;
        roundManager.isLightEnabled = light;
    }

    void ApplySinglePreset()
    {
        if (singlePreset == GfxPreset.LeaveAsIs || roundManager == null) return;
        bool tex = false, vfx = false, light = false;
        switch (singlePreset)
        {
            case GfxPreset.AllOff: tex = false; vfx = false; light = false; break;
            case GfxPreset.TexOnly: tex = true; vfx = false; light = false; break;
            case GfxPreset.VfxOnly: tex = false; vfx = true; light = false; break;
            case GfxPreset.LightOnly: tex = false; vfx = false; light = true; break;
            case GfxPreset.TexVfx: tex = true; vfx = true; light = false; break;
            case GfxPreset.TexLight: tex = true; vfx = false; light = true; break;
            case GfxPreset.VfxLight: tex = false; vfx = true; light = true; break;
            case GfxPreset.AllOn: tex = true; vfx = true; light = true; break;
            case GfxPreset.Custom: tex = customTex; vfx = customVfx; light = customLight; break;
        }
        ApplyConfig(tex, vfx, light);
    }

    string BuildLabel(string name, bool tex, bool vfx, bool light)
    {
        return string.Format("{0}   |   TEX:{1}  VFX:{2}  LIGHT:{3}",
            name, tex ? "ON" : "off", vfx ? "ON" : "off", light ? "ON" : "off");
    }

    string BuildSingleLabel()
    {
        if (singlePreset == GfxPreset.LeaveAsIs) return "GFX: (unchanged)";
        if (roundManager == null) return "GFX: " + singlePreset;
        return string.Format("{0}   |   TEX:{1}  VFX:{2}  LIGHT:{3}",
            singlePreset.ToString(),
            roundManager.isHighResTexturesEnabled ? "ON" : "off",
            roundManager.isPlayerVFXEnabled ? "ON" : "off",
            roundManager.isLightEnabled ? "ON" : "off");
    }

    void OnGUI()
    {
        if (!showLabel) return;
        if (string.IsNullOrEmpty(currentLabel)) return;
        var bgRect = new Rect(labelPosition.x, labelPosition.y, labelSize.x + 200, labelSize.y);
        GUI.color = labelBackground;
        GUI.DrawTexture(bgRect, Texture2D.whiteTexture);
        GUI.color = Color.white;
        var style = new GUIStyle(GUI.skin.label);
        style.fontSize = labelFontSize;
        style.fontStyle = FontStyle.Bold;
        style.normal.textColor = labelColor;
        style.alignment = TextAnchor.MiddleLeft;
        style.padding = new RectOffset(16, 16, 8, 8);
        GUI.Label(bgRect, currentLabel, style);
    }
}

[System.Serializable]
public class TeaserMotionStep
{
    [Tooltip("Seconds this step lasts.")]
    public float duration = 1f;
    [Header("Movement (WASD, -1..+1)")]
    [Range(-1f, 1f)] public float moveX = 0f;
    [Range(-1f, 1f)] public float moveY = 0f;
    public bool sprint = false;
    public bool slide = false;
    public bool jump = false;
    public bool crouch = false;
    public bool prone = false;
    [Header("Mouse Look (deg/sec)")]
    public float lookYawDegPerSec = 0f;
    public float lookPitchDegPerSec = 0f;
    [Header("Actions")]
    public bool shoot = false;
    public bool aim = false;
    public bool reload = false;
    public bool grenade = false;
}