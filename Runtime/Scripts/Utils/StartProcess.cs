using System;
using System.Diagnostics;
using UnityEngine;

public class StartProcess{
    public static int Do(Color color, string workingDirectory, string filename, string arguments, string tag, Action callback = null) {
#if BANTER_EDITOR && !UNITY_EDITOR && !ENABLE_MONO
        LogLine.Do("Launching banter-link with KS.Diagnostics");
        var process = new KS.Diagnostics.Process();
        process.StartInfo = new KS.Diagnostics.ProcessStartInfo();
#else
        LogLine.Do("Launching banter-link with System.Diagnostics");
        var process = new System.Diagnostics.Process();
        process.StartInfo = new System.Diagnostics.ProcessStartInfo();
#endif
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.WorkingDirectory = workingDirectory;
        process.StartInfo.FileName = filename;
        process.StartInfo.Arguments = arguments;
        process.OutputDataReceived += (sender, e) => LogLine.Do(color, tag, e.Data);
        process.ErrorDataReceived += (sender, e) => LogLine.Do(color, tag, e.Data);
        process.Exited += new EventHandler((sender, e) => callback?.Invoke());
        bool started = process.Start();
        process.EnableRaisingEvents = true;
        if (!started) {
            throw new InvalidOperationException("Could not start process: " + process);
        }
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
#if ENABLE_MONO
        // Test to see if changing priority helps with steam startup - not available in KS.Diag
        process.PriorityClass = ProcessPriorityClass.RealTime;
        process.PriorityBoostEnabled = true;
#endif
        return process.Id;
    }
}