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
    private void OnDrawGizmosSelected()
    {
        Vector3 direction = GetWindDirection();

        if (direction == Vector3.zero)
            return;

        Gizmos.DrawLine(
            transform.position,
            transform.position +
            direction * Mathf.Max(windStrength, 1f)
        );
    }
#endif
}