using UnityEngine;

namespace BS
{
    /// <summary>
    /// Keeps a worldspace Canvas's worldCamera bound to Camera.main. A one-shot assignment at
    /// load silently kills pointer input when it races camera activation (seen on Quest: the XR
    /// camera can be enabled/tagged after space canvases are configured): TrackedDeviceRaycaster
    /// returns zero hits forever while eventCamera is null — no hover, no clicks. Re-binding
    /// every frame is trivially cheap (Camera.main is cached) and also heals camera swaps.
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    public class CanvasCameraBinder : MonoBehaviour
    {
        Canvas _canvas;

        void Awake() => _canvas = GetComponent<Canvas>();

        void LateUpdate()
        {
            if (_canvas.renderMode != RenderMode.WorldSpace)
                return;

            var main = Camera.main;
            if (main != null && _canvas.worldCamera != main)
                _canvas.worldCamera = main;
        }
    }
}
