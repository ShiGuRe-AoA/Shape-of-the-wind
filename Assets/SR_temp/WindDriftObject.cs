using UnityEngine;

/// <summary>
/// 树叶 / 飘浮物在风中的极简飘荡体：
/// - 不使用 <see cref="Rigidbody"/>，纯 <c>Transform</c> 更新。
/// - 一套极简 <b>力 → 加速度 → 速度 → 位移</b> 体系。
/// - 每 <see cref="sampleInterval"/> 秒调用一次全局 <see cref="WindSampler"/> 采样风力，
///   把风向量当作外力施加；同一时刻施加一个与速度反向、大小正比于速度大小的阻力，
///   两者共同约束速度，避免无限累积。
/// - 位置每帧按当前速度积分更新；角速度用 Perlin 噪声驱动，做出"树叶在强风中乱转"的姿态。
///
/// 使用：挂到任意物体上即可。物体在场景中需被 <see cref="WindSampler"/> 覆盖到才能吃到风力。
/// </summary>
[DisallowMultipleComponent]
public class WindDriftObject : MonoBehaviour
{
    // ------------------------------------------------------------------
    // 采样与力
    // ------------------------------------------------------------------

    [Header("Sampling")]

    [Tooltip("每隔多少秒重新采一次风并重新计算阻力。数值越小反馈越及时，也越费性能。")]
    [SerializeField, Min(0.001f)]
    private float sampleInterval = 0.1f;

    [Header("Wind Force")]

    [Tooltip("风向量转成外力时的倍率。最终 windAccel = windVector * windForceMultiplier / mass。")]
    [SerializeField]
    private float windForceMultiplier = 1f;

    [Tooltip("质量。用作 F = m·a 的分母。数值越大越迟钝。")]
    [SerializeField, Min(0.0001f)]
    private float mass = 1f;

    [Header("Drag (空气阻力)")]

    [Tooltip(
        "线性阻力系数。阻力 = -dragCoefficient * velocity。\n" +
        "越大越快让速度衰减到 0，注意别调过大导致数值震荡（配合 sampleInterval 使用）。"
    )]
    [SerializeField, Min(0f)]
    private float dragCoefficient = 0.8f;

    [Tooltip("允许的最大速度。达到该值后不再累加，避免风非常强时飞出场景。")]
    [SerializeField, Min(0f)]
    private float maxSpeed = 15f;

    [Header("Initial")]

    [Tooltip("初始速度。可用来模拟已经在飘的物体。")]
    [SerializeField]
    private Vector3 initialVelocity;

    // ------------------------------------------------------------------
    // 寿命 / 自毁
    // ------------------------------------------------------------------

    [Header("Lifetime")]

    [Tooltip(
        "生成后经过多少秒自动销毁自身 GameObject。\n" +
        "<= 0 表示不自动销毁（需要外部管理生命周期）。\n" +
        "可通过 SetLifetime(...) 在运行时覆盖。"
    )]
    public float lifetime = -1f;

    // ------------------------------------------------------------------
    // 混乱旋转
    // ------------------------------------------------------------------

    [Header("Chaotic Rotation")]

    [Tooltip("三个轴的最大角速度（度 / 秒）。Perlin 噪声在 [-1, 1] 之间摆动后乘以该向量。")]
    [SerializeField]
    private Vector3 maxAngularSpeed = new Vector3(180f, 180f, 180f);

    [Tooltip("噪声推进速率。越大转得越\"抽搐\"，越小转得越舒缓。")]
    [SerializeField, Min(0f)]
    private float rotationNoiseSpeed = 1.2f;

    [Tooltip("角速度还会随当前线速度大小放大：factor = 1 + speed * speedInfluence。0 表示与速度无关。")]
    [SerializeField, Min(0f)]
    private float rotationSpeedInfluence = 0.15f;

    // ------------------------------------------------------------------
    // Debug
    // ------------------------------------------------------------------

    [Header("Debug")]

    [Tooltip("Scene 视图选中时绘制当前速度与最近一次采样的风向量。")]
    [SerializeField]
    private bool drawGizmos = true;

    // ------------------------------------------------------------------
    // 内部状态
    // ------------------------------------------------------------------

    private Vector3 velocity;
    private Vector3 lastSampledWind;
    private float sampleTimer;

    // 已存活时间（秒）。用于寿命自毁。
    private float aliveTime;

    // 每个实例独立的噪声偏移，避免所有物体同步转动
    private Vector3 noiseOffset;

    // ------------------------------------------------------------------
    // 生命周期
    // ------------------------------------------------------------------

    private void Awake()
    {
        velocity = initialVelocity;
        noiseOffset = new Vector3(
            Random.Range(0f, 1000f),
            Random.Range(0f, 1000f),
            Random.Range(0f, 1000f)
        );
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // ---- 寿命：达到 lifetime 秒后自毁（lifetime <= 0 视为永久存在） ----
        aliveTime += dt;
        if (lifetime > 0f && aliveTime >= lifetime)
        {
            Destroy(gameObject);
            return;
        }

        // ---- 每 sampleInterval 秒重新采一次风并施加一次冲量 ----
        sampleTimer -= dt;
        if (sampleTimer <= 0f)
        {
            sampleTimer += sampleInterval;
            ApplyWindAndDragImpulse();
        }

        // ---- 位置积分：无论是否采样，速度都以固定形式作用于位置 ----
        // 若速度已经因风 + 阻力平衡下来，这里就是匀速漂移。
        if (maxSpeed > 0f)
            velocity = Vector3.ClampMagnitude(velocity, maxSpeed);

        transform.position += velocity * dt;

        // ---- 混乱旋转：Perlin 噪声驱动的欧拉角速度 ----
        ApplyChaoticRotation(dt);
    }

    // ------------------------------------------------------------------
    // 力学
    // ------------------------------------------------------------------

    /// <summary>
    /// 每个采样间隔调用一次：把"这段时间内应吃到的风冲量 + 阻力冲量"一次性加到速度上。
    /// 冲量 = 加速度 * sampleInterval。加速度由 F = m·a 得到。
    /// 力包括：
    ///   1. 风力 = windVector * windForceMultiplier
    ///   2. 阻力 = -dragCoefficient * velocity
    /// </summary>
    private void ApplyWindAndDragImpulse()
    {
        lastSampledWind = WindSampler.Sample(transform.position);

        Vector3 windForce = lastSampledWind * windForceMultiplier;
        Vector3 dragForce = -dragCoefficient * velocity;

        Vector3 acceleration = (windForce + dragForce) / Mathf.Max(mass, 0.0001f);
        velocity += acceleration * sampleInterval;
    }

    // ------------------------------------------------------------------
    // 旋转
    // ------------------------------------------------------------------

    private void ApplyChaoticRotation(float dt)
    {
        float t = Time.time * rotationNoiseSpeed;

        // Perlin 输入映射到 [-1, 1]
        float nx = Mathf.PerlinNoise(t + noiseOffset.x, noiseOffset.x * 0.31f) * 2f - 1f;
        float ny = Mathf.PerlinNoise(t + noiseOffset.y, noiseOffset.y * 0.71f) * 2f - 1f;
        float nz = Mathf.PerlinNoise(t + noiseOffset.z, noiseOffset.z * 0.53f) * 2f - 1f;

        // 速度越大转得越猛
        float speedFactor = 1f + velocity.magnitude * rotationSpeedInfluence;

        Vector3 angularDeg = new Vector3(
            nx * maxAngularSpeed.x,
            ny * maxAngularSpeed.y,
            nz * maxAngularSpeed.z
        ) * speedFactor;

        // 沿世界轴自转（局部/世界一起转看起来都够乱，选世界轴避免陀螺锁死角）
        transform.Rotate(angularDeg * dt, Space.World);
    }

    // ------------------------------------------------------------------
    // 外部接口
    // ------------------------------------------------------------------

    /// <summary>当前线速度（只读）。</summary>
    public Vector3 Velocity => velocity;

    /// <summary>最近一次采样得到的风向量（只读）。</summary>
    public Vector3 LastSampledWind => lastSampledWind;

    /// <summary>外部一次性把某个瞬时冲量注入速度（例如爆炸、碰撞反弹）。</summary>
    public void AddImpulse(Vector3 impulse)
    {
        velocity += impulse / Mathf.Max(mass, 0.0001f);
    }

    /// <summary>清零速度。</summary>
    public void ResetVelocity()
    {
        velocity = Vector3.zero;
    }

    /// <summary>直接设置当前速度（覆盖式）。常用于生成后赋予随机初速度。</summary>
    public void SetVelocity(Vector3 newVelocity)
    {
        velocity = newVelocity;
    }

    /// <summary>
    /// 设置寿命并重置存活计时。<paramref name="seconds"/> &lt;= 0 表示不自动销毁。
    /// 常用于生成器在实例化后指定该物体的存在时长。
    /// </summary>
    public void SetLifetime(float seconds)
    {
        lifetime = seconds;
        aliveTime = 0f;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;

        Vector3 pos = transform.position;

        // 速度：黄色
        if (velocity.sqrMagnitude > 0.0001f)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(pos, pos + velocity);
            Gizmos.DrawSphere(pos + velocity, 0.05f);
        }

        // 最近一次采样风：青色
        if (lastSampledWind.sqrMagnitude > 0.0001f)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(pos, pos + lastSampledWind);
        }
    }
#endif
}
