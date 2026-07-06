using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 用三个 Transform 的世界坐标定义一个 <b>轴对齐包围盒（AABB）</b>：
/// 三点各自 X/Y/Z 分量取 min/max 得到盒子。不考虑倾斜、深度。
///
/// 遍历 root 下所有 <see cref="WindProbe"/>，把它们的世界坐标 (x, y, z)
/// 分别投影到 YZ / XZ / XY 三个坐标平面，作为 UV 采样对应的 XYZ 三张噪声图。
/// 采样得到的灰度作为该探针风力向量的 X / Y / Z 分量。
///
/// 最终方向 = 归一化(sampledXYZ - center)
/// 最终强度 = |sampledXYZ - center| 的模长映射到 [0, maxStrength]。
///
/// 写入 <see cref="WindProbe"/> 的 private 字段 windDirection / windStrength，
/// 通过 SerializedObject 完成，支持 Undo 与预制体覆盖标记。
/// </summary>
public class WindProbeNoiseBaker : EditorWindow
{
    // ----------------- 包围盒定义（三点 → AABB） -----------------
    [SerializeField] private Transform pointA;
    [SerializeField] private Transform pointB;
    [SerializeField] private Transform pointC;

    // ----------------- 噪声图 -----------------
    [SerializeField] private Texture2D noiseX; // 决定 X 分量，采样自 YZ 平面
    [SerializeField] private Texture2D noiseY; // 决定 Y 分量，采样自 XZ 平面
    [SerializeField] private Texture2D noiseZ; // 决定 Z 分量，采样自 XY 平面

    // ----------------- 探针查找根 -----------------
    [SerializeField] private Transform probeRoot;
    [SerializeField] private bool includeInactive = true;

    // ----------------- 强度映射 -----------------
    [SerializeField, Min(0f)] private float maxStrength = 10f;
    [Tooltip("采样灰度中心（灰度 - center）映射到 [-1, 1]。0.5 表示中性灰无风。")]
    [SerializeField, Range(0f, 1f)] private float centerGray = 0.5f;

    // ----------------- 探针生成 -----------------
    [SerializeField] private Vector3 probeSpacing = new Vector3(5f, 5f, 5f);
    [Tooltip("生成的探针名称前缀。")]
    [SerializeField] private string probeNamePrefix = "WindProbe";
    [Tooltip("生成时是否把探针边缘对齐到包围盒边界（否则均匀内缩，每格中心）。")]
    [SerializeField] private bool alignToBoxEdges = true;

    private Vector2 _scroll;

    [MenuItem("Tools/Wind/Bake Wind Probes From Noise")]
    private static void Open()
    {
        GetWindow<WindProbeNoiseBaker>("Wind Probe Baker");
    }

    private void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        EditorGUILayout.LabelField("包围盒 (AABB, 由三点确定)", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "三个 Transform 的世界坐标各自取 X/Y/Z 的 min/max，得到一个轴对齐立方体。\n" +
            "只关心三点组成的坐标范围，不考虑倾斜/朝向/深度。",
            MessageType.None);
        pointA = (Transform)EditorGUILayout.ObjectField("Point A", pointA, typeof(Transform), true);
        pointB = (Transform)EditorGUILayout.ObjectField("Point B", pointB, typeof(Transform), true);
        pointC = (Transform)EditorGUILayout.ObjectField("Point C", pointC, typeof(Transform), true);

        if (TryGetBounds(out var previewMin, out var previewMax))
        {
            Vector3 sz = previewMax - previewMin;
            EditorGUILayout.LabelField(
                $"AABB: min=({previewMin.x:F2},{previewMin.y:F2},{previewMin.z:F2})  " +
                $"size=({sz.x:F2},{sz.y:F2},{sz.z:F2})",
                EditorStyles.miniLabel);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("噪声图", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "三张图分别决定 X / Y / Z 分量：\n" +
            "  Noise X 用探针 (y, z) 归一化坐标采样（YZ 面投影）\n" +
            "  Noise Y 用探针 (x, z) 归一化坐标采样（XZ 面投影）\n" +
            "  Noise Z 用探针 (x, y) 归一化坐标采样（XY 面投影）\n" +
            "纹理需在 Import Settings 中勾选 Read/Write Enabled。",
            MessageType.Info);
        noiseX = (Texture2D)EditorGUILayout.ObjectField("Noise X (YZ)", noiseX, typeof(Texture2D), false);
        noiseY = (Texture2D)EditorGUILayout.ObjectField("Noise Y (XZ)", noiseY, typeof(Texture2D), false);
        noiseZ = (Texture2D)EditorGUILayout.ObjectField("Noise Z (XY)", noiseZ, typeof(Texture2D), false);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("探针", EditorStyles.boldLabel);
        probeRoot = (Transform)EditorGUILayout.ObjectField(
            new GUIContent("Probe Root", "在该 Transform 下递归查找所有 WindProbe"),
            probeRoot, typeof(Transform), true);
        includeInactive = EditorGUILayout.Toggle("Include Inactive", includeInactive);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("强度映射", EditorStyles.boldLabel);
        maxStrength = EditorGUILayout.FloatField(
            new GUIContent("Max Strength", "|采样向量| 归一化到 [0,1] 后乘以此值"),
            maxStrength);
        centerGray = EditorGUILayout.Slider(
            new GUIContent("Center Gray", "该灰度值视为无风；偏离该值越远方向分量越强"),
            centerGray, 0f, 1f);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("探针生成", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "点击 Regenerate 会先清空 Probe Root 下所有现存 WindProbe，" +
            "再按下面的空间间隔在 AABB 内部布点生成新的探针（不采样）。\n" +
            "Regenerate & Bake 则会在生成后立即执行一次 Bake。",
            MessageType.None);
        probeSpacing = EditorGUILayout.Vector3Field(
            new GUIContent("Spacing (X,Y,Z)", "沿世界 X / Y / Z 三个轴的探针间距（世界单位）"),
            probeSpacing);
        probeNamePrefix = EditorGUILayout.TextField(
            new GUIContent("Name Prefix", "生成的探针 GameObject 名称前缀"),
            probeNamePrefix);
        alignToBoxEdges = EditorGUILayout.Toggle(
            new GUIContent("Align To Box Edges", "开启：首末探针贴盒面；关闭：均匀内缩排布"),
            alignToBoxEdges);

        // 预览一下会生成多少个
        if (CanGenerate())
        {
            var counts = ComputeGridCounts();
            int total = counts.x * counts.y * counts.z;
            EditorGUILayout.LabelField(
                $"Grid: {counts.x} × {counts.y} × {counts.z}  =  {total} probes",
                EditorStyles.miniLabel);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            GUI.enabled = CanGenerate();
            if (GUILayout.Button("Regenerate Probes", GUILayout.Height(28)))
            {
                RegenerateProbes();
            }
            GUI.enabled = CanGenerate() && CanBake();
            if (GUILayout.Button("Regenerate & Bake", GUILayout.Height(28)))
            {
                if (RegenerateProbes())
                    Bake();
            }
            GUI.enabled = true;
        }

        EditorGUILayout.Space();
        GUI.enabled = CanBake();
        if (GUILayout.Button("Bake", GUILayout.Height(32)))
        {
            Bake();
        }
        GUI.enabled = true;

        if (!CanBake())
        {
            EditorGUILayout.HelpBox(
                "Bake 需要设置：Point A / B / C（至少两点以构成非退化盒）、三张 Noise 纹理、Probe Root。",
                MessageType.Warning);
        }

        EditorGUILayout.EndScrollView();
    }

    private bool CanBake()
    {
        return HasEnoughPoints() &&
               noiseX != null && noiseY != null && noiseZ != null &&
               probeRoot != null;
    }

    private bool CanGenerate()
    {
        return HasEnoughPoints() &&
               probeRoot != null &&
               probeSpacing.x > 1e-4f &&
               probeSpacing.y > 1e-4f &&
               probeSpacing.z > 1e-4f;
    }

    private bool HasEnoughPoints()
    {
        int c = 0;
        if (pointA != null) c++;
        if (pointB != null) c++;
        if (pointC != null) c++;
        return c >= 2; // 至少两点能构成非零盒（虽然可能有轴退化，允许）
    }

    /// <summary>
    /// 三个控制点世界坐标逐分量取 min/max。至少需要一个点，返回 false 表示完全无点。
    /// </summary>
    private bool TryGetBounds(out Vector3 min, out Vector3 max)
    {
        min = Vector3.zero;
        max = Vector3.zero;

        Vector3 lo = Vector3.zero;
        Vector3 hi = Vector3.zero;
        bool has = false;

        if (pointA != null)
        {
            lo = pointA.position; hi = pointA.position; has = true;
        }
        if (pointB != null)
        {
            if (!has) { lo = pointB.position; hi = pointB.position; has = true; }
            else { lo = Vector3.Min(lo, pointB.position); hi = Vector3.Max(hi, pointB.position); }
        }
        if (pointC != null)
        {
            if (!has) { lo = pointC.position; hi = pointC.position; has = true; }
            else { lo = Vector3.Min(lo, pointC.position); hi = Vector3.Max(hi, pointC.position); }
        }

        min = lo;
        max = hi;
        return has;
    }

    // ---------------- 生成 ----------------

    /// <summary>
    /// 计算 AABB 内沿世界 X/Y/Z 方向上的探针数量。至少 1 个。
    /// </summary>
    private Vector3Int ComputeGridCounts()
    {
        if (!TryGetBounds(out var min, out var max))
            return Vector3Int.one;

        Vector3 size = max - min;
        int cx = Mathf.Max(1, Mathf.FloorToInt(size.x / Mathf.Max(1e-4f, probeSpacing.x)) + 1);
        int cy = Mathf.Max(1, Mathf.FloorToInt(size.y / Mathf.Max(1e-4f, probeSpacing.y)) + 1);
        int cz = Mathf.Max(1, Mathf.FloorToInt(size.z / Mathf.Max(1e-4f, probeSpacing.z)) + 1);
        return new Vector3Int(cx, cy, cz);
    }

    /// <summary>
    /// 清空 probeRoot 下所有 WindProbe 所在的 GameObject（不删 root 本身），
    /// 然后按 spacing 在 AABB 内均匀铺一层探针。
    /// </summary>
    private bool RegenerateProbes()
    {
        if (!CanGenerate())
        {
            EditorUtility.DisplayDialog(
                "无法生成",
                "请先设置至少两个 Point、Probe Root，并保证 Spacing 三个分量都大于 0。",
                "OK");
            return false;
        }

        if (!TryGetBounds(out var min, out var max))
        {
            EditorUtility.DisplayDialog("包围盒无效", "没有可用的 Point。", "OK");
            return false;
        }

        Vector3 size = max - min;

        var counts = ComputeGridCounts();
        int total = counts.x * counts.y * counts.z;
        if (total > 20000)
        {
            if (!EditorUtility.DisplayDialog(
                "生成探针数量过多",
                $"将要生成 {total} 个 WindProbe，可能非常慢或卡顿。是否继续？",
                "继续", "取消"))
                return false;
        }

        Undo.SetCurrentGroupName("Regenerate Wind Probes");
        int undoGroup = Undo.GetCurrentGroup();

        // 1) 清空现存 WindProbe
        var existing = new List<WindProbe>();
        probeRoot.GetComponentsInChildren(true, existing);
        int cleared = 0;
        foreach (var p in existing)
        {
            if (p == null) continue;
            Undo.DestroyObjectImmediate(p.gameObject);
            cleared++;
        }

        // 2) 逐点生成
        int created = 0;
        try
        {
            for (int ix = 0; ix < counts.x; ix++)
            {
                for (int iy = 0; iy < counts.y; iy++)
                {
                    for (int iz = 0; iz < counts.z; iz++)
                    {
                        EditorUtility.DisplayProgressBar(
                            "Generating Wind Probes",
                            $"{created + 1}/{total}",
                            (float)(created + 1) / total);

                        float px = min.x + SampleParam(ix, counts.x, size.x);
                        float py = min.y + SampleParam(iy, counts.y, size.y);
                        float pz = min.z + SampleParam(iz, counts.z, size.z);

                        var go = new GameObject($"{probeNamePrefix}_{ix}_{iy}_{iz}");
                        Undo.RegisterCreatedObjectUndo(go, "Create Wind Probe");
                        go.transform.SetParent(probeRoot, worldPositionStays: true);
                        go.transform.position = new Vector3(px, py, pz);
                        go.transform.rotation = Quaternion.identity;
                        go.transform.localScale = Vector3.one;
                        Undo.AddComponent<WindProbe>(go);
                        created++;
                    }
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneMarkDirty();
        Debug.Log($"[WindProbeNoiseBaker] Cleared {cleared} existing probe(s), generated {created} new probe(s).");
        return created > 0;
    }

    /// <summary>
    /// 沿单轴计算第 i 个点的坐标（相对轴起点的偏移，世界单位）。
    /// alignToBoxEdges = true 时首尾贴边；count==1 时放中点。
    /// </summary>
    private float SampleParam(int index, int count, float axisLen)
    {
        if (count <= 1) return axisLen * 0.5f;
        if (alignToBoxEdges)
        {
            return axisLen * ((float)index / (count - 1));
        }
        float step = axisLen / count;
        return step * (index + 0.5f);
    }

    private static void EditorSceneMarkDirty()
    {
        if (!Application.isPlaying)
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
        }
    }

    // ---------------- 核心 ----------------

    private void Bake()
    {
        if (!EnsureReadable(noiseX) | !EnsureReadable(noiseY) | !EnsureReadable(noiseZ))
        {
            EditorUtility.DisplayDialog(
                "Noise 纹理不可读",
                "至少一张噪声图未启用 Read/Write。请在其 Texture Import Settings 中勾选 Read/Write Enabled 后重试。",
                "OK");
            return;
        }

        if (!TryGetBounds(out var min, out var max))
        {
            EditorUtility.DisplayDialog("包围盒无效", "没有可用的 Point。", "OK");
            return;
        }
        Vector3 size = max - min;
        if (size.x < 1e-4f && size.y < 1e-4f && size.z < 1e-4f)
        {
            EditorUtility.DisplayDialog("包围盒无效", "三点重合，包围盒尺寸为 0。", "OK");
            return;
        }

        var probes = new List<WindProbe>();
        probeRoot.GetComponentsInChildren(includeInactive, probes);
        if (probes.Count == 0)
        {
            EditorUtility.DisplayDialog("没有探针", "Probe Root 下未找到任何 WindProbe。", "OK");
            return;
        }

        Undo.SetCurrentGroupName("Bake Wind Probes From Noise");
        int undoGroup = Undo.GetCurrentGroup();

        int written = 0;
        int skipped = 0;

        try
        {
            for (int i = 0; i < probes.Count; i++)
            {
                var probe = probes[i];
                if (probe == null) continue;

                EditorUtility.DisplayProgressBar(
                    "Baking Wind Probes",
                    $"{probe.name} ({i + 1}/{probes.Count})",
                    (float)i / probes.Count);

                Vector3 world = probe.transform.position;
                // 各轴归一化到 [0,1]，越界 clamp（相当于把边界外投到盒面）
                float nx = SafeNormalize(world.x - min.x, size.x);
                float ny = SafeNormalize(world.y - min.y, size.y);
                float nz = SafeNormalize(world.z - min.z, size.z);

                // 三张图分别按对应平面 UV 采样
                float gx = SampleGray(noiseX, ny, nz); // YZ 平面 → 决定 X
                float gy = SampleGray(noiseY, nx, nz); // XZ 平面 → 决定 Y
                float gz = SampleGray(noiseZ, nx, ny); // XY 平面 → 决定 Z

                // 灰度中心化到 [-1, 1]
                float invDen = 1f / Mathf.Max(1e-4f, Mathf.Max(centerGray, 1f - centerGray));
                float x = (gx - centerGray) * invDen;
                float y = (gy - centerGray) * invDen;
                float z = (gz - centerGray) * invDen;

                Vector3 raw = new Vector3(x, y, z);
                float mag = raw.magnitude;
                Vector3 dir = mag > 1e-5f ? raw / mag : Vector3.zero;
                // sqrt(3) ≈ 1.732 是三个 [-1,1] 分量能达到的最大模长；归一化到 [0,1]
                float normalizedMag = Mathf.Clamp01(mag / Mathf.Sqrt(3f));
                float strength = normalizedMag * maxStrength;

                if (WriteProbe(probe, dir, strength))
                    written++;
                else
                    skipped++;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        Undo.CollapseUndoOperations(undoGroup);
        Debug.Log($"[WindProbeNoiseBaker] Baked {written} probe(s), skipped {skipped}.");
    }

    /// <summary>
    /// 归一化到 [0,1]。轴长为 0 时返回 0.5（该轴退化成一片，视为中心）。
    /// </summary>
    private static float SafeNormalize(float value, float length)
    {
        if (length < 1e-4f) return 0.5f;
        return Mathf.Clamp01(value / length);
    }

    /// <summary>
    /// 通过 SerializedObject 写入 WindProbe 的 windDirection / windStrength 私有字段。
    /// 同时把 useLocalDirection 关闭（写入的是世界方向）。
    /// </summary>
    private static bool WriteProbe(WindProbe probe, Vector3 worldDir, float strength)
    {
        var so = new SerializedObject(probe);
        var dirProp = so.FindProperty("windDirection");
        var strProp = so.FindProperty("windStrength");
        var localProp = so.FindProperty("useLocalDirection");

        if (dirProp == null || strProp == null)
        {
            Debug.LogWarning($"[WindProbeNoiseBaker] {probe.name} 缺少期望字段，跳过。", probe);
            return false;
        }

        Undo.RegisterCompleteObjectUndo(probe, "Bake Wind Probe");

        dirProp.vector3Value = worldDir;
        strProp.floatValue = strength;
        if (localProp != null)
            localProp.boolValue = false;

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(probe);

        if (PrefabUtility.IsPartOfPrefabInstance(probe))
            PrefabUtility.RecordPrefabInstancePropertyModifications(probe);

        return true;
    }

    private static float SampleGray(Texture2D tex, float u, float v)
    {
        Color c = tex.GetPixelBilinear(u, v);
        return c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
    }

    /// <summary>
    /// 保证纹理可读；不可读时尝试自动开启 Read/Write 并重新导入。
    /// </summary>
    private static bool EnsureReadable(Texture2D tex)
    {
        if (tex == null) return false;
        string path = AssetDatabase.GetAssetPath(tex);
        if (string.IsNullOrEmpty(path)) return true;

        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return true;
        if (importer.isReadable) return true;

        importer.isReadable = true;
        importer.SaveAndReimport();
        return importer.isReadable;
    }

    // ---------------- Scene Gizmo（窗口打开时绘制 AABB） ----------------

    private void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
    }

    private void OnSceneGUI(SceneView view)
    {
        if (!TryGetBounds(out var min, out var max)) return;

        Vector3 size = max - min;
        Vector3 center = (min + max) * 0.5f;

        Handles.color = new Color(0.2f, 0.9f, 0.4f, 0.9f);
        Handles.DrawWireCube(center, size);
    }
}
