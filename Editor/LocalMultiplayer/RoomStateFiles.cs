using System;
using System.IO;

namespace BS.LocalMultiplayer.Editor
{
    /// <summary>
    /// The space state the relay keeps between play sessions (RELAY.md 7): one file per room,
    /// <c>room-&lt;roomId&gt;.json</c>, in <c>&lt;MainProjectRoot&gt;/Library/SideQuestLocalMultiplayer/space-state</c>. Only
    /// the main editor's hub writes them (a corrupt one is renamed <c>room-&lt;id&gt;.corrupt-&lt;ms&gt;.json</c>, which
    /// the same pattern matches). The hub only runs during Play, so outside Play the files can simply be deleted;
    /// during Play the world owner clears the room through the relay instead.
    /// </summary>
    internal static class RoomStateFiles
    {
        const string Pattern = "room-*.json";

        internal static string DirectoryFor(string mainProjectRoot)
        {
            return Path.Combine(mainProjectRoot ?? string.Empty, "Library", "SideQuestLocalMultiplayer", "space-state");
        }

        /// <summary>The saved rooms' files; empty when there are none or the folder can't be read.</summary>
        internal static string[] List(string directory)
        {
            try
            {
                if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                {
                    return Array.Empty<string>();
                }
                return Directory.GetFiles(directory, Pattern, SearchOption.TopDirectoryOnly);
            }
            catch (Exception)
            {
                return Array.Empty<string>();
            }
        }

        internal static long TotalBytes(string[] files)
        {
            long total = 0;
            if (files == null)
            {
                return total;
            }
            foreach (var file in files)
            {
                try
                {
                    total += new FileInfo(file).Length;
                }
                catch (Exception)
                {
                    // Gone or locked: it doesn't count.
                }
            }
            return total;
        }

        /// <summary>Deletes the files; returns how many went, with the first failure's reason.</summary>
        internal static int Delete(string[] files, out string firstError)
        {
            firstError = null;
            var deleted = 0;
            if (files == null)
            {
                return deleted;
            }
            foreach (var file in files)
            {
                try
                {
                    if (File.Exists(file))
                    {
                        File.Delete(file);
                        deleted++;
                    }
                }
                catch (Exception e)
                {
                    firstError ??= Path.GetFileName(file) + ": " + e.Message;
                }
            }
            return deleted;
        }
    }
}
