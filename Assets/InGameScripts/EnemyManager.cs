using Demo.Scripts.Runtime;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class EnemyManager : MonoBehaviour
{
    public GameObject enemy;

    public GameObject player;
    public FPSController playerController;
    public List<Transform> sapwnPoints;
    public List<Enemy> enemies;

    public float spawnDuration;
    public float spawnTimer;
    public int maxEnemyCount = 1;

    public float minSpawnRadius;
    public float maxSpawnRadius;
    public float walkwableAreaRadius;

    [Header("Per-Round Enemy Speed (driven by LatinMap.csv)")]
    public float enemySpeedMin = 3.1f;
    public float enemySpeedMax = 3.1f;

    [Tooltip("OFF (default): speeds are spread evenly across [min, max] by slot, so " +
             "4 enemies over 1..4 run at exactly 1, 2, 3 and 4. ON: each enemy rolls a " +
             "uniform random speed in the range instead.")]
    public bool useRandomSpeedInRange = false;

    [Tooltip("Spawn the whole batch immediately when a round starts instead of trickling them in.")]
    public bool spawnBatchOnRoundStart = true;

    [Header("Per-Round Counters (read by RoundManager)")]
    public int enemiesSpawnedThisRound;
    public int enemiesDespawnedThisRound;

    // ---- Live-enemy registry -------------------------------------------------
    // The Enemy prefab has TWO objects tagged "Enemy" (the root and the
    // "EnemyLarge" aim-assist collider), so FindGameObjectsWithTag double counts.
    // Every Enemy registers itself here instead.
    static readonly List<Enemy> s_live = new List<Enemy>();
    static int s_nextEnemyId = 1;

    public static EnemyManager Instance { get; private set; }

    public static List<Enemy> Live { get { return s_live; } }

    public static int NextEnemyId() { return s_nextEnemyId++; }

    public static void Register(Enemy e)
    {
        if (e == null || s_live.Contains(e)) return;
        s_live.Add(e);
        if (Instance != null) Instance.enemiesSpawnedThisRound++;
        if (Instance != null) Instance.enemies = s_live;
    }

    public static void Unregister(Enemy e)
    {
        s_live.Remove(e);
        if (Instance != null) Instance.enemies = s_live;
    }

    void Awake()
    {
        Instance = this;

        // Prune rather than clear: with "Reload Domain" turned off the statics
        // survive a play session, but an Enemy whose Awake happened to run before
        // ours in this session is still valid and must not be dropped.
        for (int i = s_live.Count - 1; i >= 0; i--)
            if (s_live[i] == null) s_live.RemoveAt(i);

        s_nextEnemyId = 1;
        enemies = s_live;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        if (playerController == null && player != null)
            playerController = player.GetComponent<FPSController>();

        TimerReset();
        FillToMaxCount();
    }

    void TimerReset()
    {
        spawnTimer = spawnDuration;
    }

    /// <summary>Called by RoundManager.SetRounConfig at the start of every round.</summary>
    public void ApplyRoundConfig(int count, float speedMin, float speedMax)
    {
        maxEnemyCount = Mathf.Max(0, count);
        enemySpeedMin = speedMin;
        enemySpeedMax = Mathf.Max(speedMin, speedMax);

        enemiesSpawnedThisRound = 0;
        enemiesDespawnedThisRound = 0;
        s_nextEnemyId = 1;

        // Existing enemies belong to the previous round's config; re-slot and
        // re-speed them so nothing survives with stale settings if the round is
        // restarted mid-flight.
        int slot = 0;
        for (int i = 0; i < s_live.Count; i++)
        {
            if (s_live[i] == null) continue;
            s_live[i].speedSlot = slot;
            s_live[i].ApplySpeed(SpeedForSlot(slot));
            slot++;
        }

        spawnTimer = 0f;
    }

    /// <summary>
    /// Lowest speed slot in [0, maxEnemyCount) not held by a live enemy. Slots let
    /// a respawn step back into the gap the dead enemy left, so the round keeps
    /// the same spread of speeds rather than drifting.
    /// </summary>
    public int AcquireSpeedSlot()
    {
        int cap = Mathf.Max(1, maxEnemyCount);
        for (int slot = 0; slot < cap; slot++)
        {
            bool taken = false;
            for (int i = 0; i < s_live.Count; i++)
            {
                Enemy e = s_live[i];
                if (e != null && e.speedSlot == slot) { taken = true; break; }
            }
            if (!taken) return slot;
        }
        return 0;
    }

    /// <summary>
    /// Speed for a given slot. Evenly spread across [min, max]: with 4 enemies over
    /// 1..4 the slots come out at exactly 1, 2, 3, 4. A round whose min equals its
    /// max gives every enemy that one speed.
    /// </summary>
    public float SpeedForSlot(int slot)
    {
        int cap = Mathf.Max(1, maxEnemyCount);
        if (cap <= 1 || enemySpeedMax <= enemySpeedMin) return enemySpeedMin;
        if (useRandomSpeedInRange) return Random.Range(enemySpeedMin, enemySpeedMax);

        float t = Mathf.Clamp01(slot / (float)(cap - 1));
        return Mathf.Lerp(enemySpeedMin, enemySpeedMax, t);
    }

    public int AliveCount()
    {
        for (int i = s_live.Count - 1; i >= 0; i--)
            if (s_live[i] == null) s_live.RemoveAt(i);
        return s_live.Count;
    }

    // Update is called once per frame
    void Update()
    {
        if (playerController == null) return;
        if (!playerController.isPlayerReady || !playerController.isQoeDisabled || !playerController.isAcceptabilityDisabled)
            return;

        spawnTimer -= Time.deltaTime;

        if (spawnTimer > 0f) return;

        if (AliveCount() < maxEnemyCount)
        {
            if (spawnBatchOnRoundStart)
                FillToMaxCount();
            else
                SpawnEnemy();

            spawnTimer = spawnDuration;
        }
    }

    void FillToMaxCount()
    {
        int guard = 0;
        while (AliveCount() < maxEnemyCount && guard++ < 64)
            SpawnEnemy();
    }

    /// <summary>Logs and removes every live enemy. Safe to call with none alive.</summary>
    public void DestroyAllEnemy()
    {
        DestroyAllEnemy(Enemy.DespawnReason.RoundReset);
    }

    public void DestroyAllEnemy(Enemy.DespawnReason reason)
    {
        // Copy first: EnemyLog/Destroy mutates s_live through OnDestroy.
        var snapshot = new List<Enemy>(s_live);
        for (int i = 0; i < snapshot.Count; i++)
        {
            Enemy e = snapshot[i];
            if (e == null) continue;
            e.EnemyLog(reason);
            enemiesDespawnedThisRound++;
            Destroy(e.gameObject);
        }
        s_live.Clear();
        if (playerController != null) playerController.targetMarked = false;
    }

    /// <summary>Nearest live enemy to the player, or null.</summary>
    public GameObject GetClosestEnemy()
    {
        Enemy e = GetClosestEnemyComponent();
        return e != null ? e.gameObject : null;
    }

    public Enemy GetClosestEnemyComponent()
    {
        if (player == null) return null;

        float minSqr = float.MaxValue;
        Enemy closest = null;
        Vector3 p = player.transform.position;

        for (int i = s_live.Count - 1; i >= 0; i--)
        {
            Enemy e = s_live[i];
            if (e == null) { s_live.RemoveAt(i); continue; }

            float d = (e.transform.position - p).sqrMagnitude;
            if (d < minSqr)
            {
                minSqr = d;
                closest = e;
            }
        }
        return closest;
    }

    /// <summary>Live enemies. The returned list is the live registry - do not mutate it.</summary>
    public List<Enemy> GetAllEnemies()
    {
        AliveCount();
        return s_live;
    }

    void SpawnEnemy()
    {
        if (enemy == null || player == null) return;

        float dist = Random.Range(minSpawnRadius, maxSpawnRadius);
        float angle = Random.Range(0, 360);

        Vector3 spawnPos = CalculateDistantPoint(player.transform.position, dist, angle);

        SpawnNavMeshAgent(enemy, spawnPos);

        if (playerController != null && playerController.isEnemySpawnSpikeEnabled)
        {
            playerController.gameManager.isEventBasedDelay = true;
            playerController.perRoundEnemySpawnSpikeCount++;
        }
    }

    public Vector3 CalculateDistantPoint(Vector3 playerPosition, float distance, float angle)
    {
        float angleRad = Mathf.Deg2Rad * angle;
        float xOffset = distance * Mathf.Cos(angleRad);
        float zOffset = distance * Mathf.Sin(angleRad);

        return new Vector3(playerPosition.x + xOffset, playerPosition.y, playerPosition.z + zOffset);
    }

    public Vector3 GetRandomWalkablePositionNear(Vector3 desiredPosition, float sampleRadius)
    {
        NavMeshHit hit;
        if (NavMesh.SamplePosition(desiredPosition, out hit, sampleRadius, NavMesh.AllAreas))
        {
            return hit.position;
        }
        // If no valid position found, return desired position (for debugging)
        return desiredPosition;
    }

    public void SpawnNavMeshAgent(GameObject agentPrefab, Vector3 desiredPosition)
    {
        Vector3 spawnPosition = GetRandomWalkablePositionNear(desiredPosition, walkwableAreaRadius); // Adjust sampleRadius as needed
        Instantiate(agentPrefab, spawnPosition, Quaternion.identity);
    }
}
