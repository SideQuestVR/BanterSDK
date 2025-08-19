// using System;
// #if ENABLE_IL2CPP
// using GoodAI.Core.Diagnostics;
// #else
// using System.Diagnostics;
// #endif
// using UnityEngine;

// public class ProcessManager
// {
//         static Process process;
//         public static void Kill()
//         {
//                 if (process != null && !process.HasExited)
//                 {
//                         try
//                         {
//                                 process.Kill();
//                                 process.Dispose();
//                                 process = null;
//                         }
//                         catch (Exception ex)
//                         {
//                                 UnityEngine.Debug.LogError($"Failed to kill process: {ex.Message}");
//                         }
//                 }
//         }
//         public static void Start(string workingDirectory, string filename, string arguments, Action<string, bool> logAction = null)
//         {
//                 if(process != null)
//                 {
//                         Kill();
//                 }
//                 process = new Process();
//                 process.StartInfo = new ProcessStartInfo();

//                 process.StartInfo.UseShellExecute = false;
//                 process.StartInfo.RedirectStandardOutput = true;
//                 process.StartInfo.RedirectStandardError = true;
//                 process.StartInfo.WorkingDirectory = workingDirectory;
//                 process.StartInfo.FileName = filename;
//                 process.StartInfo.Arguments = arguments;
//                 process.OutputDataReceived += (sender, e) =>
//                 {
//                         logAction?.Invoke(e.Data, false);
//                 };
//                 process.ErrorDataReceived += (sender, e) => logAction?.Invoke(e.Data, true);
//                 // process.Exited += new EventHandler((sender, e) => {
//                 // });
//                 bool started = process.Start();
//                 process.EnableRaisingEvents = true;
//                 if (!started)
//                 {
//                         throw new InvalidOperationException("Could not start process: " + process);
//                 }
//                 process.BeginOutputReadLine();
//                 process.BeginErrorReadLine();
//         }
// }