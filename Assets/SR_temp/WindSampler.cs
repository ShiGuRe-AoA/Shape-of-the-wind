using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 通用风力查询工具。
///
/// 目标：让任何脚本 / 物体只需一行 <c>WindSampler.Sample(worldPos)</c> 即可
/// 拿到该位置的合成风向量（同 <see cref="WindFieldManager"/> 的加权规则）。
///
/// 使用方式：
/// 1. 在场景里放一个挂了本组件的 GameObject（通常和风探针放在同一层级）。
/// 2. 勾选 <see cref="autoCollectProbes"/> 让它自动扫描场景内所有 <see cref="WindProbe"/>，
///    或者手动把探针拖进 <see cref="probes"/> 列表。
/// 3. 任意脚本调用 <c>WindSampler.Sample(pos)</c> 即可。若场景里没有 WindSampler，会返回 <see cref="Vector3.zero"/>。
///
/// 与旧 <see cref="WindTest"/> 的关系：
/// - 内部逻辑完全一致（复用 <see cref="WindFieldManager"/>）。
/// - 提供全局静态入口，避免各处都要序列化 <see cref="WindTest"/> 引用。
/// </summary>
[DisallowMultipleComponent]
public class WindSampler : MonoBehaviour
{
    // ---------------- 全局单例 ----------------

    private static WindSampler activeInstance;

    /// <summary>当前场景中生效的采样器实例。可能为 null（例如场景未放置组件）。</summary>
    public static WindSampler Instance => activeInstance;

    // ---------------- Inspector ----------------

    [Header("Wind Probes")]

    [Tooltip("参与风力合成的探针。可手动填，也可通过 Auto Collect 自动扫描。")]
    [SerializeField]
    private List<WindProbe> probes = new();

    [Tooltip("Awake 时自动扫描场景内的全部 WindProbe。")]
    [SerializeField]
    private bool autoCollectProbes = true;

    [Header("Sampling")]

    [Tooltip("空间哈希单元格边长。越大邻居越少但可能漏掉远处探针，通常与 Sample Radius 同数量级。")]
    [SerializeField, Min(0.01f)]
    private float cellSize = 20f;

    [Tooltip("采样半径。超过该距离的探针不参与合成，衰减权重也基于该半径。")]
    [SerializeField, Min(0.01f)]
    private float sampleRadius = 20f;

    [Header("Debug")]

    [Tooltip("在 Scene 视图选中时用小球标记当前采样器位置及探针连线。")]
    [SerializeField]
    private bool drawGizmos = true;

    // ---------------- 内部状态 ----------------

    private WindFieldManager fieldManager;

    // ---------------- 生命周期 ----------------

    private void Awake()
    {
        RebuildField();
        Register();
    }

    private void OnEnable()
    {
        // 兼容运行时启用 / 禁用组件的用法
        if (activeInstance != this)
            Register();
    }

    private void OnDisable()
    {
        if (activeInstance == this)
            activeInstance = null;
    }

    private void OnDestroy()
    {
        if (activeInstance == this)
            activeInstance = null;
    }

    private void Register()
    {
        if (activeInstance != null && activeInstance != this)
        {
            Debug.LogWarning(
                "[WindSampler] 场景中已存在另一个 WindSampler，后注册的将覆盖前者。" +
                "若这是刻意行为可忽略；否则请只保留一个实例。",
                this
            );
        }

        activeInstance = this;
    }

    // ---------------- 公共接口 ----------------

    /// <summary>
    /// 全局静态入口：查询某世界坐标处的合成风向量。
    /// 场景中没有可用 <see cref="WindSampler"/> 时返回 <see cref="Vector3.zero"/>。
    /// </summary>
    public static Vector3 Sample(Vector3 worldPosition)
    {
        if (activeInstance == null)
            return Vector3.zero;

        return activeInstance.SampleInternal(worldPosition);
    }

    /// <summary>
    /// 与 <see cref="Sample"/> 等价的实例方法。适合已经持有具体实例引用时使用。
    /// </summary>
    public Vector3 SampleAt(Vector3 worldPosition)
    {
        return SampleInternal(worldPosition);
    }

    /// <summary>
    /// 重新扫描 / 重建空间哈希。运行时增删探针后调用一次即可。
    /// </summary>
    [ContextMenu("Rebuild Wind Field")]
    public void RebuildField()
    {
        if (autoCollectProbes)
        {
#if UNITY_2022_2_OR_NEWER
            probes = new List<WindProbe>(
                FindObjectsByType<WindProbe>(FindObjectsSortMode.None)
            );
#else
            probes = new List<WindProbe>(
                FindObjectsOfType<WindProbe>()
            );
#endif
        }

        fieldManager = new WindFieldManager
        {
            cellSize = cellSize,
            sampleRadius = sampleRadius
        };

        fieldManager.Build(probes);
    }

    /// <summary>手动设置探针集合（不改 Auto Collect 标记）。会立即重建空间哈希。</summary>
    public void SetProbes(IReadOnlyList<WindProbe> newProbes)
    {
        probes.Clear();
        if (newProbes != null)
        {
            for (int i = 0; i < newProbes.Count; i++)
            {
                if (newProbes[i] != null)
                    probes.Add(newProbes[i]);
            }
        }

        fieldManager ??= new WindFieldManager
        {
            cellSize = cellSize,
            sampleRadius = sampleRadius
        };
        fieldManager.cellSize = cellSize;
        fieldManager.sampleRadius = sampleRadius;
        fieldManager.Build(probes);
    }

    // ---------------- 内部 ----------------

    private Vector3 SampleInternal(Vector3 worldPosition)
    {
        if (fieldManager == null)
            RebuildField();

        // 若外部在运行时调过 cellSize / sampleRadius，同步一次
        if (fieldManager.cellSize != cellSize ||
            fieldManager.sampleRadius != sampleRadius)
        {
            fieldManager.cellSize = cellSize;
            fieldManager.sampleRadius = sampleRadius;
        }

        return fieldManager.WindEffect(worldPosition);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;

        Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, 0.3f);

        if (probes == null) return;

        Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.25f);
        for (int i = 0; i < probes.Count; i++)
        {
            if (probes[i] == null) continue;
            Gizmos.DrawLine(transform.position, probes[i].transform.position);
        }
    }
#endif
}
