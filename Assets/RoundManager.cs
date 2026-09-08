using Demo.Scripts.Runtime;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

[System.Serializable]
public struct RoundConfigs
{
    public List<float> roundFPS;
    public List<float> spikeMagnitude;
    public List<bool> onAimSpikeEnabled;
    public List<bool> onReloadSpikeEnabled;
    public List<bool> onMouseSpikeEnabled;
    public List<bool> onEnemySpawnSpikeEnabled;

    public List<bool> highResolutionMode;
    public List<bool> HDTextureMode;
    public List<bool> hDRISkybox;
    public List<bool> advancedLighting;
    public List<bool> playerVFX;
    public List<bool> enemyVFX;
    public List<bool> environmentVFX;

    // ---- Multi-enemy round parameters (LatinMap.csv columns 13..15) ----
    public List<int> enemyCount;      // how many enemies alive at once
    public List<float> enemySpeedMin; // per-enemy speed is rolled in [min, max]
    public List<float> enemySpeedMax;
}

[System.Serializable]
public struct PlayerTickLog
{
    public List<string> time;
    public List<float> roundTimer;
    public List<float> mouseX;
    public List<float> mouseY;

    public List<float> playerX;
    public List<float> playerY;
    public List<float> playerZ;

    public List<Quaternion> playerRot;

    public List<bool> isADS;

    public List<float> scorePerSec;
    public List<double> frameTimeMS;

    // ---- Multi-enemy tick state ----
    public List<int> enemyCount;
    public List<int> closestEnemyId;
    public List<float> closestEnemyX;
    public List<float> closestEnemyY;
    public List<float> closestEnemyZ;
    public List<float> closestEnemyDist;

    /// <summary>All live enemies as "id:x|y|z;id:x|y|z". One quoted CSV cell.</summary>
    public List<string> allEnemies;
}

public class RoundManager : MonoBehaviour
{
    public RoundConfigs roundConfigs;

    public float roundDuration;

    public float roundTimer;

    public int totalRoundNumber;

    public List<int> indexArray = new List<int>();

    public GameManager gameManager;

    public int currentRoundNumber;

    public String sessionStartTime;

    public FPSController playerController;

    public EnemyManager enemyManager;

    public long roundFrameCount = 0;
    public double frametimeCumulativeRound = 0;

    public string fileNameSuffix = "";
    public String filenamePerTick = "Data\\ClientDataPerTick.csv";
    public String filenamePerRound = "Data\\ClientDataPerRound.csv";

    public List<float> qoeValue;

    public bool acceptabilityValue;

    public int sessionID = -1;

    public bool isFTStudy;

    public int latinSquareRowNumber = 0;

    public int latinRow;

    public bool isPlayerVFXEnabled;
    public bool isEnemyVFXEnabled;
    public bool isEnvironmentVFXEnabled;
    public bool isSkyboxEnabled;
    public bool isHighResTexturesEnabled;
    public bool isLightEnabled;
    public bool isRenderResHigh;

    [Header("Multi-Enemy Defaults (used when the CSV omits the columns)")]
    public int defaultEnemyCount = 1;
    public float defaultEnemySpeedMin = 3.1f;
    public float defaultEnemySpeedMax = 3.1f;

    public GameUI gameUI;

    // ---- Config file paths ----
    const string PathGlobalConfig = "Data\\Configs\\GlobalConfig.csv";
    const string PathLatinSquare = "Data\\Configs\\LatinSquare.csv";
    const string PathLatinMap = "Data\\Configs\\LatinMap.csv";
    const string PathRoundConfig = "Data\\Configs\\RoundConfig.csv";
    const string PathSessionID = "Data\\Configs\\SessionID.csv";

    // ---- LatinMap.csv column layout ----
    const int ColFPS = 0;
    const int ColSpikeMagnitude = 1;
    const int ColAimSpike = 2;
    const int ColReloadSpike = 3;
    const int ColMouseSpike = 4;
    const int ColEnemySpawnSpike = 5;
    const int ColHighResolution = 6;
    const int ColHDTexture = 7;
    const int ColHDRISkybox = 8;
    const int ColAdvancedLighting = 9;
    const int ColPlayerVFX = 10;
    const int ColEnemyVFX = 11;
    const int ColEnvironmentVFX = 12;
    const int ColEnemyCount = 13;
    const int ColEnemySpeedMin = 14;
    const int ColEnemySpeedMax = 15;

    /// <summary>
    /// Index into roundConfigs for the round being played. Clamped, because
    /// GameUI increments currentRoundNumber past totalRoundNumber on the final
    /// round before Application.Quit() actually takes effect.
    /// </summary>
    public int CurrentConfigIndex
    {
        get
        {
            if (indexArray == null || indexArray.Count == 0) return 0;
            int round = Mathf.Clamp(currentRoundNumber, 1, indexArray.Count);
            return Mathf.Clamp(indexArray[round - 1], 0, Mathf.Max(0, roundConfigs.roundFPS.Count - 1));
        }
    }

    public int CurrentEnemyCount
    {
        get
        {
            int i = CurrentConfigIndex;
            if (roundConfigs.enemyCount == null || i >= roundConfigs.enemyCount.Count) return defaultEnemyCount;
            return Mathf.Max(0, roundConfigs.enemyCount[i]);
        }
    }

    public float CurrentEnemySpeedMin
    {
        get
        {
            int i = CurrentConfigIndex;
            if (roundConfigs.enemySpeedMin == null || i >= roundConfigs.enemySpeedMin.Count) return defaultEnemySpeedMin;
            return roundConfigs.enemySpeedMin[i];
        }
    }

    public float CurrentEnemySpeedMax
    {
        get
        {
            int i = CurrentConfigIndex;
            if (roundConfigs.enemySpeedMax == null || i >= roundConfigs.enemySpeedMax.Count) return defaultEnemySpeedMax;
            return roundConfigs.enemySpeedMax[i];
        }
    }

    // Start is called before the first frame update
    void Start()
    {
        latinSquareRowNumber = 0;
        currentRoundNumber = 1;
        fileNameSuffix = GenRandomID(6).ToString();
        sessionStartTime = System.DateTime.Now.ToString("yy:mm:dd:hh:mm:ss");

        gameManager = GetComponent<GameManager>();

        if (enemyManager == null && playerController != null)
            enemyManager = playerController.enemyManager;
        if (enemyManager == null)
            enemyManager = FindObjectOfType<EnemyManager>();

        EnsureConfigLists();

        ReadGlobalConfig();

        ReadLatinSquareSize();

        ReadFromLatinSquare();

        totalRoundNumber = roundConfigs.roundFPS.Count;

        indexArray.Clear();
        for (int i = 0; i < totalRoundNumber; i++)
        {
            indexArray.Add(i);
        }

        // Shuffle the list
        //Shuffle(indexArray);

        playerController.isQoeDisabled = true;
        playerController.isAcceptabilityDisabled = true;
        SetRounConfig();

    }

    /// <summary>Inspector-serialized lists can come back null on a fresh component.</summary>
    void EnsureConfigLists()
    {
        if (roundConfigs.roundFPS == null) roundConfigs.roundFPS = new List<float>();
        if (roundConfigs.spikeMagnitude == null) roundConfigs.spikeMagnitude = new List<float>();
        if (roundConfigs.onAimSpikeEnabled == null) roundConfigs.onAimSpikeEnabled = new List<bool>();
        if (roundConfigs.onReloadSpikeEnabled == null) roundConfigs.onReloadSpikeEnabled = new List<bool>();
        if (roundConfigs.onMouseSpikeEnabled == null) roundConfigs.onMouseSpikeEnabled = new List<bool>();
        if (roundConfigs.onEnemySpawnSpikeEnabled == null) roundConfigs.onEnemySpawnSpikeEnabled = new List<bool>();

        if (roundConfigs.highResolutionMode == null) roundConfigs.highResolutionMode = new List<bool>();
        if (roundConfigs.HDTextureMode == null) roundConfigs.HDTextureMode = new List<bool>();
        if (roundConfigs.hDRISkybox == null) roundConfigs.hDRISkybox = new List<bool>();
        if (roundConfigs.advancedLighting == null) roundConfigs.advancedLighting = new List<bool>();
        if (roundConfigs.playerVFX == null) roundConfigs.playerVFX = new List<bool>();
        if (roundConfigs.enemyVFX == null) roundConfigs.enemyVFX = new List<bool>();
        if (roundConfigs.environmentVFX == null) roundConfigs.environmentVFX = new List<bool>();

        if (roundConfigs.enemyCount == null) roundConfigs.enemyCount = new List<int>();
        if (roundConfigs.enemySpeedMin == null) roundConfigs.enemySpeedMin = new List<float>();
        if (roundConfigs.enemySpeedMax == null) roundConfigs.enemySpeedMax = new List<float>();

        if (qoeValue == null) qoeValue = new List<float>();
        if (indexArray == null) indexArray = new List<int>();
    }

    void ClearConfigLists()
    {
        roundConfigs.roundFPS.Clear();
        roundConfigs.spikeMagnitude.Clear();
        roundConfigs.onAimSpikeEnabled.Clear();
        roundConfigs.onReloadSpikeEnabled.Clear();
        roundConfigs.onMouseSpikeEnabled.Clear();
        roundConfigs.onEnemySpawnSpikeEnabled.Clear();

        roundConfigs.highResolutionMode.Clear();
        roundConfigs.HDTextureMode.Clear();
        roundConfigs.hDRISkybox.Clear();
        roundConfigs.advancedLighting.Clear();
        roundConfigs.playerVFX.Clear();
        roundConfigs.enemyVFX.Clear();
        roundConfigs.environmentVFX.Clear();

        roundConfigs.enemyCount.Clear();
        roundConfigs.enemySpeedMin.Clear();
        roundConfigs.enemySpeedMax.Clear();
    }

    void Shuffle<T>(List<T> list)
    {
        System.Random rng = new System.Random();
        int n = list.Count;
        while (n > 1)
        {
            n--;
            int k = rng.Next(n + 1);
            T value = list[k];
            list[k] = list[n];
            list[n] = value;
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (roundTimer > 0 && playerController.isPlayerReady && playerController.isQoeDisabled && playerController.isAcceptabilityDisabled)
        {
            roundTimer -= Time.deltaTime;
            frametimeCumulativeRound += Time.deltaTime;
            roundFrameCount++;
        }
        else if (roundTimer <= 0 && playerController.isQoeDisabled && playerController.isAcceptabilityDisabled)
        {
            playerController.isQoeDisabled = false;
            playerController.ResetPlayerAndDestroyEnemy();
        }

        //TestGraphicalFidelity(); // TODO: MUST DISABLE DURING FINAL BUILD

        if (!isRenderResHigh)
            Screen.SetResolution(Display.main.systemWidth / 3, Display.main.systemHeight / 3, true);
        else
            Screen.SetResolution(Display.main.systemWidth, Display.main.systemHeight, true);
    }

    void TestGraphicalFidelity()
    {
        if (UnityEngine.Input.GetKeyDown(KeyCode.Alpha1))
        {
            isHighResTexturesEnabled = !isHighResTexturesEnabled;
        }
        if (UnityEngine.Input.GetKeyDown(KeyCode.Alpha2))
        {
            isPlayerVFXEnabled = !isPlayerVFXEnabled;
        }
        if (UnityEngine.Input.GetKeyDown(KeyCode.Alpha3))
        {
            isEnemyVFXEnabled = !isEnemyVFXEnabled;
        }
        if (UnityEngine.Input.GetKeyDown(KeyCode.Alpha4))
        {
            isSkyboxEnabled = !isSkyboxEnabled;
        }
        if (UnityEngine.Input.GetKeyDown(KeyCode.Alpha5))
        {
            isLightEnabled = !isLightEnabled;
        }
        if (UnityEngine.Input.GetKeyDown(KeyCode.Alpha6))
        {
            isRenderResHigh = !isRenderResHigh;
        }
        if (UnityEngine.Input.GetKeyDown(KeyCode.Alpha7))
        {
            isEnvironmentVFXEnabled = !isEnvironmentVFXEnabled;
        }

    }

    void ReadLatinSquareSize()
    {
        if (!isFTStudy) return;

        latinSquareRowNumber = CsvUtil.ReadRows(PathRoundConfig).Count;
        Debug.Log("LATIN SQUARE final ROW COUNT = " + latinSquareRowNumber);
    }

    void ReadGlobalConfig()
    {
        var rows = CsvUtil.ReadRows(PathGlobalConfig);
        if (rows.Count == 0)
        {
            Debug.LogError("GlobalConfig.csv had no data rows.");
            return;
        }

        // Last row wins, matching the original loop.
        string[] r = rows[rows.Count - 1];

        roundDuration = CsvUtil.GetFloat(r, 0, roundDuration);
        isFTStudy = CsvUtil.GetBool(r, 1, isFTStudy);
        playerController.aimSpikeDelay = CsvUtil.GetFloat(r, 2, playerController.aimSpikeDelay);
        playerController.mouseSpikeDelay = CsvUtil.GetFloat(r, 3, playerController.mouseSpikeDelay);
        playerController.mouseSpikeDegreeOfMovement = CsvUtil.GetFloat(r, 4, playerController.mouseSpikeDegreeOfMovement);
        playerController.enemySpeedGlobal = CsvUtil.GetFloat(r, 5, playerController.enemySpeedGlobal);
        playerController.enemyHealthGlobal = CsvUtil.GetFloat(r, 6, playerController.enemyHealthGlobal);
        playerController.reticleSizeMultiplier = CsvUtil.GetFloat(r, 7, playerController.reticleSizeMultiplier);

        playerController.onHitScore = CsvUtil.GetInt(r, 8, playerController.onHitScore);
        playerController.onMissScore = CsvUtil.GetInt(r, 9, playerController.onMissScore);
        playerController.onKillScore = CsvUtil.GetInt(r, 10, playerController.onKillScore);
        playerController.onDeathScore = CsvUtil.GetInt(r, 11, playerController.onDeathScore);

        // Optional global fallbacks for the multi-enemy columns.
        defaultEnemyCount = CsvUtil.GetInt(r, 12, defaultEnemyCount);
        defaultEnemySpeedMin = CsvUtil.GetFloat(r, 13, playerController.enemySpeedGlobal);
        defaultEnemySpeedMax = CsvUtil.GetFloat(r, 14, defaultEnemySpeedMin);
    }

    int ReadSessionID()
    {
        var rows = CsvUtil.ReadRows(PathSessionID);
        if (rows.Count == 0) return -1;
        return CsvUtil.GetInt(rows[rows.Count - 1], 0, -1);
    }

    public void ReadFromLatinSquare()
    {
        EnsureConfigLists();

        sessionID = ReadSessionID();

        if (isFTStudy)
        {
            ReadFTStudyCSV();
            return;
        }

        ClearConfigLists();

        var rows = CsvUtil.ReadRows(PathLatinSquare);
        int rowIndex = sessionID - 1;
        if (rowIndex >= 0 && rowIndex < rows.Count)
        {
            string[] r = rows[rowIndex];
            for (int i = 0; i < r.Length; i++)
                roundConfigs.roundFPS.Add(CsvUtil.GetFloat(r, i, 60f));
        }
        else
        {
            Debug.LogError("LatinSquare.csv has no row for sessionID " + sessionID);
        }

        FrameRateSudySpikeConfigFiller(roundConfigs.roundFPS.Count);
    }

    /// <summary>
    /// The frame-rate study only varies FPS, so every other per-round list is
    /// padded out to the same length with neutral values.
    /// </summary>
    public void FrameRateSudySpikeConfigFiller(int length)
    {
        while (roundConfigs.spikeMagnitude.Count < length)
        {
            roundConfigs.spikeMagnitude.Add(100);
            roundConfigs.onAimSpikeEnabled.Add(false);
            roundConfigs.onReloadSpikeEnabled.Add(false);
            roundConfigs.onMouseSpikeEnabled.Add(false);
            roundConfigs.onEnemySpawnSpikeEnabled.Add(false);

            roundConfigs.highResolutionMode.Add(true);
            roundConfigs.HDTextureMode.Add(true);
            roundConfigs.hDRISkybox.Add(true);
            roundConfigs.advancedLighting.Add(true);
            roundConfigs.playerVFX.Add(true);
            roundConfigs.enemyVFX.Add(true);
            roundConfigs.environmentVFX.Add(true);

            roundConfigs.enemyCount.Add(defaultEnemyCount);
            roundConfigs.enemySpeedMin.Add(defaultEnemySpeedMin);
            roundConfigs.enemySpeedMax.Add(defaultEnemySpeedMax);
        }
    }


    // Primary Config
    public void ReadFTStudyCSV()
    {
        EnsureConfigLists();

        var latinMap = CsvUtil.ReadRows(PathLatinMap);
        if (latinMap.Count == 0)
        {
            Debug.LogError("LatinMap.csv had no data rows.");
            return;
        }

        sessionID = ReadSessionID();

        var roundOrder = CsvUtil.ReadRows(PathRoundConfig);
        if (roundOrder.Count == 0)
        {
            Debug.LogError("RoundConfig.csv had no data rows.");
            return;
        }

        if (latinSquareRowNumber <= 0) latinSquareRowNumber = roundOrder.Count;

        ClearConfigLists();

        latinRow = ((sessionID - 1) % latinSquareRowNumber) + 1;
        Debug.Log("LATIN ROW NUMBER: " + latinRow);

        string[] order = roundOrder[Mathf.Clamp(latinRow - 1, 0, roundOrder.Count - 1)];

        for (int i = 0; i < order.Length; i++)
        {
            int mapIndex = CsvUtil.GetInt(order, i, 1) - 1;
            if (mapIndex < 0 || mapIndex >= latinMap.Count)
            {
                Debug.LogError("RoundConfig.csv references LatinMap row " + (mapIndex + 1) + " which does not exist.");
                continue;
            }

            string[] c = latinMap[mapIndex];

            roundConfigs.roundFPS.Add(CsvUtil.GetFloat(c, ColFPS, 60f));
            roundConfigs.spikeMagnitude.Add(CsvUtil.GetFloat(c, ColSpikeMagnitude, 0f));
            roundConfigs.onAimSpikeEnabled.Add(CsvUtil.GetBool(c, ColAimSpike, false));
            roundConfigs.onReloadSpikeEnabled.Add(CsvUtil.GetBool(c, ColReloadSpike, false));
            roundConfigs.onMouseSpikeEnabled.Add(CsvUtil.GetBool(c, ColMouseSpike, false));
            roundConfigs.onEnemySpawnSpikeEnabled.Add(CsvUtil.GetBool(c, ColEnemySpawnSpike, false));

            roundConfigs.highResolutionMode.Add(CsvUtil.GetBool(c, ColHighResolution, true));
            roundConfigs.HDTextureMode.Add(CsvUtil.GetBool(c, ColHDTexture, true));
            roundConfigs.hDRISkybox.Add(CsvUtil.GetBool(c, ColHDRISkybox, true));
            roundConfigs.advancedLighting.Add(CsvUtil.GetBool(c, ColAdvancedLighting, true));
            roundConfigs.playerVFX.Add(CsvUtil.GetBool(c, ColPlayerVFX, true));
            roundConfigs.enemyVFX.Add(CsvUtil.GetBool(c, ColEnemyVFX, true));
            roundConfigs.environmentVFX.Add(CsvUtil.GetBool(c, ColEnvironmentVFX, true));

            // Multi-enemy columns. Missing columns fall back to the globals so
            // an older LatinMap.csv still loads.
            int count = CsvUtil.GetInt(c, ColEnemyCount, defaultEnemyCount);
            float sMin = CsvUtil.GetFloat(c, ColEnemySpeedMin, defaultEnemySpeedMin);
            float sMax = CsvUtil.GetFloat(c, ColEnemySpeedMax, sMin);
            if (sMax < sMin) { float t = sMin; sMin = sMax; sMax = t; }

            roundConfigs.enemyCount.Add(Mathf.Max(0, count));
            roundConfigs.enemySpeedMin.Add(sMin);
            roundConfigs.enemySpeedMax.Add(sMax);
        }
    }

    String GenRandomID(int len)
    {
        String alphanum = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
        String tmp_s = "";

        for (int i = 0; i < len; ++i)
        {
            tmp_s += alphanum[UnityEngine.Random.Range(0, 1000) % (alphanum.Length - 1)];
        }

        return tmp_s;
    }

    public void SetRounConfig()
    {
        roundTimer = roundDuration;
        gameManager.isFixedFT = false;

        int i = CurrentConfigIndex;

        Application.targetFrameRate = (int)roundConfigs.roundFPS[i];

        playerController.isAimSpikeEnabled = roundConfigs.onAimSpikeEnabled[i];
        playerController.isReloadSpikeEnabled = roundConfigs.onReloadSpikeEnabled[i];
        playerController.isMouseMovementSpikeEnabled = roundConfigs.onMouseSpikeEnabled[i];
        playerController.isEnemySpawnSpikeEnabled = roundConfigs.onEnemySpawnSpikeEnabled[i];

        isPlayerVFXEnabled = roundConfigs.playerVFX[i];
        isEnemyVFXEnabled = roundConfigs.enemyVFX[i];
        isSkyboxEnabled = roundConfigs.hDRISkybox[i];
        isHighResTexturesEnabled = roundConfigs.HDTextureMode[i];
        isLightEnabled = roundConfigs.advancedLighting[i];
        isRenderResHigh = roundConfigs.highResolutionMode[i];
        isEnvironmentVFXEnabled = roundConfigs.environmentVFX[i];

        gameManager.delayDuration = roundConfigs.spikeMagnitude[i];

        // Push the multi-enemy parameters for this round.
        if (enemyManager != null)
            enemyManager.ApplyRoundConfig(CurrentEnemyCount, CurrentEnemySpeedMin, CurrentEnemySpeedMax);

        roundFrameCount = 0;
        frametimeCumulativeRound = 0;

        qoeValue.Clear();
    }

    // =====================================================================
    //  LOGGING
    // =====================================================================

    /// <summary>
    /// The block of experiment-condition columns that every log repeats, so the
    /// three files can be joined on session/round.
    /// </summary>
    public static string ConditionHeader()
    {
        return "SessionID,LatinRow,RoundNumber,ConfigIndex," +
               "TargetFPS,SpikeMagnitudeMS,AimSpikeEnabled,EnemySpawnSpikeEnabled,MouseSpikeEnabled,ReloadSpikeEnabled," +
               "HDTextureMode,HighResolutionMode,AdvancedLighting,HDRISkybox,PlayerVFX,EnemyVFX,EnvironmentVFX," +
               "CfgEnemyCount,CfgEnemySpeedMin,CfgEnemySpeedMax";
    }

    /// <summary>Values matching <see cref="ConditionHeader"/>, comma terminated is caller's job.</summary>
    public string ConditionRow()
    {
        int i = CurrentConfigIndex;
        StringBuilder sb = new StringBuilder(256);
        sb.Append(sessionID).Append(',');
        sb.Append(latinRow).Append(',');
        sb.Append(currentRoundNumber).Append(',');
        sb.Append(i).Append(',');

        sb.Append(CsvUtil.F(roundConfigs.roundFPS[i])).Append(',');
        sb.Append(CsvUtil.F(roundConfigs.spikeMagnitude[i])).Append(',');
        sb.Append(roundConfigs.onAimSpikeEnabled[i]).Append(',');
        sb.Append(roundConfigs.onEnemySpawnSpikeEnabled[i]).Append(',');
        sb.Append(roundConfigs.onMouseSpikeEnabled[i]).Append(',');
        sb.Append(roundConfigs.onReloadSpikeEnabled[i]).Append(',');

        sb.Append(roundConfigs.HDTextureMode[i]).Append(',');
        sb.Append(roundConfigs.highResolutionMode[i]).Append(',');
        sb.Append(roundConfigs.advancedLighting[i]).Append(',');
        sb.Append(roundConfigs.hDRISkybox[i]).Append(',');
        sb.Append(roundConfigs.playerVFX[i]).Append(',');
        sb.Append(roundConfigs.enemyVFX[i]).Append(',');
        sb.Append(roundConfigs.environmentVFX[i]).Append(',');

        sb.Append(CurrentEnemyCount).Append(',');
        sb.Append(CsvUtil.F(CurrentEnemySpeedMin)).Append(',');
        sb.Append(CsvUtil.F(CurrentEnemySpeedMax));
        return sb.ToString();
    }

    string RoundLogHeader()
    {
        StringBuilder sb = new StringBuilder(1024);
        sb.Append("SessionID,LatinRow,RoundNumber,SessionStartTime,RoundEndTime,");
        sb.Append("TargetFPS,SpikeMagnitudeMS,AimSpikeEnabled,EnemySpawnSpikeEnabled,MouseSpikeEnabled,ReloadSpikeEnabled,");
        sb.Append("HDTextureMode,HighResolutionMode,AdvancedLighting,HDRISkybox,PlayerVFX,EnemyVFX,EnvironmentVFX,");
        sb.Append("CfgEnemyCount,CfgEnemySpeedMin,CfgEnemySpeedMax,ConfigIndex,");
        sb.Append("Score,ShotsFired,ShotsHit,HeadshotsHit,ReloadCount,TacticalReloadCount,Accuracy,");
        sb.Append("Kills,Deaths,DistanceTravelled,MouseDeltaXCumulative,MouseDeltaYCumulative,MouseDeltaTotal,");
        sb.Append("RoundFrametimeCumulativeS,RoundFrameCount,AvgFrametimeS,AvgFPS,");
        sb.Append("AimSpikeCount,ReloadSpikeCount,MouseSpikeCount,SpikeDurationCumulativeMS,AvgSpikeDurationMS,EnemySpawnSpikeCount,");
        sb.Append("DegToShootXCumulative,DegToTargetXCumulative,MinAngleToEnemyCumulative,EnemyAngularSizeCumulative,");
        sb.Append("TimeToTargetCumulative,TimeToHitCumulative,TimeToKillCumulative,");
        sb.Append("AvgDegToShootX,AvgDegToTargetX,AvgEnemyAngularSizeOnSpawn,");
        sb.Append("AvgTimeToTargetS,AvgTimeToHitS,AvgTimeToKillS,AimDuration,FiringDuration,");
        sb.Append("EnemiesSpawned,EnemiesDespawnedUnkilled,");

        int q = gameUI != null ? gameUI.numberOfSliderQuestions : 0;
        for (int i = 1; i <= q; i++)
            sb.Append("QoE_Q").Append(i).Append(',');

        sb.Append("Acceptability");
        return sb.ToString();
    }

    public void LogRoundData()
    {
        filenamePerRound = "Data\\Logs\\RoundData_" + fileNameSuffix + "_" + sessionID + "_" + ".csv";
        CsvUtil.EnsureHeader(filenamePerRound, RoundLogHeader());

        TextWriter textWriter = System.IO.File.AppendText(filenamePerRound);

        float accuracy = 0;
        if (playerController.shotsFiredPerRound > 0)
        {
            accuracy = (float)playerController.shotsHitPerRound / (float)playerController.shotsFiredPerRound;
        }

        int kills = Mathf.Max(1, playerController.roundKills); // avoid /0 -> NaN in the averages
        float degXTargetAvg = playerController.degreeToTargetXCumulative / kills;
        float degXShootAvg = playerController.degreeToShootXCumulative / kills;
        float enemySizeOnSpawnAvg = playerController.enemySizeCumulative / kills;

        float timeToTargetAvg = playerController.timeToTargetEnemyCumulative / kills;
        float timeToHitAvg = playerController.timeToHitEnemyCumulative / kills;
        float timeToKillAvg = playerController.timeToKillEnemyCumulative / kills;

        double avgspikeDurationCumulative = 0;
        int spikeCount = playerController.perRoundAimSpikeCount
                       + playerController.perRoundReloadSpikeCount
                       + playerController.perRoundMouseMovementSpikeCount;
        if (spikeCount > 0)
            avgspikeDurationCumulative = playerController.spikeDurationCumulative / spikeCount;

        double avgFT = roundFrameCount > 0 ? frametimeCumulativeRound / roundFrameCount : 0;
        double avgFPS = avgFT > 0 ? 1 / avgFT : 0;

        String qoEvalues = "";
        int questions = gameUI != null ? gameUI.numberOfSliderQuestions : 0;
        for (int i = 0; i < questions; i++)
            qoEvalues = qoEvalues + (i < qoeValue.Count ? CsvUtil.F(qoeValue[i]) : "") + ",";

        qoeValue.Clear();

        int cfg = CurrentConfigIndex;

        String roundLogLine =
           sessionID.ToString() + "," +
           latinRow.ToString() + "," +
           currentRoundNumber.ToString() + "," +
           CsvUtil.Cell(sessionStartTime) + "," +
           CsvUtil.Cell(System.DateTime.Now.ToString()) + "," +
           CsvUtil.F(roundConfigs.roundFPS[cfg]) + "," +
           CsvUtil.F(roundConfigs.spikeMagnitude[cfg]) + "," +
           roundConfigs.onAimSpikeEnabled[cfg].ToString() + "," +
           roundConfigs.onEnemySpawnSpikeEnabled[cfg].ToString() + "," +
           roundConfigs.onMouseSpikeEnabled[cfg].ToString() + "," +
           roundConfigs.onReloadSpikeEnabled[cfg].ToString() + "," +

           roundConfigs.HDTextureMode[cfg].ToString() + "," +
           roundConfigs.highResolutionMode[cfg].ToString() + "," +
           roundConfigs.advancedLighting[cfg].ToString() + "," +
           roundConfigs.hDRISkybox[cfg].ToString() + "," +
           roundConfigs.playerVFX[cfg].ToString() + "," +
           roundConfigs.enemyVFX[cfg].ToString() + "," +
           roundConfigs.environmentVFX[cfg].ToString() + "," +

           CurrentEnemyCount.ToString() + "," +
           CsvUtil.F(CurrentEnemySpeedMin) + "," +
           CsvUtil.F(CurrentEnemySpeedMax) + "," +

           cfg.ToString() + "," +
           playerController.score + "," +
           playerController.shotsFiredPerRound + "," +
           playerController.shotsHitPerRound + "," +
           playerController.headshotsHitPerRound + "," +
           playerController.realoadCountPerRound + "," +
           playerController.tacticalReloadCountPerRound + "," +
           CsvUtil.F(accuracy) + "," +
           playerController.roundKills + "," +
           playerController.roundDeaths + "," +
           CsvUtil.F(playerController.distanceTravelledPerRound) + "," +
           CsvUtil.F(playerController.delXCumilative) + "," +
           CsvUtil.F(playerController.delYCumilative) + "," +
           CsvUtil.F(playerController.delXCumilative + playerController.delYCumilative) + "," +
           CsvUtil.F(frametimeCumulativeRound) + "," +
           roundFrameCount.ToString() + "," +
           CsvUtil.F(avgFT) + "," +
           CsvUtil.F(avgFPS) + "," +
           playerController.perRoundAimSpikeCount.ToString() + "," +
           playerController.perRoundReloadSpikeCount.ToString() + "," +
           playerController.perRoundMouseMovementSpikeCount.ToString() + "," +
           CsvUtil.F(playerController.spikeDurationCumulative) + "," +
           CsvUtil.F(avgspikeDurationCumulative) + "," +
           playerController.perRoundEnemySpawnSpikeCount.ToString() + "," +
           CsvUtil.F(playerController.degreeToShootXCumulative) + "," +
           CsvUtil.F(playerController.degreeToTargetXCumulative) + "," +
           CsvUtil.F(playerController.minAnlgeToEnemyCumulative) + "," +
           CsvUtil.F(playerController.enemySizeCumulative) + "," +
           CsvUtil.F(playerController.timeToTargetEnemyCumulative) + "," +
           CsvUtil.F(playerController.timeToHitEnemyCumulative) + "," +
           CsvUtil.F(playerController.timeToKillEnemyCumulative) + "," +
           CsvUtil.F(degXShootAvg) + "," +
           CsvUtil.F(degXTargetAvg) + "," +
           CsvUtil.F(enemySizeOnSpawnAvg) + "," +
           CsvUtil.F(timeToTargetAvg) + "," +
           CsvUtil.F(timeToHitAvg) + "," +
           CsvUtil.F(timeToKillAvg) + "," +
           CsvUtil.F(playerController.aimDurationPerRound) + "," +
           CsvUtil.F(playerController.isFiringDurationPerRound) + "," +
           (enemyManager != null ? enemyManager.enemiesSpawnedThisRound : 0).ToString() + "," +
           (enemyManager != null ? enemyManager.enemiesDespawnedThisRound : 0).ToString() + "," +
           qoEvalues +
           acceptabilityValue.ToString()
            ;
        textWriter.WriteLine(roundLogLine);
        textWriter.Close();
    }

    string PlayerLogHeader()
    {
        return ConditionHeader() + "," +
               "RoundElapsedS,Timestamp,MouseDeltaX,MouseDeltaY," +
               "PlayerX,PlayerY,PlayerZ,ScorePerSec," +
               "PlayerRotX,PlayerRotY,PlayerRotZ,PlayerRotW,PlayerYawDeg,PlayerPitchDeg," +
               "IsADS,FrameTimeMS," +
               "EnemyCount,ClosestEnemyID,ClosestEnemyX,ClosestEnemyY,ClosestEnemyZ,ClosestEnemyDist,AllEnemies";
    }

    public void LogPlayerData()
    {
        string filename = "Data\\Logs\\PlayerData_" + fileNameSuffix + "_" + sessionID + "_" + ".csv";
        CsvUtil.EnsureHeader(filename, PlayerLogHeader());

        TextWriter textWriter = System.IO.File.AppendText(filename);

        string condition = ConditionRow();
        var log = playerController.playerTickLog;

        for (int i = 0; i < log.mouseX.Count; i++)
        {
            Quaternion rot = log.playerRot[i];
            Vector3 euler = rot.eulerAngles;

            StringBuilder sb = new StringBuilder(384);
            sb.Append(condition).Append(',');
            sb.Append(CsvUtil.F(log.roundTimer[i])).Append(',');
            sb.Append(CsvUtil.Cell(log.time[i])).Append(',');
            sb.Append(CsvUtil.F(log.mouseX[i])).Append(',');
            sb.Append(CsvUtil.F(log.mouseY[i])).Append(',');
            sb.Append(CsvUtil.F(log.playerX[i])).Append(',');
            sb.Append(CsvUtil.F(log.playerY[i])).Append(',');
            sb.Append(CsvUtil.F(log.playerZ[i])).Append(',');
            sb.Append(CsvUtil.F(log.scorePerSec[i])).Append(',');
            sb.Append(CsvUtil.F(rot.x)).Append(',');
            sb.Append(CsvUtil.F(rot.y)).Append(',');
            sb.Append(CsvUtil.F(rot.z)).Append(',');
            sb.Append(CsvUtil.F(rot.w)).Append(',');
            sb.Append(CsvUtil.F(euler.y)).Append(',');
            sb.Append(CsvUtil.F(euler.x)).Append(',');
            sb.Append(log.isADS[i]).Append(',');
            sb.Append(CsvUtil.F(log.frameTimeMS[i])).Append(',');
            sb.Append(log.enemyCount[i]).Append(',');
            sb.Append(log.closestEnemyId[i]).Append(',');
            sb.Append(CsvUtil.F(log.closestEnemyX[i])).Append(',');
            sb.Append(CsvUtil.F(log.closestEnemyY[i])).Append(',');
            sb.Append(CsvUtil.F(log.closestEnemyZ[i])).Append(',');
            sb.Append(CsvUtil.F(log.closestEnemyDist[i])).Append(',');
            sb.Append(CsvUtil.Cell(log.allEnemies[i]));

            textWriter.WriteLine(sb.ToString());
        }
        textWriter.Close();
    }
}
