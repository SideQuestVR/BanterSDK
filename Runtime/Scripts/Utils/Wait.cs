using System;
using System.Threading.Tasks;
using UnityEngine;

public class Wait
{
    public static async Task Until(Func<bool> condition, int frequency = 25, int timeout = -1)
    {
        UnityEngine.Debug.Log("Wait.Until");
        var waitTask = Banter.SDK.TaskRunner.Run(async () =>
        {
            while (!condition())
            {
                UnityEngine.Debug.Log("Wait.Until(() => condition())");
                await new WaitForSeconds(frequency/1000f);
            }
            UnityEngine.Debug.Log("Wait.Until(() => condition())");
        }, $"{nameof(Wait)}.{nameof(Until)}");

        if (waitTask != await Task.WhenAny(waitTask,
                Task.Delay(timeout)))
            throw new TimeoutException();

        UnityEngine.Debug.Log("Wait.Until(() => condition())");
    }
    public static async Task While(Func<bool> condition, int frequency = 1, int timeout = -1)
    {
        var waitTask = Banter.SDK.TaskRunner.Run(async () =>
        {
            while (condition()) await Task.Delay(frequency);
        }, $"{nameof(Wait)}.{nameof(While)}");

        if (waitTask != await Task.WhenAny(waitTask, Task.Delay(timeout)))
            throw new TimeoutException();
    }

}