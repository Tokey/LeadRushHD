using Demo.Scripts.Runtime;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Audio;

public class Enemy : MonoBehaviour
{
    public enum DespawnReason
    {
        Killed,       // player shot it
        RoundReset,   // round ended / player reset
        PlayerDeath   // enemy reached the player
    }

    GameObject player;
    public FPSController playerController;
    NavMeshAgent enemyAgent;

    public float maxHealth;

    float currentHealth;

    public ParticleSystem deathPE;

    public ParticleSystem explodePE;

    public SphereCollider largeCollider;

    public Transform headTransform;

    public float minAngleToPlayer;

    public GameObject manager;

    public GameObject enemyHead;

    public float angularSizeOnSpawn;

    RoundManager roundManager;

    public GameObject enemyParticleEffectContainer;
    public GameObject staticParticleEffect;

    // ---- Per-enemy identity / telemetry ----
    [Header("Runtime Identity")]
    public int enemyId;
    public float assignedSpeed;

    /// <summary>Which speed slot of the round's spread this enemy holds. -1 until Start.</summary>
    public int speedSlot = -1;

    public Vector3 spawnPosition;
    public float spawnDistanceToPlayer;
    public float spawnRoundElapsed;
    public float spawnRealTime;

    bool logged;

    void Awake()
    {
        enemyId = EnemyManager.NextEnemyId();
        EnemyManager.Register(this);
    }

    void OnDestroy()
    {
        EnemyManager.Unregister(this);
    }

    // Start is called before the first frame update
    void Start()
    {
        enemyAgent = gameObject.GetComponent<NavMeshAgent>();
        player = GameObject.FindGameObjectWithTag("Player");
        manager = GameObject.FindGameObjectWithTag("Manager");
        playerController = player.GetComponent<FPSController>();

        maxHealth = playerController.enemyHealthGlobal;
        currentHealth = maxHealth;

        var relativePos = this.transform.position - player.transform.position;

        var forward = player.transform.forward;
        minAngleToPlayer = Vector3.Angle(relativePos, forward);
        angularSizeOnSpawn = playerController.CalculateAngularSize(enemyHead, playerController.mainCamera.position);

        // Speed comes from the round config: each enemy takes a free slot in the
        // round's speed spread, so a round can hold a mix of fast and slow ones.
        // Falls back to the global speed when there is no manager.
        if (EnemyManager.Instance != null)
        {
            speedSlot = EnemyManager.Instance.AcquireSpeedSlot();
            ApplySpeed(EnemyManager.Instance.SpeedForSlot(speedSlot));
        }
        else
        {
            ApplySpeed(playerController.enemySpeedGlobal);
        }

        roundManager = manager != null ? manager.GetComponent<RoundManager>() : null;

        spawnPosition = transform.position;
        spawnDistanceToPlayer = Vector3.Distance(spawnPosition, player.transform.position);
        spawnRealTime = Time.time;
        spawnRoundElapsed = roundManager != null ? roundManager.roundDuration - roundManager.roundTimer : 0f;
    }

    public void ApplySpeed(float speed)
    {
        assignedSpeed = speed;
        if (enemyAgent == null) enemyAgent = gameObject.GetComponent<NavMeshAgent>();
        if (enemyAgent != null) enemyAgent.speed = speed;
    }

    // Update is called once per frame
    void Update()
    {
        if (!playerController.isPlayerReady || !playerController.isQoeDisabled || !playerController.isAcceptabilityDisabled)
            return;
        enemyAgent.destination = player.transform.position;

        largeCollider.transform.localScale = new Vector3(2.5F + Mathf.PingPong(Time.time, 1.0f), 1, 1);

        if (roundManager != null)
        {
            enemyParticleEffectContainer.SetActive(roundManager.isEnemyVFXEnabled);
            staticParticleEffect.SetActive(!roundManager.isEnemyVFXEnabled);
        }
    }

    public void TakeDamage(float damage)
    {
        currentHealth -= damage;
        if (currentHealth < 0)
        {
            FPSController fPSController = player.GetComponent<FPSController>();

            fPSController.degreeToTargetXCumulative += fPSController.degreeToTargetX;
            fPSController.degreeToShootXCumulative += fPSController.degreeToShootX;

            fPSController.timeToTargetEnemyCumulative += fPSController.timeToTargetEnemy;
            fPSController.timeToHitEnemyCumulative += fPSController.timeToHitEnemy;
            fPSController.timeToKillEnemyCumulative += fPSController.timeToKillEnemy;

            fPSController.minAnlgeToEnemyCumulative += minAngleToPlayer;
            fPSController.enemySizeCumulative += angularSizeOnSpawn;

            EnemyLog(DespawnReason.Killed);

            fPSController.killCooldown = .3f;
            fPSController.targetMarked = false;
            fPSController.targetShot = false;
            fPSController.PlayKillSFX();
            if (deathPE != null && roundManager != null && roundManager.isEnemyVFXEnabled)
                Instantiate(deathPE, headTransform.position, headTransform.rotation);
            //Destroy the Instantiated ParticleSystem

            fPSController.score += fPSController.onKillScore;
            fPSController.roundKills++;

            Destroy(gameObject);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.gameObject.tag == "Player")
        {
            if (explodePE != null && roundManager != null && roundManager.isEnemyVFXEnabled)
                Instantiate(explodePE, headTransform.position, headTransform.rotation);
            player.GetComponent<FPSController>().PlayDeathSFX();
            player.GetComponent<FPSController>().RespawnPlayer(DespawnReason.PlayerDeath);
        }
    }

    // =====================================================================
    //  LOGGING
    // =====================================================================

    public static string LogHeader()
    {
        return RoundManager.ConditionHeader() + "," +
               "SessionStartTime,EventTime," +
               "EnemyID,Outcome,EnemySpeed,EnemiesAliveAtEvent," +
               "SpawnRoundElapsedS,DespawnRoundElapsedS,LifetimeS," +
               "SpawnX,SpawnY,SpawnZ,SpawnDistToPlayer," +
               "DespawnX,DespawnY,DespawnZ,DespawnDistToPlayer," +
               "HealthRemaining,MaxHealth,MinAngleToPlayerOnSpawn,AngularSizeOnSpawn," +
               "DegToTargetX,DegToTargetY,DegToShootX,DegToShootY," +
               "TimeToTargetS,TimeToHitS,TimeToKillS,TargetMarked,TargetShot";
    }

    public void EnemyLog() { EnemyLog(DespawnReason.RoundReset); }

    public void EnemyLog(DespawnReason reason)
    {
        if (logged) return;   // DestroyAllEnemy can race a kill in the same frame
        logged = true;

        if (roundManager == null && manager != null) roundManager = manager.GetComponent<RoundManager>();
        if (roundManager == null || player == null) return;

        FPSController fPSController = player.GetComponent<FPSController>();

        string filenameEnemyLog = "Data\\Logs\\EnemyData_" + roundManager.fileNameSuffix + "_" + roundManager.sessionID + "_" + ".csv";
        CsvUtil.EnsureHeader(filenameEnemyLog, LogHeader());

        float despawnElapsed = roundManager.roundDuration - roundManager.roundTimer;
        Vector3 despawnPos = transform.position;
        float despawnDist = Vector3.Distance(despawnPos, player.transform.position);
        int aliveAtEvent = EnemyManager.Instance != null ? EnemyManager.Instance.AliveCount() : 0;

        StringBuilder sb = new StringBuilder(512);
        sb.Append(roundManager.ConditionRow()).Append(',');
        sb.Append(CsvUtil.Cell(roundManager.sessionStartTime)).Append(',');
        sb.Append(CsvUtil.Cell(System.DateTime.Now.ToString())).Append(',');

        sb.Append(enemyId).Append(',');
        sb.Append(reason).Append(',');
        sb.Append(CsvUtil.F(assignedSpeed)).Append(',');
        sb.Append(aliveAtEvent).Append(',');

        sb.Append(CsvUtil.F(spawnRoundElapsed)).Append(',');
        sb.Append(CsvUtil.F(despawnElapsed)).Append(',');
        sb.Append(CsvUtil.F(Time.time - spawnRealTime)).Append(',');

        sb.Append(CsvUtil.F(spawnPosition.x)).Append(',');
        sb.Append(CsvUtil.F(spawnPosition.y)).Append(',');
        sb.Append(CsvUtil.F(spawnPosition.z)).Append(',');
        sb.Append(CsvUtil.F(spawnDistanceToPlayer)).Append(',');

        sb.Append(CsvUtil.F(despawnPos.x)).Append(',');
        sb.Append(CsvUtil.F(despawnPos.y)).Append(',');
        sb.Append(CsvUtil.F(despawnPos.z)).Append(',');
        sb.Append(CsvUtil.F(despawnDist)).Append(',');

        sb.Append(CsvUtil.F(currentHealth)).Append(',');
        sb.Append(CsvUtil.F(maxHealth)).Append(',');
        sb.Append(CsvUtil.F(minAngleToPlayer)).Append(',');
        sb.Append(CsvUtil.F(angularSizeOnSpawn)).Append(',');

        sb.Append(CsvUtil.F(fPSController.degreeToTargetX)).Append(',');
        sb.Append(CsvUtil.F(fPSController.degreeToTargetY)).Append(',');
        sb.Append(CsvUtil.F(fPSController.degreeToShootX)).Append(',');
        sb.Append(CsvUtil.F(fPSController.degreeToShootY)).Append(',');

        sb.Append(CsvUtil.F(fPSController.timeToTargetEnemy)).Append(',');
        sb.Append(CsvUtil.F(fPSController.timeToHitEnemy)).Append(',');
        sb.Append(CsvUtil.F(fPSController.timeToKillEnemy)).Append(',');
        sb.Append(fPSController.targetMarked).Append(',');
        sb.Append(fPSController.targetShot);

        TextWriter textWriter = File.AppendText(filenameEnemyLog);
        textWriter.WriteLine(sb.ToString());
        textWriter.Close();

        // The aim-effort accumulators describe the engagement that just ended.
        // Only a kill closes an engagement; a round reset should not zero them
        // before LogRoundData has read the cumulatives.
        if (reason == DespawnReason.Killed)
        {
            fPSController.degreeToTargetX = 0;
            fPSController.degreeToTargetY = 0;
            fPSController.degreeToShootX = 0;
            fPSController.degreeToShootY = 0;

            fPSController.timeToKillEnemy = 0;
            fPSController.timeToHitEnemy = 0;
            fPSController.timeToTargetEnemy = 0;
        }
    }
}
