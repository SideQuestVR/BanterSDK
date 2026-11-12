using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BS
{
    /// <summary>
    /// What a bundle holds, written at build time: <c>assets.manifest.json</c>, in the low-poly library's envelope.
    /// </summary>
    /// <remarks>
    /// A scene bundle can hold nothing but scenes, so a world carries it in its scene: an inactive root named
    /// <see cref="HolderName"/> whose children's names are the JSON, in order. Only GameObjects, so a client that has
    /// never heard of it loads the world as it always did. A kit bundle holds it as a TextAsset. Read with JObject rather
    /// than by reflection: a player's managed code stripping can't touch it.
    /// </remarks>
    internal sealed class BSAssetManifest
    {
        internal const string FileName = "assets.manifest.json";
        internal const string HolderName = "__BSAssetManifest";
        internal const int SchemaVersion = 1;
        internal const string GeneratorVersion = "1.0.0";
        /// <summary>Most characters in one child's name.</summary>
        internal const int ChunkLength = 8000;

        internal sealed class Item
        {
            internal string Name;
            internal string Path;
            internal string Guid;
            internal AssetType? Type;
            internal long Size;
        }

        internal readonly List<Item> Assets = new List<Item>();

        // ------------------------------------------------------------------------------------------------- reading

        /// <summary>False for text that isn't a manifest, or one from a newer schema than this client reads.</summary>
        internal static bool TryParse(string json, out BSAssetManifest manifest)
        {
            manifest = null;
            if (string.IsNullOrWhiteSpace(json)) return false;
            try
            {
                var root = JObject.Parse(json);
                if (root.Value<int?>("schemaVersion") is not int version || version < 1 || version > SchemaVersion) return false;
                if (!(root["assets"] is JArray assets)) return false;
                var parsed = new BSAssetManifest();
                foreach (var asset in assets.OfType<JObject>())
                {
                    var name = asset.Value<string>("name");
                    var path = asset.Value<string>("path");
                    if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(path)) continue;
                    parsed.Assets.Add(new Item
                    {
                        Name = name ?? NameOf(path),
                        Path = path,
                        Guid = asset.Value<string>("guid"),
                        Type = Enum.TryParse<AssetType>(asset.Value<string>("type"), out var type) ? type : (AssetType?)null,
                        Size = asset.Value<long?>("size") ?? 0,
                    });
                }
                manifest = parsed;
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        /// <summary>A bundle with no manifest: what its asset names say. Sizes are unknown, types come from the extensions.</summary>
        internal static BSAssetManifest FromAssetNames(IEnumerable<string> names)
        {
            var manifest = new BSAssetManifest();
            foreach (var path in names)
            {
                if (path.EndsWith(FileName, StringComparison.OrdinalIgnoreCase)) continue;
                manifest.Assets.Add(new Item { Name = NameOf(path), Path = path, Type = TypeOf(path) });
            }
            return manifest;
        }

        internal static string NameOf(string path)
        {
            var slash = path.LastIndexOf('/');
            var file = slash < 0 ? path : path.Substring(slash + 1);
            var dot = file.LastIndexOf('.');
            return dot > 0 ? file.Substring(0, dot) : file;
        }

        /// <summary>The type an asset's extension says it is; null when it doesn't say.</summary>
        internal static AssetType? TypeOf(string path)
        {
            var dot = path.LastIndexOf('.');
            switch (dot < 0 ? "" : path.Substring(dot + 1).ToLowerInvariant())
            {
                case "png": case "jpg": case "jpeg": case "tga": case "psd": case "exr": case "bmp": case "tif": case "tiff":
                    return AssetType.Texture2D;
                case "mp3": case "ogg": case "wav": case "aif": case "aiff":
                    return AssetType.AudioClip;
                case "mat":
                    return AssetType.Material;
                case "mesh":
                    return AssetType.Mesh;
                case "prefab": case "fbx": case "obj":
                    return AssetType.GameObject;
                case "anim":
                    return AssetType.AnimationClip;
                case "shader":
                    return AssetType.Shader;
                case "ttf": case "otf":
                    return AssetType.Font;
                default:
                    return null;
            }
        }

        // ------------------------------------------------------------------------------------------------- writing

        internal string ToJson(string generatedAtUtc, string unityVersion, string renderPipeline, string colorSpace)
        {
            var assets = new JArray();
            foreach (var item in Assets)
            {
                assets.Add(new JObject
                {
                    ["name"] = item.Name,
                    ["path"] = item.Path,
                    ["guid"] = item.Guid,
                    ["type"] = item.Type?.ToString(),
                    ["size"] = item.Size,
                });
            }
            return new JObject
            {
                ["schemaVersion"] = SchemaVersion,
                ["generatedAtUtc"] = generatedAtUtc,
                ["generatorVersion"] = GeneratorVersion,
                ["unityVersion"] = unityVersion,
                ["renderPipeline"] = renderPipeline,
                ["colorSpace"] = colorSpace,
                ["counts"] = new JObject { ["assets"] = Assets.Count },
                ["assets"] = assets,
            }.ToString(Formatting.None);
        }

        /// <summary>The JSON as the names of a world's holder's children.</summary>
        internal static IEnumerable<string> ToChunks(string json)
        {
            for (var start = 0; start < json.Length;)
            {
                var length = Math.Min(ChunkLength, json.Length - start);
                // Never between the halves of a surrogate pair: a name that ended on one wouldn't survive serialization.
                if (start + length < json.Length && char.IsHighSurrogate(json[start + length - 1])) length--;
                yield return json.Substring(start, length);
                start += length;
            }
        }

        internal static string FromChunks(IEnumerable<string> chunks)
        {
            var json = new StringBuilder();
            foreach (var chunk in chunks) json.Append(chunk);
            return json.ToString();
        }
    }
}
