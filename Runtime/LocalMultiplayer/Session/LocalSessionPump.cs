using System;
using UnityEngine;

namespace BS.LocalMultiplayer
{
    /// <summary>
    /// Hands the frame's pose and object transforms to the socket after every module has written them. The
    /// host drains the inbound queue first thing in the frame (execution order -950); this runs last, so a
    /// frame queued in any Update or LateUpdate leaves in the same frame.
    /// </summary>
    [DefaultExecutionOrder(32000)]
    [AddComponentMenu("")]
    internal sealed class LocalSessionPump : MonoBehaviour
    {
        [NonSerialized] internal LocalSession Session;

        void LateUpdate()
        {
            var session = Session;
            if (session == null) return;
            try
            {
                session.FlushOutgoing();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}
