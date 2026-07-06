using UnityEngine;

/// <summary>
/// 鸟类飞行物理控制器。
///
/// 职责（依据项目概况文档 §11、§12、§14 与翅膀系统需求 §7 划分）：
/// - 不读输入：所有飞行输入通过 BirdWingInputController 的只读属性读取。
/// - 不存输入：不缓存额外输入字段，避免与控制器状态二次同步。
/// - 不写鸟翼姿态：骨骼旋转完全交由 BirdWingInputController 处理。
/// - 负责把 拍翼 / 风推 / 风升 / 转向 转换为对 Rigidbody 的力与扭矩。
///
/// 物理执行点：FixedUpdate（Rigidbody 强相关）。
/// 仅使用基础 API：AddForce / AddTorque / rb.velocity 读取。
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class BirdFlightController : MonoBehaviour
{
    // ---------------------------------------------------------------------
    // 引用
    // ---------------------------------------------------------------------

    [Header("References")]

    [Tooltip("鸟翼输入状态控制器。留空则自动从同物体获取。")]
    [SerializeField]
    private BirdWingInputController wingController;

    [Tooltip("Rigidbody。留空则自动从同物体获取。")]
    [SerializeField]
    private Rigidbody rb;

    [Tooltip(
        "风力获取来源。默认使用全局 WindSampler；若场景没有 WindSampler，" +
        "则回退到 BirdWingInputController.WindForce（由外部驱动者写入）。"
    )]
    [SerializeField]
    private bool useGlobalWindSampler = true;

    // ---------------------------------------------------------------------
    // 拍翼主动升力
    // ---------------------------------------------------------------------

    [Header("Active Flap Lift (主动拍翼升力)")]

    [Tooltip("主动拍翼升力倍率。施加方向：鸟自身 transform.up。")]
    [SerializeField]
    private float activeLiftMultiplier = 18f;

    [Tooltip(
        "向下挥动产生的左右翼差异同时还会贡献一个绕鸟前向(roll)的姿态扭矩。\n" +
        "由 (rightDownStrength - leftDownStrength) 触发。"
    )]
    [SerializeField]
    private float flapAsymmetryRollTorque = 4f;

    [Tooltip("拍翼前推：每次向下拍翼附带的鸟自身 forward 方向推力。模拟实际飞鸟前移。")]
    [SerializeField]
    private float flapForwardThrust = 6f;

    // ---------------------------------------------------------------------
    // 持续向前推进力（Throttle）
    // ---------------------------------------------------------------------
    //
    // 设计原则（重要）：
    // - 鸟的"前进方向"始终是鸟头方向 transform.forward。
    // - 翅膀只参与"转向"（改变 transform 朝向）与"升力"（改变高度），
    //   不直接产生其他方向的推力。
    // - 玩家通过 W / S 增大 / 减小 Throttle；Throttle 存在上限，下限恒为 0，
    //   不会反向（即鸟不会"倒飞"）。
    // - 当前 Throttle 大小由外部（驱动器）调用 SetThrottle / AdjustThrottle 写入，
    //   施力方向永远是 transform.forward。

    [Header("Forward Throttle (持续向前推进力)")]

    [Tooltip(
        "持续向前推进力的最大值。\n" +
        "实际加速度 = currentThrottle * forwardThrustMultiplier，方向恒为 transform.forward。"
    )]
    [SerializeField, Min(0f)]
    private float maxThrottle = 10f;

    [Tooltip("Throttle 转换为实际向前加速度的倍率。")]
    [SerializeField, Min(0f)]
    private float forwardThrustMultiplier = 1f;

    [Tooltip("初始 Throttle 值（游戏开始时）。会被夹到 [0, maxThrottle]。")]
    [SerializeField, Min(0f)]
    private float initialThrottle = 0f;

    /// <summary>当前向前推进力大小。范围 [0, maxThrottle]，下限恒为 0，不会反向。</summary>
    private float currentThrottle;

    /// <summary>只读暴露当前 Throttle，供 UI / 调试读取。</summary>
    public float CurrentThrottle => currentThrottle;

    /// <summary>只读暴露 Throttle 上限。</summary>
    public float MaxThrottle => maxThrottle;

    // ---------------------------------------------------------------------
    // 风力影响
    // ---------------------------------------------------------------------

    [Header("Wind Forces (风作用)")]

    [Tooltip("沿风向的推动力倍率。windPushForce = windDir * windStrength * windPushMultiplier。")]
    [SerializeField]
    private float windPushMultiplier = 0.35f;

    [Tooltip(
        "沿两翼中垂线方向的升力倍率。\n" +
        "windLiftForce = wingUpDir * windStrength * wingSpan * windLiftMultiplier。"
    )]
    [SerializeField]
    private float windLiftMultiplier = 0.6f;

    [Tooltip("风力大小上限，防止多个风探针重叠时风力无限叠加。")]
    [SerializeField, Min(0f)]
    private float maxWindStrength = 8f;

    [Tooltip("风升力是否只统计风的垂直分量（避免水平风也产生向上升力）。")]
    [SerializeField]
    private bool windLiftUseOnlyVerticalComponent = false;

    // ---------------------------------------------------------------------
    // 滑翔基础升力（与翼展挂钩）
    // ---------------------------------------------------------------------

    [Header("Glide Lift (滑翔基础升力)")]

    [Tooltip(
        "翼展带来的恒定上升力倍率（沿世界 up）。\n" +
        "glideLift = Vector3.up * wingSpan * glideLiftMultiplier。\n" +
        "用于让玩家展开翅膀时即使没风也能延缓下落。"
    )]
    [SerializeField]
    private float glideLiftMultiplier = 4f;

    // ---------------------------------------------------------------------
    // 转向
    // ---------------------------------------------------------------------

    [Header("Turning (转向)")]

    [Tooltip(
        "左右翼倾角差值带来的 Yaw（绕世界 Up）转向速度倍率，单位：度/秒。\n" +
        "yawDelta = sign * tiltDiffNormalized * yawSpeedMultiplier。"
    )]
    [SerializeField]
    private float yawSpeedMultiplier = 90f;

    [Tooltip("WingTiltDifference 归一化使用的最大差值，单位：度。差值超过该值后转向饱和。")]
    [SerializeField, Min(1f)]
    private float yawTiltDifferenceFullRange = 120f;

    [Tooltip("转向方向。1 表示 (rightTilt - leftTilt > 0) 向右偏航；-1 反向。")]
    [SerializeField]
    private float yawDirectionSign = 1f;

    [Tooltip("Yaw 输入死区，避免抖动。单位：度。")]
    [SerializeField, Min(0f)]
    private float yawDeadZone = 2f;

    [Tooltip(
        "整体倾角(BodyTilt) 转 Roll 倾斜：让鸟绕自身 forward 轴倾斜，提升体感。\n" +
        "rollTorque = -bodyTilt * bodyTiltRollMultiplier。"
    )]
    [SerializeField]
    private float bodyTiltRollMultiplier = 0.3f;

    // ---------------------------------------------------------------------
    // 重力与阻尼
    // ---------------------------------------------------------------------

    [Header("Gravity & Damping")]

    [Tooltip("手动重力大小（向下加速度，m/s^2）。本组件接管重力，启用 useGravity 会更可控。")]
    [SerializeField]
    private float gravity = 9.8f;

    [Tooltip(
        "是否使用 rb.useGravity。\n" +
        "推荐关闭，让本组件统一施加重力，便于调参。"
    )]
    [SerializeField]
    private bool useUnityGravity = false;

    [Tooltip("水平速度阻尼，越接近 1 减速越慢。每物理步乘一次。")]
    [SerializeField, Range(0.8f, 1f)]
    private float horizontalDamping = 0.995f;

    [Tooltip("垂直速度阻尼。")]
    [SerializeField, Range(0.8f, 1f)]
    private float verticalDamping = 0.998f;

    [Tooltip("垂直速度上下限（向下负，向上正）。X=下限，Y=上限。")]
    [SerializeField]
    private Vector2 verticalSpeedClamp = new Vector2(-14f, 9f);

    // ---------------------------------------------------------------------
    // 姿态限制（防止累积扭矩导致空中乱转）
    // ---------------------------------------------------------------------

    [Header("Attitude Limits (姿态限制)")]

    [Tooltip(
        "是否启用水平倾角（Roll/Pitch）硬限制。\n" +
        "开启后，鸟身体的 Roll（绕自身 forward）与 Pitch（绕自身 right）会被夹到下面的角度范围内，\n" +
        "避免长时间按住转向键时角速度持续累积导致鸟翻滚 / 倒飞 / 空中乱转。"
    )]
    [SerializeField]
    private bool clampAttitude = true;

    [Tooltip("最大 Roll 角（绕鸟自身 forward 轴的左右翻滚），单位：度。建议 25~60。")]
    [SerializeField, Range(0f, 89f)]
    private float maxRollAngle = 45f;

    [Tooltip("最大 Pitch 角（绕鸟自身 right 轴的俯仰），单位：度。建议 30~70。")]
    [SerializeField, Range(0f, 89f)]
    private float maxPitchAngle = 60f;

    [Tooltip(
        "接近角度上限时对相应轴的角速度施加的阻尼系数 (0~1)。\n" +
        "1=完全不阻尼（直接撞墙夹角度），0=立刻清零。\n" +
        "推荐 0.05~0.2，让限制更柔顺、避免抽搐感。"
    )]
    [SerializeField, Range(0f, 1f)]
    private float attitudeClampAngularDamping = 0.1f;

    // ---------------------------------------------------------------------
    // 调试
    // ---------------------------------------------------------------------

    [Header("Debug")]

    [Tooltip("是否在 Console 输出每物理步的关键力。")]
    [SerializeField]
    private bool debugLog;

    [Tooltip("是否在 Scene 视图绘制力 Gizmos（选中物体时显示）。")]
    [SerializeField]
    private bool drawGizmos = true;

    [Tooltip("Gizmos 力线长度倍率。仅影响绘制，不影响实际数值。")]
    [SerializeField]
    private float gizmoForceScale = 0.05f;

    // ---------------------------------------------------------------------
    // 运行时缓存（仅供调试 / Gizmos 显示）
    // ---------------------------------------------------------------------

    private Vector3 lastActiveLift;
    private Vector3 lastWindPush;
    private Vector3 lastWindLift;
    private Vector3 lastGlideLift;
    private Vector3 lastGravity;
    private Vector3 lastWindSampled;

    public Vector3 LastActiveLift => lastActiveLift;
    public Vector3 LastWindPush => lastWindPush;
    public Vector3 LastWindLift => lastWindLift;
    public Vector3 LastGlideLift => lastGlideLift;
    public Vector3 LastSampledWind => lastWindSampled;

    // ---------------------------------------------------------------------
    // 生命周期
    // ---------------------------------------------------------------------

    private void Reset()
    {
        rb = GetComponent<Rigidbody>();
        wingController = GetComponent<BirdWingInputController>();
    }

    private void Awake()
    {
        if (rb == null)
            rb = GetComponent<Rigidbody>();

        if (wingController == null)
            wingController = GetComponent<BirdWingInputController>();

        if (rb == null)
        {
            Debug.LogError(
                "[BirdFlightController] 当前对象没有 Rigidbody。",
                this
            );
            enabled = false;
            return;
        }

        if (wingController == null)
        {
            Debug.LogError(
                "[BirdFlightController] 当前对象没有 BirdWingInputController。",
                this
            );
            enabled = false;
            return;
        }

        rb.useGravity = useUnityGravity;

        currentThrottle = Mathf.Clamp(initialThrottle, 0f, maxThrottle);
    }

    private void OnValidate()
    {
        if (yawTiltDifferenceFullRange < 1f)
            yawTiltDifferenceFullRange = 1f;

        if (initialThrottle < 0f) initialThrottle = 0f;
        if (initialThrottle > maxThrottle) initialThrottle = maxThrottle;
    }

    // ---------------------------------------------------------------------
    // Throttle 外部接口
    // ---------------------------------------------------------------------

    /// <summary>
    /// 直接设置当前向前推进力。会被夹到 [0, maxThrottle]，不会反向。
    /// </summary>
    public void SetThrottle(float value)
    {
        currentThrottle = Mathf.Clamp(value, 0f, maxThrottle);
    }

    /// <summary>
    /// 按增量调整当前向前推进力。delta 可正可负，但结果会被夹到 [0, maxThrottle]，
    /// 不会反向。常用于驱动器以"每秒增减量 * Time.deltaTime"的方式连续调节。
    /// </summary>
    public void AdjustThrottle(float delta)
    {
        currentThrottle = Mathf.Clamp(
            currentThrottle + delta,
            0f,
            maxThrottle
        );
    }

    // ---------------------------------------------------------------------
    // 物理主循环
    // ---------------------------------------------------------------------

    private void FixedUpdate()
    {
        if (wingController == null || rb == null)
            return;

        float dt = Time.fixedDeltaTime;

        // 1) 从输入控制器读取本帧鸟翼状态（只读消费）
        WingState left = wingController.LeftWing;
        WingState right = wingController.RightWing;
        float wingSpan = wingController.WingSpan;
        float bodyTilt = wingController.BodyTilt;
        float wingTiltDiff = wingController.WingTiltDifference;

        // 2) 取得本帧综合风力
        Vector3 wind = ResolveWind();
        lastWindSampled = wind;

        // 3) 力计算（写入 lastXxx 仅供调试）
        lastActiveLift = CalcuActiveLift(left, right);
        lastWindPush = CalcuWindPush(wind);
        lastWindLift = CalcuWindLift(wind, wingSpan);
        lastGlideLift = CalcuGlideLift(wingSpan);
        lastGravity = useUnityGravity
            ? Vector3.zero
            : Vector3.down * gravity;

        // 4) 应用线性力
        Vector3 totalAccel =
            lastActiveLift +
            lastWindPush +
            lastWindLift +
            lastGlideLift +
            lastGravity;

        rb.AddForce(totalAccel, ForceMode.Acceleration);

        // 5) 拍翼前推（与"向下挥翼平均强度"成正比，沿鸟自身 forward）
        float forwardThrust =
            wingController.AverageDownFlapStrength * flapForwardThrust;

        if (forwardThrust > 0.0001f)
        {
            rb.AddForce(
                transform.forward * forwardThrust,
                ForceMode.Acceleration
            );
        }

        // 5.5) 持续向前推进力（Throttle）
        // 严格沿 transform.forward 施加，保证鸟的"前进方向始终是鸟头方向"。
        // currentThrottle 已被 Clamp 到 [0, maxThrottle]，不会出现反向。
        if (currentThrottle > 0.0001f)
        {
            rb.AddForce(
                transform.forward * (currentThrottle * forwardThrustMultiplier),
                ForceMode.Acceleration
            );
        }

        // 6) Yaw 转向（绕世界 Up）
        ApplyYaw(wingTiltDiff, dt);

        // 7) Roll 倾斜扭矩（用整体倾角 + 拍翼差异）
        ApplyRollTorque(bodyTilt, left, right);

        // 8) 阻尼与垂直限速
        ApplyDampingAndClamps();

        // 9) 姿态硬限制：限制 Roll / Pitch，避免长时间按转向键导致鸟翻滚乱转
        if (clampAttitude)
        {
            ClampAttitude();
        }

        if (debugLog)
        {
            Debug.Log(
                $"[BirdFlightController] " +
                $"ActiveLift={lastActiveLift.magnitude:F2} " +
                $"WindPush={lastWindPush.magnitude:F2} " +
                $"WindLift={lastWindLift.magnitude:F2} " +
                $"Glide={lastGlideLift.magnitude:F2} " +
                $"Vel={rb.velocity}",
                this
            );
        }
    }

    // ---------------------------------------------------------------------
    // 风力来源
    // ---------------------------------------------------------------------

    /// <summary>
    /// 优先使用全局 <see cref="WindSampler"/> 查询本帧风；若场景没有可用采样器，
    /// 或用户显式关闭 <c>useGlobalWindSampler</c>，则回退到
    /// <see cref="BirdWingInputController.WindForce"/>（由外部输入驱动者填入）。
    /// </summary>
    private Vector3 ResolveWind()
    {
        Vector3 raw;

        if (useGlobalWindSampler && WindSampler.Instance != null)
        {
            raw = WindSampler.Sample(rb.worldCenterOfMass);
        }
        else
        {
            raw = wingController.WindForce;
        }

        return Vector3.ClampMagnitude(raw, maxWindStrength);
    }

    // ---------------------------------------------------------------------
    // 力计算
    // ---------------------------------------------------------------------

    /// <summary>
    /// 主动拍翼升力。只统计向下拍翼，沿鸟自身 up 方向。
    /// activeLift = transform.up * averageDownFlapStrength * activeLiftMultiplier。
    /// </summary>
    private Vector3 CalcuActiveLift(WingState left, WingState right)
    {
        float strength =
            wingController.AverageDownFlapStrength *
            activeLiftMultiplier;

        return transform.up * strength;
    }

    /// <summary>沿风向的推动力。</summary>
    private Vector3 CalcuWindPush(Vector3 wind)
    {
        return wind * windPushMultiplier;
    }

    /// <summary>
    /// 沿两翼中垂线向上的风升力。
    /// 中垂线方向取 transform.up（鸟身体的"背部朝上"方向，已被 Roll 倾斜影响）。
    /// 翼展越大、风越强，升力越强。
    /// </summary>
    private Vector3 CalcuWindLift(Vector3 wind, float wingSpan)
    {
        float strength = windLiftUseOnlyVerticalComponent
            ? Mathf.Abs(Vector3.Project(wind, Vector3.up).magnitude)
            : wind.magnitude;

        return transform.up *
               strength *
               Mathf.Clamp01(wingSpan) *
               windLiftMultiplier;
    }

    /// <summary>翼展提供的恒定基础滑翔升力（沿世界 Up）。</summary>
    private Vector3 CalcuGlideLift(float wingSpan)
    {
        return Vector3.up *
               Mathf.Clamp01(wingSpan) *
               glideLiftMultiplier;
    }

    // ---------------------------------------------------------------------
    // 转向 / 姿态
    // ---------------------------------------------------------------------

    /// <summary>
    /// Yaw 转向：使用 rb.MoveRotation，避免破坏 Rigidbody 物理状态。
    /// 参考 PlayerInput.ApplyYawByInclination 的设计，但用 WingTiltDifference 替代 inclination。
    /// </summary>
    private void ApplyYaw(float wingTiltDifference, float dt)
    {
        if (Mathf.Abs(wingTiltDifference) < yawDeadZone)
            return;

        float t = Mathf.Clamp(
            wingTiltDifference / yawTiltDifferenceFullRange,
            -1f,
            1f
        );

        float yawDelta =
            yawDirectionSign *
            t *
            yawSpeedMultiplier *
            dt;

        Quaternion delta = Quaternion.AngleAxis(yawDelta, Vector3.up);
        rb.MoveRotation(rb.rotation * delta);
    }

    /// <summary>
    /// Roll 扭矩：用 BodyTilt 让鸟绕自身 forward 倾斜，加左右翼向下挥差异作为辅助。
    /// 使用 AddTorque，符合"只用 AddForce / AddTorque 基础 API"约束。
    /// </summary>
    private void ApplyRollTorque(
        float bodyTilt,
        WingState left,
        WingState right)
    {
        float asymmetry =
            Mathf.Max(0f, right.normalizedFlapSpeed) -
            Mathf.Max(0f, left.normalizedFlapSpeed);

        float roll =
            -bodyTilt * bodyTiltRollMultiplier +
            asymmetry * flapAsymmetryRollTorque;

        if (Mathf.Abs(roll) < 0.0001f)
            return;

        rb.AddTorque(transform.forward * roll, ForceMode.Acceleration);
    }

    private void ApplyDampingAndClamps()
    {
        Vector3 v = rb.velocity;

        v.x *= horizontalDamping;
        v.z *= horizontalDamping;
        v.y *= verticalDamping;

        v.y = Mathf.Clamp(
            v.y,
            verticalSpeedClamp.x,
            verticalSpeedClamp.y
        );

        rb.velocity = v;
    }

    /// <summary>
    /// 姿态硬限制：把鸟身体的 Roll（绕自身 forward）与 Pitch（绕自身 right）
    /// 夹紧到 [-maxRollAngle, +maxRollAngle] / [-maxPitchAngle, +maxPitchAngle] 范围内。
    ///
    /// 设计要点：
    /// 1) 不限制 Yaw —— 玩家需要自由 360° 转向，水平方向的旋转不应被约束。
    /// 2) 实现方式：把世界坐标下的当前姿态分解为
    ///       yawOnly  =  仅保留 forward 在水平面上投影得到的朝向（Roll/Pitch=0）
    ///       localResidual = inverse(yawOnly) * currentRotation   →  剩下的 Roll/Pitch
    ///    再把 localResidual 转为欧拉角，取 X(Pitch)、Z(Roll)，分别 Clamp，最后重组。
    /// 3) 接近上限时按 attitudeClampAngularDamping 给相应轴的角速度乘一个 (1-阻尼)，
    ///    避免到位瞬间硬撞墙的抽搐感。
    /// 4) 当 forward 几乎垂直时（俯冲到接近竖直），yaw 分解会退化，
    ///    此处用 Mathf.Epsilon 判断兜底，跳过本帧的姿态夹紧，等鸟自然恢复后再生效。
    /// </summary>
    private void ClampAttitude()
    {
        Quaternion current = rb.rotation;

        // 计算"仅 Yaw"的参考姿态：把当前 forward 投影到水平面上
        Vector3 fwd = current * Vector3.forward;
        Vector3 fwdFlat = new Vector3(fwd.x, 0f, fwd.z);

        if (fwdFlat.sqrMagnitude < 1e-6f)
        {
            // 几乎垂直冲天 / 俯冲，yaw 不可靠，跳过本帧夹紧
            return;
        }

        Quaternion yawOnly = Quaternion.LookRotation(
            fwdFlat.normalized,
            Vector3.up
        );

        // 把 current 表达到 yawOnly 的局部空间下：residual 只包含 Pitch 与 Roll
        Quaternion residual = Quaternion.Inverse(yawOnly) * current;
        Vector3 e = residual.eulerAngles;

        // eulerAngles 是 [0,360)，转回 [-180,180]
        float pitch = NormalizeAngle(e.x);
        float roll = NormalizeAngle(e.z);

        float clampedPitch = Mathf.Clamp(pitch, -maxPitchAngle, maxPitchAngle);
        float clampedRoll = Mathf.Clamp(roll, -maxRollAngle, maxRollAngle);

        bool changed =
            !Mathf.Approximately(clampedPitch, pitch) ||
            !Mathf.Approximately(clampedRoll, roll);

        if (changed)
        {
            Quaternion newResidual = Quaternion.Euler(
                clampedPitch,
                e.y, // y 应当接近 0（已被 yawOnly 提走），保留以防数值漂移
                clampedRoll
            );

            Quaternion newRot = yawOnly * newResidual;
            rb.MoveRotation(newRot);

            // 对超界的轴施加额外角速度阻尼，避免下一帧又被同方向扭矩撞墙
            Vector3 angVel = rb.angularVelocity;
            // 鸟自身坐标系下的 Roll(Z)/Pitch(X) 轴
            Vector3 selfRight = newRot * Vector3.right;
            Vector3 selfForward = newRot * Vector3.forward;

            float damp = 1f - attitudeClampAngularDamping;

            if (!Mathf.Approximately(clampedPitch, pitch))
            {
                // 削弱绕 self-right (pitch) 的分量
                float pitchComp = Vector3.Dot(angVel, selfRight);
                angVel -= selfRight * (pitchComp * (1f - damp));
            }

            if (!Mathf.Approximately(clampedRoll, roll))
            {
                // 削弱绕 self-forward (roll) 的分量
                float rollComp = Vector3.Dot(angVel, selfForward);
                angVel -= selfForward * (rollComp * (1f - damp));
            }

            rb.angularVelocity = angVel;
        }
    }

    /// <summary>把 [0,360) 的欧拉角转到 [-180,180]，便于 Clamp。</summary>
    private static float NormalizeAngle(float deg)
    {
        deg %= 360f;
        if (deg > 180f) deg -= 360f;
        else if (deg < -180f) deg += 360f;
        return deg;
    }

    // ---------------------------------------------------------------------
    // Gizmos
    // ---------------------------------------------------------------------

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos)
            return;

        Vector3 origin = transform.position;

        // 主动拍翼升力（绿色）
        Gizmos.color = Color.green;
        Gizmos.DrawLine(origin, origin + lastActiveLift * gizmoForceScale);

        // 风推（青色）
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(origin, origin + lastWindPush * gizmoForceScale);

        // 风升（黄色）
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(origin, origin + lastWindLift * gizmoForceScale);

        // 滑翔升力（白色）
        Gizmos.color = Color.white;
        Gizmos.DrawLine(origin, origin + lastGlideLift * gizmoForceScale);

        // 采样到的原始风（品红）
        Gizmos.color = Color.magenta;
        Gizmos.DrawLine(origin, origin + lastWindSampled * gizmoForceScale);
    }
}
