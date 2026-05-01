using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WindProbe : MonoBehaviour
{
    public Vector3 windDirection = new Vector3(0, 0, 0);
    public float windStrength;

    public Vector3 GetWindDirection()
    {
        return windDirection;
    }

    public float GetWindStrength()
    {
        return windStrength;
    }
}
