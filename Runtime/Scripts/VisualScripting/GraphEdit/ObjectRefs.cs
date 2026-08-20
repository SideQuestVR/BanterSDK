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
            foreach (var descriptor in descriptors)
            {
                var slot = descriptor.i;
                if (slot < 0 || slot >= result.Length) continue;
                result[slot] = ResolveSingle(descriptor, out var failed);
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

        public static UnityObject ResolveSingle(ObjectRefDescriptor descriptor, out bool failed)
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
                    if (RuntimeCodebase.TryDeserializeType(descriptor.assetType, out var assetType))
                    {
                        // Best effort over loaded objects; assets in bundles are findable once
                        // loaded, nothing is loaded on demand here.
                        foreach (var candidate in Resources.FindObjectsOfTypeAll(assetType))
                        {
                            if (candidate.name == descriptor.name) return candidate;
                        }
                    }
                    failed = true;
                    return null;
                }
                default:
                    failed = true;
                    return null;
            }
        }

        static GameObject FindGameObject(ObjectRefDescriptor descriptor)
        {
            if (!string.IsNullOrEmpty(descriptor.bid))
            {
                var obj = BSScene.Instance().GetObjectByBid(descriptor.bid);
                if (obj.gameObject != null) return obj.gameObject;
            }
            if (!string.IsNullOrEmpty(descriptor.path))
            {
                var found = GameObject.Find(descriptor.path);
                if (found != null) return found;
            }
            return null;
        }
    }
}
#endif
