using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInput : MonoBehaviour
{
    public PlayerAction inputActions;

    [SerializeField] private float flapLiftCoefficient = 1f;//拍翼升力系数
    [SerializeField] private float windLiftCoefficient = 1f;//拍翼升力系数

    [SerializeField] private float forwardAcceleration = 5f;//前进加速度
    [SerializeField] private float backwardAcceleration = -10f;//减速加速度
    [SerializeField] private float constantAcceleration = -0.5f;//恒定减速加速度
    [SerializeField] private float maxSpeed = 20f;//最快速度
    [SerializeField] private float minSpeed = 20f;//最慢速度
    [SerializeField] private float gravitySpeed = 9.8f;


    [SerializeField] private Rigidbody rb;

    private Vector2 leftWingInput;
    private Vector2 leftWingInput_previous;

    private Vector2 rightWingInput;
    private Vector2 rightWingInput_previous;


    private float inclination;//倾角
    private Vector3 normal;//法线向量
    private float wingspan;//翼展
    private Vector3 flapLift;//拍翼升力

    private bool speedUp;
    private bool speedDown;

    private float currentSpeed;

    private void Awake()
    {
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

        inputActions.Enable();
    }

    private void SpeedUp_canceled(InputAction.CallbackContext ctx)
    {
        speedUp = false;
    }
    private void SpeedDown_canceled(InputAction.CallbackContext ctx)
    {
        speedDown = false;
    }

    private void SpeedDown_performed(InputAction.CallbackContext ctx)
    {
        //Gamepad pad = ctx.control.device as Gamepad;
        //if (pad.leftShoulder.isPressed && pad.rightShoulder.isPressed)
        //{
        //    Debug.Log("SpeedDown");
        //    speedDown = true;
        //}
        speedDown = true;
    }

    private void SpeedUp_performed(InputAction.CallbackContext ctx)
    {
        //var pad = Gamepad.current;
        //if (pad == null) return;

        //bool bothPressed =
        //    pad.leftTrigger.ReadValue() > 0.5f &&
        //    pad.rightTrigger.ReadValue() > 0.5f;

        //speedUp = bothPressed;
        speedUp = true;
    }



    //private void RightWingInput_performed(InputAction.CallbackContext context)
    //{
    //    rightWingInput = context.ReadValue<Vector2>();
    //}

    //private void LeftWingInput_performed(InputAction.CallbackContext context)
    //{
    //    leftWingInput = context.ReadValue<Vector2>();
    //}

    void Update()
    {
        leftWingInput_previous = leftWingInput;
        rightWingInput_previous = rightWingInput;

        leftWingInput = inputActions.Main.LeftWingInput.ReadValue<Vector2>();
        rightWingInput = inputActions.Main.RightWingInput.ReadValue<Vector2>();

        wingspan = CalcuWingspan();
        inclination = CalcuInclination();
        normal = CalcuPlayerNormal(inclination);
        flapLift = CalcuFlapLift();

        ApplySpeedChange();
        ApplyPhysics();
        ApplyYawByInclination(inclination);

        rb.MovePosition(transform.position + transform.forward * currentSpeed * Time.deltaTime);
       // Debug.Log($"Wingspan: {wingspan}, Inclination: {inclination}, Normal: {normal}");



    }

    private float CalcuWingspan()
    {
        float leftSpan = (leftWingInput - new Vector2(1, 0)).magnitude;
        float rightSpan = (rightWingInput - new Vector2(-1, 0)).magnitude;


        return leftSpan + rightSpan;

    }
    private float CalcuInclination()
    {
        Vector2 leftWingVector = (leftWingInput - new Vector2(1, 0));
        Vector2 rightWingVector = (rightWingInput - new Vector2(-1, 0));
        float leftHorizonAngle = Vector2.Angle(Vector2.left, leftWingVector);
        float rightHorizonAngle = Vector2.Angle(Vector2.right, rightWingVector);

        return leftHorizonAngle - rightHorizonAngle;
    }
    private Vector3 CalcuPlayerNormal(float inclination)
    {
        Vector3 axis = transform.forward;
        float angle = inclination;
        Vector3 normal = Quaternion.AngleAxis(angle, axis) * Vector3.up;
        return normal;
    }
    private Vector3 CalcuFlapLift()
    {
        float leftFlapSpeed = flapLiftCoefficient * (leftWingInput_previous.y - leftWingInput.y) / Time.deltaTime;
        float rightFlapSpeed = flapLiftCoefficient * (rightWingInput_previous.y - rightWingInput.y) / Time.deltaTime;

        float horizontalFlapLift = leftFlapSpeed - rightFlapSpeed;
        float verticalFlapLift = leftFlapSpeed + rightFlapSpeed;
        if(verticalFlapLift < 0) { verticalFlapLift *= 0.2f; }

        return new Vector3(horizontalFlapLift, verticalFlapLift, 0);
    }

    private void ApplySpeedChange()
    {
        if (speedUp)
        {
            currentSpeed += forwardAcceleration * Time.deltaTime;
        }
        if (speedDown)
        {
            currentSpeed += backwardAcceleration * Time.deltaTime;
        }
        currentSpeed += constantAcceleration * Time.deltaTime;

        currentSpeed = Mathf.Clamp(currentSpeed, minSpeed, maxSpeed);
    }
    private void ApplyYawByInclination(float inclination)
    {
        // 倾角死区，避免轻微抖动
        if (Mathf.Abs(inclination) < 1f)
            return;

        // 最大可参与计算的倾角（超过按最大算）
        float maxInclination = 60f;

        // 最大转向速度（度/秒）
        float maxYawSpeed = 120f;

        // 把 inclination 映射到 -1 ~ 1
        float t = Mathf.Clamp(inclination / maxInclination, -1f, 1f);

        // 正值逆时针，负值顺时针（从Y轴俯视看）
        float yawDelta = -t * maxYawSpeed * Time.deltaTime;

        transform.Rotate(0f, yawDelta, 0f, Space.World);
    }
    private void ApplyPhysics()
    {
        Vector3 velocity = rb.velocity;

        float flapUp = flapLift.y;
        float lateral = flapLift.x;

        // 拍翼 -> 直接给一个可控的速度增量
        velocity += transform.up * flapUp * Time.deltaTime;
        velocity += transform.right * lateral * Time.deltaTime;

        // 滑翔升力
        velocity += Vector3.up * windLiftCoefficient * wingspan * Time.deltaTime;

        // 手动重力（作为速度项，而不是刚体加速度）
        velocity += Vector3.down * gravitySpeed * Time.deltaTime;

        // 人工阻尼，避免越飞越快
        velocity.x *= 0.98f;
        velocity.z *= 0.98f;
        velocity.y *= 0.995f;

        // 限速
        velocity.y = Mathf.Clamp(velocity.y, -10f, 7f);

        rb.velocity = velocity;
    }
}
