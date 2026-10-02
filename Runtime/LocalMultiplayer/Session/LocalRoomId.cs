using System.Text;
using UnityEngine.SceneManagement;

namespace BS.LocalMultiplayer
{
    /// <summary>
    /// The relay room a player joins (RELAY.md 2): "s-" + FNV-1a 64 of the lower-cased active scene path, or
    /// "s-untitled". Production derives the room from the space URL (NetworkService.RoomIdFromUrl), but every
    /// local page is http://localhost:&lt;port&gt;, so the open scene is the space's identity here. Clones only
    /// load saved scenes, so players of one project agree on it. The persisted space state is
    /// Library/SideQuestLocalMultiplayer/space-state/room-&lt;id&gt;.json.
    /// </summary>
    public static class LocalRoomId
    {
        public const string Untitled = "s-untitled";

        /// <summary>The room of the active scene.</summary>
        public static string ForActiveScene()
        {
            return ForScenePath(SceneManager.GetActiveScene().path);
        }

        /// <summary>The room of a scene asset path ("Assets/Scenes/Main.unity"); matches ^[A-Za-z0-9_-]{1,96}$.</summary>
        public static string ForScenePath(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath)) return Untitled;
            return "s-" + Fnv1a64(scenePath.ToLowerInvariant()).ToString("x16");
        }

        /// <summary>FNV-1a, 64 bit, over the UTF-8 bytes.</summary>
        public static ulong Fnv1a64(string text)
        {
            unchecked
            {
                ulong hash = 14695981039346656037UL;
                foreach (var b in Encoding.UTF8.GetBytes(text ?? ""))
                {
                    hash ^= b;
                    hash *= 1099511628211UL;
                }
                return hash;
            }
        }
    }
}
