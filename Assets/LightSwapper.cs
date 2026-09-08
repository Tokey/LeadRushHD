using System.Collections.Generic;
using UnityEngine;

public class LightSwapper : MonoBehaviour
{
    private List<Material[]> originalMaterials = new List<Material[]>();
    public Material lowQualityMaterial;

    private List<Renderer> childRenderers = new List<Renderer>();
    private List<Light> childSpotlights = new List<Light>();
    private Dictionary<GameObject, Material[]> bulbGlassOriginalMaterials = new Dictionary<GameObject, Material[]>();

    private bool previousLowTextureMode = false;
    private bool previousLightEnabled = true;

    GameObject manager;
    RoundManager roundManager;

    void Start()
    {
        manager = GameObject.FindGameObjectWithTag("Manager");
        roundManager = manager.GetComponent<RoundManager>();

        // Collect all renderers and spotlights
        childRenderers.AddRange(GetComponentsInChildren<Renderer>());
        childSpotlights.AddRange(GetComponentsInChildren<Light>());

        foreach (Renderer renderer in childRenderers)
        {
            originalMaterials.Add(renderer.materials);

            // Store original materials for objects tagged "BulbGlass"
            if (renderer.gameObject.CompareTag("BulbGlass"))
            {
                bulbGlassOriginalMaterials[renderer.gameObject] = renderer.materials;
            }
        }

        previousLowTextureMode = roundManager.isHighResTexturesEnabled;
        previousLightEnabled = roundManager.isLightEnabled;

        UpdateMaterials();
        UpdateLights();
        UpdateBulbGlassMaterials();
    }

    void Update()
    {
        if (roundManager.isHighResTexturesEnabled != previousLowTextureMode)
        {
            UpdateMaterials();
            previousLowTextureMode = roundManager.isHighResTexturesEnabled;
        }

        if (roundManager.isLightEnabled != previousLightEnabled)
        {
            UpdateLights();
            UpdateBulbGlassMaterials();
            previousLightEnabled = roundManager.isLightEnabled;
        }
    }

    private void UpdateMaterials()
    {
        for (int i = 0; i < childRenderers.Count; i++)
        {
            Renderer renderer = childRenderers[i];

            // Skip "BulbGlass" objects when swapping general materials
            if (renderer.gameObject.CompareTag("BulbGlass"))
            {
                continue;
            }

            if (!roundManager.isHighResTexturesEnabled)
            {
                Material[] lowQualityMats = new Material[renderer.materials.Length];
                for (int j = 0; j < lowQualityMats.Length; j++)
                {
                    lowQualityMats[j] = lowQualityMaterial;
                }
                renderer.materials = lowQualityMats;
            }
            else
            {
                renderer.materials = originalMaterials[i];
            }
        }
    }

    private void UpdateLights()
    {
        foreach (Light light in childSpotlights)
        {
            // Enable or disable spotlights based on isLightEnabled
            light.enabled = roundManager.isLightEnabled;
        }
    }

    private void UpdateBulbGlassMaterials()
    {
        foreach (var bulbGlass in bulbGlassOriginalMaterials)
        {
            Renderer renderer = bulbGlass.Key.GetComponent<Renderer>();
            if (!roundManager.isLightEnabled)
            {
                // Swap "BulbGlass" materials to low-quality texture
                Material[] lowQualityMats = new Material[renderer.materials.Length];
                for (int i = 0; i < lowQualityMats.Length; i++)
                {
                    lowQualityMats[i] = lowQualityMaterial;
                }
                renderer.materials = lowQualityMats;
            }
            else
            {
                // Restore original "BulbGlass" materials
                renderer.materials = bulbGlass.Value;
            }
        }
    }
}
