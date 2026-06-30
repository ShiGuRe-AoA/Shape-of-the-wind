using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 鸟的程序化细节状态控制器。
///
/// 设计定位：
/// - 不接管整体骨骼姿态（那是 BirdWingInputController 的职责），
///   只负责在 BirdWingInputController 输出的姿态之上，叠加细节层程序化动画。
/// - 当前实现的细节：在风力作用下，让一组"羽毛骨骼"以其各自的初始局部姿态为基准，
///   绕指定轴来回（正弦）摆动；摆动幅度正比于鸟当前受到的风力大小。
///
/// 关键约定：
/// 1) 初始姿态来自 Start 时刻的 localRotation 缓存，所有运行时旋转都基于该缓存叠加，
///    避免逐帧累积漂移。
/// 2) 必须在 BirdWingInputController.ApplyWingBoneRotations 之后执行，
///    否则会被它覆盖。为此本组件在 LateUpdate 中应用旋转——Unity 中 LateUpdate
///    一定晚于同帧所有 Update / ApplyInput 调用，能稳定覆盖到细节羽毛上。
/// 3) 当前禁用 DOTween（项目尚未导入），改用 Mathf.Sin(phase) 的纯手算正弦。
///    后续接入 DOTween 后，可把摆动相位 / 缓动替换为 DOTween 的 Tweener，
///    接口保持 RegisterFeathers / SetFeathers 等不变。
/// </summary>
public class BirdProceduralAnimator : MonoBehaviour
{
    // ---------------------------------------------------------------------
    // 引用
    // ---------------------------------------------------------------------

    [Header("References")]

    [Tooltip(
        "风力来源。留空则自动从同物体或父物体上的 BirdWingInputController 获取。\n" +
        "本组件通过 wingController.WindForce 读取当前综合风力。"
    )]
    [SerializeField]
    private BirdWingInputController wingController;

    // ---------------------------------------------------------------------
    // 羽毛配置
    // ---------------------------------------------------------------------

    /// <summary>
    /// 一根羽毛骨骼的配置项。允许逐根设置自己的旋转轴 / 相位 / 幅度倍率，
    /// 这样多根羽毛可以错峰摆动，避免视觉上的"齐刷刷一起转"。
    /// </summary>
    [System.Serializable]
    public struct FeatherEntry
    {
        [Tooltip("羽毛骨骼 Transform。")]
        public Transform bone;

        [Tooltip(
            "摆动绕骨骼局部空间的旋转轴。\n" +
            "若为零向量，将退化为本组件 defaultFeatherAxis。"
        )]
        public Vector3 localAxis;

        [Tooltip(
            "本根羽毛的相位偏移，单位：度。让不同羽毛错峰摆动。\n" +
            "0 表示与全局相位同步。"
        )]
        public float phaseOffsetDegrees;

        [Tooltip(
            "本根羽毛的幅度倍率（与全局 windAngleMultiplier 相乘）。\n" +
            "默认 1。设 0 可临时禁用某根羽毛而不删除条目。"
        )]
        public float amplitudeScale;
    }

    [Header("Feathers")]

    [Tooltip("待程序化驱动的羽毛骨骼列表。每根可单独配置旋转轴 / 相位 / 幅度。")]
    [SerializeField]
    private List<FeatherEntry> feathers = new List<FeatherEntry>();

    [Tooltip(
        "羽毛默认局部旋转轴。当某根羽毛的 localAxis 为零向量时使用本字段。"
    )]
    [SerializeField]
    private Vector3 defaultFeatherAxis = Vector3.right;

    // ---------------------------------------------------------------------
    // 风力 → 摆动幅度
    // ---------------------------------------------------------------------

    [Header("Wind → Sway Amplitude")]

    [Tooltip(
        "风力大小到摆动半幅角度（度）的线性倍率。\n" +
        "最终摆动半幅 ≈ Clamp(windForce.magnitude * windAngleMultiplier, 0, maxSwayAngle)。"
    )]
    [SerializeField, Min(0f)]
    private float windAngleMultiplier = 2f;

    [Tooltip(
        "摆动半幅上限（度）。无论风多大，单根羽毛绕初始姿态的偏移都不会超过此值再乘以单根的 amplitudeScale。"
    )]
    [SerializeField, Min(0f)]
    private float maxSwayAngle = 25f;

    [Tooltip(
        "风力大小低于此阈值时视为无风，羽毛回到初始姿态（避免微小数值带来抖动）。"
    )]
    [SerializeField, Min(0f)]
    private float windDeadZone = 0.05f;

    [Tooltip(
        "幅度对风力变化的跟随平滑时间（秒）。0=无平滑，瞬时跟随；越大越柔顺。\n" +
        "用一个简单的 SmoothDamp 让幅度变化不抖。"
    )]
    [SerializeField, Min(0f)]
    private float amplitudeSmoothTime = 0.15f;

    // ---------------------------------------------------------------------
    // 摆动频率
    // ---------------------------------------------------------------------

    [Header("Sway Frequency")]

    [Tooltip("羽毛摆动的基础频率（Hz）。1 表示每秒一个完整来回周期。")]
    [SerializeField, Min(0f)]
    private float swayFrequency = 1.5f;

    [Tooltip(
        "频率随风力增长的额外系数（Hz / 单位风力）。\n" +
        "实际频率 = swayFrequency + windFrequencyBoost * windMagnitude。\n" +
        "设 0 即风越大只会越摆得大但不变快。"
    )]
    [SerializeField, Min(0f)]
    private float windFrequencyBoost = 0f;

    // ---------------------------------------------------------------------
    // 调试
    // ---------------------------------------------------------------------

    [Header("Debug")]

    [Tooltip("开启后每若干帧输出一行风力 / 当前幅度 / 相位等信息。")]
    [SerializeField]
    private bool debugLog;

    [Tooltip("debugLog 打印的帧间隔。")]
    [SerializeField, Min(1)]
    private int debugLogFrameInterval = 30;

    // ---------------------------------------------------------------------
    // 运行时状态
    // ---------------------------------------------------------------------

    /// <summary>每根羽毛的初始 localRotation 缓存。索引与 feathers 一一对应。</summary>
    private readonly List<Quaternion> featherInitialLocalRotations =
        new List<Quaternion>();

    /// <summary>初始姿态是否已经被缓存。</summary>
    private bool initialPoseCaptured;

    /// <summary>全局相位（弧度）。每帧按当前频率累积，所有羽毛共享。</summary>
    private float globalPhase;

    /// <summary>当前平滑后的摆动半幅（度）。</summary>
    private float currentAmplitudeDegrees;

    /// <summary>SmoothDamp 用的内部速度。</summary>
    private float amplitudeSmoothVelocity;

    // ---------------------------------------------------------------------
    // 生命周期
    // ---------------------------------------------------------------------

    private void Reset()
    {
        wingController = GetComponentInParent<BirdWingInputController>();
    }

    private void Awake()
    {
        if (wingController == null)
        {
            wingController = GetComponentInParent<BirdWingInputController>();
        }
    }

    private void Start()
    {
        // 与 BirdWingInputController 一致：在 Start 缓存初始姿态，
        // 避免 Awake 阶段骨骼层级 / 初始 Pose 尚未稳定。
        CacheInitialFeatherRotations();
    }

    private void OnValidate()
    {
        if (defaultFeatherAxis.sqrMagnitude < 1e-6f)
            defaultFeatherAxis = Vector3.right;
    }

    /// <summary>
    /// 用 LateUpdate 而不是 Update，确保在 BirdWingInputController.ApplyInput
    /// 写入的骨骼旋转之后执行，不会被它覆盖。
    /// </summary>
    private void LateUpdate()
    {
        if (!initialPoseCaptured)
            CacheInitialFeatherRotations();

        float windMagnitude = wingController != null
            ? wingController.WindForce.magnitude
            : 0f;

        // 1) 计算"目标半幅"。低于死区视为无风。
        float targetAmplitude;
        if (windMagnitude < windDeadZone)
        {
            targetAmplitude = 0f;
        }
        else
        {
            targetAmplitude = Mathf.Min(
                windMagnitude * windAngleMultiplier,
                maxSwayAngle
            );
        }

        // 2) 平滑跟随，避免风力跳变带来羽毛瞬时抽搐。
        if (amplitudeSmoothTime > 0.0001f)
        {
            currentAmplitudeDegrees = Mathf.SmoothDamp(
                currentAmplitudeDegrees,
                targetAmplitude,
                ref amplitudeSmoothVelocity,
                amplitudeSmoothTime
            );
        }
        else
        {
            currentAmplitudeDegrees = targetAmplitude;
            amplitudeSmoothVelocity = 0f;
        }

        // 3) 累积全局相位。频率随风力线性增长（可选）。
        float frequency = swayFrequency + windFrequencyBoost * windMagnitude;
        globalPhase += frequency * Mathf.PI * 2f * Time.deltaTime;

        // 把相位限制到 [0, 2π) 防止长时间运行后浮点精度退化。
        const float TwoPi = Mathf.PI * 2f;
        if (globalPhase > TwoPi)
            globalPhase -= TwoPi * Mathf.Floor(globalPhase / TwoPi);

        ApplyFeatherSway();

        if (debugLog && Time.frameCount % debugLogFrameInterval == 0)
        {
            Debug.Log(
                $"[BirdProceduralAnimator] " +
                $"Wind={windMagnitude:F2} " +
                $"TargetAmp={targetAmplitude:F2} " +
                $"CurAmp={currentAmplitudeDegrees:F2} " +
                $"Freq={frequency:F2}Hz " +
                $"Phase={globalPhase:F2}",
                this
            );
        }
    }

    // ---------------------------------------------------------------------
    // 公共接口（运行时增删 / 重新缓存）
    // ---------------------------------------------------------------------

    /// <summary>
    /// 运行时整体替换羽毛列表，并以新羽毛骨骼的当前 localRotation 为初始基准。
    /// </summary>
    public void SetFeathers(IEnumerable<FeatherEntry> newFeathers)
    {
        feathers.Clear();
        if (newFeathers != null)
        {
            foreach (FeatherEntry entry in newFeathers)
            {
                feathers.Add(entry);
            }
        }
        CacheInitialFeatherRotations();
    }

    /// <summary>
    /// 追加一根羽毛骨骼，并把它当前的 localRotation 视为该羽毛的初始基准。
    /// </summary>
    public void AddFeather(FeatherEntry entry)
    {
        feathers.Add(entry);
        featherInitialLocalRotations.Add(
            entry.bone != null
                ? entry.bone.localRotation
                : Quaternion.identity
        );
    }

    /// <summary>
    /// 强制把当前所有羽毛骨骼的 localRotation 重新作为初始基准。
    /// 仅在外部确实想"以当前姿态作为零摆动姿态"时调用。
    /// 注意：如果当前姿态已经被本组件偏移过，重新缓存会污染基准。
    /// </summary>
    public void RecaptureInitialPose()
    {
        CacheInitialFeatherRotations();
    }

    // ---------------------------------------------------------------------
    // 内部实现
    // ---------------------------------------------------------------------

    private void CacheInitialFeatherRotations()
    {
        featherInitialLocalRotations.Clear();

        for (int i = 0; i < feathers.Count; i++)
        {
            Transform bone = feathers[i].bone;
            featherInitialLocalRotations.Add(
                bone != null ? bone.localRotation : Quaternion.identity
            );
        }

        initialPoseCaptured = true;
    }

    private void ApplyFeatherSway()
    {
        int count = feathers.Count;
        if (count == 0)
            return;

        // 全局 sin(phase)，每根羽毛再叠加自己的相位偏移与幅度倍率。
        for (int i = 0; i < count; i++)
        {
            FeatherEntry entry = feathers[i];
            Transform bone = entry.bone;
            if (bone == null)
                continue;

            Quaternion initialRot = i < featherInitialLocalRotations.Count
                ? featherInitialLocalRotations[i]
                : Quaternion.identity;

            Vector3 axis = entry.localAxis.sqrMagnitude > 1e-6f
                ? entry.localAxis.normalized
                : defaultFeatherAxis.normalized;

            if (axis.sqrMagnitude < 1e-6f)
                continue;

            float phaseRad =
                globalPhase + entry.phaseOffsetDegrees * Mathf.Deg2Rad;

            // 单根羽毛的幅度 = 全局当前半幅 * 本根倍率。
            // amplitudeScale 默认值在 Inspector 中可能为 0（结构体默认值），
            // 这里特别处理一下：amplitudeScale==0 在序列化默认时表示"未设置"，
            // 视为 1 以避免新加的羽毛默认完全不动。
            // 想真正禁用某根羽毛，请显式把 amplitudeScale 设为很小但非零的值（如 0.0001）。
            float scale = Mathf.Approximately(entry.amplitudeScale, 0f)
                ? 1f
                : entry.amplitudeScale;

            float angle =
                currentAmplitudeDegrees * scale * Mathf.Sin(phaseRad);

            Quaternion offset = Quaternion.AngleAxis(angle, axis);

            // 始终基于初始姿态叠加，避免逐帧累积。
            bone.localRotation = initialRot * offset;
        }
    }

    // ---------------------------------------------------------------------
    // 调试可视化
    // ---------------------------------------------------------------------

    private void OnDrawGizmosSelected()
    {
        if (feathers == null) return;

        Gizmos.color = Color.magenta;
        for (int i = 0; i < feathers.Count; i++)
        {
            Transform bone = feathers[i].bone;
            if (bone == null) continue;

            Vector3 axisLocal = feathers[i].localAxis.sqrMagnitude > 1e-6f
                ? feathers[i].localAxis.normalized
                : defaultFeatherAxis.normalized;

            // 把局部轴转到世界空间，长度固定 0.1，方便在场景看清楚每根羽毛的摆动平面法线。
            Vector3 axisWorld = bone.TransformDirection(axisLocal) * 0.1f;
            Gizmos.DrawLine(bone.position, bone.position + axisWorld);
        }
    }
}
