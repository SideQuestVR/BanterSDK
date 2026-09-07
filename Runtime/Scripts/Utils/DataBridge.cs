using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BS
{
    /// <summary>
    /// DataBridge exists to give the SDK access to data
    /// that can't be accessed outside the Banter client itself.
    /// </summary>
    public class DataBridge
    {
        public Func<bool> IsSpaceFavourited = () => false;
        public Func<bool> IsSpaceOwner = () => false;
        public Func<BSSynced, BSObjectId, bool> NSODoIOwn = (_, _) => false;

        public Action<BSAttachment> AttachObject = _ => { };
        public Action<BSAttachment> DetachObject = _ => { };

        public Action<long> CloneAvatar = _ => { };

        public Action<(string eventName, KeyValuePair<string,string>[] keyValues)> SendTelemetry = _ => { };

        /// <summary>
        /// Reads and writes the current world's persisted files. Unimplemented here on purpose: a
        /// write is an authenticated call as the signed-in SideQuest user, and the SDK has no auth
        /// and no reference to the platform SDK. The Banter client installs the real one.
        /// </summary>
        public Func<PersistFileRequest, Task<PersistFileResult>> PersistFile
            = _ => Task.FromResult(PersistFileResult.Unavailable("persisted files are not available in this build"));
    }

    /// <summary>What the page asked for. <see cref="Name"/> is already prefixed and sanitised.</summary>
    public class PersistFileRequest
    {
        /// <summary>One of "set", "get", "list", "remove", "info".</summary>
        public string Op;

        /// <summary>The full on-world file name, e.g. "__persisted_shanes_editor.json". Empty for list/info.</summary>
        public string Name;

        /// <summary>The bytes to store, for "set" only.</summary>
        public string Data;
    }

    /// <summary>The reply, serialised straight back to the page.</summary>
    public class PersistFileResult
    {
        public bool ok;
        public string error;

        // set / get
        public string name;
        public string data;
        public string url;
        public long filesId;
        public int bytes;
        public string savedAt;

        // list
        public List<PersistFileEntry> files;

        // info
        public string worldId;
        public string slug;
        public bool signedIn;
        public bool canWrite;

        /// <summary>Why a write is impossible, for a UI that wants to say so before trying.</summary>
        public string reason;

        public static PersistFileResult Unavailable(string why)
            => new PersistFileResult { ok = false, error = why, reason = why };
    }

    public class PersistFileEntry
    {
        public string name;
        public long filesId;
        public int bytes;
        public string updated;
        public string url;
    }
}
