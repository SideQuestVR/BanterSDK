using System;
using System.Globalization;
using System.Threading;
using BS.UI.Bridge;
using NUnit.Framework;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UIElements;
using SetUIProperty = BS.VisualScripting.SetUIProperty;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// The Visual Scripting UI property nodes: Set UI Property sends numbers of any type (it used to send "0" for
    /// anything but a boxed float), and the Get UI nodes rely on the bridge answering inside HandleMessage.
    /// </summary>
    public class SetUIPropertyFormatTests
    {
        [TestCase(5, "5")]
        [TestCase(2.5d, "2.5")]
        [TestCase(0.25f, "0.25")]
        [TestCase(16777217L, "16777217")]
        [TestCase(10000000, "10000000")]
        [TestCase("7.5", "7.5")]
        [TestCase(" 42 ", "42")]
        [TestCase("10000000", "10000000")] // as written: through a float it became "1E+07", which int.Parse rejects
        [TestCase(true, "1")]
        [TestCase(false, "0")]
        public void NumbersOfAnyType_AreSentInvariant(object value, string expected)
        {
            var previous = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            try
            {
                Assert.AreEqual(expected, SetUIProperty.FormatNumber(value, "fallback"));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        [TestCase("1,5")]
        [TestCase("abc")]
        public void NonNumericText_SendsTheFallback(string value) =>
            Assert.AreEqual("100", SetUIProperty.FormatNumber(value, "100"));

        [Test]
        public void NonNumericTypes_SendTheFallback() =>
            Assert.AreEqual("100", SetUIProperty.FormatNumber(Vector3.one, "100"));

        // A slider can't hold NaN or an infinity, and past float's range the bridge's parse overflows.
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        [TestCase(float.NaN)]
        [TestCase(1e300)]
        [TestCase("NaN")]
        [TestCase("Infinity")]
        [TestCase("1e39")]
        public void NonFiniteOrOutOfRange_SendTheFallback(object value) =>
            Assert.AreEqual("100", SetUIProperty.FormatNumber(value, "100"));

        [Test]
        public void GetCommands_AreAnsweredInsideHandleMessage()
        {
            // Get UI Text/Value/Property/Style write the answer onto the flow that called them, which works only
            // because the bridge fires UIProperty_*/UIStyle_* before HandleMessage returns.
            const string panelId = "SetUIPropertyFormatTests_panel";
            var host = new GameObject(nameof(SetUIPropertyFormatTests));
            var bridge = host.AddComponent<UIElementBridge>();
            UIElementBridge.RegisterPanelInstance(panelId, bridge, null);
            var root = new VisualElement();
            var label = new Label("hello");
            root.Add(label);
            var id = bridge.ProcessVisualElementTree(root, "fmt")[label];

            object text = null, opacity = null;
            var textHook = new EventHook($"UIProperty_{id}_text");
            var opacityHook = new EventHook($"UIStyle_{id}_opacity");
            Action<CustomEventArgs> onText = args => text = args.arguments[0];
            Action<CustomEventArgs> onOpacity = args => opacity = args.arguments[0];
            EventBus.Register(textHook, onText);
            EventBus.Register(opacityHook, onOpacity);
            try
            {
                UIElementBridge.HandleMessage(panelId + MessageDelimiters.PRIMARY + UICommands.GET_UI_PROPERTY +
                                              MessageDelimiters.PRIMARY + id + MessageDelimiters.SECONDARY + "text");
                Assert.AreEqual("hello", text);

                UIElementBridge.HandleMessage(panelId + MessageDelimiters.PRIMARY + UICommands.GET_UI_STYLE +
                                              MessageDelimiters.PRIMARY + id + MessageDelimiters.SECONDARY + "opacity");
                Assert.AreEqual("1", opacity);
            }
            finally
            {
                EventBus.Unregister(textHook, onText);
                EventBus.Unregister(opacityHook, onOpacity);
                UIElementBridge.UnregisterPanelInstance(panelId);
                UnityEngine.Object.DestroyImmediate(host);
            }
        }
    }
}
