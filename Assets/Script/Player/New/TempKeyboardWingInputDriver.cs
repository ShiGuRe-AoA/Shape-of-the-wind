using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 临时键盘输入驱动。仅用于在没有手柄 / VR 设备时快速调试 BirdWingInputController + BirdFlightController。
///
/// 按键映射：
/// - 翅膀挥动：
///   Q：左翼快速向上（flapSpeed 取负，tilt 取负）
///   A：左翼快速向下（flapSpeed 取正，tilt 取正）
///   E：右翼快速向上
///   D：右翼快速向下
///   同侧上 + 下同时按下时，向下优先。
/// - 持续向前推进力（Throttle）：
///   W：增大向前推进力（按住持续累加，存在上限，由 BirdFlightController.maxThrottle 限制）
///   S：减小向前推进力（按住持续递减，下限为 0，不会反向）
/// - 整体姿态 / 转向：
///   方向键 Left  / 方向键 Right：整体 BodyTilt 向左 / 向右（影响 Roll 倾斜）
///   方向键 Up    / 方向键 Down ：翼展 wingSpan 增 / 减
/// - 默认翼展拉满 = 1，可在 Inspector 调。
///
/// 风力来源：
/// - 通过全局 WindSampler.Sample 查询当前位置风，并写入 BirdWingInputData.windForce。
///   这样 BirdWingInputController 与 BirdFlightController 都能拿到一致风力。
/// - 场景中若不存在 WindSampler，返回零向量。
///
/// 不做的事：
/// - 不读取任何玩法逻辑、不施力、不改风场、不改 BirdWingInputController 任何骨骼配置。
/// - 严格走唯一入口 BirdWingInputController.ApplyInput。
/// </summary>
[RequireComponent(typeof(BirdWingInputController))]
public class TempKeyboardWingInputDriver : MonoBehaviour
{
    [Header("Refs")]

    [Tooltip("被驱动的鸟翼输入控制器。留空则自动从同物体获取。")]
    [SerializeField]
    private BirdWingInputController wingController;

    [Tooltip(
        "可选：被驱动的飞行控制器。用于通过 W / S 调节持续向前推进力（Throttle）。\n" +
        "留空则尝试自动从同物体获取；仍找不到时 W / S 失效，其他输入照常工作。"
    )]
    [SerializeField]
    private BirdFlightController flightController;

    [Header("Flap Speed (绝对值)")]

    [Tooltip("按 A / D 时单翼向下挥动速度（正值）。会写入 leftFlapSpeed / rightFlapSpeed。")]
    [SerializeField]
    private float downFlapSpeed = 5f;

    [Tooltip("按 Q / E 时单翼向上抬起速度（绝对值，写入时取负）。")]
    [SerializeField]
    private float upFlapSpeed = 5f;

    [Header("Wing Tilt (单位：度)")]

    [Tooltip("按 A / D 时单翼向下倾角（正值表示向下偏）。")]
    [SerializeField]
    private float downWingTilt = 30f;

    [Tooltip("按 Q / E 时单翼向上倾角（写入时取负）。")]
    [SerializeField]
    private float upWingTilt = 30f;

    [Header("Body Tilt / Wing Span (方向键)")]

    [Tooltip("按方向键 Left/Right 时整体倾角(度)，影响飞行控制器的 Roll 倾斜与转向辅助。")]
    [SerializeField]
    private float bodyTiltMagnitude = 30f;

    [Tooltip("按方向键 Up/Down 时翼展每秒变化速率。")]
    [SerializeField]
    private float wingSpanSpeed = 1.5f;

    [Header("Throttle (W/S 持续推进力)")]

    [Tooltip("按住 W 时每秒增加的 Throttle 数值。")]
    [SerializeField, Min(0f)]
    private float throttleIncreaseRate = 4f;

    [Tooltip("按住 S 时每秒减小的 Throttle 数值。下限恒为 0，不会反向。")]
    [SerializeField, Min(0f)]
    private float throttleDecreaseRate = 6f;

    [Header("Smoothing")]

    [Tooltip("挥动速度的回归平滑时间，单位秒。0 表示松开按键立刻归零。")]
    [SerializeField]
    private float flapSpeedSmoothTime = 0.05f;

    [Tooltip("翅膀倾角的回归平滑时间，单位秒。0 表示松开按键立刻归零。")]
    [SerializeField]
    private float wingTiltSmoothTime = 0.08f;

    [Tooltip("整体倾角(BodyTilt) 的平滑时间。")]
    [SerializeField]
    private float bodyTiltSmoothTime = 0.1f;

    [Header("Defaults")]

    [Tooltip("默认翼展。需求要求默认拉满 = 1。")]
    [SerializeField, Range(0f, 1f)]
    private float defaultWingSpan = 1f;

    [Tooltip("是否在 Console 输出当前键盘按键状态。")]
    [SerializeField]
    private bool debugLog;

    // 当前平滑后的输出值
    private float currentLeftFlapSpeed;
    private float currentRightFlapSpeed;
    private float currentLeftWingTilt;
    private float currentRightWingTilt;
    private float currentBodyTilt;
    private float currentWingSpan;

    // SmoothDamp 速度缓存
    private float leftFlapVel;
    private float rightFlapVel;
    private float leftTiltVel;
    private float rightTiltVel;
    private float bodyTiltVel;

    private bool wingSpanInitialized;

    private void Reset()
    {
        wingController = GetComponent<BirdWingInputController>();
        flightController = GetComponent<BirdFlightController>();
    }

    private void Awake()
    {
        if (wingController == null)
            wingController = GetComponent<BirdWingInputController>();

        if (flightController == null)
            flightController = GetComponent<BirdFlightController>();

        currentWingSpan = defaultWingSpan;
        wingSpanInitialized = true;
    }

    private void Update()
    {
        if (wingController == null)
            return;

        if (!wingSpanInitialized)
        {
            currentWingSpan = defaultWingSpan;
            wingSpanInitialized = true;
        }

        Keyboard kb = Keyboard.current;
        if (kb == null)
            return;

        // -------- 翅膀挥动 --------
        bool leftUp = kb.qKey.isPressed;
        bool leftDown = kb.aKey.isPressed;
        bool rightUp = kb.eKey.isPressed;
        bool rightDown = kb.dKey.isPressed;

        float targetLeftFlap = 0f;
        float targetLeftTilt = 0f;
        if (leftDown)
        {
            targetLeftFlap = downFlapSpeed;
            targetLeftTilt = downWingTilt;
        }
        else if (leftUp)
        {
            targetLeftFlap = -upFlapSpeed;
            targetLeftTilt = -upWingTilt;
        }

        float targetRightFlap = 0f;
        float targetRightTilt = 0f;
        if (rightDown)
        {
            targetRightFlap = downFlapSpeed;
            targetRightTilt = downWingTilt;
        }
        else if (rightUp)
        {
            targetRightFlap = -upFlapSpeed;
            targetRightTilt = -upWingTilt;
        }

        // -------- 整体姿态 / 翼展 --------
        bool tiltLeftKey = kb.leftArrowKey.isPressed;
        bool tiltRightKey = kb.rightArrowKey.isPressed;
        bool spanUpKey = kb.upArrowKey.isPressed;
        bool spanDownKey = kb.downArrowKey.isPressed;

        float targetBodyTilt = 0f;
        if (tiltLeftKey)
            targetBodyTilt = -bodyTiltMagnitude;
        else if (tiltRightKey)
            targetBodyTilt = bodyTiltMagnitude;

        if (spanUpKey)
            currentWingSpan += wingSpanSpeed * Time.deltaTime;
        if (spanDownKey)
            currentWingSpan -= wingSpanSpeed * Time.deltaTime;

        currentWingSpan = Mathf.Clamp01(currentWingSpan);

        // -------- Throttle (W/S) --------
        // 仅通过 BirdFlightController 的公共接口调节，下限恒为 0、上限由 maxThrottle 限制，
        // 不会反向 —— 保证"前进方向永远是鸟头方向"。
        if (flightController != null)
        {
            bool throttleUp = kb.wKey.isPressed;
            bool throttleDown = kb.sKey.isPressed;

            float throttleDelta = 0f;
            if (throttleUp) throttleDelta += throttleIncreaseRate * Time.deltaTime;
            if (throttleDown) throttleDelta -= throttleDecreaseRate * Time.deltaTime;

            if (!Mathf.Approximately(throttleDelta, 0f))
            {
                flightController.AdjustThrottle(throttleDelta);
            }
        }

        // -------- 平滑 --------
        currentLeftFlapSpeed = Mathf.SmoothDamp(
            currentLeftFlapSpeed,
            targetLeftFlap,
            ref leftFlapVel,
            flapSpeedSmoothTime
        );

        currentRightFlapSpeed = Mathf.SmoothDamp(
            currentRightFlapSpeed,
            targetRightFlap,
            ref rightFlapVel,
            flapSpeedSmoothTime
        );

        currentLeftWingTilt = Mathf.SmoothDamp(
            currentLeftWingTilt,
            targetLeftTilt,
            ref leftTiltVel,
            wingTiltSmoothTime
        );

        currentRightWingTilt = Mathf.SmoothDamp(
            currentRightWingTilt,
            targetRightTilt,
            ref rightTiltVel,
            wingTiltSmoothTime
        );

        currentBodyTilt = Mathf.SmoothDamp(
            currentBodyTilt,
            targetBodyTilt,
            ref bodyTiltVel,
            bodyTiltSmoothTime
        );

        // -------- 风力查询 --------
        Vector3 windForce = WindSampler.Sample(transform.position);

        // -------- 写入鸟翼输入控制器 --------
        BirdWingInputData data = new BirdWingInputData
        {
            leftFlapSpeed = currentLeftFlapSpeed,
            rightFlapSpeed = currentRightFlapSpeed,
            leftWingTilt = currentLeftWingTilt,
            rightWingTilt = currentRightWingTilt,
            wingSpan = currentWingSpan,
            bodyTilt = currentBodyTilt,
            windForce = windForce
        };

        wingController.ApplyInput(data);

        if (debugLog)
        {
            float throttleNow =
                flightController != null ? flightController.CurrentThrottle : 0f;

            Debug.Log(
                $"[TempKeyboardWingInputDriver] " +
                $"Q={leftUp} A={leftDown} E={rightUp} D={rightDown} | " +
                $"LFlap={currentLeftFlapSpeed:F2} RFlap={currentRightFlapSpeed:F2} " +
                $"LTilt={currentLeftWingTilt:F1} RTilt={currentRightWingTilt:F1} " +
                $"BodyTilt={currentBodyTilt:F1} Span={currentWingSpan:F2} " +
                $"Throttle={throttleNow:F2} " +
                $"Wind={windForce.magnitude:F2}",
                this
            );
        }
    }
}
