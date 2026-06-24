using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInput : MonoBehaviour
{
    public PlayerAction inputActions;

    [Header("Lift")]

    [SerializeField]
    private float flapLiftCoefficient = 5.8f; // 拍翼升力系数

    [SerializeField]
    private float windLiftCoefficient = 2f; // 展翼产生的持续升力

    [Tooltip("拍翼效果倍率。1为原始效果，0.8表示减弱20%。")]
    [SerializeField, Range(0f, 1f)]
    private float flapEffectMultiplier = 0.8f;

    [Header("Forward Movement")]

    [SerializeField]
    private float forwardAcceleration = 2f; // 前进加速度

    [SerializeField]
    private float backwardAcceleration = -5f; // 减速加速度

    [SerializeField]
    private float constantAcceleration = -0.1f; // 恒定减速加速度

    [SerializeField]
    private float maxSpeed = 8f; // 最快速度

    [SerializeField]
    private float minSpeed = 0.1f; // 最慢速度

    [Header("Gravity")]

    [SerializeField]
    private float gravitySpeed = 8f;

    [Header("Wind Field")]

    [Tooltip("场景中的风场管理对象。")]
    [SerializeField]
    private WindTest windField;

    [Tooltip("风场向上、向下分量对玩家的影响倍率。")]
    [SerializeField, Range(0f, 1f)]
    private float verticalWindInfluence = 0.35f;

    [Tooltip("风场水平方向对玩家的影响倍率。小地图建议保持较低。")]
    [SerializeField, Range(0f, 1f)]
    private float horizontalWindInfluence = 0.05f;

    [Tooltip("限制多个风探针重叠时的最大风加速度。")]
    [SerializeField, Min(0f)]
    private float maxWindAcceleration = 4f;

    [Header("References")]

    [SerializeField]
    private Rigidbody rb;

    [Header("Debug")]

    [SerializeField]
    private bool showWindDebug;

    private Vector2 leftWingInput;
    private Vector2 leftWingInput_previous;

    private Vector2 rightWingInput;
    private Vector2 rightWingInput_previous;

    private float inclination; // 倾角
    private Vector3 normal; // 法线向量
    private float wingspan; // 翼展
    private Vector3 flapLift; // 拍翼升力

    private bool speedUp;
    private bool speedDown;

    private float currentSpeed;

    private Vector3 currentWind;

    public Vector3 CurrentWind => currentWind;
    public float CurrentSpeed => currentSpeed;
    public float CurrentWingspan => wingspan;
    public Vector3 CurrentFlapLift => flapLift;

    private void Awake()
    {
        if (rb == null)
            rb = GetComponent<Rigidbody>();

        if (rb == null)
        {
            Debug.LogError(
                "[PlayerInput] 当前对象没有 Rigidbody。",
                this
            );

            enabled = false;
            return;
        }

        if (inputActions == null)
        {
            inputActions = new PlayerAction();

            //inputActions.Main.LeftWingInput.performed += LeftWingInput_performed;
            //inputActions.Main.RightWingInput.performed += RightWingInput_performed;

            inputActions.Main.SpeedUp.performed += SpeedUp_performed;
            inputActions.Main.SpeedDown.performed += SpeedDown_performed;

            inputActions.Main.SpeedUp.canceled += SpeedUp_canceled;
            inputActions.Main.SpeedDown.canceled += SpeedDown_canceled;
        }

        // 当前脚本已经手动施加重力。
        rb.useGravity = false;

        currentSpeed = minSpeed;

        inputActions.Enable();
    }

    private void OnEnable()
    {
        inputActions?.Enable();
    }

    private void OnDisable()
    {
        inputActions?.Disable();

        speedUp = false;
        speedDown = false;
    }

    private void OnDestroy()
    {
        if (inputActions == null)
            return;

        inputActions.Main.SpeedUp.performed -= SpeedUp_performed;
        inputActions.Main.SpeedDown.performed -= SpeedDown_performed;

        inputActions.Main.SpeedUp.canceled -= SpeedUp_canceled;
        inputActions.Main.SpeedDown.canceled -= SpeedDown_canceled;

        inputActions.Dispose();
    }

    private void SpeedUp_canceled(
        InputAction.CallbackContext ctx)
    {
        speedUp = false;
    }

    private void SpeedDown_canceled(
        InputAction.CallbackContext ctx)
    {
        speedDown = false;
    }

    private void SpeedDown_performed(
        InputAction.CallbackContext ctx)
    {
        //Gamepad pad = ctx.control.device as Gamepad;
        //if (pad.leftShoulder.isPressed && pad.rightShoulder.isPressed)
        //{
        //    Debug.Log("SpeedDown");
        //    speedDown = true;
        //}

        speedDown = true;
    }

    private void SpeedUp_performed(
        InputAction.CallbackContext ctx)
    {
        //var pad = Gamepad.current;
        //if (pad == null) return;

        //bool bothPressed =
        //    pad.leftTrigger.ReadValue() > 0.5f &&
        //    pad.rightTrigger.ReadValue() > 0.5f;

        //speedUp = bothPressed;

        speedUp = true;
    }

    //private void RightWingInput_performed(
    //    InputAction.CallbackContext context)
    //{
    //    rightWingInput = context.ReadValue<Vector2>();
    //}

    //private void LeftWingInput_performed(
    //    InputAction.CallbackContext context)
    //{
    //    leftWingInput = context.ReadValue<Vector2>();
    //}

    private void Update()
    {
        leftWingInput_previous = leftWingInput;
        rightWingInput_previous = rightWingInput;

        leftWingInput =
            inputActions.Main.LeftWingInput.ReadValue<Vector2>();

        rightWingInput =
            inputActions.Main.RightWingInput.ReadValue<Vector2>();

        wingspan = CalcuWingspan();
        inclination = CalcuInclination();
        normal = CalcuPlayerNormal(inclination);
        flapLift = CalcuFlapLift();

        ApplySpeedChange();
        ApplyPhysics();
        ApplyYawByInclination(inclination);

        rb.MovePosition(
            transform.position +
            transform.forward *
            currentSpeed *
            Time.deltaTime
        );

        if (showWindDebug)
        {
            Debug.Log(
                $"Wind={currentWind}, " +
                $"FlapY={flapLift.y:F2}, " +
                $"Wingspan={wingspan:F2}, " +
                $"Velocity={rb.velocity}"
            );
        }

        // Debug.Log(
        //     $"Wingspan: {wingspan}, " +
        //     $"Inclination: {inclination}, " +
        //     $"Normal: {normal}"
        // );
    }

    private float CalcuWingspan()
    {
        float leftSpan =
            (
                leftWingInput -
                new Vector2(1f, 0f)
            ).magnitude;

        float rightSpan =
            (
                rightWingInput -
                new Vector2(-1f, 0f)
            ).magnitude;

        return leftSpan + rightSpan;
    }

    private float CalcuInclination()
    {
        Vector2 leftWingVector =
            leftWingInput -
            new Vector2(1f, 0f);

        Vector2 rightWingVector =
            rightWingInput -
            new Vector2(-1f, 0f);

        float leftHorizonAngle =
            Vector2.Angle(
                Vector2.left,
                leftWingVector
            );

        float rightHorizonAngle =
            Vector2.Angle(
                Vector2.right,
                rightWingVector
            );

        return leftHorizonAngle -
               rightHorizonAngle;
    }

    private Vector3 CalcuPlayerNormal(float inclination)
    {
        Vector3 axis = transform.forward;
        float angle = inclination;

        Vector3 playerNormal =
            Quaternion.AngleAxis(
                angle,
                axis
            ) * Vector3.up;

        return playerNormal;
    }

    private Vector3 CalcuFlapLift()
    {
        float safeDeltaTime =
            Mathf.Max(
                Time.deltaTime,
                0.0001f
            );

        float leftFlapSpeed =
            flapLiftCoefficient *
            (
                leftWingInput_previous.y -
                leftWingInput.y
            ) /
            safeDeltaTime;

        float rightFlapSpeed =
            flapLiftCoefficient *
            (
                rightWingInput_previous.y -
                rightWingInput.y
            ) /
            safeDeltaTime;

        float horizontalFlapLift =
            leftFlapSpeed -
            rightFlapSpeed;

        float verticalFlapLift =
            leftFlapSpeed +
            rightFlapSpeed;

        if (verticalFlapLift < 0f)
        {
            verticalFlapLift *= 0.2f;
        }

        return new Vector3(
            horizontalFlapLift,
            verticalFlapLift,
            0f
        );
    }

    private void ApplySpeedChange()
    {
        if (speedUp)
        {
            currentSpeed +=
                forwardAcceleration *
                Time.deltaTime;
        }

        if (speedDown)
        {
            currentSpeed +=
                backwardAcceleration *
                Time.deltaTime;
        }

        currentSpeed +=
            constantAcceleration *
            Time.deltaTime;

        currentSpeed = Mathf.Clamp(
            currentSpeed,
            minSpeed,
            maxSpeed
        );
    }

    private void ApplyYawByInclination(float inclination)
    {
        // 倾角死区，避免轻微抖动。
        if (Mathf.Abs(inclination) < 1f)
            return;

        // 最大可参与计算的倾角。
        float maxInclination = 60f;

        // 最大转向速度，单位为度/秒。
        float maxYawSpeed = 120f;

        // 将 inclination 映射到 -1 ~ 1。
        float t = Mathf.Clamp(
            inclination / maxInclination,
            -1f,
            1f
        );

        // 正值逆时针，负值顺时针。
        float yawDelta =
            -t *
            maxYawSpeed *
            Time.deltaTime;

        transform.Rotate(
            0f,
            yawDelta,
            0f,
            Space.World
        );
    }

    private void ApplyPhysics()
    {
        Vector3 velocity = rb.velocity;

        float flapUp =
            flapLift.y *
            flapEffectMultiplier;

        float lateral =
            flapLift.x *
            flapEffectMultiplier;

        // 拍翼产生速度增量。
        // 通过 flapEffectMultiplier 稍微减弱拍翼效果。
        velocity +=
            transform.up *
            flapUp *
            Time.deltaTime;

        velocity +=
            transform.right *
            lateral *
            Time.deltaTime;

        // 初版滑翔升力。
        // 目前仍然是翼展越大，持续升力越强。
        velocity +=
            Vector3.up *
            windLiftCoefficient *
            wingspan *
            Time.deltaTime;

        ApplyWind(ref velocity);

        // 手动重力。
        velocity +=
            Vector3.down *
            gravitySpeed *
            Time.deltaTime;

        // 人工阻尼，避免越飞越快。
        velocity.x *= 0.98f;
        velocity.z *= 0.98f;
        velocity.y *= 0.995f;

        // 垂直限速。
        velocity.y = Mathf.Clamp(
            velocity.y,
            -10f,
            7f
        );

        rb.velocity = velocity;
    }

    private void ApplyWind(ref Vector3 velocity)
    {
        if (windField == null)
        {
            currentWind = Vector3.zero;
            return;
        }

        currentWind =
            windField.SampleWind(
                rb.worldCenterOfMass
            );

        // 防止多个探针重叠时风力无限叠加。
        Vector3 limitedWind =
            Vector3.ClampMagnitude(
                currentWind,
                maxWindAcceleration
            );

        // 将风拆成竖直分量与水平分量。
        Vector3 verticalWind =
            Vector3.Project(
                limitedWind,
                Vector3.up
            );

        Vector3 horizontalWind =
            Vector3.ProjectOnPlane(
                limitedWind,
                Vector3.up
            );

        // 小地图中水平风影响保持较低，
        // 上升气流的作用更加明显。
        Vector3 appliedWind =
            verticalWind *
            verticalWindInfluence +
            horizontalWind *
            horizontalWindInfluence;

        velocity +=
            appliedWind *
            Time.deltaTime;
    }
}