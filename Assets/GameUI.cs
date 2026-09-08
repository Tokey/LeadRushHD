using Demo.Scripts.Runtime;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

public class GameUI : MonoBehaviour
{

    public Image highAlertTop;
    public Image highAlertBottom;
    public Image highAlertLeft;
    public Image highAlertRight;

    public Image hitMarkerImage;

    public TMPro.TMP_Text ammoText;
    public TMPro.TMP_Text healthText;
    public TMPro.TMP_Text scoreText;
    public TMPro.TMP_Text durationText;

    public TMPro.TMP_Text killsText;
    public TMPro.TMP_Text deathsText;

    public TMPro.TMP_Text roundText;

    public GameObject player;

    public RoundManager roundManager;

    public EnemyManager enemyManager;

    public bool highAlertBlinkOn;

    float highAlertBlinkerValue;

    public AudioClip beepSFX;

    bool beeperFuse;

    public GameObject readyText;

    public GameObject qoeHolderGO;
    public Slider qoeSlider;
    public TMPro.TMP_Text sliderText;
    public GameObject qoeSubmitGO;

    public GameManager gameManager;
    public TMPro.TMP_Text lagText;

    float sliderHandleRed;
    float sliderHandleGreen;
    public Image SliderHandle;

    public GameObject acceptabilityGO;

    public int numberOfSliderQuestions;
    public int currentSliderQuestionNumber;

    public List<String> sliderQues;
    public List<String> qoeLowStr;
    public List<String> qoeHighStr;


    public TMPro.TMP_Text qoEQuesText;
    public TMPro.TMP_Text qoELowText;
    public TMPro.TMP_Text qoEHighText;

    float qoEHolderPosYInit;

    // Start is called before the first frame update
    void Start()
    {
        beeperFuse = true;
        currentSliderQuestionNumber = 0;
        qoEHolderPosYInit = qoeHolderGO.transform.position.y;
    }

    // Update is called once per frame
    void Update()
    {
        UpdateHitMarker();

        UpdateUITexts();

        UpdateHighAlert();
    }

    // Edge order: 0 = Top (front), 1 = Bottom (back), 2 = Left, 3 = Right.
    const int EdgeTop = 0;
    const int EdgeBottom = 1;
    const int EdgeLeft = 2;
    const int EdgeRight = 3;
    readonly float[] edgeNearestDistance = new float[4];

    void UpdateHighAlert()
    {
        FPSController fps = player.GetComponent<FPSController>();

        if (fps.deathTimeOut > 0)
        {
            highAlertTop.gameObject.SetActive(true);
            highAlertBottom.gameObject.SetActive(true);
            highAlertLeft.gameObject.SetActive(true);
            highAlertRight.gameObject.SetActive(true);

            float distance = fps.deathTimeOut;

            highAlertTop.rectTransform.localScale = new Vector3(distance, 1, 1f);
            highAlertBottom.rectTransform.localScale = new Vector3(distance, 1, 1);
            highAlertLeft.rectTransform.localScale = new Vector3(distance, 1, 1);
            highAlertRight.rectTransform.localScale = new Vector3(distance, 1, 1);

            highAlertTop.color = new Color(1, 0, 0, 1);
            highAlertBottom.color = new Color(1, 0, 0, 1);
            highAlertLeft.color = new Color(1, 0, 0, 1);
            highAlertRight.color = new Color(1, 0, 0, 1);
            return;
        }

        // With several enemies alive, each edge shows the nearest enemy that lies
        // in that direction, so an enemy behind you is never masked by one in
        // front. Previously a single (arbitrary) enemy drove all four edges.
        for (int i = 0; i < 4; i++) edgeNearestDistance[i] = float.MaxValue;

        List<Enemy> enemies = enemyManager != null ? enemyManager.GetAllEnemies() : null;

        Enemy closest = null;
        float closestDistance = float.MaxValue;

        if (enemies != null)
        {
            Vector3 playerPos = player.transform.position;

            for (int i = 0; i < enemies.Count; i++)
            {
                Enemy e = enemies[i];
                if (e == null) continue;

                Vector3 enemyPos = e.transform.position;
                Vector3 relativePoint = player.transform.InverseTransformPoint(enemyPos);
                float distance = Vector3.Distance(playerPos, enemyPos);

                int edge;
                if (Math.Abs(relativePoint.x) > Math.Abs(relativePoint.z))
                    edge = relativePoint.x > 0.0f ? EdgeRight : EdgeLeft;
                else
                    edge = relativePoint.z > 0.0f ? EdgeTop : EdgeBottom;

                if (distance < edgeNearestDistance[edge])
                    edgeNearestDistance[edge] = distance;

                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closest = e;
                }
            }
        }

        if (closest == null)
        {
            highAlertTop.gameObject.SetActive(false);
            highAlertBottom.gameObject.SetActive(false);
            highAlertLeft.gameObject.SetActive(false);
            highAlertRight.gameObject.SetActive(false);
            return;
        }

        // The blink phase and the beep are driven by the single nearest enemy, so
        // the alarm still has one coherent rhythm rather than four.
        UpdateHighAlertBlink(closestDistance / 2f, closest.GetComponent<AudioSource>());

        ApplyEdge(highAlertTop, edgeNearestDistance[EdgeTop], true);
        ApplyEdge(highAlertBottom, edgeNearestDistance[EdgeBottom], false);
        ApplyEdge(highAlertLeft, edgeNearestDistance[EdgeLeft], false);
        ApplyEdge(highAlertRight, edgeNearestDistance[EdgeRight], false);
    }

    /// <summary>
    /// Shows one edge indicator scaled and tinted by the nearest enemy on that
    /// side. rawDistance is float.MaxValue when nothing is on that side.
    /// </summary>
    void ApplyEdge(Image edge, float rawDistance, bool isTop)
    {
        if (rawDistance >= float.MaxValue || rawDistance <= 0f)
        {
            edge.gameObject.SetActive(false);
            return;
        }

        edge.gameObject.SetActive(true);

        float d = rawDistance / 2f;   // matches the original SetHighAlertAlpha scaling

        float scaleX = isTop ? 1f / ((d / 5f) + 1f) : 1f / ((d / 5f) + 0.9f);
        edge.rectTransform.localScale = new Vector3(scaleX, 1, isTop ? 0.9f : 1f);

        float intensity = 1f / (d / 10f);   // red == green -> yellow, brighter when near
        float alpha = highAlertBlinkOn ? intensity : 0f;
        float green = highAlertBlinkOn ? intensity : 0f;

        edge.color = new Color(intensity, green, 0f, alpha);
    }

    void UpdateHighAlertBlink(float distance, AudioSource source)
    {
        if (distance <= 0f) return;

        if (!highAlertBlinkOn)
        {
            beeperFuse = true;
        }
        else if (beeperFuse)
        {
            PlayBeepSFX(source);
            beeperFuse = false;
        }

        highAlertBlinkerValue = Mathf.PingPong(Time.time, (distance / 1 + 0.1f));

        if (highAlertBlinkerValue > (distance / 10 + .05f))
            highAlertBlinkOn = true;
        else highAlertBlinkOn = false;
    }
    void UpdateUITexts()
    {
        ammoText.text = "" + player.GetComponent<FPSController>().GetGun().currentAmmoCount;
        healthText.text = "      " + (player.GetComponent<FPSController>().liveAccuracy*100.0f).ToString("##");
        scoreText.text = "Score: " + player.GetComponent<FPSController>().score;
        durationText.text = "Duration: " + roundManager.roundTimer.ToString("###");

        killsText.text = "Kills: " + player.GetComponent<FPSController>().roundKills;
        deathsText.text = "Deaths: " + player.GetComponent<FPSController>().roundDeaths;

        lagText.text = gameManager.delayDuration.ToString();

        

        if (!player.GetComponent<FPSController>().isPlayerReady)
            readyText.SetActive(true);
        else
            readyText.SetActive(false);

        qoeHolderGO.gameObject.SetActive(!player.GetComponent<FPSController>().isQoeDisabled);

        if (qoeHolderGO.activeSelf)
        {
            roundManager.isRenderResHigh = true;
            if (currentSliderQuestionNumber < numberOfSliderQuestions)
            { 
                qoEQuesText.text = sliderQues[currentSliderQuestionNumber].ToString(); 
                qoELowText.text = qoeLowStr[currentSliderQuestionNumber].ToString();
                qoEHighText.text = qoeHighStr[currentSliderQuestionNumber].ToString();
            }
           
            qoeHolderGO.transform.position = new Vector3(qoeHolderGO.transform.position.x, qoEHolderPosYInit + currentSliderQuestionNumber * (-200) + 100, qoeHolderGO.transform.position.z) ;

            if (roundManager.currentRoundNumber <= roundManager.totalRoundNumber)
                roundText.text = "Round\n " + roundManager.currentRoundNumber + "/" + roundManager.totalRoundNumber;
            else
                roundText.text = "Thank You!";

            sliderText.text = (qoeSlider.value/10.0).ToString("#.#");
            /*if (qoeSlider.value != 3.000f)
                qoeSubmitGO.SetActive(true);
            else
                qoeSubmitGO.SetActive(false);*/

            if (qoeSlider.value <= 30)
            {
                sliderHandleRed = 1; // From 10 to 30, red is full
            }
            else
            {
                sliderHandleRed = (50 - qoeSlider.value) / 20; // Decreases to 0 by the time it reaches 50
            }

            // sliderHandleGreen scaling
            if (qoeSlider.value <= 30)
            {
                sliderHandleGreen = (qoeSlider.value - 10) / 20; // Increases to 1 by the time it reaches 30
            }
            else
            {
                sliderHandleGreen = 1; // Remains full from 30 to 50
            }

            SliderHandle.color = new Color(sliderHandleRed, sliderHandleGreen,0,1);
        }
        else
        {
            roundText.text = "";
        }
    }

    void UpdateHitMarker()
    {
        if (player.GetComponent<FPSController>().killCooldown > 0)
        {
            hitMarkerImage.color = new Color(1, 0, 0, 1);
        }
        else if (player.GetComponent<FPSController>().headshotCooldown > 0)
        {
            hitMarkerImage.color = new Color(1, 1, 0, 1);
        }
        else if (player.GetComponent<FPSController>().regularHitCooldown > 0)
        {
            hitMarkerImage.color = new Color(1, 1, 1, 1);
        }
        else
        {
            hitMarkerImage.color = new Color(0, 0, 0, 0);
        }
    }

    void PlayBeepSFX(AudioSource source)
    {
        if (source == null || beepSFX == null) return;

        source.volume = UnityEngine.Random.Range(.2f, .3f);
        source.pitch = 0.7f;

        source.PlayOneShot(beepSFX);
    }

    public void QOESubmitPressed()
    {
        if (currentSliderQuestionNumber < numberOfSliderQuestions)
        {
            roundManager.qoeValue.Add((float)qoeSlider.value / 10.0f);
            currentSliderQuestionNumber++;
            qoeSlider.value = 30.000f;
            qoeSubmitGO.SetActive(false);


            /*qoeSubmitGO.SetActive(false);
            acceptabilityGO.SetActive(true);
            player.GetComponent<FPSController>().isAcceptabilityDisabled = false;
            player.GetComponent<FPSController>().isQoeDisabled = true;*/
        }
        if (currentSliderQuestionNumber >= numberOfSliderQuestions)
        {
            qoeSubmitGO.SetActive(false);
            acceptabilityGO.SetActive(true);
            player.GetComponent<FPSController>().isAcceptabilityDisabled = false;
            player.GetComponent<FPSController>().isQoeDisabled = true;
            currentSliderQuestionNumber = 0;
        }
        /*player.GetComponent<FPSController>().isPlayerReady = false;

        roundManager.currentRoundNumber++;
        
        if (roundManager.currentRoundNumber > roundManager.totalRoundNumber)
        {
            TextWriter textWriter = null;
            string filename = "Data\\Configs\\SessionID.csv";

            while (textWriter == null)
                textWriter = File.CreateText(filename);

            roundManager.sessionID++;

            if (roundManager.sessionID > 12)
                roundManager.sessionID = 1;

            textWriter.WriteLine("SessionID");
            textWriter.WriteLine(roundManager.sessionID);
            textWriter.Close();

            Application.Quit(); 
        }
        roundManager.SetRounConfig();
        player.GetComponent<FPSController>().ResetRound();
        enemyManager.spawnTimer = 0;*/
    }

    public void YesPressed()
    {
        roundManager.acceptabilityValue = true;

        roundManager.LogRoundData();
        roundManager.LogPlayerData();

        acceptabilityGO.SetActive(false);
        player.GetComponent<FPSController>().isAcceptabilityDisabled = true;
        player.GetComponent<FPSController>().isPlayerReady = false;

        roundManager.currentRoundNumber++;

        if (roundManager.currentRoundNumber > roundManager.totalRoundNumber)
        {
            TextWriter textWriter = null;
            string filename = "Data\\Configs\\SessionID.csv";

            while (textWriter == null)
                textWriter = File.CreateText(filename);

            roundManager.sessionID++;

            /*if (roundManager.sessionID > 12)
                roundManager.sessionID = 1;*/

            textWriter.WriteLine("SessionID");
            textWriter.WriteLine(roundManager.sessionID);
            textWriter.Close();

            Application.Quit();
        }
        roundManager.SetRounConfig();
        player.GetComponent<FPSController>().ResetRound();
        enemyManager.spawnTimer = 0;
    }

    public void NoPressed()
    {
        roundManager.acceptabilityValue = false;

        roundManager.LogRoundData();
        roundManager.LogPlayerData();

        acceptabilityGO.SetActive(false);
        player.GetComponent<FPSController>().isAcceptabilityDisabled = true;
        player.GetComponent<FPSController>().isPlayerReady = false;

        roundManager.currentRoundNumber++;

        if (roundManager.currentRoundNumber > roundManager.totalRoundNumber)
        {
            TextWriter textWriter = null;
            string filename = "Data\\Configs\\SessionID.csv";

            while (textWriter == null)
                textWriter = File.CreateText(filename);

            roundManager.sessionID++;

            /*if (roundManager.sessionID > roundManager.totalRoundNumber/2) // MUST CHANGE
                roundManager.sessionID = 1;*/

            textWriter.WriteLine("SessionID");
            textWriter.WriteLine(roundManager.sessionID);
            textWriter.Close();

            Application.Quit();
        }
        roundManager.SetRounConfig();
        player.GetComponent<FPSController>().ResetRound();
        enemyManager.spawnTimer = 0;
    }
}
