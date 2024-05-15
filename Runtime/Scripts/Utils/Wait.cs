using System;
using System.Threading.Tasks;

public class Wait{
    public static async Task Until(Func<bool> condition, int frequency = 25, int timeout = -1)
    {
        UnityEngine.Debug.Log("Wait.Until");
        var waitTask = Task.Run(async () =>
        {
            while (!condition()) {
            UnityEngine.Debug.Log("Wait.Until(() => condition())");
                await Task.Delay(frequency);
            }
            UnityEngine.Debug.Log("Wait.Until(() => condition())");
        });

        if (waitTask != await Task.WhenAny(waitTask, 
                Task.Delay(timeout))) 
            throw new TimeoutException();

        UnityEngine.Debug.Log("Wait.Until(() => condition())");
    }
    public static async Task While(Func<bool> condition, int frequency = 1, int timeout = -1)
    {
        var waitTask = Task.Run(async () =>
        {
            while (condition()) await Task.Delay(frequency);
        });

        if(waitTask != await Task.WhenAny(waitTask, Task.Delay(timeout)))
            throw new TimeoutException();
    }

}