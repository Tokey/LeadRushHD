using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// The experiment configs are read with paths relative to the working directory
/// (see RoundManager.PathGlobalConfig and GameManager), which is the project root
/// in the editor but the folder holding the player executable in a build. Unity
/// only ships what lives under Assets, so without this step a fresh build starts
/// with no Data folder and every config read fails.
///
/// After each standalone build this copies the project's Data\Configs and the
/// top level Data\*.csv feeds next to the executable, and creates an empty
/// Data\Logs for the run to write into. Data\Logs is deliberately not copied -
/// previous sessions' logs do not belong in a build.
/// </summary>
public class CopyDataOnBuild : IPostprocessBuildWithReport
{
    public int callbackOrder { get { return 0; } }

    public void OnPostprocessBuild(BuildReport report)
    {
        string outputPath = report.summary.outputPath;
        if (string.IsNullOrEmpty(outputPath))
            return;

        // outputPath is the executable on Windows/Linux and the .app bundle
        // directory on macOS; either way the player's working directory is the
        // folder that contains it.
        string destRoot = Directory.Exists(outputPath)
            ? Directory.GetParent(outputPath).FullName
            : Path.GetDirectoryName(outputPath);

        if (string.IsNullOrEmpty(destRoot))
            return;

        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string sourceData = Path.Combine(projectRoot, "Data");

        if (!Directory.Exists(sourceData))
        {
            Debug.LogWarning("CopyDataOnBuild: no Data folder at " + sourceData + "; the build will start without configs.");
            return;
        }

        string destData = Path.Combine(destRoot, "Data");

        try
        {
            int copied = 0;

            // Top level feeds (FrametimerFeedSine.csv, DelayDuration.csv, ...).
            copied += CopyCsvFiles(sourceData, destData);

            // Configs, which the game also writes back to (SessionID.csv).
            string sourceConfigs = Path.Combine(sourceData, "Configs");
            if (Directory.Exists(sourceConfigs))
                copied += CopyCsvFiles(sourceConfigs, Path.Combine(destData, "Configs"));
            else
                Debug.LogWarning("CopyDataOnBuild: no Data\\Configs folder at " + sourceConfigs + ".");

            // The logger creates this itself, but shipping it empty makes the
            // build's output location obvious.
            Directory.CreateDirectory(Path.Combine(destData, "Logs"));

            Debug.Log("CopyDataOnBuild: copied " + copied + " csv file(s) to " + destData);
        }
        catch (Exception e)
        {
            Debug.LogError("CopyDataOnBuild: failed to copy Data to " + destData + ": " + e.Message);
        }
    }

    /// <summary>Copies the *.csv directly inside sourceDir into destDir, overwriting.</summary>
    static int CopyCsvFiles(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        int copied = 0;
        foreach (string file in Directory.GetFiles(sourceDir, "*.csv", SearchOption.TopDirectoryOnly))
        {
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), true);
            copied++;
        }
        return copied;
    }
}
