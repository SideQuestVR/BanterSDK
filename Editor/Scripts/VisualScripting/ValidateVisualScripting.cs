using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.VisualScripting;

namespace BS.SDKEditor
{
    [InitializeOnLoad]
    public class ValidateVisualScripting
    {
#if !GREENFIELD_PROJECT
        [InitializeOnLoadMethod]
        static void Init()
        {
            if (AssetDatabase.IsAssetImportWorkerProcess())
                return;
            // No AssetDatabase.Refresh: a domain reload already follows one, and refreshing from inside
            // the load can start another import, and another reload, while the SDK first installs.
            CheckVsNodes(SceneManager.GetActiveScene().GetRootGameObjects(), refresh: false);
        }
#endif

        // Cap against a macro that nests itself, as in ScriptGraphSession.IsBlockedDeep.
        private const int MaxNestDepth = 16;

        /// <summary>
        /// Every element of <paramref name="graph"/> and of every graph nested in it: embedded subgraphs,
        /// state units, states and transitions. The client only checks a machine's top-level graph, so a
        /// node inside a nest that isn't checked here runs as soon as the parent's flow reaches it.
        /// </summary>
        private static List<string> GetElementsDeep(IGraph graph, GraphReference reference, int depth = 0)
        {
            var output = new List<string>();
            if (graph == null || depth > MaxNestDepth)
            {
                return output;
            }
            foreach (var e in graph.elements)
            {
                output.AddRange(GrabElements(e, reference, depth));
            }
            return output;
        }

        private static List<string> GetElementsFromStateGraph(GraphReference reference, StateGraph graph)
        {
            return GetElementsDeep(graph, reference);
        }

        private static List<string> GrabElements(IGraphElement e, GraphReference reference, int depth = 0)
        {
            var output = new List<string>();

            // no analytics identifier implemented, but they're harmless
            if (e is StickyNote || e is GraphGroup)
            {
                return output;
            }

            if (e is StateUnit)
            {
                if ((((StateUnit)e).nest?.source == GraphSource.Embed && ((StateUnit)e).nest?.graph?.elements.Count() > 0) || ((StateUnit)e).nest?.source == GraphSource.Macro)
                {
                    output.Add(e.GetAnalyticsIdentifier()?.Identifier?.Split('(')[0].Trim());
                }
            }
            else if (e is SubgraphUnit)
            {
                if ((((SubgraphUnit)e).nest?.source == GraphSource.Embed && ((SubgraphUnit)e).nest?.graph?.elements.Count() > 0) || ((SubgraphUnit)e).nest?.source == GraphSource.Macro)
                {
                    output.Add(e.GetAnalyticsIdentifier()?.Identifier?.Split('(')[0].Trim());
                }
            }
            else
            {
                try
                {
                    output.Add(e.GetAnalyticsIdentifier()?.Identifier?.Split('(')[0].Trim());
                }
                catch (Exception ex)
                {
                    Debug.Log($"Could not add element {e?.guid} {reference?.graph?.title} because of {ex}");
                }
            }

            // And whatever it nests: an embedded subgraph or state graph, a state's or transition's graph.
            if (e is IGraphParentElement parent)
            {
                output.AddRange(GetElementsDeep(parent.childGraph, reference, depth + 1));
            }
            return output;
        }

        private static string CleanString(string keyword)
        {
            return keyword.ToLowerInvariant().Replace(" ", "").Replace(".", "");
        }

        private static List<string> FindNodesFromScriptGraphAssetGuid(string guid)
        {
            var output = new List<string>();
            var assetPath = AssetDatabase.GUIDToAssetPath(guid);
            var sga = AssetDatabase.LoadAssetAtPath<ScriptGraphAsset>(assetPath);
            if (sga?.graph?.elements.Count() > 0)
            {
                output = GetElementsDeep(sga.graph, sga.GetReference().AsReference());
            }
            return output;
        }

        private static List<string> FindNodesFromStateGraphAssetGuid(string guid)
        {
            var output = new List<string>();
            var assetPath = AssetDatabase.GUIDToAssetPath(guid);
            var sga = AssetDatabase.LoadAssetAtPath<StateGraphAsset>(assetPath);
            // pick up the first layer's elements
            if (sga?.graph?.elements.Count() > 0)
            {
                //Debug.Log($"stategraphasset {sga.name} has {sga.graph?.elements.Count()} elements");
                output = output.Concat(GetElementsFromStateGraph(sga.GetReference().AsReference(), sga.graph)).ToList();
            }

            return output;
        }

        private static List<string> GetElementsFromScriptMachine(ScriptMachine scriptMachine)
        {
            var output = new List<string>();
            if (scriptMachine == null || (scriptMachine.graph?.elements.Count() == 0 && scriptMachine.nest?.embed?.elements.Count == 0))
            {
                return output;
            }
            try
            {
                var reference = scriptMachine.GetReference().AsReference();
                foreach (var e in scriptMachine.graph?.elements)
                {
                    output = output.Concat(GrabElements(e, reference)).ToList();
                }
                foreach (var e in scriptMachine.nest?.embed?.elements)
                {
                    output = output.Concat(GrabElements(e, reference)).ToList();
                }
            }
            catch (Exception ex)
            {
                Debug.Log($"Could not add scriptMachine {scriptMachine?.GetType()}{scriptMachine?.GetEntityId()} {scriptMachine?.name} because of {ex} ");
            }
            return output;
        }

        public static bool CheckVsNodes() => CheckVsNodes(SceneManager.GetActiveScene().GetRootGameObjects());

        /// <summary>
        /// Logs every node the client won't run, from the project's graph assets and prefabs and from
        /// every machine under <paramref name="sceneRoots"/>. False if there are any, or on error.
        /// </summary>
        public static bool CheckVsNodes(IEnumerable<GameObject> sceneRoots, bool refresh = true)
        {
            try
            {
                var notAllowedElements = CollectDisallowedElements(sceneRoots, refresh);
                if (notAllowedElements.Count() > 0)
                {
                    Debug.LogError("[VisualScripting] Found elements that are not allowed for Visual Scripting");
                    foreach (var element in notAllowedElements)
                    {
                        Debug.LogError("[VisualScripting] Element not allowed in Banter: " + element);
                    }
                    return false;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[VisualScripting] Encountered an error while searching in all scripts {e.Message} {e.StackTrace}");
                return false;
            }
            return true;
        }

        /// <summary>
        /// The node identifiers the client won't run: from every script/state graph asset, every
        /// machine in every prefab, and every machine (inactive included) under <paramref name="sceneRoots"/>,
        /// nested graphs included.
        /// Throws if a prefab can't be inspected, since then the answer isn't known.
        /// </summary>
        public static List<string> CollectDisallowedElements(IEnumerable<GameObject> sceneRoots, bool refresh)
        {
            var everything = new List<string>();
            if (refresh) AssetDatabase.Refresh();
            string[] scriptguids = AssetDatabase.FindAssets("t:ScriptGraphAsset");

            string[] stateguids = AssetDatabase.FindAssets("t:StateGraphAsset");

            foreach (string guid in scriptguids)
            {
                everything.AddRange(FindNodesFromScriptGraphAssetGuid(guid));
                //everything = (List<string>)everything.Concat(FindNodesFromScriptGraphAssetGuid(guid));
            }
            foreach (string guid in stateguids)
            {
                everything.AddRange(FindNodesFromStateGraphAssetGuid(guid));
            }
            var paths = AssetDatabase.GetAllAssetPaths().Select(path => path).Where(File.Exists).Where(f => Path.GetExtension(f) == ".prefab");
            foreach (var p in paths)
            {
                var assetPath = p;
                UnityEngine.Object o = AssetDatabase.LoadMainAssetAtPath(assetPath);
                if (o != null)
                {
                    try
                    {
                        GameObject go = (GameObject)o;
                        // Every machine in the prefab, not just the root's first.
                        foreach (var scriptMachine in go.GetComponentsInChildren<ScriptMachine>(true))
                        {
                            if (scriptMachine?.nest?.source == GraphSource.Embed)
                                everything.AddRange(GetElementsFromScriptMachine(scriptMachine));
                        }
                        foreach (var stateMachine in go.GetComponentsInChildren<StateMachine>(true))
                        {
                            if (stateMachine?.nest?.source == GraphSource.Embed)
                                everything.AddRange(GetElementsFromStateGraph(stateMachine.GetReference().AsReference(), stateMachine.graph));
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.Log($"Error while loading prefabs to search from them in path {assetPath} {e.Message} {e.StackTrace}");
                        throw new InvalidOperationException($"Could not inspect the Visual Scripting in prefab {assetPath}: {e.Message}", e);
                    }
                }
            }
            // Every machine in the scene, not just those on root objects.
            foreach (var root in sceneRoots ?? Enumerable.Empty<GameObject>())
            {
                if (root == null) continue;
                foreach (var scriptMachine in root.GetComponentsInChildren<ScriptMachine>(true))
                {
                    if (scriptMachine?.nest?.source == GraphSource.Embed)
                        everything.AddRange(GetElementsFromScriptMachine(scriptMachine));
                }
                foreach (var stateMachine in root.GetComponentsInChildren<StateMachine>(true))
                {
                    if (stateMachine?.nest?.source == GraphSource.Embed)
                        everything.AddRange(GetElementsFromStateGraph(stateMachine.GetReference().AsReference(), stateMachine.graph));
                }
            }

            return everything.Distinct().Where(IsDisallowed).ToList();
        }

        internal static bool IsDisallowed(string id)
        {
            bool isVs = id?.StartsWith("Unity.VisualScripting.") ?? false;
            bool isBanterVs = (id?.StartsWith("BS.VisualScripting.") ?? false) || (id?.StartsWith("Banter.VisualScripting.") ?? false);
            bool isPicaVoxelVs = id?.StartsWith("PicaVoxel.VisualScripting.") ?? false;

            // Mirrors the client's BanterStubsAllowed.IsBlocked: nothing outside these is allowed.
            return !(id == null || isVs || isBanterVs || isPicaVoxelVs || VsStubsAllowed.members.Contains(id));
        }
    }
}
