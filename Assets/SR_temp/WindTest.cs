using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 场景风场入口。
/// PlayerInput 通过 SampleWind 查询当前位置的风。
/// </summary>
public class WindTest : MonoBehaviour
{
    [Header("Wind Probes")]
    [SerializeField]
    private List<WindProbe> probes = new();

    [SerializeField, Min(0.01f)]
    private float cellSize = 20f;

    [SerializeField, Min(0.01f)]
    private float sampleRadius = 20f;

    [Tooltip("启动时自动寻找场景内全部 WindProbe。")]
    [SerializeField]
    private bool autoCollectProbes;

    private WindFieldManager windFieldManager;

    public IReadOnlyList<WindProbe> Probes =>
        probes;

    private void Awake()
    {
        RebuildField();
    }

    [ContextMenu("Rebuild Wind Field")]
    public void RebuildField()
    {
        if (autoCollectProbes)
        {
#if UNITY_2022_2_OR_NEWER
            probes = new List<WindProbe>(
                FindObjectsByType<WindProbe>(
                    FindObjectsSortMode.None
                )
            );
#else
            probes = new List<WindProbe>(
                FindObjectsOfType<WindProbe>()
            );
#endif
        }

        windFieldManager =
            new WindFieldManager
            {
                cellSize = cellSize,
                sampleRadius = sampleRadius
            };

        windFieldManager.Build(probes);
    }

    public Vector3 SampleWind(
        Vector3 worldPosition)
    {
        if (windFieldManager == null)
            RebuildField();

        return windFieldManager.WindEffect(
            worldPosition
        );
    }
}