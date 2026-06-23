using System;
using UnityEngine;

/// <summary>
/// 根据玩家的相对高度切换 StyleTransitionController 中的风格预设。
/// </summary>
public class AltitudeStyleSwitcher : MonoBehaviour
{
    [Serializable]
    public class AltitudeStyleBand
    {
        [Tooltip("StyleTransitionController 中配置的 Preset ID。")]
        public string presetId;

        [Tooltip("进入该风格层的最低相对高度。")]
        public float minHeight;
    }

    [Header("References")]

    [SerializeField]
    private Transform target;

    [Tooltip("高度基准。为空时使用世界坐标 Y=0。")]
    [SerializeField]
    private Transform heightOrigin;

    [SerializeField]
    private StyleTransitionController
        styleController;

    [Header("Altitude Bands")]

    [Tooltip("必须按 Min Height 从低到高排列。")]
    [SerializeField]
    private AltitudeStyleBand[] bands =
    {
        new AltitudeStyleBand
        {
            presetId = "Low",
            minHeight = 0f
        },

        new AltitudeStyleBand
        {
            presetId = "Middle",
            minHeight = 50f
        },

        new AltitudeStyleBand
        {
            presetId = "High",
            minHeight = 100f
        }
    };

    [Header("Transition")]

    [SerializeField, Min(0.01f)]
    private float transitionDuration = 1f;

    [Tooltip("跨越边界后还需要额外移动的高度，防止临界位置反复切换。")]
    [SerializeField, Min(0f)]
    private float hysteresis = 3f;

    [SerializeField, Min(0.01f)]
    private float checkInterval = 0.1f;

    [SerializeField]
    private bool applyOnStart = true;

    private int currentBandIndex = -1;
    private float nextCheckTime;

    public float CurrentRelativeHeight
    {
        get
        {
            if (target == null)
                return 0f;

            float originY =
                heightOrigin != null
                    ? heightOrigin.position.y
                    : 0f;

            return
                target.position.y -
                originY;
        }
    }

    private void Start()
    {
        if (target == null)
            target = transform;

        if (bands == null ||
            bands.Length == 0)
        {
            return;
        }

        currentBandIndex =
            FindInitialBand(
                CurrentRelativeHeight
            );

        if (applyOnStart)
            ApplyBand(currentBandIndex);
    }

    private void Update()
    {
        if (target == null ||
            styleController == null ||
            bands == null ||
            bands.Length == 0)
        {
            return;
        }

        if (Time.unscaledTime <
            nextCheckTime)
        {
            return;
        }

        nextCheckTime =
            Time.unscaledTime +
            checkInterval;

        int nextBand =
            EvaluateBandWithHysteresis(
                CurrentRelativeHeight,
                currentBandIndex
            );

        if (nextBand ==
            currentBandIndex)
        {
            return;
        }

        currentBandIndex =
            nextBand;

        ApplyBand(currentBandIndex);
    }

    private int FindInitialBand(
        float height)
    {
        int result = 0;

        for (int i = 1;
             i < bands.Length;
             i++)
        {
            if (height <
                bands[i].minHeight)
            {
                break;
            }

            result = i;
        }

        return result;
    }

    private int EvaluateBandWithHysteresis(
        float height,
        int index)
    {
        index = Mathf.Clamp(
            index,
            0,
            bands.Length - 1
        );

        // 上升时必须超过：
        // 下一层高度 + hysteresis。
        while (
            index + 1 < bands.Length &&
            height >=
            bands[index + 1].minHeight +
            hysteresis)
        {
            index++;
        }

        // 下降时必须低于：
        // 当前层高度 - hysteresis。
        while (
            index > 0 &&
            height <
            bands[index].minHeight -
            hysteresis)
        {
            index--;
        }

        return index;
    }

    private void ApplyBand(int index)
    {
        if (styleController == null ||
            index < 0 ||
            index >= bands.Length)
        {
            return;
        }

        string presetId =
            bands[index].presetId;

        if (string.IsNullOrWhiteSpace(
                presetId))
        {
            return;
        }

        styleController.TransitionToPreset(
            presetId,
            transitionDuration
        );
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (bands == null)
            return;

        for (int i = 1;
             i < bands.Length;
             i++)
        {
            if (bands[i] == null ||
                bands[i - 1] == null)
            {
                continue;
            }

            if (bands[i].minHeight <
                bands[i - 1].minHeight)
            {
                Debug.LogWarning(
                    "[AltitudeStyleSwitcher] " +
                    "Bands 必须按 Min Height 从低到高排列。",
                    this
                );

                break;
            }
        }
    }
#endif
}