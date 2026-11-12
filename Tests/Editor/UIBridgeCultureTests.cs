using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using BS.UI.Bridge;
using BS.UI.Elements;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// The page writes numbers with a '.', so the UI bridge reads them, and writes its style values back to
    /// scripts, culture-invariant. Every test runs under de-DE, whose decimal separator is ','.
    /// </summary>
    public class UIBridgeCultureTests
    {
        const string PanelId = "UIBridgeCultureTests_panel";

        CultureInfo previousCulture;
        GameObject host;
        UIElementBridge bridge;
        VisualElement root;

        [SetUp]
        public void SetUp()
        {
            previousCulture = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            host = new GameObject(nameof(UIBridgeCultureTests));
            bridge = host.AddComponent<UIElementBridge>();
            UIElementBridge.RegisterPanelInstance(PanelId, bridge, null);
            root = new VisualElement();
        }

        [TearDown]
        public void TearDown()
        {
            Thread.CurrentThread.CurrentCulture = previousCulture;
            UIElementBridge.UnregisterPanelInstance(PanelId);
            Object.DestroyImmediate(host);
        }

        // Registers the elements with the bridge and returns the ids the page addresses them by.
        string[] Register(params VisualElement[] elements)
        {
            foreach (var element in elements) root.Add(element);
            var ids = bridge.ProcessVisualElementTree(root, "culture");
            return elements.Select(element => ids[element]).ToArray();
        }

        static void Send(string command, params string[] fields) =>
            UIElementBridge.HandleMessage(PanelId + MessageDelimiters.PRIMARY + command + MessageDelimiters.PRIMARY +
                                          string.Join(MessageDelimiters.SECONDARY, fields));

        [Test]
        public void RgbaAlpha_IsReadWithADot()
        {
            // The logged failure: background-color§rgba(30, 27, 36, 0.94) -> "Input string was not in a correct format".
            var box = new VisualElement();
            var id = Register(box)[0];
            Send(UICommands.SET_UI_STYLE, id, "background-color", "rgba(30, 27, 36, 0.94)");
            Assert.AreEqual(0.94f, box.style.backgroundColor.value.a, 1e-4f);
        }

        [Test]
        public void LengthsAndFloats_AreReadWithADot()
        {
            var box = new VisualElement();
            var id = Register(box)[0];
            Send(UICommands.SET_UI_STYLE, id, "width", "12.5px");
            Send(UICommands.SET_UI_STYLE, id, "opacity", "0.5");
            Assert.AreEqual(12.5f, box.style.width.value.value, 1e-4f);
            Assert.AreEqual(0.5f, box.style.opacity.value, 1e-4f);
        }

        [Test]
        public void SliderValues_AreReadWithADot()
        {
            // A plain Slider goes through the bridge's own switch, a BSUISlider through its generated SetProperty.
            var plain = new Slider(0f, 1f);
            var generated = new BSUISlider();
            var ids = Register(plain, generated);
            Send(UICommands.SET_UI_PROPERTY, ids[0], "value", "0.25");
            Send(UICommands.SET_UI_PROPERTY, ids[1], "value", "0.75");
            Assert.AreEqual(0.25f, plain.value, 1e-4f);
            Assert.AreEqual(0.75f, generated.value, 1e-4f);
        }

        [Test]
        public void StyleValuesSentToScripts_AreWrittenWithADot()
        {
            // The Visual Scripting style getters parse these invariant, so the bridge writes them invariant too.
            Assert.AreEqual("1.5px", Format("FormatStyleLength", new StyleLength(1.5f)));
            Assert.AreEqual("50.5%", Format("FormatStyleLength", new StyleLength(Length.Percent(50.5f))));
            Assert.AreEqual("0.94", Format("FormatStyleFloat", new StyleFloat(0.94f)));
        }

        string Format(string method, object value) =>
            (string)typeof(UIElementBridge).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(bridge, new[] { value });

        [Test]
        public void NumberFormat_IsInvariantAndHasNoThousandsSeparator()
        {
            Assert.IsTrue(NumberFormat.TryParse("0.5", out var half));
            Assert.AreEqual(0.5f, half);
            Assert.IsFalse(NumberFormat.TryParse("0,5", out _), "a comma decimal must not read as 5");
            Assert.AreEqual(12.5f, NumberFormat.Parse("12.5"));
            Assert.AreEqual(0.135d, NumberFormat.ParseDouble("0.135"));
            // Parse used to allow thousands separators: a stray comma decimal read as 94 rather than failing.
            Assert.Throws<System.FormatException>(() => NumberFormat.Parse("0,94"));
            Assert.Throws<System.FormatException>(() => NumberFormat.ParseDouble("1,5"));
        }

        [Test]
        public void UINumbers_AlwaysNameACulture()
        {
            // Stops new drift in the UI bridge and the Visual Scripting UI nodes:
            // - a parse or conversion without a culture misreads the page's '.' on comma-decimal locales
            //   (NumberFormat.Parse/TryParse are the invariant helpers);
            // - a number interpolated straight into a CSS length ($"{width}px") is written with the player's comma.
            // A bare float.ToString() needs type information to find, so it is not checked here.
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(UIElementBridge).Assembly);
            if (package == null) Assert.Ignore("The SDK is not installed as a package in this project.");

            var cultureLess = new Regex(@"\b(float|double|Single|Double)\.(Try)?Parse\(|Convert\.(To(Single|Double)|ChangeType)\(");
            var unitInterpolation = new Regex(@"\{(?!NumberFormat\.Format\()[^{}""]+\}(px|%|em|rem|deg)""");
            var offenders = new[] { "Runtime/Scripts/UI", "VisualScripting/UI" }
                .Select(folder => Path.Combine(package.resolvedPath, folder))
                .Where(Directory.Exists)
                .SelectMany(folder => Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories))
                .SelectMany(file => File.ReadAllLines(file).Select((line, index) => (file, line, number: index + 1)))
                .Where(entry => (cultureLess.IsMatch(entry.line) && !entry.line.Contains("CultureInfo")) ||
                                unitInterpolation.IsMatch(entry.line))
                .Select(entry => $"{Path.GetFileName(entry.file)}:{entry.number}: {entry.line.Trim()}")
                .ToList();
            Assert.IsEmpty(offenders, string.Join("\n", offenders));
        }
    }
}
