using System;
using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    /// <summary>
    /// Remembers which SideQuest world this scene publishes to, and which runtime script-graph
    /// edits have already been pulled back into the project.
    /// </summary>
    /// <remarks>
    /// This lives in the SCENE rather than in ProjectPrefs because the mapping is a property of the
    /// scene, not of the project — one project can author several worlds — and because it then
    /// travels with the scene file through git instead of sitting in a single shared asset that
    /// two branches would fight over.
    ///
    /// It is authoring-only data with no runtime behaviour. The Builder puts it on a GameObject
    /// tagged <c>EditorOnly</c>, which Unity strips from every build; CustomSceneProcessor strips
    /// it again on the build copy so a stray untagged one can never ship either.
    /// </remarks>
    [DisallowMultipleComponent]
    public class BSWorldLink : MonoBehaviour
    {
        /// <summary>The GameObject the Builder creates to hold this.</summary>
        public const string HolderName = "__WorldLink";

        [Tooltip("SideQuest worlds_id this scene publishes to. Set by the Banter Builder when you pick a world.")]
        public string worldId;

        [Tooltip("The world's space slug — the subdomain its files are served from.")]
        public string slug;

        [Tooltip("When the runtime script-graph overrides were last checked against this scene.")]
        public string lastSyncedAt;

        [Tooltip("Runtime graph edits already applied to this scene. Cleared entry by entry once a world upload has removed them from __persisted_script_graphs.json.")]
        public List<SyncedGraphRecord> syncedGraphs = new List<SyncedGraphRecord>();

        /// <summary>The world's web root, where its persisted files are served from.</summary>
        public string WebRoot => string.IsNullOrEmpty(slug) ? null : "https://" + slug + ".worldspace.host";

        public bool IsLinked => !string.IsNullOrEmpty(worldId) && !string.IsNullOrEmpty(slug);
    }

    /// <summary>
    /// One runtime graph edit that has been applied to the scene and is therefore now redundant in
    /// the world's override file.
    /// </summary>
    [Serializable]
    public class SyncedGraphRecord
    {
        /// <summary>Entry id from __persisted_script_graphs.json — what the prune matches on.</summary>
        public string entryId;

        /// <summary>BSObjectId.Id of the GameObject the machine sits on.</summary>
        public string bid;

        /// <summary>Index into that GameObject's ScriptMachine components.</summary>
        public int machineIndex;

        /// <summary>Graph title at sync time, for the upload confirmation to name it readably.</summary>
        public string title;

        /// <summary>ISO-8601. When this project applied the edit.</summary>
        public string syncedAt;

        /// <summary>
        /// The entry's own savedAt when it was applied. If the file has moved on since — someone
        /// edited the graph again at runtime — the prune must leave the newer entry alone.
        /// </summary>
        public string sourceSavedAt;
    }
}
