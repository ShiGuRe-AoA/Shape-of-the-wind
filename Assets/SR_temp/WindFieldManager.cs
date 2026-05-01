using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices.WindowsRuntime;
using UnityEngine;

public class WindFieldManager
{
    public float cellSize = 20f;
    public float sampleRadius = 20f;

    public Dictionary<Vector3Int, List<WindProbe>> windProbeMap = new();

    public void Build(List<WindProbe> probes)
    {
        windProbeMap.Clear();

        foreach (var probe in probes)
        {
            Vector3Int key = WorldToCell(probe.transform.position);

            if (!windProbeMap.TryGetValue(key, out var list))
            {
                list = new List<WindProbe>();
                windProbeMap[key] = list;
            }

            list.Add(probe);
        }
    }

    public Vector3Int WorldToCell(Vector3 pos)
    {
        return new Vector3Int(
            Mathf.FloorToInt(pos.x / cellSize),
            Mathf.FloorToInt(pos.y / cellSize),
            Mathf.FloorToInt(pos.z / cellSize)
        );
    }

    //返回计算过的风效果
    public Vector3 WindEffect(Vector3 worldPos)
    {
        Vector3Int center = WorldToCell(worldPos);
        Vector3 totalWindEffect = Vector3.zero;

        int searchRange = Mathf.CeilToInt(sampleRadius / cellSize);

        for (int x = -searchRange; x <= searchRange; x++)
        {
            for (int y = -searchRange; y <= searchRange; y++)
            {
                for (int z = -searchRange; z <= searchRange; z++)
                {
                    Vector3Int key = new Vector3Int(
                        center.x + x,
                        center.y + y,
                        center.z + z
                        );

                    if (!windProbeMap.TryGetValue(key, out var list))
                        continue;

                    foreach (var probe in list)
                    {
                        float distance = Vector3.Distance(worldPos, probe.transform.position);

                        if (distance > sampleRadius)
                            continue;

                        float weight = Mathf.Clamp01(1f - distance / sampleRadius);
                        
                        Vector3 direction = probe.windDirection.normalized;
                        totalWindEffect += direction * probe.windStrength * weight;
                    }
                }
            }
        }
        return totalWindEffect;
    }

    //返回附近的风探针列表
    //public List<WindProbe> SampleWindProbes(Vector3 worldPos)
    //{
    //    Vector3Int center = WorldToCell(worldPos);

    //    List<WindProbe> result = new List<WindProbe>();
    //    for (int x = -1; x <= 1; x++)
    //    {
    //        for (int y = -1; y <= 1; y++)
    //        {
    //            for (int z = -1; z <= 1; z++)
    //            {
    //                Vector3Int key = center + new Vector3Int(x, y, z);
    //                if (!windProbeMap.TryGetValue(key, out var list))
    //                    continue;

    //                foreach (var probe in list)
    //                {
    //                    result.Add(probe);
    //                }
    //            }
    //        }
    //    }
    //    return result;
    //}
}
