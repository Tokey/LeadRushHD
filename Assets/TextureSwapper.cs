using System.Collections.Generic;
using UnityEngine;

public class TextureSwapper : MonoBehaviour
{
    private List<Material[]> originalMaterials = new List<Material[]>();
    public Material lowQualityMaterial;

    private List<Renderer> childRenderers = new List<Renderer>();
    private bool previousLowTextureMode = false;

    GameObject manager;
    RoundManager roundManager;

    void Start()
    {
        manager = GameObject.FindGameObjectWithTag("Manager");
        roundManager = manager.GetComponent<RoundManager>();

        // Add renderers to the list but exclude those from GameObjects with the "EnvironmentParticles" tag
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>())
        {
            if (renderer.gameObject.CompareTag("EnvironmentParticles"))
                continue;

            childRenderers.Add(renderer);
            originalMaterials.Add(renderer.materials);
        }

        previousLowTextureMode = roundManager.isHighResTexturesEnabled;
        UpdateMaterials();
    }

    void Update()
    {
        if (roundManager.isHighResTexturesEnabled != previousLowTextureMode)
        {
            UpdateMaterials();
            previousLowTextureMode = roundManager.isHighResTexturesEnabled;
        }
    }

    private void UpdateMaterials()
    {
        for (int i = 0; i < childRenderers.Count; i++)
        {
            Renderer renderer = childRenderers[i];
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
}
