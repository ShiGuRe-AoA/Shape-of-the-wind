using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 风飘物生成器：
/// - 内部维护一组带权重的预制体（每个预制体应携带 <see cref="WindDriftObject"/> 组件）。
/// - 每隔 <see cref="generateInterval"/> 秒按权重随机挑一个预制体生成一次。
/// - 生成时在指定的生成范围内随机取位置，赋予一个方向、大小都随机的初速度，
///   并调用 <see cref="WindDriftObject.SetLifetime(float)"/> 设置自毁时间。
///
/// 使用：放到场景空物体上，配置 <see cref="entries"/> 与参数即可。
/// </summary>
[DisallowMultipleComponent]
public class WindDriftSpawner : MonoBehaviour
{
    // ------------------------------------------------------------------
    // 预制体条目
    // ------------------------------------------------------------------

    [System.Serializable]
    public class Entry
    {
        [Tooltip("要生成的预制体（应携带 WindDriftObject 组件）。")]
        public WindDriftObject prefab;

        [Tooltip("该条目的相对权重。数值越大越容易被抽到；<= 0 视为不参与抽取。")]
        [Min(0f)]
        public float weight = 1f;
    }

    [Header("Prefabs")]

    [Tooltip("可生成的预制体列表。每个条目可分别配置权重。")]
    [SerializeField]
    private List<Entry> entries = new List<Entry>();

    // ------------------------------------------------------------------
    // 生成节奏
    // ------------------------------------------------------------------

    [Header("Timing")]

    [Tooltip("每隔多少秒生成一次。")]
    [SerializeField, Min(0.0001f)]
    private float generateInterval = 1f;

    [Tooltip("首次生成前的额外延迟（秒）。")]
    [SerializeField, Min(0f)]
    private float initialDelay = 0f;

    [Tooltip("最大同时存在的实例数量。<= 0 表示不限制。达到上限时暂停生成。")]
    [SerializeField]
    private int maxAlive = 0;

    // ------------------------------------------------------------------
    // 生成位置
    // ------------------------------------------------------------------

    [Header("Spawn Position")]

    [Tooltip("生成位置的参考中心。留空则使用本对象位置。")]
    [SerializeField]
    private Transform spawnCenter;

    [Tooltip("生成位置在中心周围的随机盒子半尺寸（局部坐标）。为 0 表示固定在中心。")]
    [SerializeField]
    private Vector3 spawnHalfExtents = new Vector3(2f, 0f, 2f);

    // ------------------------------------------------------------------
    // 初速度
    // ------------------------------------------------------------------

    [Header("Initial Velocity")]

    [Tooltip("初始速度大小的随机范围（单位 / 秒）。x = 最小，y = 最大。")]
    [SerializeField]
    private Vector2 initialSpeedRange = new Vector2(0f, 3f);

    [Tooltip(
        "是否只在水平面（XZ）内随机方向。\n" +
        "关闭则在整个球面上均匀随机；开启则纯水平方向。"
    )]
    [SerializeField]
    private bool horizontalOnly = false;

    // ------------------------------------------------------------------
    // 寿命
    // ------------------------------------------------------------------

    [Header("Lifetime")]

    [Tooltip("每个生成实例的寿命随机范围（秒）。x = 最小，y = 最大。<= 0 表示永不销毁。")]
    [SerializeField]
    private Vector2 lifetimeRange = new Vector2(5f, 10f);

    // ------------------------------------------------------------------
    // 其他
    // ------------------------------------------------------------------

    [Header("Misc")]

    [Tooltip("生成的实例是否作为本对象的子物体。")]
    [SerializeField]
    private bool parentToThis = false;

    // ------------------------------------------------------------------
    // 内部状态
    // ------------------------------------------------------------------

    private float timer;
    private readonly List<WindDriftObject> alive = new List<WindDriftObject>();

    // ------------------------------------------------------------------
    // 生命周期
    // ------------------------------------------------------------------

    private void OnEnable()
    {
        timer = -initialDelay;
    }

    private void Update()
    {
        timer += Time.deltaTime;
        if (timer < generateInterval) return;

        // 用 while，防止 lag 后一次补多个（避免堆积）
        while (timer >= generateInterval)
        {
            timer -= generateInterval;

            CleanupDead();
            if (maxAlive > 0 && alive.Count >= maxAlive) continue;

            SpawnOne();
        }
    }

    // ------------------------------------------------------------------
    // 生成
    // ------------------------------------------------------------------

    private void SpawnOne()
    {
        WindDriftObject prefab = PickWeighted();
        if (prefab == null) return;

        Vector3 pos = SampleSpawnPosition();
        Quaternion rot = Random.rotationUniform;

        WindDriftObject instance = Instantiate(
            prefab,
            pos,
            rot,
            parentToThis ? transform : null
        );

        // 随机初速度
        Vector3 dir = horizontalOnly
            ? RandomHorizontalDirection()
            : Random.onUnitSphere;

        float speed = Random.Range(
            Mathf.Min(initialSpeedRange.x, initialSpeedRange.y),
            Mathf.Max(initialSpeedRange.x, initialSpeedRange.y)
        );

        instance.SetVelocity(dir * speed);

        // 随机寿命
        float life = Random.Range(
            Mathf.Min(lifetimeRange.x, lifetimeRange.y),
            Mathf.Max(lifetimeRange.x, lifetimeRange.y)
        );
        instance.SetLifetime(life);

        alive.Add(instance);
    }

    /// <summary>按 <see cref="Entry.weight"/> 加权抽取一个预制体。</summary>
    private WindDriftObject PickWeighted()
    {
        if (entries == null || entries.Count == 0) return null;

        float total = 0f;
        for (int i = 0; i < entries.Count; i++)
        {
            Entry e = entries[i];
            if (e != null && e.prefab != null && e.weight > 0f)
                total += e.weight;
        }
        if (total <= 0f) return null;

        float r = Random.value * total;
        float acc = 0f;
        for (int i = 0; i < entries.Count; i++)
        {
            Entry e = entries[i];
            if (e == null || e.prefab == null || e.weight <= 0f) continue;

            acc += e.weight;
            if (r <= acc) return e.prefab;
        }

        // 浮点误差兜底
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            Entry e = entries[i];
            if (e != null && e.prefab != null && e.weight > 0f)
                return e.prefab;
        }
        return null;
    }

    private Vector3 SampleSpawnPosition()
    {
        Transform t = spawnCenter != null ? spawnCenter : transform;
        Vector3 local = new Vector3(
            Random.Range(-spawnHalfExtents.x, spawnHalfExtents.x),
            Random.Range(-spawnHalfExtents.y, spawnHalfExtents.y),
            Random.Range(-spawnHalfExtents.z, spawnHalfExtents.z)
        );
        return t.TransformPoint(local);
    }

    private static Vector3 RandomHorizontalDirection()
    {
        float ang = Random.Range(0f, Mathf.PI * 2f);
        return new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
    }

    private void CleanupDead()
    {
        for (int i = alive.Count - 1; i >= 0; i--)
        {
            if (alive[i] == null) alive.RemoveAt(i);
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Transform t = spawnCenter != null ? spawnCenter : transform;
        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.35f);
        Matrix4x4 old = Gizmos.matrix;
        Gizmos.matrix = t.localToWorldMatrix;
        Gizmos.DrawWireCube(Vector3.zero, spawnHalfExtents * 2f);
        Gizmos.matrix = old;
    }
#endif
}
