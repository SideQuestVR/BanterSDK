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
        var val = "<color=\"orange\">" + (dateString == null ? DateTime.Now.ToString("HH:mm:ss") : dateString) + ": <color=\"white\">" + text;
        statusMessages.Insert(0, val);
        statusBar.text = "STATUS: " + val;
        if (statusMessages.Count > 300)
        {
            statusMessages = statusMessages.GetRange(0, 300);
        }

        buildProgress.Rebuild();
        if (!skipWrite)
        {
            File.AppendAllLines(logFile, new string[] { text + ":::" + DateTime.Now.ToString("HH:mm:ss") }); 
        }
    }
    public void ClearLogs()
    {
        statusMessages.Clear();
        buildProgress.Rebuild();
    }
    public void HideProgressBar()
    {
        buildProgressBar.style.display = DisplayStyle.None;
    }
}