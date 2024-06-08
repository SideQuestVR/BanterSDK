
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.VisualScripting;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
public class ValidateVisualScriptng{
    private static List<string> GetElementsFromIGraph(GraphReference reference, IGraph graph, string assetPath)
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
    private static List<string> GetElementsFromStateGraph(GraphReference reference, StateGraph graph, string assetPath)
    {
        var output = new List<string>();
#if UNITY_EDITOR && !BANTER_EDITOR
        if (graph == null) {
            return output;
        }
        // get this layer's elements
        output = GetElementsFromIGraph(reference, graph, assetPath);
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
                                output = (List<string>)output.Concat(GetElementsFromStateGraph(reference.ChildReference((INesterStateTransition)transition, false), (StateGraph)childGraph, assetPath));
                            }
                            else
                            {
                                output = (List<string>)output.Concat(GrabElements(e, stateName, null, reference.ChildReference((INesterStateTransition)transition, false), childGraph, assetPath));
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
                                output = (List<string>)output.Concat(GetElementsFromStateGraph(reference.ChildReference((INesterState)state, false), (StateGraph)childGraph, assetPath));
                            } else
                            {
                                output = (List<string>)output.Concat(GrabElements(e, stateName, null, reference.ChildReference((INesterState)state, false), childGraph, assetPath));
                            }
                        }
                    }
                }
            }
            
        }
#endif
        return output;
    }
    private static List<string> GrabElements(IGraphElement e, string stateName, GameObject gameObject, GraphReference reference, IGraph graph, string assetPath)
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
                Debug.Log($"Could not add element {e?.guid} {assetPath} {reference?.graph?.title} because of {ex}");
            }
        }
#endif
        return (List<string>)output.Distinct();
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
        return (List<string>)output.Distinct();
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
            output = (List<string>)output.Concat(GetElementsFromStateGraph(sga.GetReference().AsReference(), sga.graph, assetPath));
        }
        
#endif
        return (List<string>)output.Distinct();
    }

     private static List<string> GetElementsFromScriptMachine(ScriptMachine scriptMachine, string assetPath)
        {
             var output = new List<string>();
#if UNITY_EDITOR && !BANTER_EDITOR 
            if (scriptMachine == null || (scriptMachine.graph.elements.Count() == 0 && scriptMachine.nest?.embed?.elements.Count == 0))
            {
                return output;
            }

            var reference = scriptMachine.GetReference().AsReference();
            foreach (var e in scriptMachine.graph.elements)
            {
                output = (List<string>)output.Concat(GrabElements(e, "", scriptMachine.gameObject, reference, scriptMachine.graph, assetPath));
            }
            foreach (var e in scriptMachine.nest?.embed?.elements)
            {
                output = (List<string>)output.Concat(GrabElements(e, "", scriptMachine.gameObject, reference, scriptMachine.graph, assetPath));
            }

#endif 
            return output;
        }

        
       
    public static void CheckVsNodes() {
#if UNITY_EDITOR && !BANTER_EDITOR
        var everything = new List<string>();
        try {
            string[] guids = AssetDatabase.FindAssets("t:ScriptGraphAsset");
            foreach (string guid in guids)
            {
                everything = (List<string>)everything.Concat(FindNodesFromScriptGraphAssetGuid(guid));
            }            
            foreach (string guid in guids)
            {
                everything = (List<string>)everything.Concat(FindNodesFromStateGraphAssetGuid(guid));
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
                        if (scriptMachine?.nest?.source == Unity.VisualScripting.GraphSource.Embed)
                            everything = (List<string>)everything.Concat(GetElementsFromScriptMachine(scriptMachine, assetPath));

                        var stateMachine = go.GetComponent<StateMachine>();
                        if (stateMachine?.nest?.source == Unity.VisualScripting.GraphSource.Embed)
                            everything = (List<string>)everything.Concat(GetElementsFromStateGraph(stateMachine.GetReference().AsReference(), stateMachine.graph, assetPath));
                    } catch (Exception e)
                    {
                        Debug.Log($"Error while loading prefabs to search from them in path {assetPath} {e.Message} {e.StackTrace}");
                    }
                }
            }
            var notAllowedElements = everything.Where( e => {
                var id = e;
                var isVs = id?.StartsWith("Unity.VisualScripting.")??false;
                if(isVs) {
                    Debug.LogWarning("VS node: " + id + " is allowed, skipping");
                }
                return !(id == null || isVs || AotStubsAllowed.members.Contains(id));
            });
            foreach(var element in notAllowedElements) {
                Debug.LogError("Element not allowed: " + element);
            }
            if(notAllowedElements.Count() > 0) {
                Debug.LogError("Found elements that are not allowed for Visual Scripting");
            }
        } catch(Exception e){
            Debug.LogError($"Encountered an error while searching in all scripts {e.Message} {e.StackTrace}");
        }
#endif        
    }
}