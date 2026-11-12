// Probe app start-up: logs the graphics and XR state, then runs the native import test on Unity's device.
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.XR;

public class VProbe : MonoBehaviour
{
    [DllImport("vprobe")] static extern int VProbe_RunTest();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() => DontDestroyOnLoad(new GameObject("VProbe").AddComponent<VProbe>());

    IEnumerator Start()
    {
        yield return new WaitForSeconds(2f);
        Debug.Log($"[VProbe] graphics {SystemInfo.graphicsDeviceType} '{SystemInfo.graphicsDeviceVersion}' " +
                  $"gpu '{SystemInfo.graphicsDeviceName}', XR active {XRSettings.isDeviceActive} " +
                  $"device '{XRSettings.loadedDeviceName}' stereo {XRSettings.stereoRenderingMode}");
        int r = VProbe_RunTest();
        Debug.Log($"[VProbe] test returned {r}");
    }
}
