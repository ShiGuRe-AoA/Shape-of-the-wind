using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 将 WindProbe 按空间网格分类，并计算指定位置的合成风。
/// </summary>
public class WindFieldManager
{
    public float cellSize = 20f;
    public float sampleRadius = 20f;

    private readonly Dictionary<Vector3Int, List<WindProbe>>
        windProbeMap = new();

    public void Build(IReadOnlyList<WindProbe> probes)
    {
        windProbeMap.Clear();

        if (probes == null)
            return;

        for (int i = 0; i < probes.Count; i++)
        {
            WindProbe probe = probes[i];

            if (probe == null)
                continue;

            Vector3Int key =
                WorldToCell(probe.transform.position);

            if (!windProbeMap.TryGetValue(
                    key,
                    out List<WindProbe> list))
            {
                list = new List<WindProbe>();
                windProbeMap.Add(key, list);
            }

            list.Add(probe);
        }
    }

    public Vector3Int WorldToCell(Vector3 position)
    {
        float safeCellSize =
            Mathf.Max(cellSize, 0.01f);

        return new Vector3Int(
            Mathf.FloorToInt(position.x / safeCellSize),
            Mathf.FloorToInt(position.y / safeCellSize),
            Mathf.FloorToInt(position.z / safeCellSize)
        );
    }

    /// <summary>
    /// 返回指定世界位置受到的合成风加速度。
    /// </summary>
    public Vector3 WindEffect(Vector3 worldPosition)
    {
        float safeCellSize =
            Mathf.Max(cellSize, 0.01f);

        float safeRadius =
            Mathf.Max(sampleRadius, 0.01f);

        Vector3Int center =
            WorldToCell(worldPosition);

        Vector3 totalWind =
            Vector3.zero;

        int searchRange =
            Mathf.CeilToInt(
                safeRadius / safeCellSize
            );

        float radiusSqr =
            safeRadius * safeRadius;

        for (int x = -searchRange;
             x <= searchRange;
             x++)
        {
            for (int y = -searchRange;
                 y <= searchRange;
                 y++)
            {
                for (int z = -searchRange;
                     z <= searchRange;
                     z++)
                {
                    Vector3Int key =
                        center +
                        new Vector3Int(x, y, z);

                    if (!windProbeMap.TryGetValue(
                            key,
                            out List<WindProbe> probes))
                    {
                        continue;
                    }

                    for (int i = 0;
                         i < probes.Count;
                         i++)
                    {
                        WindProbe probe =
                            probes[i];

                        if (probe == null)
                            continue;

                        Vector3 offset =
                            probe.transform.position -
                            worldPosition;

                        float distanceSqr =
                            offset.sqrMagnitude;

                        if (distanceSqr > radiusSqr)
                            continue;

                        float distance =
                            Mathf.Sqrt(distanceSqr);

                        float weight =
                            Mathf.Clamp01(
                                1f -
                                distance / safeRadius
                            );

                        // 平滑衰减，避免进入探针范围时风力突变。
                        weight =
                            weight *
                            weight *
                            (3f - 2f * weight);

                        totalWind +=
                            probe.GetWindVector() *
                            weight;
                    }
                }
            }
        }

        return totalWind;
    }
}