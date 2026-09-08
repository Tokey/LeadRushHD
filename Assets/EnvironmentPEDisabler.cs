using UnityEngine;

public class EnvironmentPEDisabler : MonoBehaviour
{
    private GameObject manager;
    private RoundManager roundManager;

    private bool previousVFXState;
    private GameObject[] environmentParticles;

    void Start()
    {
        // Find the RoundManager to track the VFX state
        manager = GameObject.FindGameObjectWithTag("Manager");
        roundManager = manager.GetComponent<RoundManager>();

        // Get all GameObjects with the "EnvironmentParticles" tag
        environmentParticles = GameObject.FindGameObjectsWithTag("EnvironmentParticles");

        // Store the initial state of isEnvironmentVFXEnabled
        previousVFXState = roundManager.isEnvironmentVFXEnabled;

        // Update the active state of the objects at the start
        ToggleEnvironmentParticles(roundManager.isEnvironmentVFXEnabled);
    }

    void Update()
    {
        // Check if the state has changed and toggle objects accordingly
        if (roundManager.isEnvironmentVFXEnabled != previousVFXState)
        {
            ToggleEnvironmentParticles(roundManager.isEnvironmentVFXEnabled);
            previousVFXState = roundManager.isEnvironmentVFXEnabled;
        }
    }

    private void ToggleEnvironmentParticles(bool enable)
    {
        // Toggle the active state of all "EnvironmentParticles" objects
        foreach (GameObject particle in environmentParticles)
        {
            particle.SetActive(enable);
        }
    }
}
