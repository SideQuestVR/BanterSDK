using System;
using System.Collections.Generic;
using UnityEngine;

namespace BS.LocalMultiplayer.Attachments
{
    /// <summary>
    /// What the attachment module tells the creator, through the host's diagnostics (overlay and window) and the
    /// console. Two kinds: warnings that stay while their condition holds (an instruction waiting for an object that
    /// doesn't exist here, an Id the room refuses), and one-time notes for production behaviour that looks like a
    /// bug but is mirrored on purpose, each citing the Greenfield lines responsible. Main thread only.
    /// </summary>
    internal sealed class AttachmentDiagnostics
    {
        public const string QuirkPrefix = "attach.quirk.";

        readonly ILocalDiagnostics _sink;
        readonly HashSet<string> _quirksNoted = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> _active = new HashSet<string>(StringComparer.Ordinal);

        public AttachmentDiagnostics(ILocalDiagnostics sink)
        {
            _sink = sink;
        }

        /// <summary>Notes a mirrored production quirk, once per play session.</summary>
        public void Quirk(string key, string message)
        {
            if (!_quirksNoted.Add(key))
            {
                return;
            }
            Debug.Log("[LocalMP][Attachments] " + message);
            Sink()?.Set(QuirkPrefix + key, DiagnosticLevel.Info, message);
        }

        /// <summary>Raises a warning that stays until <see cref="Clear"/>; repeated calls with the same key are free.</summary>
        public void Warn(string key, string message)
        {
            if (!_active.Add(key))
            {
                return;
            }
            Debug.LogWarning("[LocalMP][Attachments] " + message);
            Sink()?.Set(key, DiagnosticLevel.Warning, message);
        }

        public bool IsActive(string key) => _active.Contains(key);

        public void Clear(string key)
        {
            if (_active.Remove(key))
            {
                Sink()?.Clear(key);
            }
        }

        /// <summary>Removes every entry this module raised (the host is going away).</summary>
        public void ClearAll()
        {
            var sink = Sink();
            if (sink != null)
            {
                foreach (var key in _active)
                {
                    sink.Clear(key);
                }
                foreach (var key in _quirksNoted)
                {
                    sink.Clear(QuirkPrefix + key);
                }
            }
            _active.Clear();
            _quirksNoted.Clear();
        }

        // The sink may be a component that died before us (teardown order).
        ILocalDiagnostics Sink() => AttachmentServices.IsAlive(_sink) ? _sink : null;
    }

    /// <summary>Lookups of services other modules provide, which may be MonoBehaviours that have been destroyed.</summary>
    internal static class AttachmentServices
    {
        /// <summary>Non-null, and not a destroyed UnityEngine.Object behind an interface reference.</summary>
        public static bool IsAlive(object service) =>
            service != null && !(service is UnityEngine.Object unityObject && unityObject == null);

        public static T Get<T>(ILocalHost host) where T : class
        {
            if (host == null)
            {
                return null;
            }
            var service = host.Get<T>();
            return IsAlive(service) ? service : null;
        }
    }
}
