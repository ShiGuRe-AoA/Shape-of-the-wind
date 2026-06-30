using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 单个翅膀的状态数据。
/// 由 BirdWingInputController 维护，飞行控制器、体力系统等可只读消费。
/// </summary>
[System.Serializable]
public struct WingState
{
    /// <summary>翅膀挥动速度。向下挥动为正，向上挥动为负。</summary>
    public float flapSpeed;

    /// <summary>翅膀倾角，单位：度。用于转向与骨骼弯曲。</summary>
    public float tilt;

    /// <summary>归一化后的挥动速度，范围 -1 到 1。</summary>
    public float normalizedFlapSpeed;

    /// <summary>归一化后的翅膀倾角，范围 -1 到 1。</summary>
    public float normalizedTilt;

    /// <summary>当前是否处于有效向下拍翼。</summary>
    public bool isDownFlapping;

    /// <summary>只统计向下拍翼的强度，范围 0 到 1。</summary>
    public float downFlapStrength;
}

/// <summary>
/// 外部输入系统（手柄模拟 / 未来 VR 手柄）写入鸟翼输入状态时使用的统一数据结构。
/// 所有字段约定为鸟自身局部空间下的语义值，除了 windForce 使用世界空间向量。
/// </summary>
[System.Serializable]
public struct BirdWingInputData
{
    /// <summary>左翼挥动速度。向下为正，向上为负。单位与 maxAbsFlapSpeed 一致。</summary>
    public float leftFlapSpeed;

    /// <summary>右翼挥动速度。向下为正，向上为负。</summary>
    public float rightFlapSpeed;

    /// <summary>左翼倾角，单位：度。</summary>
    public float leftWingTilt;

    /// <summary>右翼倾角，单位：度。</summary>
    public float rightWingTilt;

    /// <summary>翼展，0 到 1。</summary>
    public float wingSpan;

    /// <summary>整体倾角，单位：度。</summary>
    public float bodyTilt;

    /// <summary>当前受到的综合风力，世界空间向量。方向为风向，长度为风力大小。</summary>
    public Vector3 windForce;
}

/// <summary>
/// 鸟翼输入状态控制器。
/// 不读取任何输入设备，只接收外部系统通过 ApplyInput 写入的鸟翼输入数据，
/// 统一存储、限制、推导，并把翅膀倾角应用到左右翼骨骼的局部旋转。
/// 飞行控制器、体力系统、UI 等通过本组件的只读属性消费数据。
/// </summary>
public class BirdWingInputController : MonoBehaviour
{
    // ---------------------------------------------------------------------
    // Inspector 可调参数
    // ---------------------------------------------------------------------

    [Header("Input Limits")]

    [Tooltip("flapSpeed 归一化使用的最大绝对值。原始 flapSpeed 除以该值得到 normalizedFlapSpeed。")]
    [SerializeField]
    private float maxAbsFlapSpeed = 5f;

    [Tooltip("单翼倾角允许的最大绝对值，单位：度。")]
    [SerializeField]
    private float maxAbsWingTilt = 60f;

    [Tooltip("整体倾角允许的最大绝对值，单位：度。")]
    [SerializeField]
    private float maxAbsBodyTilt = 60f;

    [Tooltip("是否对输入数值进行 Clamp。关闭后只做归一化但不限制。")]
    [SerializeField]
    private bool clampInputValues = true;

    [Tooltip("判定 isDownFlapping 时的归一化速度阈值，建议保留较小值避免抖动。")]
    [SerializeField, Range(0f, 1f)]
    private float downFlapThreshold = 0.05f;

    [Header("Wing Bones")]

    [Tooltip("左翼骨骼。建议顺序：从靠近身体的根部 -> 翼尖。")]
    [SerializeField]
    private List<Transform> leftWingBones = new List<Transform>();

    [Tooltip("右翼骨骼。建议顺序：从靠近身体的根部 -> 翼尖。")]
    [SerializeField]
    private List<Transform> rightWingBones = new List<Transform>();

    [Header("Wing Bone Rotation")]

    [Tooltip("骨骼倾角整体倍率。最终骨骼旋转角度 = tilt * wingBoneTiltMultiplier * 分布权重 * 符号。")]
    [SerializeField]
    private float wingBoneTiltMultiplier = 1f;

    [Tooltip("左翼骨骼局部空间的旋转轴。默认 X 轴（适用于翅膀骨骼朝向沿模型 X 轴延伸的常见绑定）。")]
    [SerializeField]
    private Vector3 leftWingTiltAxis = Vector3.up;

    [Tooltip("右翼骨骼局部空间的旋转轴。默认 X 轴。")]
    [SerializeField]
    private Vector3 rightWingTiltAxis = Vector3.up;

    [Tooltip("骨骼倾角分布曲线。x 为骨骼归一化位置(0=根部,1=翼尖)，y 为该骨骼受倾角影响的比例。")]
    [SerializeField]
    private AnimationCurve boneTiltDistribution =
        AnimationCurve.Linear(0f, 0.4f, 1f, 1f);

    [Tooltip("左翼骨骼旋转符号。若模型骨骼朝向不对称可在此翻转。")]
    [SerializeField]
    private float leftWingBoneSign = 1f;

    [Tooltip("右翼骨骼旋转符号。若模型骨骼朝向不对称可在此翻转。")]
    [SerializeField]
    private float rightWingBoneSign = 1f;

    [Header("Debug")]

    [Tooltip("开启后，每次接收到输入时输出一行关键数据到 Console。")]
    [SerializeField]
    private bool debugLogInput;

    [Tooltip("Gizmos 中风力箭头长度倍率。仅用于可视化，不影响实际数值。")]
    [SerializeField]
    private float gizmoWindArrowScale = 1f;

    // ---------------------------------------------------------------------
    // 内部状态
    // ---------------------------------------------------------------------

    [SerializeField] private WingState leftWing;
    [SerializeField] private WingState rightWing;
    [SerializeField, Range(0f, 1f)] private float wingSpan;
    [SerializeField] private float bodyTilt;
    [SerializeField] private Vector3 windForce;

    private float averageFlapSpeed;
    private float averageDownFlapStrength;
    private float wingTiltDifference;
    private float wingTiltAverage;
    private bool hasInput;

    private readonly List<Quaternion> leftWingInitialLocalRotations =
        new List<Quaternion>();

    private readonly List<Quaternion> rightWingInitialLocalRotations =
        new List<Quaternion>();

    /// <summary>
    /// 初始姿态是否已经被缓存。仅在第一次 Start 或显式 RecaptureInitialPose 后为 true。
    /// 用于保证：所有骨骼旋转都基于"游戏开始那一刻"的初始 localRotation，
    /// 而不是后续被本组件修改过的旋转。
    /// </summary>
    private bool initialPoseCaptured;

    // ---------------------------------------------------------------------
    // 公开只读属性
    // ---------------------------------------------------------------------

    /// <summary>左翼当前状态。</summary>
    public WingState LeftWing => leftWing;

    /// <summary>右翼当前状态。</summary>
    public WingState RightWing => rightWing;

    /// <summary>翼展，0 到 1。</summary>
    public float WingSpan => wingSpan;

    /// <summary>整体倾角，单位：度。</summary>
    public float BodyTilt => bodyTilt;

    /// <summary>当前综合风力，世界空间向量。</summary>
    public Vector3 WindForce => windForce;

    /// <summary>左右翼挥动速度平均值（保留正负方向）。</summary>
    public float AverageFlapSpeed => averageFlapSpeed;

    /// <summary>左右翼有效向下拍翼强度平均值，0 到 1。</summary>
    public float AverageDownFlapStrength => averageDownFlapStrength;

    /// <summary>右翼倾角 - 左翼倾角。用于转向。</summary>
    public float WingTiltDifference => wingTiltDifference;

    /// <summary>左右翼倾角平均值。可用于整体滑翔姿态。</summary>
    public float WingTiltAverage => wingTiltAverage;

    /// <summary>是否已经接收过至少一次有效输入。</summary>
    public bool HasInput => hasInput;

    // ---------------------------------------------------------------------
    // 生命周期
    // ---------------------------------------------------------------------

    private void Awake()
    {
        // 仅做存在性检查，不在这里缓存初始姿态：
        // 防止在 Awake 阶段骨骼层级 / 初始 Pose 尚未稳定时取到错误旋转。
        if (leftWingBones == null || leftWingBones.Count == 0)
        {
            Debug.LogWarning(
                "[BirdWingInputController] 左翼骨骼列表为空，将跳过左翼骨骼旋转。",
                this
            );
        }

        if (rightWingBones == null || rightWingBones.Count == 0)
        {
            Debug.LogWarning(
                "[BirdWingInputController] 右翼骨骼列表为空，将跳过右翼骨骼旋转。",
                this
            );
        }
    }

    private void Start()
    {
        // 关键：在 Start 中缓存"游戏开始那一刻"骨骼的真实初始 localRotation。
        // 此时模型导入旋转、父物体变换、其他 Awake 中可能的姿态初始化均已完成。
        // 之后所有骨骼旋转都基于这份缓存重算，确保非零初始旋转被尊重，且不会逐帧累积。
        CacheInitialBoneRotations();
    }

    private void OnValidate()
    {
        if (maxAbsFlapSpeed < 0.0001f)
            maxAbsFlapSpeed = 0.0001f;

        if (maxAbsWingTilt < 0f)
            maxAbsWingTilt = 0f;

        if (maxAbsBodyTilt < 0f)
            maxAbsBodyTilt = 0f;

        if (boneTiltDistribution == null ||
            boneTiltDistribution.length == 0)
        {
            boneTiltDistribution =
                AnimationCurve.Linear(0f, 0.4f, 1f, 1f);
        }
    }

    // ---------------------------------------------------------------------
    // 唯一外部输入入口
    // ---------------------------------------------------------------------

    /// <summary>
    /// 外部输入系统调用本函数写入一次完整的鸟翼输入数据。
    /// 内部会限制范围、计算派生值、并立刻刷新翅膀骨骼旋转。
    /// </summary>
    /// <param name="inputData">
    /// 鸟翼输入数据。flapSpeed/tilt/bodyTilt 约定为鸟自身局部空间下的语义值，
    /// windForce 为世界空间向量。
    /// </param>
    public void ApplyInput(BirdWingInputData inputData)
    {
        UpdateWingState(
            ref leftWing,
            inputData.leftFlapSpeed,
            inputData.leftWingTilt
        );

        UpdateWingState(
            ref rightWing,
            inputData.rightFlapSpeed,
            inputData.rightWingTilt
        );

        wingSpan = clampInputValues
            ? Mathf.Clamp01(inputData.wingSpan)
            : inputData.wingSpan;

        bodyTilt = clampInputValues
            ? Mathf.Clamp(inputData.bodyTilt, -maxAbsBodyTilt, maxAbsBodyTilt)
            : inputData.bodyTilt;

        windForce = inputData.windForce;

        RecalculateDerivedValues();
        ApplyWingBoneRotations();

        hasInput = true;

        if (debugLogInput)
        {
            Debug.Log(
                $"[BirdWingInputController] " +
                $"LFlap={leftWing.flapSpeed:F2} RFlap={rightWing.flapSpeed:F2} " +
                $"LTilt={leftWing.tilt:F1} RTilt={rightWing.tilt:F1} " +
                $"Span={wingSpan:F2} BodyTilt={bodyTilt:F1} " +
                $"Wind={windForce.magnitude:F2}",
                this
            );
        }
    }

    /// <summary>
    /// ApplyInput 的别名，方便外部按习惯命名调用。
    /// </summary>
    public void SetInput(BirdWingInputData inputData)
    {
        ApplyInput(inputData);
    }

    // ---------------------------------------------------------------------
    // 内部更新逻辑
    // ---------------------------------------------------------------------

    private void UpdateWingState(
        ref WingState state,
        float rawFlapSpeed,
        float rawTilt)
    {
        float clampedFlap = clampInputValues
            ? Mathf.Clamp(rawFlapSpeed, -maxAbsFlapSpeed, maxAbsFlapSpeed)
            : rawFlapSpeed;

        float clampedTilt = clampInputValues
            ? Mathf.Clamp(rawTilt, -maxAbsWingTilt, maxAbsWingTilt)
            : rawTilt;

        state.flapSpeed = clampedFlap;
        state.tilt = clampedTilt;

        state.normalizedFlapSpeed = Mathf.Clamp(
            clampedFlap / Mathf.Max(0.0001f, maxAbsFlapSpeed),
            -1f,
            1f
        );

        state.normalizedTilt = Mathf.Clamp(
            clampedTilt / Mathf.Max(0.0001f, maxAbsWingTilt),
            -1f,
            1f
        );

        state.downFlapStrength = Mathf.Clamp01(state.normalizedFlapSpeed);
        state.isDownFlapping = state.downFlapStrength > downFlapThreshold;
    }

    private void RecalculateDerivedValues()
    {
        averageFlapSpeed =
            (leftWing.flapSpeed + rightWing.flapSpeed) * 0.5f;

        averageDownFlapStrength =
            (Mathf.Max(0f, leftWing.normalizedFlapSpeed) +
             Mathf.Max(0f, rightWing.normalizedFlapSpeed)) * 0.5f;

        wingTiltDifference = rightWing.tilt - leftWing.tilt;
        wingTiltAverage = (leftWing.tilt + rightWing.tilt) * 0.5f;
    }

    // ---------------------------------------------------------------------
    // 骨骼旋转
    // ---------------------------------------------------------------------

    private void CacheInitialBoneRotations()
    {
        leftWingInitialLocalRotations.Clear();
        if (leftWingBones != null)
        {
            for (int i = 0; i < leftWingBones.Count; i++)
            {
                Transform bone = leftWingBones[i];

                leftWingInitialLocalRotations.Add(
                    bone != null ? bone.localRotation : Quaternion.identity
                );
            }
        }

        rightWingInitialLocalRotations.Clear();
        if (rightWingBones != null)
        {
            for (int i = 0; i < rightWingBones.Count; i++)
            {
                Transform bone = rightWingBones[i];

                rightWingInitialLocalRotations.Add(
                    bone != null ? bone.localRotation : Quaternion.identity
                );
            }
        }

        initialPoseCaptured = true;
    }

    /// <summary>
    /// 重新把当前左右翼骨骼的 localRotation 作为"初始姿态基准"重新缓存。
    /// 适用场景：
    /// - 运行时切换 / 重绑骨骼后；
    /// - 美术在运行时修改了模型初始 Pose 并希望立刻生效；
    /// - 一些需要把鸟"复位"到当前姿态、之后再按倾角偏移的玩法流程。
    /// 调用前请确保骨骼已经处于你想作为"零倾角"基准的姿态。
    /// </summary>
    public void RecaptureInitialPose()
    {
        CacheInitialBoneRotations();
    }

    /// <summary>
    /// 运行时重新指派左右翼骨骼，并自动以新骨骼的当前姿态作为初始基准。
    /// </summary>
    public void SetWingBones(
        List<Transform> newLeftWingBones,
        List<Transform> newRightWingBones)
    {
        leftWingBones = newLeftWingBones ?? new List<Transform>();
        rightWingBones = newRightWingBones ?? new List<Transform>();
        CacheInitialBoneRotations();
    }

    private void ApplyWingBoneRotations()
    {
        ApplyBoneRotationsToWing(
            leftWingBones,
            leftWingInitialLocalRotations,
            leftWing,
            leftWingTiltAxis,
            leftWingBoneSign
        );

        ApplyBoneRotationsToWing(
            rightWingBones,
            rightWingInitialLocalRotations,
            rightWing,
            rightWingTiltAxis,
            rightWingBoneSign
        );
    }

    private void ApplyBoneRotationsToWing(
        List<Transform> bones,
        List<Quaternion> initialRotations,
        WingState wingState,
        Vector3 localAxis,
        float sideSign)
    {
        if (bones == null || bones.Count == 0)
            return;

        // 若尚未缓存过初始姿态（例如外部在 Start 之前就调用了 ApplyInput），
        // 用当前 localRotation 作为基准缓存一次。
        // 注意：如果是因为外部已经把骨骼数量改了，不能直接重新缓存——
        // 那样会把"已被本组件偏移过的姿态"误当作初始姿态。
        // 推荐做法是外部改完骨骼后显式调用 SetWingBones / RecaptureInitialPose。
        if (!initialPoseCaptured)
        {
            CacheInitialBoneRotations();
        }

        // 旋转轴若为零向量则跳过，避免 NaN。
        Vector3 normalizedAxis = localAxis.sqrMagnitude > 0.0001f
            ? localAxis.normalized
            : Vector3.zero;

        if (normalizedAxis == Vector3.zero)
            return;

        int count = bones.Count;

        for (int i = 0; i < count; i++)
        {
            Transform bone = bones[i];
            if (bone == null)
                continue;

            // 缓存与当前骨骼数量不匹配时退化为 identity，避免索引越界。
            // 不再就地重新缓存，防止用偏移后的姿态污染初始基准。
            Quaternion initialRot = i < initialRotations.Count
                ? initialRotations[i]
                : Quaternion.identity;

            float normalizedIndex = count <= 1
                ? 1f
                : i / (float)(count - 1);

            float distribution =
                boneTiltDistribution != null
                    ? boneTiltDistribution.Evaluate(normalizedIndex)
                    : 1f;

            float angle =
                wingState.tilt *
                wingBoneTiltMultiplier *
                sideSign *
                distribution;

            Quaternion offset =
                Quaternion.AngleAxis(angle, normalizedAxis);

            // 关键：始终基于"初始局部旋转"叠加，避免逐帧累积。
            // 对带有非零初始 rotation 的骨骼也成立——初始姿态会被完整保留，
            // 倾角偏移只在初始姿态的局部坐标系下增量叠加。
            bone.localRotation = initialRot * offset;
        }
    }

    // ---------------------------------------------------------------------
    // 调试可视化
    // ---------------------------------------------------------------------

    private void OnDrawGizmosSelected()
    {
        Vector3 origin = transform.position;

        // 风力箭头
        if (windForce.sqrMagnitude > 0.0001f)
        {
            Gizmos.color = Color.cyan;
            Vector3 windEnd =
                origin + windForce * gizmoWindArrowScale;
            Gizmos.DrawLine(origin, windEnd);
            Gizmos.DrawSphere(windEnd, 0.05f);
        }

        // 翼展可视化：沿 transform.right 画一条与 wingSpan 等长的线
        Gizmos.color = Color.yellow;
        Vector3 spanHalf = transform.right * wingSpan;
        Gizmos.DrawLine(origin - spanHalf, origin + spanHalf);

        // 左右翼倾角可视化：用一段贴着翅膀方向的短线表示倾角偏移
        Gizmos.color = Color.green;
        Vector3 leftDir = Quaternion.AngleAxis(
            leftWing.tilt,
            transform.forward
        ) * (-transform.right);
        Gizmos.DrawLine(origin, origin + leftDir * 0.5f);

        Gizmos.color = Color.red;
        Vector3 rightDir = Quaternion.AngleAxis(
            -rightWing.tilt,
            transform.forward
        ) * transform.right;
        Gizmos.DrawLine(origin, origin + rightDir * 0.5f);
    }
}
