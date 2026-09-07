#if GREENFIELD_PROJECT
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using BS.SDKEditor;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace BS.SDKEditor
{
    /// <summary>
    /// Editor-side reader and writer for a world's <c>__persisted_*.json</c> files — the same files
    /// Shane's Editor writes at runtime.
    /// </summary>
    /// <remarks>
    /// Reads are plain unauthenticated GETs against the world's web root, which the API serves with
    /// <c>no-store</c>, so there is nothing to invalidate and no token to spend. Writes go through
    /// <see cref="SqEditorAppApi.UploadFileToWorld"/> with asset type <see cref="ExtraAssetType"/>,
    /// reusing the Builder's stored login rather than starting a second one.
    /// </remarks>
    public static class PersistedWorldFiles
    {
        /// <summary>
        /// <c>SocietyAssetType.Extra</c>. The only asset type the API keys by NAME — types 1-4 are
        /// single slots per world, so attaching two files under one of them silently destroys the
        /// first, and deletes its stored bytes.
        /// </summary>
        public const UploadAssetType ExtraAssetType = UploadAssetType.Extra;

        public const string ShanesEditorFile = "__persisted_shanes_editor.json";
        public const string ScriptGraphsFile = "__persisted_script_graphs.json";

        public static string WebRoot(string slug) => "https://" + slug + ".worldspace.host";

        public static string UrlFor(string slug, string fileName) => WebRoot(slug) + "/" + fileName;

        /// <summary>
        /// Fetch one persisted file. <paramref name="onCompleted"/> gets null when the world has
        /// never had that file — a 404 here is the normal first-run state, not a failure.
        /// </summary>
        public static IEnumerator Fetch(string slug, string fileName, Action<string> onCompleted, Action<string> onError)
        {
            if (string.IsNullOrEmpty(slug))
            {
                onError?.Invoke("This scene is not linked to a world yet — pick one in the Banter Builder.");
                yield break;
            }

            using (var request = UnityWebRequest.Get(UrlFor(slug, fileName)))
            {
                yield return request.SendWebRequest();

                if (request.responseCode == 404)
                {
                    onCompleted?.Invoke(null);
                    yield break;
                }
                if (request.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke($"Could not read {fileName}: {request.error}");
                    yield break;
                }
                onCompleted?.Invoke(request.downloadHandler.text);
            }
        }

        /// <summary>Replace one persisted file. The signed-in user must own the world.</summary>
        public static IEnumerator Upload(SqEditorAppApi api, string worldId, string slug, string fileName,
                                         string contents, Action onCompleted, Action<string> onError)
        {
            if (api?.User == null)
            {
                onError?.Invoke("Not signed in to SideQuest — sign in from the Banter Builder window.");
                yield break;
            }

            var bytes = Encoding.UTF8.GetBytes(contents);
            string failure = null;
            var done = false;

            yield return api.UploadFileToWorld(fileName, bytes, worldId, slug,
                _ => done = true,
                e => failure = e.Message,
                ExtraAssetType, UploadAssetTypePlatform.Any);

            if (failure != null) onError?.Invoke($"Could not write {fileName}: {failure}");
            else if (!done) onError?.Invoke($"Could not write {fileName}: the upload reported nothing.");
            else onCompleted?.Invoke();
        }

        // ------------------------------------------------------------------ payload shapes

        /// <summary>
        /// Parse leniently: a payload written by a newer editor must not be rejected wholesale, or
        /// upgrading the runtime editor would strand every project on the old one.
        /// </summary>
        public static T Parse<T>(string json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                return JsonConvert.DeserializeObject<T>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Banter] Could not read a persisted world file: " + e.Message);
                return null;
            }
        }

        public static string Write(object payload) => JsonConvert.SerializeObject(payload, Formatting.None);
    }

    /// <summary>__persisted_script_graphs.json — the runtime graph overrides for one world.</summary>
    public class PersistedScriptGraphs
    {
        public int v = 1;
        public string savedAt;
        public List<PersistedScriptGraphEntry> entries = new List<PersistedScriptGraphEntry>();
    }

    public class PersistedScriptGraphEntry
    {
        /// <summary>Stable id for this override, minted by whoever wrote it. The prune key.</summary>
        public string id;

        /// <summary>BSObjectId.Id of the GameObject the machine is on.</summary>
        public string bid;

        public int machineIndex;

        /// <summary>Runtime instance id at save time. Diagnostic only — it does not survive a reload.</summary>
        public string unityId;

        /// <summary>Hierarchy path at save time, used only when the bid cannot be matched.</summary>
        public string path;

        public string title;

        /// <summary>Content hash of the graph this edit was based on, for staleness detection.</summary>
        public string baseGraphRef;

        public string savedAt;

        /// <summary>The graph itself — exactly what the `!sg!` save subcommand returns.</summary>
        public Newtonsoft.Json.Linq.JObject envelope;
    }

    /// <summary>
    /// __persisted_shanes_editor.json. Only the fields this side needs are typed; everything else
    /// rides through <see cref="Extra"/> untouched, which is what lets the editor prune a graph
    /// reference without understanding — or destroying — the scene overlay beside it.
    /// </summary>
    public class PersistedShanesEditor
    {
        [JsonProperty("graphs")]
        public List<PersistedGraphRef> Graphs { get; set; }

        [JsonExtensionData]
        public IDictionary<string, Newtonsoft.Json.Linq.JToken> Extra { get; set; }
    }

    public class PersistedGraphRef
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("bid")] public string Bid { get; set; }
        [JsonProperty("machineIndex")] public int MachineIndex { get; set; }
        [JsonProperty("title")] public string Title { get; set; }
        [JsonProperty("baseGraphRef")] public string BaseGraphRef { get; set; }
        [JsonProperty("savedAt")] public string SavedAt { get; set; }

        [JsonExtensionData]
        public IDictionary<string, Newtonsoft.Json.Linq.JToken> Extra { get; set; }
    }
}
#endif
