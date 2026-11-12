using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using BS.UI.Bridge;
using BS.UI.Core;
using BS.UI.Elements;
using NUnit.Framework;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UIElements;
using GetUIProperty = BS.VisualScripting.GetUIProperty;
using GetUIStyle = BS.VisualScripting.GetUIStyle;
using GetUIText = BS.VisualScripting.GetUIText;
using GetUIValue = BS.VisualScripting.GetUIValue;
using SetUIProperty = BS.VisualScripting.SetUIProperty;
using UIPropertyNameVS = BS.VisualScripting.UIPropertyNameVS;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// The Visual Scripting UI property nodes inside a real Flow. A Get UI node's answer has to reach the next node
    /// through a value connection: it used to be written onto a throwaway Flow, so reading it threw "cannot be
    /// fetched dynamically". Set UI Property has to reach the element. A bare bridge answers synchronously, as in play.
    /// </summary>
    public class UIPropertyNodeFlowTests
    {
        // Stands in for the next node: it triggers the node under test and reads its output through a value
        // connection, the path (Flow.GetValue(ValueInput) -> the source output) that threw before.
        sealed class NextNode : Unit
        {
            [DoNotSerialize] public ControlOutput trigger;
            [DoNotSerialize] public ValueInput read;

            protected override void Definition()
            {
                trigger = ControlOutput("trigger");
                read = ValueInput<object>("read");
            }
        }

        GameObject host;
        UIElementBridge bridge;
        BSUIPanel panel;
        VisualElement root;

        [SetUp]
        public void SetUp()
        {
            host = new GameObject(nameof(UIPropertyNodeFlowTests));
            bridge = host.AddComponent<UIElementBridge>();
            panel = host.AddComponent<BSUIPanel>();
            // What InitializePanel leaves behind, without the UIDocument and scene it needs: the nodes find the panel
            // on the bridge's GameObject and refuse one with no bridge or no panel id.
            SetField(panel, "uiElementBridge", bridge);
            SetField(panel, "internalPanelId", 0);
            UIElementBridge.RegisterPanelInstance(panel.GetFormattedPanelId(), bridge, panel);
            root = new VisualElement();
        }

        [TearDown]
        public void TearDown()
        {
            UIElementBridge.UnregisterPanelInstance(panel.GetFormattedPanelId());
            UIElementResolver.ClearCache();
            UnityEngine.Object.DestroyImmediate(host);
        }

        static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);

        // Registers the elements with the bridge in one pass and returns their ids.
        string[] Register(params VisualElement[] elements)
        {
            foreach (var element in elements) root.Add(element);
            var ids = bridge.ProcessVisualElementTree(root, "flow");
            return elements.Select(element => ids[element]).ToArray();
        }

        // Triggers `node` with `inputs` as its port values; returns what the next node reads from `output` (or null).
        static object Run<T>(T node, Func<T, ValueOutput> output, params (string key, object value)[] inputs) where T : Unit
        {
            var graph = new FlowGraph();
            var next = new NextNode();
            graph.units.Add(node);
            graph.units.Add(next);
            foreach (var (key, value) in inputs)
                node.defaultValues[key] = value;
            graph.controlConnections.Add(new ControlConnection(next.trigger, node.controlInputs.First()));
            if (output != null)
                graph.valueConnections.Add(new ValueConnection(output(node), next.read));

            var asset = ScriptableObject.CreateInstance<ScriptGraphAsset>();
            asset.graph = graph;
            var flow = Flow.New(GraphReference.New(asset, true));
            try
            {
                flow.Invoke(next.trigger);
                return output != null ? flow.GetValue(next.read) : null;
            }
            finally
            {
                flow.Dispose();
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void GetUIText_ReachesTheNextNode()
        {
            var id = Register(new Label("hello"))[0];
            Assert.AreEqual("hello", Run(new GetUIText(), node => node.textValue, ("Element ID", id)));
        }

        [Test]
        public void GetUIValue_ReachesTheNextNode()
        {
            var id = Register(new Slider(0f, 1f) { value = 0.25f })[0];
            Assert.AreEqual(0.25f, (float)Run(new GetUIValue(), node => node.value, ("Element ID", id)), 1e-4f);
        }

        [Test]
        public void GetUIValue_ReadsTypedTextWithEitherDecimal()
        {
            // A TextField holds what the player typed: "2,5" on a German keyboard, "2.5" anywhere.
            var previous = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            try
            {
                var ids = Register(new TextField { value = "2,5" }, new TextField { value = "2.5" });
                Assert.AreEqual(2.5f, (float)Run(new GetUIValue(), node => node.value, ("Element ID", ids[0])), 1e-4f);
                Assert.AreEqual(2.5f, (float)Run(new GetUIValue(), node => node.value, ("Element ID", ids[1])), 1e-4f);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        [Test]
        public void GetUIProperty_ReachesTheNextNode()
        {
            var id = Register(new Label("hello"))[0];
            Assert.AreEqual("hello", Run(new GetUIProperty(), node => node.propertyValue,
                ("Element ID", id), ("Property", UIPropertyNameVS.Text)));
        }

        [Test]
        public void GetUIStyle_ReachesTheNextNode()
        {
            var id = Register(new VisualElement())[0];
            Assert.AreEqual("1", Run(new GetUIStyle(), node => node.styleValue,
                ("Element ID", id), ("Style Property", UIStyleProperty.Opacity)));
        }

        [Test]
        public void SetUIProperty_ReachesTheElement()
        {
            // An Integer used to be sent as "0", and the lowercase "scrolldecelerationrate" matched no setter.
            var slider = new Slider(0f, 100f);
            var scroll = new BSUIScrollView();
            var ids = Register(slider, scroll);
            Run(new SetUIProperty(), null, ("Element ID", ids[0]), ("Property", UIPropertyNameVS.Value), ("Value", 42));
            Run(new SetUIProperty(), null, ("Element ID", ids[1]), ("Property", UIPropertyNameVS.Scrolldecelerationrate), ("Value", 0.5f));
            Assert.AreEqual(42f, slider.value, 1e-4f);
            Assert.AreEqual(0.5f, scroll.ScrollDecelerationRate, 1e-4f);
        }

        [Test]
        public void PanelId_IsTheRealOneBeforeStart()
        {
            // Process UXML Tree can initialise a panel before its Start runs Init(); it must not register as panel_0_0.
            Assert.AreEqual($"panel_{BSScene.UnityId(host)}_{BSScene.UnityId(panel)}", panel.GetFormattedPanelId());
        }
    }
}
