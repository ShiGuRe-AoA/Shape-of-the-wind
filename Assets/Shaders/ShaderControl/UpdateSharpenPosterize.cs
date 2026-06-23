using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UpdateSharpenPosterize : MonoBehaviour
{
    [SerializeField] private Material mat;

    [SerializeField]
    [Range(0, 5f)] private float noiseSpeed = 0.5f;

    [SerializeField]
    [Range(0, 5f)] private float noiseRange = 0.2f;

    private float posterizeSteps;
    private float seed;

    private void Awake()
    {
        if (mat == null) throw new ArgumentNullException(nameof(mat));
        posterizeSteps = mat.GetFloat("_PosterizeSteps");

        seed = UnityEngine.Random.Range(0f, 1000f);
    }

    // Update is called once per frame
    void Update()
    {
        float t = Time.time * noiseSpeed + seed;

        // Perlin: ouput 0 - 1
        float noise = Mathf.PerlinNoise(t, 0f);
        
        // -1 - 1
        noise = noise * 2f - 1f;

        float value = posterizeSteps + noise * noiseRange;
        mat.SetFloat("_PosterizeSteps", value);
    }
}
