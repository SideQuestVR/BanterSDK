using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine.UIElements;

public class Status
{
    public List<string> statusMessages = new List<string>();
    Label statusBar;
    ListView buildProgress;
    ProgressBar buildProgressBar;

    string logFile;

    public Status(Label statusBar, ListView buildProgress, ProgressBar buildProgressBar)
    {
        this.statusBar = statusBar;
        this.buildProgress = buildProgress;
        this.buildProgressBar = buildProgressBar;
        logFile = DateTime.Now.ToString("yyyy-MM-dd") + "_BanterBuilder.log";
        if (File.Exists(logFile))
        {
            var lines = File.ReadAllLines(logFile).Select(line => line.Trim()).Where(line => !string.IsNullOrEmpty(line)).ToArray();
            var max = lines.Length > 300 ? 300 : lines.Length;
            for (int i = 0; i < max; i++)
            {
                var parts = lines[i].Split(new[] { ":::" }, StringSplitOptions.None);
                AddStatus(parts[0], parts.Length > 1 ? parts[1] : null, true);
            }
        }
    }
    public void AddStatus(string text, string dateString = null, bool skipWrite = false)
    {
        var val = "<color=#999999>" + (dateString == null ? DateTime.Now.ToString("HH:mm:ss") : dateString) + ": <color=#FFFFFF>" + text;
        statusMessages.Insert(0, val);
        statusBar.text = "STATUS: " + val;
        if (statusMessages.Count > 300)
        {
            // Trim IN PLACE — reassigning the field (GetRange returns a new list) orphans the
            // ListView's itemsSource, which is bound once to this exact list, so nothing new would
            // render after the first time we cross 300 (e.g. a large replayed log file).
            statusMessages.RemoveRange(300, statusMessages.Count - 300);
        }

        buildProgress.Rebuild();
        if (!skipWrite)
        {
            File.AppendAllLines(logFile, new string[] { text + ":::" + DateTime.Now.ToString("HH:mm:ss") }); 
        }
    }
    public void ClearLogs()
    {
        if (File.Exists(logFile))
        {
            File.Delete(logFile);
        }
        statusMessages.Clear();
        buildProgress.Rebuild();
    }
    /// <summary>
    /// Show the in-window upload bar. <paramref name="pct"/> is 0-100.
    /// Must be called from the main thread.
    /// </summary>
    public void ShowProgress(string title, float pct)
    {
        buildProgressBar.style.display = DisplayStyle.Flex;
        buildProgressBar.title = title;
        buildProgressBar.value = UnityEngine.Mathf.Clamp(pct, 0f, 100f);
    }

    public void HideProgressBar()
    {
        buildProgressBar.style.display = DisplayStyle.None;
        buildProgressBar.value = 0f;
    }
}