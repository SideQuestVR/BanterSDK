// <mirror source="Packages/com.sidequest.packetparty/Runtime/Util/UnityMainThread.cs" sha256="52cd72b8154be5dd9a1d7df4f9f007ca528de22e6f083d3d20c4a53deef5bf98" mode="port" />
// UnityMainThread.Post/Update (a queue drained on the main thread, at most 256 actions a frame, each one guarded),
// owned by a state module instead of a hidden static GameObject, so nothing posted survives the module.
using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace BS.LocalMultiplayer.State
{
    /// <summary>
    /// Where the ported state code posts the continuations of its relay requests, as production posts to
    /// PacketParty's UnityMainThread: <see cref="Post"/> is safe from any thread, <see cref="Drain"/> runs on the main
    /// thread from the owning module's Update. <see cref="Close"/> drops everything still queued and ignores later
    /// posts, so a request that completes after play stops (or after a script reload) touches nothing.
    /// </summary>
    public sealed class MainThreadQueue
    {
        // Drain at most ~256 queued actions per frame to bound worst-case frame cost.
        private const int MaxPerFrame = 256;

        private readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();
        private volatile bool _closed;

        public int Count => _queue.Count;

        public void Post(Action action)
        {
            if (action == null || _closed) return;
            _queue.Enqueue(action);
        }

        public void Drain()
        {
            for (int i = 0; i < MaxPerFrame; i++)
            {
                if (_closed || !_queue.TryDequeue(out var action)) break;
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
        }

        public void Close()
        {
            _closed = true;
            while (_queue.TryDequeue(out _))
            {
            }
        }
    }
}
