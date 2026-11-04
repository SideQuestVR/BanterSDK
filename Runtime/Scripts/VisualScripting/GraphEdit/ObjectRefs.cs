#if GREENFIELD_PROJECT
using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace BS
{
    /// <summary>
    /// Portable description of one slot in a graph's SerializationData.objectReferences array.
    /// The FullSerializer JSON stores Unity objects as integer indexes into that array, so an
    /// envelope carries these descriptors alongside the JSON and resolves them back on load.
    /// </summary>
    public class ObjectRefDescriptor
    {
        public int i;
        public string kind; // null | sceneObject | sceneComponent | macroAsset | asset | missing
        public string bid;
        public string component;
        public int componentIndex;
        public string assetType;
        public string name;
        public string path;
        // For kind == "missing": the descriptor that failed to resolve, preserved verbatim so a
        // re-save never silently drops the reference.
        public ObjectRefDescriptor original;
    }

    public class ObjectRefWarning
    {
        public string code;
        public int slot;
        public ObjectRefDescriptor descriptor;
    }

    public static class ObjectRefResolver
    {
        public static List<ObjectRefDescriptor> Describe(UnityObject[] references,
            Dictionary<int, ObjectRefDescriptor> preservedOriginals = null)
        {
            var result = new List<ObjectRefDescriptor>();
            if (references == null) return result;
            for (var i = 0; i < references.Length; i++)
            {
                ObjectRefDescriptor descriptor;
                if (references[i] == null && preservedOriginals != null
                    && preservedOriginals.TryGetValue(i, out var original))
                {
                    descriptor = new ObjectRefDescriptor { kind = "missing", original = original };
                }
                else
                {
                    descriptor = DescribeSingle(references[i]);
                }
                descriptor.i = i;
                result.Add(descriptor);
            }
            return result;
        }

        public static ObjectRefDescriptor DescribeSingle(UnityObject reference)
        {
            if (reference == null)
            {
                return new ObjectRefDescriptor { kind = "null" };
            }

            if (reference is GameObject go)
            {
                return new ObjectRefDescriptor
                {
                    kind = "sceneObject",
                    bid = go.GetComponent<BSObjectId>()?.Id,
                    name = go.name,
                    path = MachineDirectory.HierarchyPath(go.transform),
                };
            }

            if (reference is Component component)
            {
                var componentType = component.GetType();
                var siblings = component.gameObject.GetComponents(componentType);
                return new ObjectRefDescriptor
                {
                    kind = "sceneComponent",
                    bid = component.GetComponent<BSObjectId>()?.Id,
                    component = componentType.FullName,
                    componentIndex = Array.IndexOf(siblings, component),
                    name = component.gameObject.name,
                    path = MachineDirectory.HierarchyPath(component.transform),
                };
            }

            if (reference is MacroScriptableObject)
            {
                return new ObjectRefDescriptor
                {
                    kind = "macroAsset",
                    assetType = reference.GetType().FullName,
                    name = reference.name,
                };
            }

            return new ObjectRefDescriptor
            {
                kind = "asset",
                assetType = reference.GetType().FullName,
                name = reference.name,
            };
        }

        public static UnityObject[] Resolve(List<ObjectRefDescriptor> descriptors, List<ObjectRefWarning> warnings)
        {
            if (descriptors == null) return Array.Empty<UnityObject>();
            var result = new UnityObject[descriptors.Count];
            // One index of the world's assets for the whole envelope, built on first need.
            WorldAssets world = null;
            foreach (var descriptor in descriptors)
            {
                var slot = descriptor.i;
                if (slot < 0 || slot >= result.Length) continue;
                if (world == null && IsAssetKind(descriptor) && Application.isPlaying) world = WorldAssets.Collect();
                result[slot] = ResolveSingle(descriptor, out var failed, world);
                if (failed)
                {
                    warnings?.Add(new ObjectRefWarning
                    {
                        code = "unresolvedRef",
                        slot = slot,
                        descriptor = descriptor.kind == "missing" ? descriptor.original : descriptor,
                    });
                }
            }
            return result;
        }

        static bool IsAssetKind(ObjectRefDescriptor descriptor) =>
            descriptor != null && (descriptor.kind == "macroAsset" || descriptor.kind == "asset");

        public static UnityObject ResolveSingle(ObjectRefDescriptor descriptor, out bool failed) =>
            ResolveSingle(descriptor, out failed, IsAssetKind(descriptor) && Application.isPlaying ? WorldAssets.Collect() : null);

        /// <param name="world">
        /// What asset kinds may resolve to (S1 N8): the world's own assets. Null means unrestricted, which only edit mode
        /// uses: there is no page and no world there, and the script-graph sync window sees the open project.
        /// </param>
        internal static UnityObject ResolveSingle(ObjectRefDescriptor descriptor, out bool failed, WorldAssets world)
        {
            failed = false;
            if (descriptor == null || descriptor.kind == "null") return null;
            if (descriptor.kind == "missing")
            {
                failed = true;
                return null;
            }

            switch (descriptor.kind)
            {
                case "sceneObject":
                {
                    var go = FindGameObject(descriptor);
                    failed = go == null;
                    return go;
                }
                case "sceneComponent":
                {
                    var go = FindGameObject(descriptor);
                    if (go != null && RuntimeCodebase.TryDeserializeType(descriptor.component, out var componentType))
                    {
                        var components = go.GetComponents(componentType);
                        var index = Mathf.Clamp(descriptor.componentIndex, 0, components.Length - 1);
                        if (components.Length > 0) return components[index];
                    }
                    failed = true;
                    return null;
                }
                case "macroAsset":
                case "asset":
                {
                    if (RuntimeCodebase.TryDeserializeType(descriptor.assetType, out var assetType)
                        && typeof(UnityObject).IsAssignableFrom(assetType))
                    {
                        // Best effort over loaded objects; assets in bundles are findable once
                        // loaded, nothing is loaded on demand here.
                        foreach (var candidate in Resources.FindObjectsOfTypeAll(assetType))
                        {
                            // Never something made at runtime (S1). Every session's staging graph is one, under a
                            // predictable name: resolving it let a page point a live Subgraph or Set Script Graph at
                            // a graph it then edited in place, past the install check. Applied graphs and the
                            // client's own runtime objects carry the same flags.
                            if (IsRuntimeMade(candidate)) continue;
                            if (candidate.name != descriptor.name) continue;
                            // Scene objects found this way must still be the world's own (S1).
                            if (candidate is GameObject candidateObject)
                            {
                                if (BSWorldScope.Contains(candidateObject)) return candidate;
                                continue;
                            }
                            if (candidate is Component candidateComponent)
                            {
                                if (BSWorldScope.Contains(candidateComponent)) return candidate;
                                continue;
                            }
                            // Any other loaded object only if it is the world's own (S1 N8): before, a page could name
                            // the client's clips, ScriptableObjects and macros, which neither the gate nor the
                            // UnityEvent guard has judged.
                            if (world == null || world.Contains(candidate)) return candidate;
                        }
                        // Not referenced by the world's content yet, but shipped in one of its kit bundles.
                        var fromBundle = world?.FromBundles(descriptor.name, assetType);
                        if (fromBundle != null && !IsRuntimeMade(fromBundle)) return fromBundle;
                    }
                    failed = true;
                    return null;
                }
                default:
                    failed = true;
                    return null;
            }
        }

        /// <summary>
        /// Objects the running app made and marked not-to-save: the script-graph sessions' staging and applied graphs,
        /// and the client's runtime assets. Assets loaded from a bundle or the AssetDatabase do not carry these flags.
        /// </summary>
        static bool IsRuntimeMade(UnityObject candidate) =>
            (candidate.hideFlags & (HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild | HideFlags.DontUnloadUnusedAsset)) != 0;

        static GameObject FindGameObject(ObjectRefDescriptor descriptor)
        {
            if (!string.IsNullOrEmpty(descriptor.bid))
            {
                var scene = BSScene.Instance();
                var obj = scene != null ? scene.GetObjectByBid(descriptor.bid) : default;
                if (obj.gameObject != null && BSWorldScope.Contains(obj.gameObject)) return obj.gameObject;

                // The scene registers objects in BSObjectId.Awake, which has not run for an object
                // that started inactive — or for anything at all in edit mode, which is where the
                // Unity sync window resolves an override's references. A scan that includes
                // inactive objects, only on that miss.
                var unregistered = MachineDirectory.FindUnregistered(descriptor.bid);
                if (unregistered != null) return unregistered;
            }
            if (!string.IsNullOrEmpty(descriptor.path))
            {
                var found = GameObject.Find(descriptor.path);
                if (found != null && BSWorldScope.Contains(found)) return found;
            }
            return null;
        }
    }

    /// <summary>
    /// The non-scene objects a page's graph may name (S1 N8): the world's own assets, as far as the runtime can tell.
    /// </summary>
    /// <remarks>
    /// <para>Unity cannot say which bundle a loaded object came from, and a streamed scene bundle cannot list its assets,
    /// so this is built from what the world itself points at: (1) every Unity object the world's machines reference -
    /// their macros, the macros nested in their graphs, and the objects in port defaults, literals and graph variables,
    /// followed through those macros; authored graphs of machines a page has overridden count too; and (2) the main
    /// assets of the world's loaded kit bundles, looked up by name. Anything else - the client's clips,
    /// ScriptableObjects, materials, macros - does not resolve.</para>
    /// <para>Limit: an asset only the world's scene uses (a material on a renderer, not in any graph) is not reachable by
    /// name until the client records which objects a world load brought in (see the proposed BSAssetBundle hook).</para>
    /// </remarks>
    internal sealed class WorldAssets
    {
        readonly HashSet<UnityObject> referenced = new HashSet<UnityObject>();
        readonly HashSet<IGraph> walked = new HashSet<IGraph>();
        readonly List<AssetBundle> bundles = new List<AssetBundle>();

        /// <summary>Index the running world: its machines (Script and State) and its kit bundles.</summary>
        internal static WorldAssets Collect()
        {
            var world = new WorldAssets();
            foreach (var behaviour in FindObjects.All<LudiqBehaviour>(FindObjectsInactive.Include))
            {
                if (!(behaviour is IMachine machine) || !BSWorldScope.Contains(behaviour)) continue;
                world.AddMachine(machine);
                if (behaviour is ScriptMachine scriptMachine
                    && ScriptGraphSession.TryGetAuthored(scriptMachine, out var authoredMacro, out var authoredEmbed))
                {
                    world.AddValue(authoredMacro, 0);
                    world.AddGraph(authoredEmbed, 0);
                }
            }
            foreach (var bundleComponent in FindObjects.All<BSAssetBundle>(FindObjectsInactive.Include))
            {
                if (!BSWorldScope.Contains(bundleComponent)) continue;
                var bundle = bundleComponent.assetBundle;
                if (bundle != null && !bundle.isStreamedSceneAssetBundle) world.bundles.Add(bundle);
            }
            return world;
        }

        /// <summary>An index of exactly these graphs (tests, and callers that already know the world's graphs).</summary>
        internal static WorldAssets FromGraphs(params IGraph[] graphs)
        {
            var world = new WorldAssets();
            foreach (var graph in graphs)
            {
                world.AddGraph(graph, 0);
            }
            return world;
        }

        internal bool Contains(UnityObject candidate) => candidate != null && referenced.Contains(candidate);

        /// <summary>A main asset of the world's kit bundles with this name (bundle paths are lower case; names are not).</summary>
        internal UnityObject FromBundles(string name, Type type)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var bundle in bundles)
            {
                if (bundle == null) continue;
                foreach (var path in bundle.GetAllAssetNames())
                {
                    if (!string.Equals(System.IO.Path.GetFileNameWithoutExtension(path), name, StringComparison.OrdinalIgnoreCase)) continue;
                    var asset = bundle.LoadAsset(path, type);
                    if (asset == null || asset.name != name) continue;
                    // Loaded straight from the bundle, so never judged by the world's load (S1 R3-2): judged now, or refused.
                    return BSUnityEventGuard.JudgeKitAsset(asset, path) ? asset : null;
                }
            }
            return null;
        }

        void AddMachine(IMachine machine)
        {
            AddValue(machine.nest?.macro as UnityObject, 0);
            AddGraph(machine.nest?.graph, 0);
        }

        void AddGraph(IGraph graph, int depth)
        {
            if (graph == null || depth > BanterStubsAllowed.MaxNestingDepth || !walked.Add(graph)) return;
            foreach (var element in graph.elements)
            {
                if (element is IGraphNesterElement nester)
                {
                    AddValue(nester.nest?.macro as UnityObject, depth + 1);
                }
                if (element is IGraphParentElement parent)
                {
                    AddGraph(parent.childGraph, depth + 1);
                }
                if (element is IUnit unit)
                {
                    if (unit.defaultValues != null)
                    {
                        foreach (var value in unit.defaultValues.Values) AddValue(value, depth);
                    }
                    if (unit is Literal literal) AddValue(literal.value, depth);
                }
            }
            if (graph is IGraphWithVariables withVariables && withVariables.variables != null)
            {
                foreach (var declaration in withVariables.variables) AddValue(declaration.value, depth);
            }
        }

        void AddValue(object value, int depth)
        {
            switch (value)
            {
                case null:
                case string _:
                    return;
                case UnityObject unityObject:
                    if (unityObject == null || !referenced.Add(unityObject)) return;
                    // A macro's own graph points at more of the world's assets.
                    if (unityObject is IMacro macro) AddGraph(macro.graph, depth + 1);
                    return;
                case IGraph graph:
                    AddGraph(graph, depth + 1);
                    return;
                case System.Collections.IEnumerable sequence when depth < BanterStubsAllowed.MaxNestingDepth:
                    foreach (var item in sequence) AddValue(item, depth + 1);
                    return;
            }
        }
    }
}
#endif
