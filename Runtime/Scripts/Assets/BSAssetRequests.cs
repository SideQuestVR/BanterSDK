using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace BS
{
    /// <summary>
    /// <c>BS.Assets</c>' side of the wire, ASSET_DATABASE: <c>op§field§...</c>, fields encoded with WireText. Answers are
    /// JSON, built with JObject because a player's stripping can't see what reflection would need.
    /// </summary>
    internal static class BSAssetRequests
    {
        internal const string Hello = "hello", Load = "load", Find = "find", Release = "release";

        /// <summary>Most lines one Find answers with: a larger answer is a long time on the bus.</summary>
        internal const int MaxListings = 1000;

        internal static async void Handle(BSLink link, string data, int reqId)
        {
            var registry = BSAssetRegistry.Instance;
            var database = registry.Database;
            try
            {
                var parts = data.Split(MessageDelimiters.SECONDARY);
                switch (parts[0])
                {
                    case Hello:
                        Reply(link, reqId, "1");
                        break;
                    case Load:
                        var key = KeyOf(parts);
                        var wanted = TypeOf(parts, 2);
                        var sources = registry.Sources;
                        var entry = await database.Acquire(key, BSAssetSources.IsCritical(key), () => sources.Load(key, wanted));
                        if (!database.Retired) Reply(link, reqId, Describe(entry).ToString(Formatting.None));
                        break;
                    case Find:
                        var pattern = Field(parts, 1);
                        Reply(link, reqId, Listings(registry, pattern.Length == 0 ? null : new BSAssetPattern(pattern), TypeOf(parts, 2)));
                        break;
                    case Release:
                        var held = database.Find(Field(parts, 1));
                        Reply(link, reqId, held != null && database.Release(held.Key) ? new JObject { ["id"] = held.Meta.id }.ToString(Formatting.None) : "null");
                        break;
                    default:
                        throw new ArgumentException("unknown operation " + parts[0]);
                }
            }
            catch (Exception e)
            {
                // Its page has gone, and the next page's request ids start again: an answer would reach the wrong request.
                if (database.Retired) return;
                // A world's bad key is the page's to report: an error here would open the client's error panel.
                Debug.LogWarning($"[BSAssetRequests] {e.Message}");
                link.ReplyError(reqId, e is BSAssetLimitException ? e.Message : APICommands.ASSET_DATABASE + ": " + e.Message);
            }
        }

        static string Field(string[] parts, int index) => parts.Length > index ? WireText.Decode(parts[index]) : "";

        static BSAssetKey KeyOf(string[] parts)
        {
            var text = Field(parts, 1);
            if (text.Length > BSAssetKey.MaxLength) throw new BSAssetLimitException($"LIMIT: a key has at most {BSAssetKey.MaxLength} characters");
            if (!BSAssetKey.TryParse(text, out var key)) throw new ArgumentException("not an asset key: " + text);
            return key;
        }

        static AssetType? TypeOf(string[] parts, int index)
        {
            var field = Field(parts, index);
            return int.TryParse(field, out var type) ? (AssetType)type : (AssetType?)null;
        }

        static void Reply(BSLink link, int reqId, string json) =>
            link.Send(APICommands.REQUEST_ID + MessageDelimiters.REQUEST_ID + reqId + MessageDelimiters.PRIMARY + WireText.Encode(json));

        static JObject Describe(BSAssetEntry entry) => new JObject
        {
            ["id"] = entry.Meta.id,
            ["key"] = entry.Key,
            ["type"] = (int)entry.Meta.type,
            ["size"] = entry.Meta.memorySize,
            ["url"] = entry.Meta.url,
        };

        static string Listings(BSAssetRegistry registry, BSAssetPattern pattern, AssetType? type)
        {
            var found = new JArray();
            foreach (var listing in registry.Sources.List(registry.Database))
            {
                if (found.Count >= MaxListings) break;
                if (type != null && listing.Type != type) continue;
                if (pattern != null && !pattern.Matches(listing.Key)) continue;
                found.Add(new JObject
                {
                    ["key"] = listing.Key,
                    ["name"] = listing.Name,
                    ["type"] = listing.Type == null ? null : (JToken)(int)listing.Type.Value,
                    ["size"] = listing.Size,
                    ["loaded"] = listing.Loaded,
                });
            }
            return found.ToString(Formatting.None);
        }
    }
}
