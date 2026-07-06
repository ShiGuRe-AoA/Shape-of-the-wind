using UnityEngine;

public class WindProbe : MonoBehaviour
{
    [Tooltip("风向。开启 Use Local Direction 时，该方向基于探针本地坐标。")]
    [SerializeField]
    private Vector3 windDirection = Vector3.forward;

    [Tooltip("风强度，当前作为风加速度使用。")]
    [SerializeField, Min(0f)]
    private float windStrength = 5f;

    [Tooltip("是否将 Wind Direction 视为探针本地坐标方向。")]
    [SerializeField]
    private bool useLocalDirection = true;

#if UNITY_EDITOR
    [Header("Gizmos")]
    [Tooltip("是否始终绘制 Gizmos（不勾选则仅在选中时绘制）。")]
    [SerializeField]
    private bool alwaysDrawGizmos = true;

    [Tooltip("风力箭头长度 = strength * lengthPerStrength。")]
    [SerializeField, Min(0f)]
    private float gizmoLengthPerStrength = 0.5f;

    [Tooltip("箭头长度上限，避免风力过大时线条过长。0 表示不限制。")]
    [SerializeField, Min(0f)]
    private float gizmoMaxLength = 20f;

    [Tooltip("原点小球半径。0 表示不画。")]
    [SerializeField, Min(0f)]
    private float gizmoOriginRadius = 0.08f;

    [Tooltip("风力为 0（弱风）与最大值时的颜色，按强度插值。")]
    [SerializeField]
    private Color gizmoColorLow = new Color(0.4f, 0.8f, 1f, 0.9f);

    [SerializeField]
    private Color gizmoColorHigh = new Color(1f, 0.3f, 0.15f, 1f);

    [Tooltip("颜色插值用的参考最大风强。")]
    [SerializeField, Min(0.0001f)]
    private float gizmoStrengthReference = 10f;
#endif

    public Vector3 GetWindDirection()
    {
        Vector3 direction = useLocalDirection
            ? transform.TransformDirection(windDirection)
            : windDirection;

        if (direction.sqrMagnitude <= 0.0001f)
            return Vector3.zero;

        return direction.normalized;
    }

    public float GetWindStrength()
    {
        return windStrength;
    }

    public Vector3 GetWindVector()
    {
        return GetWindDirection() * windStrength;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (alwaysDrawGizmos)
            DrawWindGizmo(false);
    }

    private void OnDrawGizmosSelected()
    {
        DrawWindGizmo(true);
    }

    private void DrawWindGizmo(bool selected)
    {
        Vector3 direction = GetWindDirection();
        Vector3 origin = transform.position;

        // 颜色：按强度在 low → high 之间插值；选中时提亮
        float t = Mathf.Clamp01(windStrength / Mathf.Max(0.0001f, gizmoStrengthReference));
        Color color = Color.Lerp(gizmoColorLow, gizmoColorHigh, t);
        if (!selected)
            color.a *= 0.65f;
        Gizmos.color = color;

        if (gizmoOriginRadius > 0f)
            Gizmos.DrawSphere(origin, gizmoOriginRadius);

        if (direction == Vector3.zero || windStrength <= 0.0001f)
            return;

        float length = windStrength * gizmoLengthPerStrength;
        if (gizmoMaxLength > 0f)
            length = Mathf.Min(length, gizmoMaxLength);
        if (length <= 0.0001f)
            return;

        Vector3 tip = origin + direction * length;
        Gizmos.DrawLine(origin, tip);

        // 箭头头部：两条短线构成的 "V"，长度与线体成比例
        float headLen = Mathf.Max(0.05f, length * 0.2f);
        // 取一个与 direction 尽量不平行的辅助向量以生成侧向轴
        Vector3 side = Vector3.Cross(
            direction,
            Mathf.Abs(direction.y) < 0.95f ? Vector3.up : Vector3.right
        ).normalized;
        Vector3 up = Vector3.Cross(side, direction).normalized;

        Vector3 back = tip - direction * headLen;
        float halfWidth = headLen * 0.5f;

        Gizmos.DrawLine(tip, back + side * halfWidth);
        Gizmos.DrawLine(tip, back - side * halfWidth);
        Gizmos.DrawLine(tip, back + up * halfWidth);
        Gizmos.DrawLine(tip, back - up * halfWidth);

        // 选中时额外画一个标签显示强度
        if (selected)
        {
            UnityEditor.Handles.color = color;
            UnityEditor.Handles.Label(
                tip + direction * (headLen * 0.6f),
                $"{windStrength:F2}"
            );
        }
    }
#endif
}