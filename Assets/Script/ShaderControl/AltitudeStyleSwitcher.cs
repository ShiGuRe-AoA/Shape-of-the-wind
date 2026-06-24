using System;
using UnityEngine;

/// <summary>
/// 根据玩家相对高度切换 StyleTransitionController 中的风格预设。
///
/// 包含：
/// 1. 高度滞回，避免边界附近反复切换；
/// 2. 区域稳定时间，避免快速经过多个区域时连续触发；
/// 3. 一次跨过多个高度层时直接选择最终层级。
/// </summary>
public class AltitudeStyleSwitcher : MonoBehaviour
{
    [Serializable]
    public class AltitudeStyleBand
    {
        [Tooltip("StyleTransitionController 中对应的 Preset ID。")]
        public string presetId;

        [Tooltip("该高度层开始生效的最低相对高度。")]
        public float minHeight;
    }

    [Header("References")]

    [Tooltip("需要检测高度的玩家。")]
    [SerializeField]
    private Transform target;

    [Tooltip("高度基准。为空时使用世界坐标 Y=0。")]
    [SerializeField]
    private Transform heightOrigin;

    [SerializeField]
    private StyleTransitionController styleController;

    [Header("Altitude Bands")]

    [Tooltip("必须按照 Min Height 从低到高排列。")]
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
            minHeight = 5f
        },

        new AltitudeStyleBand
        {
            presetId = "HighMiddle",
            minHeight = 10f
        },

        new AltitudeStyleBand
        {
            presetId = "High",
            minHeight = 15f
        },

        new AltitudeStyleBand
        {
            presetId = "HighHigh",
            minHeight = 20f
        }
    };

    [Header("Transition")]

    [SerializeField, Min(0.01f)]
    private float transitionDuration = 1f;

    [Tooltip(
        "跨越高度边界后还需要额外移动的距离，" +
        "用于防止边界附近反复切换。"
    )]
    [SerializeField, Min(0f)]
    private float hysteresis = 1f;

    [Tooltip("高度检测间隔。")]
    [SerializeField, Min(0.01f)]
    private float checkInterval = 0.05f;

    [Tooltip(
        "进入新高度区域后，需要在该区域稳定停留多久才切换。"
    )]
    [SerializeField, Min(0f)]
    private float bandStableTime = 0.15f;

    [Tooltip("启动时立即应用当前高度对应的风格。")]
    [SerializeField]
    private bool applyOnStart = true;

    [Header("Debug")]

    [SerializeField]
    private bool showDebugLog;

    private int currentBandIndex = -1;

    // 当前正在等待确认的候选高度层。
    private int candidateBandIndex = -1;

    private float candidateStartTime;
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

        if (styleController == null)
        {
            Debug.LogError(
                "[AltitudeStyleSwitcher] " +
                "StyleTransitionController 没有设置。",
                this
            );

            enabled = false;
            return;
        }

        if (bands == null ||
            bands.Length == 0)
        {
            Debug.LogError(
                "[AltitudeStyleSwitcher] " +
                "没有配置高度风格区域。",
                this
            );

            enabled = false;
            return;
        }

        currentBandIndex =
            FindInitialBand(
                CurrentRelativeHeight
            );

        candidateBandIndex = -1;

        if (applyOnStart)
        {
            ApplyBand(
                currentBandIndex
            );
        }
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

        float currentHeight =
            CurrentRelativeHeight;

        int detectedBandIndex =
            EvaluateBandWithHysteresis(
                currentHeight,
                currentBandIndex
            );

        // 仍然处于当前高度层。
        // 清除之前等待中的候选区域。
        if (detectedBandIndex ==
            currentBandIndex)
        {
            candidateBandIndex = -1;
            return;
        }

        // 第一次检测到新的候选高度层，
        // 开始计算稳定停留时间。
        if (candidateBandIndex !=
            detectedBandIndex)
        {
            candidateBandIndex =
                detectedBandIndex;

            candidateStartTime =
                Time.unscaledTime;

            if (showDebugLog)
            {
                Debug.Log(
                    $"[AltitudeStyleSwitcher] " +
                    $"检测到候选区域：{GetPresetId(candidateBandIndex)}，" +
                    $"Height={currentHeight:F2}",
                    this
                );
            }

            return;
        }

        // 尚未在候选区域停留足够长的时间。
        if (Time.unscaledTime -
            candidateStartTime <
            bandStableTime)
        {
            return;
        }

        // 候选区域已经稳定，
        // 正式切换当前区域。
        currentBandIndex =
            candidateBandIndex;

        candidateBandIndex = -1;

        ApplyBand(
            currentBandIndex
        );
    }

    /// <summary>
    /// 查找启动时所在的高度层。
    /// </summary>
    private int FindInitialBand(
        float height)
    {
        int result = 0;

        for (int i = 1;
             i < bands.Length;
             i++)
        {
            if (bands[i] == null)
                continue;

            if (height <
                bands[i].minHeight)
            {
                break;
            }

            result = i;
        }

        return result;
    }

    /// <summary>
    /// 根据当前区域和滞回值计算目标区域。
    ///
    /// 支持一次跨越多个区域，例如：
    /// Low 直接移动到 High 时，会直接返回 High。
    /// </summary>
    private int EvaluateBandWithHysteresis(
        float height,
        int index)
    {
        index = Mathf.Clamp(
            index,
            0,
            bands.Length - 1
        );

        // 玩家向上移动。
        //
        // 需要超过：
        // 下一层 Min Height + Hysteresis
        while (
            index + 1 < bands.Length &&
            bands[index + 1] != null &&
            height >=
            bands[index + 1].minHeight +
            hysteresis)
        {
            index++;
        }

        // 玩家向下移动。
        //
        // 需要低于：
        // 当前层 Min Height - Hysteresis
        while (
            index > 0 &&
            bands[index] != null &&
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
            index >= bands.Length ||
            bands[index] == null)
        {
            return;
        }

        string presetId =
            bands[index].presetId;

        if (string.IsNullOrWhiteSpace(
                presetId))
        {
            Debug.LogWarning(
                $"[AltitudeStyleSwitcher] " +
                $"高度层 {index} 没有设置 Preset ID。",
                this
            );

            return;
        }

        styleController.TransitionToPreset(
            presetId,
            transitionDuration
        );

        if (showDebugLog)
        {
            Debug.Log(
                $"[AltitudeStyleSwitcher] " +
                $"切换到区域 {index}，" +
                $"Preset={presetId}，" +
                $"Height={CurrentRelativeHeight:F2}",
                this
            );
        }
    }

    private string GetPresetId(int index)
    {
        if (index < 0 ||
            index >= bands.Length ||
            bands[index] == null)
        {
            return "Invalid";
        }

        return bands[index].presetId;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        transitionDuration =
            Mathf.Max(
                transitionDuration,
                0.01f
            );

        checkInterval =
            Mathf.Max(
                checkInterval,
                0.01f
            );

        hysteresis =
            Mathf.Max(
                hysteresis,
                0f
            );

        bandStableTime =
            Mathf.Max(
                bandStableTime,
                0f
            );

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
                    "Bands 必须按照 Min Height 从低到高排列。",
                    this
                );

                break;
            }
        }
    }
#endif
}