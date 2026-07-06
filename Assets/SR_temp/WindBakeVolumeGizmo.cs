using UnityEngine;

/// <summary>
/// 用三个 Transform 的世界坐标位置直接确定一个 <b>轴对齐包围盒（AABB）</b>：
/// 三点各自的 X/Y/Z 分量取 min/max 得到盒子的两个对角。不考虑倾斜、深度。
///
/// 语义与 <c>WindProbeNoiseBaker</c> 保持一致：两者读取同样的三点计算相同的 AABB。
/// </summary>
[ExecuteAlways]
public class WindBakeVolumeGizmo : MonoBehaviour
{
    [Header("包围盒控制点（三点各自 X/Y/Z 取 min/max 得到 AABB）")]
    [Tooltip("控制点 A。留空则使用当前 Transform 自身。")]
    [SerializeField] private Transform pointA;

    [Tooltip("控制点 B。")]
    [SerializeField] private Transform pointB;

    [Tooltip("控制点 C。")]
    [SerializeField] private Transform pointC;

    [Header("显示")]
    [Tooltip("始终绘制（否则只在选中时绘制）。")]
    [SerializeField] private bool alwaysDraw = true;

    [Tooltip("盒子棱边颜色。")]
    [SerializeField] private Color edgeColor = new Color(0.2f, 0.9f, 0.4f, 0.9f);

    [Tooltip("盒子填充面颜色（Alpha 控制透明度，0 则不画填充）。")]
    [SerializeField] private Color faceColor = new Color(0.2f, 0.9f, 0.4f, 0.08f);

    [Tooltip("在三个控制点处绘制小球，方便识别定位。")]
    [SerializeField] private bool drawCornerHandles = true;

    [Tooltip("控制点小球半径。")]
    [SerializeField, Min(0f)] private float cornerHandleRadius = 0.15f;

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (alwaysDraw)
            DrawVolume(false);
    }

    private void OnDrawGizmosSelected()
    {
        DrawVolume(true);
    }

    private void DrawVolume(bool selected)
    {
        if (!TryGetBounds(out var min, out var max)) return;

        Vector3 size = max - min;
        Vector3 center = (min + max) * 0.5f;

        // 8 个顶点（世界轴对齐）
        Vector3 p000 = new Vector3(min.x, min.y, min.z);
        Vector3 p100 = new Vector3(max.x, min.y, min.z);
        Vector3 p010 = new Vector3(min.x, max.y, min.z);
        Vector3 p110 = new Vector3(max.x, max.y, min.z);
        Vector3 p001 = new Vector3(min.x, min.y, max.z);
        Vector3 p101 = new Vector3(max.x, min.y, max.z);
        Vector3 p011 = new Vector3(min.x, max.y, max.z);
        Vector3 p111 = new Vector3(max.x, max.y, max.z);

        // 填充面（Gizmos 无 DrawQuad，用交叉线簇近似半透明填充）
        if (faceColor.a > 0f)
        {
            var fc = faceColor;
            if (!selected) fc.a *= 0.6f;
            Gizmos.color = fc;
            DrawQuad(p000, p100, p110, p010); // 底
            DrawQuad(p001, p101, p111, p011); // 顶
            DrawQuad(p000, p100, p101, p001); // 前
            DrawQuad(p010, p110, p111, p011); // 后
            DrawQuad(p000, p010, p011, p001); // 左
            DrawQuad(p100, p110, p111, p101); // 右
        }

        // 棱
        Color ec = edgeColor;
        if (!selected) ec.a *= 0.75f;
        Gizmos.color = ec;
        // 底面
        Gizmos.DrawLine(p000, p100);
        Gizmos.DrawLine(p100, p110);
        Gizmos.DrawLine(p110, p010);
        Gizmos.DrawLine(p010, p000);
        // 顶面
        Gizmos.DrawLine(p001, p101);
        Gizmos.DrawLine(p101, p111);
        Gizmos.DrawLine(p111, p011);
        Gizmos.DrawLine(p011, p001);
        // 立柱
        Gizmos.DrawLine(p000, p001);
        Gizmos.DrawLine(p100, p101);
        Gizmos.DrawLine(p010, p011);
        Gizmos.DrawLine(p110, p111);

        // 三个控制点小球
        if (drawCornerHandles && cornerHandleRadius > 0f)
        {
            if (pointA != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawSphere(pointA.position, cornerHandleRadius);
            }
            if (pointB != null)
            {
                Gizmos.color = new Color(1f, 0.6f, 0.2f);
                Gizmos.DrawSphere(pointB.position, cornerHandleRadius);
            }
            if (pointC != null)
            {
                Gizmos.color = new Color(0.4f, 1f, 0.6f);
                Gizmos.DrawSphere(pointC.position, cornerHandleRadius);
            }
        }

        // 选中时在中心显示标签
        if (selected)
        {
            UnityEditor.Handles.color = edgeColor;
            UnityEditor.Handles.Label(
                center,
                $"Bake Volume (AABB)\nSize: {size.x:F2} × {size.y:F2} × {size.z:F2}"
            );
        }
    }

    /// <summary>
    /// 收集三个控制点世界坐标，逐分量 min/max 得到 AABB。
    /// 若三点没有全部有效，退化为使用能拿到的点（至少 1 个）。任何一个轴上厚度为 0 时仍然会画出退化盒。
    /// </summary>
    private bool TryGetBounds(out Vector3 min, out Vector3 max)
    {
        min = Vector3.zero;
        max = Vector3.zero;

        Transform a = pointA != null ? pointA : transform;
        Vector3? pa = a != null ? (Vector3?)a.position : null;
        Vector3? pb = pointB != null ? (Vector3?)pointB.position : null;
        Vector3? pc = pointC != null ? (Vector3?)pointC.position : null;

        Vector3 lo = Vector3.zero;
        Vector3 hi = Vector3.zero;
        bool has = false;

        if (pa.HasValue) { lo = pa.Value; hi = pa.Value; has = true; }
        if (pb.HasValue)
        {
            if (!has) { lo = pb.Value; hi = pb.Value; has = true; }
            else { lo = Vector3.Min(lo, pb.Value); hi = Vector3.Max(hi, pb.Value); }
        }
        if (pc.HasValue)
        {
            if (!has) { lo = pc.Value; hi = pc.Value; has = true; }
            else { lo = Vector3.Min(lo, pc.Value); hi = Vector3.Max(hi, pc.Value); }
        }

        min = lo;
        max = hi;
        return has;
    }

    /// <summary>
    /// Gizmos 没有内置画四边形，用交叉短线簇模拟半透明填充。
    /// </summary>
    private static void DrawQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        const int subdiv = 6;
        for (int i = 0; i <= subdiv; i++)
        {
            float t = i / (float)subdiv;
            Vector3 pAB = Vector3.Lerp(a, b, t);
            Vector3 pDC = Vector3.Lerp(d, c, t);
            Gizmos.DrawLine(pAB, pDC);

            Vector3 pAD = Vector3.Lerp(a, d, t);
            Vector3 pBC = Vector3.Lerp(b, c, t);
            Gizmos.DrawLine(pAD, pBC);
        }
    }
#endif
}
