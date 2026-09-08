using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

public class SkySwapper : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created

    public Camera camera;
    bool previousSkySolid;
    GameObject manager;
    RoundManager roundManager;
    void Start()
    {

        manager = GameObject.FindGameObjectWithTag("Manager");
        roundManager = manager.GetComponent<RoundManager>();

        previousSkySolid = roundManager.isSkyboxEnabled;
        camera = GetComponent<Camera>();
        UpdateSky();
    }

    void Update()
    {
        if (roundManager.isSkyboxEnabled != previousSkySolid)
        {
            UpdateSky();
            previousSkySolid = roundManager.isSkyboxEnabled;
        }
    }

    void UpdateSky()
    {
        if (roundManager.isSkyboxEnabled)
        {
            camera.GetComponent<HDAdditionalCameraData>().clearColorMode = HDAdditionalCameraData.ClearColorMode.Sky;
        }
        else
        {
            camera.GetComponent<HDAdditionalCameraData>().clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
        }
    }
}
