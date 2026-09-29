#if !GREENFIELD_PROJECT && !BANTER_FLEX

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BS
{
    /// <summary>
    /// Lets <see cref="BSDesktopController"/> keep a mouse press away from the UI.
    /// </summary>
    /// <remarks>
    /// While the controller owns a press (a grab or a scene click that was nearer than any UI),
    /// this raycaster reports a top-priority hit on itself. The EventSystem then delivers that
    /// press to this object, which handles nothing, instead of to a canvas or panel further back
    /// along the same ray — uGUI world canvases are not blocked by 3D colliders on their own.
    /// Doing it here means the input module never has to be switched off and on.
    /// </remarks>
    [AddComponentMenu("")]
    public class BSDesktopPointerCapture : BaseRaycaster
    {
        Camera _camera;

        /// <summary>True while the controller owns the current press.</summary>
        public bool Capturing { get; set; }

        public override Camera eventCamera => _camera != null ? _camera : _camera = GetComponent<Camera>();

        // Sort order is compared before distance, so this wins over every other raycaster.
        public override int sortOrderPriority => Capturing ? int.MaxValue : int.MinValue;
        public override int renderOrderPriority => Capturing ? int.MaxValue : int.MinValue;

        public override void Raycast(PointerEventData eventData, List<RaycastResult> resultAppendList)
        {
            if (!Capturing)
                return;

            resultAppendList.Add(new RaycastResult
            {
                gameObject = gameObject,
                module = this,
                distance = 0f,
                screenPosition = eventData.position,
                displayIndex = eventCamera != null ? eventCamera.targetDisplay : 0,
            });
        }
    }
}

#endif
