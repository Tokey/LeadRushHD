using System.Collections;
using System.IO;
using UnityEngine;

public class HDRPScreenshotAuto : MonoBehaviour
{
    RoundManager rm;

    // Screenshot delay after switching quality (seconds)
    public float settleTime = 2f;

    // Screenshot save folder
    string saveFolder;

    void Start()
    {
        rm = FindObjectOfType<RoundManager>();
        if (rm == null)
        {
            Debug.LogError("HDRPScreenshotAuto: RoundManager not found.");
            return;
        }

        saveFolder = Path.Combine(Application.dataPath, "..", "HDRP_Screenshots");
        Directory.CreateDirectory(saveFolder);

        StartCoroutine(RunAllCombinations());
    }

    IEnumerator RunAllCombinations()
    {
        Debug.Log("Starting HDRP screenshot captures...");

        // 8 combinations (TEX, VFX, LIGHT)
        // Format: {TEX, VFX, LIGHT}
        bool[][] combos = new bool[][]
        {
            new bool[]{false, false, false}, // All OFF
            new bool[]{true,  false, false}, // TEX only
            new bool[]{false, true,  false}, // VFX only
            new bool[]{false, false, true }, // LIGHT only
            new bool[]{true,  true,  false}, // TEX + VFX
            new bool[]{true,  false, true }, // TEX + LIGHT
            new bool[]{false, true,  true }, // VFX + LIGHT
            new bool[]{true,  true,  true }, // ALL ON
        };

        string[] names = new string[]
        {
            "AllOff",
            "TexOnly",
            "VfxOnly",
            "LightOnly",
            "TexVfx",
            "TexLight",
            "VfxLight",
            "AllOn"
        };

        for (int i = 0; i < combos.Length; i++)
        {
            ApplyConfig(combos[i][0], combos[i][1], combos[i][2]);
            yield return new WaitForSeconds(settleTime);

            //string fileName = $"{names[i]}_{System.DateTime.Now:HHmmss_fff}.png";
            string fileName = $"{names[i]}.png";
            string path = Path.Combine(saveFolder, fileName);

            ScreenCapture.CaptureScreenshot(path);
            Debug.Log("Captured: " + path);
        }

        Debug.Log("HDRP screenshot automation finished.");
    }

    void ApplyConfig(bool tex, bool vfx, bool light)
    {
        rm.isHighResTexturesEnabled = tex;
        rm.isSkyboxEnabled = tex;

        rm.isPlayerVFXEnabled = vfx;
        rm.isEnemyVFXEnabled = vfx;
        rm.isEnvironmentVFXEnabled = vfx;

        rm.isLightEnabled = light;
        

        //rm.isRenderResHigh = tex; // or leave unchanged if you want
    }
}
