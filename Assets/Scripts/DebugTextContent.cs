using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class DebugTextContent : MonoBehaviour
{
    public Text DebugTextPrefab;
    public ScrollRect ScrollRect;
    public bool EnableStacktraces = false;
    public LogType LogLevel = LogType.Error;

    private bool _wasAtBottom = true;    

    private static string GetLogColor(LogType type)
    {
        switch (type)
        {
            case LogType.Log:
                return "#ffffffff";

            case LogType.Warning:
                return "#ffff44ff";

            case LogType.Assert:
                return "#ff7733ff";

            case LogType.Error:
                return "#ff2222ff";

            case LogType.Exception:
                return "#ff44ffff";

            default:
                return "#ffffffff";
        }
    }

    private bool ShouldLog(LogType type)
    {
        switch (type)
        {
            case LogType.Log:
                return LogLevel == LogType.Log;
            case LogType.Warning:
                return LogLevel == LogType.Log || LogLevel == LogType.Warning;
            case LogType.Error:
                return LogLevel == LogType.Log || LogLevel == LogType.Warning || LogLevel == LogType.Error;
            case LogType.Assert:
                return LogLevel == LogType.Log || LogLevel == LogType.Warning || LogLevel == LogType.Error || LogLevel == LogType.Assert;
            case LogType.Exception:
                return true;
            default:
                return false;
        }
    }

    private void OnEnable()
    {
        Application.logMessageReceived += HandleLog;
    }

    private void OnDisable()
    {
        Application.logMessageReceived -= HandleLog;
    }

    private void Update()
    {
        _wasAtBottom = ScrollRect.verticalNormalizedPosition <= 0.001f;
    }

    private void HandleLog(string logString, string stackTrace, LogType type)
    {
        if (ShouldLog(type))
        {
            Text newText = Instantiate(DebugTextPrefab);
            if (EnableStacktraces)
            {
                newText.text = "<color=" + GetLogColor(type) + ">" + logString.TrimEnd('\n') + "\n" + stackTrace.TrimEnd('\n') + "</color>";
            }
            else
            {
                newText.text = "<color=" + GetLogColor(type) + ">" + logString.TrimEnd('\n') + "</color>";
            }
            newText.transform.SetParent(transform, false);

            if (_wasAtBottom)
            {
                Canvas.ForceUpdateCanvases();
                ScrollRect.verticalNormalizedPosition = 0.0f;
                Canvas.ForceUpdateCanvases();
            }
        }
    }

    public void SetEnableStacktraces(bool enableStacktraces)
    {
        EnableStacktraces = enableStacktraces;
    }

    public void SetLogLevel(int logLevel)
    {
        LogLevel = (LogType)logLevel;

        throw new System.Exception();
    }

    public void Clear()
    {
        List<GameObject> children = new List<GameObject>();
        foreach (Transform child in transform)
        {
            children.Add(child.gameObject);
        }

        children.ForEach(child => Destroy(child));
    }
}
