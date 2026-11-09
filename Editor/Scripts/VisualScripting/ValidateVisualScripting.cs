using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.VisualScripting;
using NCalc = Unity.VisualScripting.Dependencies.NCalc;

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
        /// state units, states and transitions.
        /// </summary>
        private static List<string> GetElementsDeep(IGraph graph, GraphReference reference, int depth = 0, UnlistedSink unlisted = null)
        {
            var output = new List<string>();
            if (graph == null || depth > MaxNestDepth)
            {
                if (graph != null) unlisted?.TooDeep();
                return output;
            }
            unlisted?.JudgeVariables(graph);
            foreach (var e in graph.elements)
            {
                output.AddRange(GrabElements(e, reference, depth, unlisted));
            }
            return output;
        }

        private static List<string> GetElementsFromStateGraph(GraphReference reference, StateGraph graph, UnlistedSink unlisted = null)
        {
            return GetElementsDeep(graph, reference, 0, unlisted);
        }

        private static List<string> GrabElements(IGraphElement e, GraphReference reference, int depth = 0, UnlistedSink unlisted = null)
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
                string id;
                try
                {
                    id = e.GetAnalyticsIdentifier()?.Identifier?.Split('(')[0].Trim();
                }
                catch (Exception ex)
                {
                    // Not knowing what a node is must not pass as knowing it's fine.
                    throw new InvalidOperationException($"Could not identify {e?.GetType().FullName} in '{reference?.graph?.title}': {ex.Message}", ex);
                }
                // Connections and transitions are unnamed and run nothing. Anything else unnamed is unknown.
                if (id != null) output.Add(id);
                else if (!(e is IUnitConnection || e is IStateTransition)) output.Add($"<unidentified {e.GetType().FullName}>");
            }
            unlisted?.Judge(e);

            // And whatever it nests: an embedded subgraph or state graph, a state's or transition's graph.
            if (e is IGraphParentElement parent)
            {
                output.AddRange(GetElementsDeep(parent.childGraph, reference, depth + 1, unlisted));
            }
            return output;
        }

        private static string CleanString(string keyword)
        {
            return keyword.ToLowerInvariant().Replace(" ", "").Replace(".", "");
        }

        private static List<string> FindNodesFromScriptGraphAssetGuid(string guid, UnlistedSink unlisted = null)
        {
            var output = new List<string>();
            var assetPath = AssetDatabase.GUIDToAssetPath(guid);
            var sga = AssetDatabase.LoadAssetAtPath<ScriptGraphAsset>(assetPath);
            if (sga?.graph?.elements.Count() > 0)
            {
                unlisted?.At(assetPath, sga, null);
                output = GetElementsDeep(sga.graph, sga.GetReference().AsReference(), 0, unlisted);
            }
            return output;
        }

        private static List<string> FindNodesFromStateGraphAssetGuid(string guid, UnlistedSink unlisted = null)
        {
            var output = new List<string>();
            var assetPath = AssetDatabase.GUIDToAssetPath(guid);
            var sga = AssetDatabase.LoadAssetAtPath<StateGraphAsset>(assetPath);
            // pick up the first layer's elements
            if (sga?.graph?.elements.Count() > 0)
            {
                //Debug.Log($"stategraphasset {sga.name} has {sga.graph?.elements.Count()} elements");
                unlisted?.At(assetPath, sga, null);
                output = output.Concat(GetElementsFromStateGraph(sga.GetReference().AsReference(), sga.graph, unlisted)).ToList();
            }

            return output;
        }

        private static IEnumerable<IGraphElement> ElementsOf(IGraph graph) =>
            (IEnumerable<IGraphElement>)graph?.elements ?? Enumerable.Empty<IGraphElement>();

        internal static List<string> GetElementsFromScriptMachine(ScriptMachine scriptMachine, UnlistedSink unlisted = null)
        {
            var output = new List<string>();
            if (scriptMachine == null || (scriptMachine.graph?.elements.Count() == 0 && scriptMachine.nest?.embed?.elements.Count == 0))
            {
                return output;
            }
            // A machine that can't be read throws: the caller must not take it for a clean one.
            var reference = scriptMachine.GetReference().AsReference();
            unlisted?.JudgeVariables(scriptMachine.graph);
            foreach (var e in ElementsOf(scriptMachine.graph))
            {
                output = output.Concat(GrabElements(e, reference, 0, unlisted)).ToList();
            }
            unlisted?.JudgeVariables(scriptMachine.nest?.embed);
            foreach (var e in ElementsOf(scriptMachine.nest?.embed))
            {
                output = output.Concat(GrabElements(e, reference, 0, unlisted)).ToList();
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
        public static List<string> CollectDisallowedElements(IEnumerable<GameObject> sceneRoots, bool refresh) =>
            Collect(sceneRoots, refresh, null);

        /// <summary>What <see cref="CollectGraphReport"/> found.</summary>
        internal sealed class GraphReport
        {
            /// <summary>The node identifiers the client won't run, exactly as <see cref="CollectDisallowedElements"/> returns them.</summary>
            public List<string> Disallowed;
            /// <summary>Nodes the client runs today but a coming update won't, with where each is used.</summary>
            public List<UnlistedUse> Unlisted;
        }

        /// <summary>
        /// <see cref="CollectDisallowedElements"/>, plus the nodes that aren't on the Banter node list (see
        /// <see cref="Classify"/>). Throws exactly when <see cref="CollectDisallowedElements"/> does: judging the
        /// unlisted nodes never throws.
        /// </summary>
        internal static GraphReport CollectGraphReport(IEnumerable<GameObject> sceneRoots, bool refresh)
        {
            var unlisted = new UnlistedSink();
            var disallowed = Collect(sceneRoots, refresh, unlisted);
            return new GraphReport { Disallowed = disallowed, Unlisted = unlisted.Uses };
        }

        private static List<string> Collect(IEnumerable<GameObject> sceneRoots, bool refresh, UnlistedSink unlisted)
        {
            var everything = new List<string>();
            if (refresh) AssetDatabase.Refresh();
            string[] scriptguids = AssetDatabase.FindAssets("t:ScriptGraphAsset");

            string[] stateguids = AssetDatabase.FindAssets("t:StateGraphAsset");

            foreach (string guid in scriptguids)
            {
                everything.AddRange(FindNodesFromScriptGraphAssetGuid(guid, unlisted));
                //everything = (List<string>)everything.Concat(FindNodesFromScriptGraphAssetGuid(guid));
            }
            foreach (string guid in stateguids)
            {
                everything.AddRange(FindNodesFromStateGraphAssetGuid(guid, unlisted));
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
                            {
                                unlisted?.At(assetPath + " > " + HierarchyPath(scriptMachine.transform), o, null);
                                everything.AddRange(GetElementsFromScriptMachine(scriptMachine, unlisted));
                            }
                        }
                        foreach (var stateMachine in go.GetComponentsInChildren<StateMachine>(true))
                        {
                            if (stateMachine?.nest?.source == GraphSource.Embed)
                            {
                                unlisted?.At(assetPath + " > " + HierarchyPath(stateMachine.transform), o, null);
                                everything.AddRange(GetElementsFromStateGraph(stateMachine.GetReference().AsReference(), stateMachine.graph, unlisted));
                            }
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
                    {
                        unlisted?.At(HierarchyPath(scriptMachine.transform), null, scriptMachine.gameObject);
                        everything.AddRange(GetElementsFromScriptMachine(scriptMachine, unlisted));
                    }
                }
                foreach (var stateMachine in root.GetComponentsInChildren<StateMachine>(true))
                {
                    if (stateMachine?.nest?.source == GraphSource.Embed)
                    {
                        unlisted?.At(HierarchyPath(stateMachine.transform), null, stateMachine.gameObject);
                        everything.AddRange(GetElementsFromStateGraph(stateMachine.GetReference().AsReference(), stateMachine.graph, unlisted));
                    }
                }
            }

            return everything.Distinct().Where(IsDisallowed).ToList();
        }

        static string HierarchyPath(Transform transform)
        {
            var path = new StringBuilder(transform.name);
            for (var parent = transform.parent; parent != null; parent = parent.parent)
                path.Insert(0, parent.name + "/");
            return path.ToString();
        }

        static readonly HashSet<string> OperatorMethods = new HashSet<string>(StringComparer.Ordinal)
        {
            "op_Addition", "op_Subtraction", "op_Multiply", "op_Division", "op_Modulus",
            "op_ExclusiveOr", "op_BitwiseAnd", "op_BitwiseOr", "op_LeftShift", "op_RightShift",
            "op_Equality", "op_Inequality", "op_GreaterThan", "op_LessThan", "op_GreaterThanOrEqual", "op_LessThanOrEqual",
            "op_UnaryNegation", "op_UnaryPlus", "op_LogicalNot", "op_OnesComplement",
        };

        static HashSet<string> listedTypes;
        static HashSet<string> listedMembers;

        /// <summary>Every id on the list.</summary>
        static HashSet<string> ListedMembers =>
            listedMembers ??= new HashSet<string>(VsStubsAllowed.members, StringComparer.Ordinal);

        /// <summary>Types the list holds at least one member of ("X.Member", "X..ctor").</summary>
        static HashSet<string> ListedTypes
        {
            get
            {
                if (listedTypes == null)
                {
                    listedTypes = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var member in VsStubsAllowed.members)
                    {
                        var end = member.EndsWith("..ctor", StringComparison.Ordinal) ? member.Length - "..ctor".Length : member.LastIndexOf('.');
                        if (end > 0) listedTypes.Add(member.Substring(0, end));
                    }
                }
                return listedTypes;
            }
        }

        /// <summary>The list holds no arithmetic or comparison operators: any type with a listed member has them.</summary>
        internal static bool IsListedOperator(string id)
        {
            var dot = id?.LastIndexOf(".op_", StringComparison.Ordinal) ?? -1;
            if (dot <= 0 || !OperatorMethods.Contains(id.Substring(dot + 1))) return false;
            return ListedTypes.Contains(id.Substring(0, dot));
        }

        /// <summary>Node packs whose node types the client runs: Unity's own Visual Scripting nodes, the BS nodes, PicaVoxel's.</summary>
        static bool IsNodePackType(string id) =>
            id != null && (id.StartsWith("Unity.VisualScripting.") || id.StartsWith("BS.VisualScripting.")
                           || id.StartsWith("Banter.VisualScripting.") || id.StartsWith("PicaVoxel.VisualScripting."));

        internal static bool IsDisallowed(string id)
        {
            // Mirrors the client's BanterStubsAllowed.IsBlocked: nothing outside these is allowed.
            return !(id == null || IsNodePackType(id) || ListedMembers.Contains(id) || IsListedOperator(id));
        }

        // ---- The Banter node list, read node by node ---------------------------------------------------------------------
        //
        // A coming client update reads the node list more closely than IsDisallowed does. A member node (Get, Set, Invoke)
        // has to be on the list itself. Literal, Expose and Create Struct nodes are judged by their type, and so are the
        // type values a graph holds (a Get Component type, a graph variable). A formula reads members with Get Member
        // nodes, not "[a.b]". A graph nested more than MaxNestDepth levels deep doesn't run. Until that update is out the
        // Builder warns about these: it doesn't block.

        /// <summary>What the client does with a node.</summary>
        internal enum VsNodeVerdict
        {
            /// <summary>On the Banter node list.</summary>
            Allowed,
            /// <summary>Runs today, but isn't on the node list: a coming client update won't run a graph that uses it. A warning.</summary>
            Unlisted,
            /// <summary>The client doesn't run a graph that uses it (<see cref="IsDisallowed"/>). An error.</summary>
            Disallowed,
        }

        /// <summary>A node a coming client update won't run, and where it's used.</summary>
        internal sealed class UnlistedUse
        {
            /// <summary>The node, e.g. "Literal of System.Version" or a member node's identifier.</summary>
            public string Node;
            /// <summary>The graph asset's path, "prefab path > object path", or the scene object's path.</summary>
            public string Where;
            /// <summary>The graph asset or prefab, when it isn't in the scene.</summary>
            public UnityEngine.Object Asset;
            /// <summary>The scene object whose machine uses it.</summary>
            public GameObject SceneObject;
        }

        /// <summary>Gathers <see cref="UnlistedUse"/>s while the graphs are walked. Never throws into the walk.</summary>
        internal sealed class UnlistedSink
        {
            public readonly List<UnlistedUse> Uses = new List<UnlistedUse>();
            readonly HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            string where = "";
            UnityEngine.Object asset;
            GameObject sceneObject;

            /// <summary>Where the graphs walked next are.</summary>
            public void At(string where, UnityEngine.Object asset, GameObject sceneObject)
            {
                this.where = where ?? "";
                this.asset = asset;
                this.sceneObject = sceneObject;
            }

            public void Add(string node)
            {
                if (node == null || !seen.Add(node + "\n" + where)) return;
                Uses.Add(new UnlistedUse { Node = node, Where = where, Asset = asset, SceneObject = sceneObject });
            }

            public void Judge(IGraphElement element)
            {
                try
                {
                    if (Classify(element, out var node) == VsNodeVerdict.Unlisted) Add(node);
                }
                catch (Exception)
                {
                    // A warning can't block, so a node that can't be judged is listed rather than passed.
                    Add(SafeLabel(element) + " (couldn't be checked)");
                }
            }

            public void JudgeVariables(IGraph graph)
            {
                if (!(graph is IGraphWithVariables withVariables)) return;
                try
                {
                    if (withVariables.variables == null) return;
                    foreach (var declaration in withVariables.variables)
                    {
                        var value = UnlistedValue(declaration.value, 0);
                        if (value != null) Add($"Graph variable \"{declaration.name}\" holding {value}");
                    }
                }
                catch (Exception)
                {
                    Add("Graph variables of " + (graph.title ?? "an untitled graph") + " (couldn't be checked)");
                }
            }

            public void TooDeep() => Add($"Graphs nested more than {MaxNestDepth} levels deep");
        }

        /// <summary>
        /// Whether the client runs <paramref name="element"/>: <see cref="VsNodeVerdict.Disallowed"/> for what
        /// <see cref="IsDisallowed"/> refuses (and unnamed nodes), <see cref="VsNodeVerdict.Unlisted"/> for what it runs
        /// today but a coming update won't, with <paramref name="node"/> naming it; otherwise allowed. Nesting is the
        /// walk's business, not this.
        /// </summary>
        internal static VsNodeVerdict Classify(IGraphElement element, out string node)
        {
            node = null;
            if (element == null || element is StickyNote || element is GraphGroup) return VsNodeVerdict.Allowed;
            var id = IdentifierOf(element);
            // Connections and transitions are unnamed and run nothing; the walk reports any other unnamed element.
            if (id == null) return (element is IUnitConnection || element is IStateTransition) ? VsNodeVerdict.Allowed : VsNodeVerdict.Disallowed;
            if (IsDisallowed(id)) return VsNodeVerdict.Disallowed;
            node = UnlistedNode(element, id);
            return node == null ? VsNodeVerdict.Allowed : VsNodeVerdict.Unlisted;
        }

        static string IdentifierOf(IGraphElement element) =>
            element.GetAnalyticsIdentifier()?.Identifier?.Split('(')[0].Trim();

        /// <summary>What to call an element <see cref="IsDisallowed"/> passes, if the node list doesn't hold it; else null.</summary>
        static string UnlistedNode(IGraphElement element, string id)
        {
            switch (element)
            {
                case MemberUnit _:
                    if (!ListedMembers.Contains(id) && !IsListedOperator(id)) return id;
                    break;
                case Expose expose:
                    if (!IsListedType(expose.type)) return "Expose of " + NameOf(expose.type);
                    var output = UnlistedExposeOutput(expose);
                    if (output != null) return output;
                    break;
                case CreateStruct createStruct:
                    if (!IsListedType(createStruct.type)) return "Create Struct of " + NameOf(createStruct.type);
                    break;
                case Literal literal:
                    var literalNode = UnlistedLiteral(literal);
                    if (literalNode != null) return literalNode;
                    break;
                case Formula formula:
                    if (FormulaReadsMembers(formula.formula)) return $"Formula \"{Shorten(formula.formula)}\"";
                    break;
                default:
                    if (!IsNodePackType(id) && !ListedMembers.Contains(id)) return id;
                    break;
            }
            // What a node's unconnected inputs hold is part of the graph too: a Get Component type, say.
            if (element is IUnit unit && unit.defaultValues != null)
            {
                foreach (var value in unit.defaultValues.Values)
                {
                    var unlisted = UnlistedValue(value, 0);
                    if (unlisted != null) return $"{Label(element, id)} with an input set to {unlisted}";
                }
            }
            return null;
        }

        /// <summary>Expose offers every public member of its type; each output something reads has to be listed.</summary>
        static string UnlistedExposeOutput(Expose expose)
        {
            if (expose.members == null) return "Expose of " + NameOf(expose.type);
            foreach (var pair in expose.members)
            {
                if (!pair.Key.hasAnyConnection) continue;
                if (pair.Value == null) return "Expose of " + NameOf(expose.type);
                var member = expose.type.FullName + "." + pair.Value.name;
                if (!ListedMembers.Contains(member)) return member + " (an Expose output)";
            }
            return null;
        }

        static string UnlistedLiteral(Literal literal)
        {
            var type = literal.type;
            if (type == null) return "Literal of " + NameOf(null);
            if (typeof(Type).IsAssignableFrom(type))
            {
                var held = UnlistedValue(literal.value, 0);
                return held == null ? null : "Literal holding " + held;
            }
            if (!IsListedLiteralType(type)) return "Literal of " + NameOf(type);
            var value = UnlistedValue(literal.value, 0);
            return value == null ? null : $"Literal of {NameOf(type)} holding {value}";
        }

        const int MaxValueDepth = 8;

        /// <summary>A type value in <paramref name="value"/> the list doesn't allow, named; null if there's none.</summary>
        static string UnlistedValue(object value, int depth)
        {
            switch (value)
            {
                case null:
                case string _:
                case UnityEngine.Object _:
                case IGraphElement _:
                    return null;
                case Type type:
                    return IsListedTypeValue(type) ? null : "the type " + NameOf(type);
            }
            if (!(value is IEnumerable sequence)) return null;
            if (depth >= MaxValueDepth) return $"data nested more than {MaxValueDepth} levels deep";
            if (value is IDictionary dictionary)
            {
                foreach (DictionaryEntry entry in dictionary)
                {
                    var found = UnlistedValue(entry.Key, depth + 1) ?? UnlistedValue(entry.Value, depth + 1);
                    if (found != null) return found;
                }
                return null;
            }
            foreach (var item in sequence)
            {
                var found = UnlistedValue(item, depth + 1);
                if (found != null) return found;
            }
            return null;
        }

        static bool IsListedType(Type type) => type != null && ListedTypes.Contains(type.FullName ?? "");

        static bool IsPlainData(Type type) =>
            type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal);

        /// <summary>A type a graph may hold as a value: plain data, or a type the list holds a member of.</summary>
        static bool IsListedTypeValue(Type type)
        {
            if (type == null || type.IsGenericParameter) return false;
            if (type.IsArray) return IsListedTypeValue(type.GetElementType());
            // The client takes Variables as a type value (Get Component of Variables), though no member of it is listed.
            return IsPlainData(type) || type == typeof(Variables) || IsListedType(type);
        }

        /// <summary>A Literal's type: plain data, a listed type, a one-dimensional array of those, or an object slot.</summary>
        static bool IsListedLiteralType(Type type)
        {
            if (type == null) return false;
            if (IsPlainData(type) || IsListedType(type)) return true;
            if (type.IsArray) return type.GetArrayRank() == 1 && IsListedLiteralType(type.GetElementType());
            return typeof(UnityEngine.Object).IsAssignableFrom(type);
        }

        static readonly Dictionary<string, bool> formulaVerdicts = new Dictionary<string, bool>(StringComparer.Ordinal);

        /// <summary>Whether a formula reads members ("[a.member]", "[a.method()]"). One that doesn't parse counts as reading them.</summary>
        internal static bool FormulaReadsMembers(string formula)
        {
            if (string.IsNullOrEmpty(formula)) return false;
            if (formulaVerdicts.TryGetValue(formula, out var known)) return known;
            bool reads;
            try
            {
                var finder = new DottedNameFinder();
                NCalc.Expression.Compile(formula, true)?.Accept(finder);
                reads = finder.Found;
            }
            catch (Exception)
            {
                reads = true;
            }
            if (formulaVerdicts.Count < 4096) formulaVerdicts[formula] = reads;
            return reads;
        }

        sealed class DottedNameFinder : NCalc.LogicalExpressionVisitor
        {
            public bool Found;

            public override void Visit(NCalc.TernaryExpression ternary)
            {
                ternary.LeftExpression?.Accept(this);
                ternary.MiddleExpression?.Accept(this);
                ternary.RightExpression?.Accept(this);
            }

            public override void Visit(NCalc.BinaryExpression binary)
            {
                binary.LeftExpression?.Accept(this);
                binary.RightExpression?.Accept(this);
            }

            public override void Visit(NCalc.UnaryExpression unary) => unary.Expression?.Accept(this);

            public override void Visit(NCalc.ValueExpression value) { }

            public override void Visit(NCalc.FunctionExpression function)
            {
                if (function.Expressions == null) return;
                foreach (var argument in function.Expressions)
                    argument?.Accept(this);
            }

            public override void Visit(NCalc.IdentifierExpression identifier)
            {
                if (identifier.Name != null && identifier.Name.IndexOf('.') >= 0) Found = true;
            }
        }

        static string NameOf(Type type) => type?.FullName ?? "an unresolved type";

        static string Shorten(string text) => text == null ? "" : text.Length <= 60 ? text : text.Substring(0, 57) + "...";

        /// <summary>How the warning names a node: type-configured nodes by their type, the rest by identifier.</summary>
        static string Label(IGraphElement element, string id)
        {
            switch (element)
            {
                case Literal literal: return "Literal of " + NameOf(literal.type);
                case Expose expose: return "Expose of " + NameOf(expose.type);
                case CreateStruct createStruct: return "Create Struct of " + NameOf(createStruct.type);
            }
            return id;
        }

        static string SafeLabel(IGraphElement element)
        {
            try
            {
                return Label(element, IdentifierOf(element) ?? element.GetType().FullName);
            }
            catch (Exception)
            {
                return element?.GetType().FullName ?? "A node";
            }
        }
    }
}
