using System;
using UnityEngine;

public class LogTag
{
    public const string Banter = "[Banter]";
    public const string BanterBrowser = "[BanterBrowsr]";
    public const string API = "[API]";
    public const string ElectronCompiler = "[ElectronCompiler]";
    public const string InjectionCompiler = "[InjectionCompiler]";
}

public class LogLine
{

    public static Color browserColor = new Color(1, 127f / 255f, 80f / 255f);
    public static Color banterColor = new Color(125f / 255f, 249f / 255f, 1f);
    public static Color injectionColor = new Color(1, 93f / 255f, 143f / 255f);
    public static Color electronColor = new Color(62f / 255f, 180f / 255f, 137f / 255f);

    public static void Err(string line)
    {
        Do(Color.red, LogTag.Banter, line);
    }
    public static void Do(string line)
    {
        Do(banterColor, LogTag.Banter, line);
    }

    public static void Do(Color color, string tag, string line)
    {
        if (!string.IsNullOrEmpty(line))
        {
#if UNITY_EDITOR
                Debug.Log(string.Format("<color=#4f4f4f>" + tag + "</color> <color=#{0:X2}{1:X2}{2:X2}>{3}</color>", (byte)(color.r * 255f), (byte)(color.g * 255f), (byte)(color.b * 255f), line));
#elif LOGLINE
                Debug.Log(string.Format(DateTime.Now.ToString("HH:mm:ss.fff") + ": " + tag + " {0}", line));
#endif
            //Console.WriteLine(string.Format(tag + " " + line));
        }
    }
}