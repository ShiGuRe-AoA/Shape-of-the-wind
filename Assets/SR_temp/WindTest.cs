using System.Collections.Generic;
using UnityEngine;

public class WindTest : MonoBehaviour
{
    public GameObject bird;
    public List<WindProbe> probes = new List<WindProbe>();

    private WindFieldManager windFieldManager;

    void Start()
    {
        windFieldManager = new WindFieldManager();

        windFieldManager.Build(probes);

        Debug.Log("Wind probes count: " + probes.Count);
    }

    void Update()
    {
        if (bird == null || windFieldManager == null)
            return;

        Vector3 wind = windFieldManager.WindEffect(bird.transform.position);
        Debug.Log("Wind: " + wind);
    }
}