// <mirror source="Packages/com.sidequest.packetparty/Runtime/PacketPartyClient.cs" sha256="6ab88cbf53c894dcdd8273685d80972419478499cfec5f09de9499a52b97367d" mode="port" />
// <mirror source="Packages/com.sidequest.packetparty/Runtime/Contracts/UserStateContracts.cs" sha256="0b91705d38f3faecc385f14751be09340607ef56dd83cd85c8e8288dee40988f" mode="verbatim" />
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace BS.LocalMultiplayer
{
    /// <summary>
    /// This player's engine user state: the root keys the attachments and seats write (attachment_&lt;Id&gt;,
    /// pilot), never shown to the page. A port of PacketPartyClient's EditableOwnUserState
    /// (PacketPartyClient.cs:343-354, 2924-3029, 5482-5488): a JSON document the engine edits freely, diffed
    /// every 50 ms against what the relay has and sent as atomic app.userState.batch chunks of at most 64
    /// operations, deletes first, both sorted. A chunk that fails keeps the chunks before it, and the same
    /// document is not retried until it changes (the failed fingerprint latch). Everything is pushed again
    /// on every new session; a resumed one keeps what the relay already has.
    /// </summary>
    internal sealed class EngineUserStateChannel
    {
        public const float SyncIntervalSeconds = 0.05f;
        public const int BatchSize = 64;

        // Sends one app.userState.batch body ({ops, atomic}) and resolves with the whole reply.
        readonly Func<JObject, CancellationToken, Task<JObject>> _sendBatch;

        readonly JObject _document = new JObject();
        readonly Dictionary<string, JToken> _baseline = new Dictionary<string, JToken>(StringComparer.Ordinal);
        Task _syncTask = Task.CompletedTask;
        string _failedFingerprint;
        float _nextSyncAt;
        // Bumped by every reset. A sync that was in flight when its session ended must not write its
        // half-applied baseline over the fresh session's empty one.
        int _generation;

        public EngineUserStateChannel(Func<JObject, CancellationToken, Task<JObject>> sendBatch)
        {
            _sendBatch = sendBatch ?? throw new ArgumentNullException(nameof(sendBatch));
        }

        /// <summary>The document as the engine wrote it (EditableOwnUserState).</summary>
        public JObject Document => _document;

        /// <summary>True while a diff is being sent.</summary>
        public bool Syncing => !_syncTask.IsCompleted;

        /// <summary>
        /// Sets a root key. "" is a value (the attachment bridges' "cleared"), not a delete, as production's
        /// SetLocalUserState; null removes the key, so the next diff deletes it on the relay.
        /// </summary>
        public void Set(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (value == null)
            {
                _document.Remove(key);
            }
            else
            {
                _document[key] = value;
            }
        }

        /// <summary>Every frame, from the session: the 50 ms auto-sync of PacketPartyClient.Update.</summary>
        public void Tick(bool connected, float unscaledTime, CancellationToken cancellation)
        {
            if (connected && _syncTask.IsCompleted && unscaledTime >= _nextSyncAt)
            {
                _nextSyncAt = unscaledTime + SyncIntervalSeconds;
                TryAutoSync(cancellation);
            }
        }

        /// <summary>A new session (production's TeardownAsync): the relay has nothing of ours, so send it all.</summary>
        public void ResetForNewSession()
        {
            _generation++;
            _baseline.Clear();
            _failedFingerprint = null;
            _nextSyncAt = 0f;
        }

        /// <summary>A resumed session (TeardownPartialAsync): the relay kept our state; only lift the latch.</summary>
        public void ResetForResume()
        {
            _failedFingerprint = null;
        }

        /// <summary>Diff and send now, retrying a latched document (SyncEditableOwnUserStateAsync).</summary>
        public Task SyncNowAsync(CancellationToken cancellation = default)
        {
            return StartSync(cancellation, retryFailedSnapshot: true);
        }

        async void TryAutoSync(CancellationToken cancellation)
        {
            try
            {
                await StartSync(cancellation, retryFailedSnapshot: false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (LocalRelayException e) when (e.Code == RelayProtocol.ErrorClosed || e.Code == RelayProtocol.ErrorNotConnected)
            {
                // The session ended under the sync; the next one pushes everything again.
            }
            catch (Exception e)
            {
                // Production raises OnError, which NetworkService logs as a warning.
                var code = e is LocalRelayException relay ? relay.Code : e.GetType().Name;
                Debug.LogWarning($"[LocalMP] Engine user state didn't sync [{code}] {e.Message}");
            }
        }

        Task StartSync(CancellationToken cancellation, bool retryFailedSnapshot)
        {
            if (!_syncTask.IsCompleted) return _syncTask;
            var target = FlatState.Flatten(_document);
            if (FlatState.FlatEquals(_baseline, target)) return Task.CompletedTask;
            var fingerprint = FlatState.Fingerprint(target);
            if (!retryFailedSnapshot && string.Equals(fingerprint, _failedFingerprint, StringComparison.Ordinal))
            {
                return Task.CompletedTask;
            }
            _syncTask = SyncCoreAsync(target, fingerprint, _generation, cancellation);
            return _syncTask;
        }

        async Task SyncCoreAsync(Dictionary<string, JToken> target, string targetFingerprint, int generation, CancellationToken cancellation)
        {
            var working = FlatState.CloneFlat(_baseline);
            var operations = BuildOperations(working, target);
            try
            {
                for (var offset = 0; offset < operations.Count; offset += BatchSize)
                {
                    var chunk = new JArray();
                    var end = Math.Min(offset + BatchSize, operations.Count);
                    for (var index = offset; index < end; index++) chunk.Add(operations[index]);
                    var reply = await _sendBatch(new JObject
                    {
                        ["ops"] = chunk,
                        ["atomic"] = true
                    }, cancellation);
                    // RequestUserStateAsync: a refused batch throws, with the server's code.
                    if (reply == null || reply.Value<bool?>("ok") != true)
                    {
                        var error = reply?.Value<string>("error");
                        throw new LocalRelayException(error ?? "user_state_error",
                            $"user state {RelayProtocol.UserStateBatch} failed: {error}");
                    }
                    for (var index = offset; index < end; index++)
                    {
                        ApplyOperation(working, operations[index]);
                    }
                }
                if (generation != _generation) return;
                ReplaceBaseline(target);
                _failedFingerprint = null;
            }
            catch
            {
                if (generation == _generation)
                {
                    // Preserve any earlier accepted chunks so a retry does not issue deletions for paths that
                    // are already gone on the server.
                    ReplaceBaseline(working);
                    _failedFingerprint = targetFingerprint;
                }
                throw;
            }
        }

        /// <summary>The operations that turn <paramref name="baseline"/> into <paramref name="target"/>: deletes,
        /// then sets, each in ordinal path order (BuildEditableOwnUserStateOperations).</summary>
        internal static List<JObject> BuildOperations(
            IReadOnlyDictionary<string, JToken> baseline,
            IReadOnlyDictionary<string, JToken> target)
        {
            var operations = new List<JObject>();
            var removedPaths = new SortedSet<string>(baseline.Keys, StringComparer.Ordinal);
            removedPaths.ExceptWith(target.Keys);
            foreach (string path in removedPaths)
                operations.Add(new JObject { ["op"] = "delete", ["path"] = path });

            var targetPaths = new SortedSet<string>(target.Keys, StringComparer.Ordinal);
            foreach (string path in targetPaths)
            {
                if (baseline.TryGetValue(path, out var previous) && JToken.DeepEquals(previous, target[path]))
                    continue;
                operations.Add(new JObject
                {
                    ["op"] = "set",
                    ["path"] = path,
                    ["value"] = target[path]?.DeepClone() ?? JValue.CreateNull()
                });
            }
            return operations;
        }

        static void ApplyOperation(Dictionary<string, JToken> state, JObject operation)
        {
            string path = operation.Value<string>("path");
            if (string.IsNullOrEmpty(path)) return;
            if (string.Equals(operation.Value<string>("op"), "delete", StringComparison.Ordinal))
                state.Remove(path);
            else
                state[path] = operation["value"]?.DeepClone() ?? JValue.CreateNull();
        }

        void ReplaceBaseline(IReadOnlyDictionary<string, JToken> values)
        {
            _baseline.Clear();
            if (values == null) return;
            foreach (var pair in values)
                _baseline[pair.Key] = pair.Value?.DeepClone() ?? JValue.CreateNull();
        }

        /// <summary>
        /// PacketParty's StateTree (Contracts/UserStateContracts.cs:64-154), the parts the channel uses, copied
        /// verbatim: the same dotted leaf paths as the server, arrays and empty objects staying leaves.
        /// </summary>
        internal static class FlatState
        {
            /// <summary>
            /// Flatten a mutable JSON document into the same dotted leaf paths used
            /// by Packet Party. Arrays and empty objects remain leaf values.
            /// </summary>
            public static Dictionary<string, JToken> Flatten(JObject document)
            {
                var result = new Dictionary<string, JToken>(System.StringComparer.Ordinal);
                if (document == null) return result;
                foreach (var property in document.Properties())
                    FlattenInto(property.Name, property.Value, result);
                return result;
            }

            public static bool FlatEquals(
                IReadOnlyDictionary<string, JToken> left,
                IReadOnlyDictionary<string, JToken> right)
            {
                if (ReferenceEquals(left, right)) return true;
                if (left == null || right == null || left.Count != right.Count) return false;
                foreach (var pair in left)
                {
                    if (!right.TryGetValue(pair.Key, out var value) || !JToken.DeepEquals(pair.Value, value))
                        return false;
                }
                return true;
            }

            public static Dictionary<string, JToken> CloneFlat(IReadOnlyDictionary<string, JToken> values)
            {
                var clone = new Dictionary<string, JToken>(System.StringComparer.Ordinal);
                if (values == null) return clone;
                foreach (var pair in values)
                    clone[pair.Key] = pair.Value?.DeepClone() ?? JValue.CreateNull();
                return clone;
            }

            internal static string Fingerprint(IReadOnlyDictionary<string, JToken> values)
            {
                if (values == null || values.Count == 0) return string.Empty;
                var builder = new StringBuilder();
                foreach (var pair in values.OrderBy(entry => entry.Key, System.StringComparer.Ordinal))
                {
                    string json = (pair.Value ?? JValue.CreateNull()).ToString(Formatting.None);
                    builder.Append(pair.Key.Length).Append(':').Append(pair.Key)
                        .Append('=').Append(json.Length).Append(':').Append(json).Append(';');
                }
                return builder.ToString();
            }

            private static void FlattenInto(string path, JToken value, Dictionary<string, JToken> result)
            {
                if (value is JObject childObject && childObject.HasValues)
                {
                    foreach (var property in childObject.Properties())
                        FlattenInto($"{path}.{property.Name}", property.Value, result);
                    return;
                }
                result[path] = value?.DeepClone() ?? JValue.CreateNull();
            }
        }
    }
}
