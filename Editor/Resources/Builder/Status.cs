using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

public class Status
{
    List<string> statusMessages = new List<string>();
    Label statusBar;
    ListView buildProgress;
    ProgressBar buildProgressBar;

    public Status(Label statusBar, ListView buildProgress, ProgressBar buildProgressBar)
    {
        this.statusBar = statusBar;
        this.buildProgress = buildProgress;
        this.buildProgressBar = buildProgressBar;
    }
    public void AddStatus(string text)
    {
        var val = "<color=\"orange\">" + DateTime.Now.ToString("HH:mm:ss") + ": <color=\"white\">" + text;
        statusMessages.Insert(0, val);
        statusBar.text = "STATUS: " + val;
        if (statusMessages.Count > 300)
        {
            statusMessages = statusMessages.GetRange(0, 300);
        }

        buildProgress.Rebuild();
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