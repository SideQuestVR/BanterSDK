// <mirror source="Packages/com.sidequest.packetparty/Runtime/Transforms/PacketPartyTransformRuntime.cs" sha256="11d524870f00cdd38eecad4647167f8d0cd40f0f4cc688894dba057a069fafe9" mode="port" />
// The scheduling half of PacketPartyTransformRuntime for synced objects: one Tick per frame over every local
// publisher while the room is joined (PacketPartyClient.Update, :5475-5480 ticks it while Connected), Reset on
// disconnect, and the receive routing of accepted frames to their view. Descriptors, the control plane and the binary
// transform-v6 codec are replaced by the relay's tf.objects lane (ILocalSession.QueueObjectFrame /
// ObjectFrameReceived); a publisher restart bumps an epoch instead of registering a new descriptor.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace BS.LocalMultiplayer.Objects
{
    /// <summary>
    /// Ticks the synced objects' publishers and routes received object frames to the component bound to each id.
    /// Owned by <see cref="SyncedObjectsModule"/>; main thread only.
    /// </summary>
    public sealed class LocalTransformRuntime : IDisposable
    {
        readonly ILocalSession _session;
        readonly LocalObjectBinder _binder;
        readonly List<LocalSyncedTransform> _publishers = new List<LocalSyncedTransform>();
        readonly HashSet<LocalSyncedTransform> _transforms = new HashSet<LocalSyncedTransform>();
        readonly List<LocalSyncedTransform> _scratch = new List<LocalSyncedTransform>();
        readonly Action<string, ObjectFrame> _onFrame;
        int _nextEpoch;
        bool _disposed;

        public long FramesSent { get; private set; }
        public long FramesReceived { get; private set; }
        public long FramesRejected { get; private set; }

        public LocalTransformRuntime(ILocalSession session, LocalObjectBinder binder)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _binder = binder ?? throw new ArgumentNullException(nameof(binder));
            _onFrame = HandleFrame;
            _session.ObjectFrameReceived += _onFrame;
            _binder.Disconnected += Reset;
        }

        /// <summary>Publishers may start: the room is joined (production: IsTransformTransportReady).</summary>
        public bool IsTransportReady => !_disposed && _binder.IsJoined;

        public int PublisherCount => _publishers.Count;

        /// <summary>A fresh stream identity for a starting publisher; never reused while local multiplayer runs.</summary>
        internal int NextEpoch() => ++_nextEpoch;

        internal void Register(LocalSyncedTransform syncTransform)
        {
            if (syncTransform != null) _transforms.Add(syncTransform);
        }

        internal void Unregister(LocalSyncedTransform syncTransform)
        {
            if (ReferenceEquals(syncTransform, null)) return;
            _transforms.Remove(syncTransform);
            _publishers.Remove(syncTransform);
        }

        internal void AddPublisher(LocalSyncedTransform syncTransform)
        {
            if (syncTransform != null && !_publishers.Contains(syncTransform)) _publishers.Add(syncTransform);
        }

        internal void RemovePublisher(LocalSyncedTransform syncTransform)
        {
            _publishers.Remove(syncTransform);
        }

        /// <summary>Send every due publisher's frame. Returns how many frames were queued.</summary>
        public int Tick(double captureTimestampMs)
        {
            if (!IsTransportReady) return 0;
            int sent = 0;
            for (int i = _publishers.Count - 1; i >= 0; i--)
            {
                var publisher = _publishers[i];
                if (publisher == null)
                {
                    _publishers.RemoveAt(i);
                    continue;
                }
                try
                {
                    if (publisher.TryPublish(captureTimestampMs, _session)) sent++;
                }
                catch (Exception ex)
                {
                    // One broken publisher must not stop the others from ticking.
                    Debug.LogException(ex, publisher);
                }
            }
            FramesSent += sent;
            return sent;
        }

        void HandleFrame(string fromRoomSessionId, ObjectFrame frame)
        {
            if (_disposed || string.IsNullOrEmpty(frame.ObjectId)) return;
            if (!_binder.TryGetBound(frame.ObjectId, out var networkObject))
            {
                FramesRejected++;
                return;
            }
            var syncTransform = networkObject.SyncTransform;
            if (syncTransform == null)
            {
                FramesRejected++;
                return;
            }
            FramesReceived++;
            syncTransform.ReceiveFrame(fromRoomSessionId, in frame);
        }

        /// <summary>The session ended: every publisher stops and every remote stream is forgotten (production Transforms.Reset).</summary>
        public void Reset()
        {
            _scratch.Clear();
            _scratch.AddRange(_transforms);
            foreach (var syncTransform in _scratch)
            {
                if (syncTransform != null) syncTransform.ResetForDisconnect();
            }
            _scratch.Clear();
            _publishers.Clear();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _session.ObjectFrameReceived -= _onFrame;
            _binder.Disconnected -= Reset;
            _publishers.Clear();
            _transforms.Clear();
        }
    }
}
