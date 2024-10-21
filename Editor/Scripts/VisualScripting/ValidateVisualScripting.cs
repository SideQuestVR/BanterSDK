#if BANTER_VISUAL_SCRIPTING
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.VisualScripting;

namespace Banter.SDKEditor
{
    [InitializeOnLoad]
    public class ValidateVisualScripting
    {
#if !BANTER_EDITOR
        [InitializeOnLoadMethod]
        static void Init()
        {
            CheckVsNodes();
        }
#endif

        private static List<string> GetElementsFromIGraph(GraphReference reference, IGraph graph)
        {
                var output = new List<string>();
            if (graph == null || graph.elements.Count == 0)
            {
                return output;
            }

            foreach (var e in graph.elements)
            {
                output.Add(e.GetAnalyticsIdentifier()?.Identifier?.Split('(')[0].Trim());
            }
            return output;
        }
        
        private static List<string> GetElementsFromStateGraph(GraphReference reference, StateGraph graph)
        {
            var output = new List<string>();
            if (graph == null) {
                return output;
            }
            // get this layer's elements
            output = GetElementsFromIGraph(reference, graph);
            // get each states' elements
            if (graph.states.Count() > 0)
            {
                foreach (var transition in graph.transitions)
                {
                    if (transition is INesterStateTransition)
                    {
                        IGraph childGraph = ((INesterStateTransition)transition).childGraph;
                        if (childGraph?.elements.Count() > 0)
                        {
                            foreach (var e in childGraph.elements)
                            {
                                var stateName = !String.IsNullOrEmpty(childGraph.title) ? childGraph.title : "Script State";
                                if (childGraph is StateGraph)
                                {
                                    output = output.Concat(GetElementsFromStateGraph(reference.ChildReference((INesterStateTransition)transition, false), (StateGraph)childGraph)).ToList();
                                }
                                else
                                {
                                    output = output.Concat(GrabElements(e, reference.ChildReference((INesterStateTransition)transition, false))).ToList();
                                }
                            }
                        }
                    }
                }

                foreach (var state in graph.states)
                {
                    if (state is INesterState)
                    {
                        IGraph childGraph = ((INesterState)state).childGraph;
                        if (childGraph?.elements.Count() > 0)
                        {
                            foreach (var e in childGraph.elements)
                            {
                                var stateName = !String.IsNullOrEmpty(childGraph.title) ? childGraph.title : "Script State";
                                if (childGraph is StateGraph)
                                {
                                    output = output.Concat(GetElementsFromStateGraph(reference.ChildReference((INesterState)state, false), (StateGraph)childGraph)).ToList();
                                } else
                                {
                                    output = output.Concat(GrabElements(e, reference.ChildReference((INesterState)state, false))).ToList();
                                }
                            }
                        }
                    }
                }
                
            }
            return output;
        }
        private static List<string> GrabElements(IGraphElement e,GraphReference reference)
        {
            var output = new List<string>();
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
                } catch (Exception ex)
                {
                    Debug.Log($"Could not add element {e?.guid} {reference?.graph?.title} because of {ex}");
                }
            }
            return output;
        }

        private static bool IsIgnoredElement(IGraphElement graphElement)
        {
            switch (graphElement.GetType().ToString())
            {
                case "Bolt.ControlConnection":
                case "Bolt.ValueConnection":
                case "Unity.VisualScripting.ControlConnection":
                case "Unity.VisualScripting.ValueConnection":
                    return true;
            }

            return false;
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
                foreach (var e in sga.graph.elements)
                {
                    output.Add(e.GetAnalyticsIdentifier()?.Identifier?.Split('(')[0].Trim());
                }
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
            if (scriptMachine == null || (scriptMachine.graph.elements.Count() == 0 && scriptMachine.nest?.embed?.elements.Count == 0))
            {
                return output;
            }

            var reference = scriptMachine.GetReference().AsReference();
            foreach (var e in scriptMachine.graph.elements)
            {
                output = output.Concat(GrabElements(e, reference)).ToList();
            }
            foreach (var e in scriptMachine.nest?.embed?.elements)
            {
                output = output.Concat(GrabElements(e, reference)).ToList();
            }
            return output;
        }
       
        public static bool CheckVsNodes() {
            var everything = new List<string>();
            var notAllowedElements = new List<string>();
            try {
                AssetDatabase.Refresh();
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
                    if(o != null)
                    {
                        try
                        {
                            GameObject go = (GameObject)o;
                            var scriptMachine = go.GetComponent<ScriptMachine>();
                            if (scriptMachine?.nest?.source == GraphSource.Embed)
                                everything.AddRange(GetElementsFromScriptMachine(scriptMachine));

                            var stateMachine = go.GetComponent<StateMachine>();
                            if (stateMachine?.nest?.source == GraphSource.Embed)
                                everything.AddRange(GetElementsFromStateGraph(stateMachine.GetReference().AsReference(), stateMachine.graph));
                        } catch (Exception e)
                        {
                            Debug.Log($"Error while loading prefabs to search from them in path {assetPath} {e.Message} {e.StackTrace}");
                            return false;
                        }
                    }
                }
                SceneManager.GetActiveScene().GetRootGameObjects().ToList().ForEach(go => {
                    var scriptMachine = go.GetComponent<ScriptMachine>();
                    if (scriptMachine?.nest?.source == GraphSource.Embed)
                        everything.AddRange(GetElementsFromScriptMachine(scriptMachine));

                    var stateMachine = go.GetComponent<StateMachine>();
                    if (stateMachine?.nest?.source == GraphSource.Embed)
                        everything.AddRange(GetElementsFromStateGraph(stateMachine.GetReference().AsReference(), stateMachine.graph));
                });

                notAllowedElements = everything.Distinct().Where( e => {
                    string id = e;
                    bool isVs = id?.StartsWith("Unity.VisualScripting.") ?? false;
                    bool isBanterVs = id?.StartsWith("Banter.VisualScripting.") ?? false;
                    
                    var notAllowed = !(id == null || isVs || isBanterVs || VsStubsAllowed.members.Contains(id));
                    return notAllowed;
                }).ToList();

                if(notAllowedElements.Count() > 0) {
                    Debug.LogError("[VisualScripting] Found elements that are not allowed for Visual Scripting");
                    foreach(var element in notAllowedElements) {
                        Debug.LogError("[VisualScripting] Element not allowed in Banter: " + element);
                    }
                    return false;
                }
            } catch(Exception e){
                Debug.LogError($"[VisualScripting] Encountered an error while searching in all scripts {e.Message} {e.StackTrace}");
                return false;
            }
            return true;
        }
    }
}
#endif