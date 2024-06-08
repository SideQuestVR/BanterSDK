
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.VisualScripting;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.SceneManagement;
public class ValidateVisualScriptng{
    private static List<string> GetElementsFromIGraph(GraphReference reference, IGraph graph)
    {
            var output = new List<string>();
#if UNITY_EDITOR && !BANTER_EDITOR
        if (graph == null || graph.elements.Count == 0)
        {
            return output;
        }

        foreach (var e in graph.elements)
        {
            output.Add(e.GetAnalyticsIdentifier()?.Identifier?.Split('(')[0].Trim());
        }
#endif
        return output;
    }
    private static List<string> GetElementsFromStateGraph(GraphReference reference, StateGraph graph)
    {
        var output = new List<string>();
#if UNITY_EDITOR && !BANTER_EDITOR
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
                                output = (List<string>)output.Concat(GetElementsFromStateGraph(reference.ChildReference((INesterStateTransition)transition, false), (StateGraph)childGraph));
                            }
                            else
                            {
                                output = (List<string>)output.Concat(GrabElements(e, reference.ChildReference((INesterStateTransition)transition, false)));
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
                                output = (List<string>)output.Concat(GetElementsFromStateGraph(reference.ChildReference((INesterState)state, false), (StateGraph)childGraph));
                            } else
                            {
                                output = (List<string>)output.Concat(GrabElements(e, reference.ChildReference((INesterState)state, false)));
                            }
                        }
                    }
                }
            }
            
        }
#endif
        return output;
    }
    private static List<string> GrabElements(IGraphElement e,GraphReference reference)
    {
        var output = new List<string>();
#if UNITY_EDITOR && !BANTER_EDITOR
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
#endif
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
#if UNITY_EDITOR && !BANTER_EDITOR
        var assetPath = AssetDatabase.GUIDToAssetPath(guid);
        var sga = AssetDatabase.LoadAssetAtPath<ScriptGraphAsset>(assetPath);
        if (sga?.graph?.elements.Count() > 0)
        {
            foreach (var e in sga.graph.elements)
            {
                output.Add(e.GetAnalyticsIdentifier()?.Identifier?.Split('(')[0].Trim());
            }
        }
#endif
        return output;
    }

    private static List<string> FindNodesFromStateGraphAssetGuid(string guid)
    {
        var output = new List<string>();
#if UNITY_EDITOR && !BANTER_EDITOR
        var assetPath = AssetDatabase.GUIDToAssetPath(guid);
        var sga = AssetDatabase.LoadAssetAtPath<StateGraphAsset>(assetPath);
        // pick up the first layer's elements
        if (sga?.graph?.elements.Count() > 0)
        {
            //Debug.Log($"stategraphasset {sga.name} has {sga.graph?.elements.Count()} elements");
            output = output.Concat(GetElementsFromStateGraph(sga.GetReference().AsReference(), sga.graph)).ToList();
        }
        
#endif
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

        
       
    public static void CheckVsNodes() {
#if !BANTER_EDITOR
        var everything = new List<string>();
        try {
            string[] guids = AssetDatabase.FindAssets("t:ScriptGraphAsset");
            foreach (string guid in guids)
            {
                everything = everything.Concat(FindNodesFromScriptGraphAssetGuid(guid)).ToList();
            }            
            foreach (string guid in guids)
            {
                everything = everything.Concat(FindNodesFromStateGraphAssetGuid(guid)).ToList();
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
                            everything = everything.Concat(GetElementsFromScriptMachine(scriptMachine)).ToList();

                        var stateMachine = go.GetComponent<StateMachine>();
                        if (stateMachine?.nest?.source == GraphSource.Embed)
                            everything = everything.Concat(GetElementsFromStateGraph(stateMachine.GetReference().AsReference(), stateMachine.graph)).ToList();
                    } catch (Exception e)
                    {
                        Debug.Log($"Error while loading prefabs to search from them in path {assetPath} {e.Message} {e.StackTrace}");
                    }
                }
            }
            SceneManager.GetActiveScene().GetRootGameObjects().ToList().ForEach(go => {
                var scriptMachine = go.GetComponent<ScriptMachine>();
                if (scriptMachine?.nest?.source == GraphSource.Embed)
                    everything = everything.Concat(GetElementsFromScriptMachine(scriptMachine)).ToList();

                var stateMachine = go.GetComponent<StateMachine>();
                if (stateMachine?.nest?.source == GraphSource.Embed)
                    everything = everything.Concat(GetElementsFromStateGraph(stateMachine.GetReference().AsReference(), stateMachine.graph)).ToList();
            });
            var notAllowedElements = everything.Distinct().ToList().Where( e => {
                var id = e;
                var isVs = id?.StartsWith("Unity.VisualScripting.")??false;
                if(isVs) {
                    Debug.LogWarning("[VisualScripting] VS node: " + id + " is allowed, skipping");
                }
                return !(id == null || isVs || AotStubsAllowed.members.Contains(id));
            });
            foreach(var element in notAllowedElements.Distinct().ToList()) {
                Debug.LogError("[VisualScripting] Element not allowed in Banter: " + element);
            }
            if(notAllowedElements.Count() > 0) {
                Debug.LogError("[VisualScripting] Found elements that are not allowed for Visual Scripting");
            }
        } catch(Exception e){
            Debug.LogError($"[VisualScripting] Encountered an error while searching in all scripts {e.Message} {e.StackTrace}");
        }
#endif        
    }
}