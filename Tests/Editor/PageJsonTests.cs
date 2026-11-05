using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// JsonUtility overflows the native stack on deep nesting, which no catch stops, so what a page writes is checked first
    /// (PageJson). The depth used at the call sites is the one that took the Editor down: if a guard is missing, this
    /// suite does not fail, it crashes.
    /// </summary>
    public class PageJsonTests
    {
        const int CrashingDepth = 100000;

        static string Nested(string open, int depth)
        {
            var text = new StringBuilder(open.Length * depth);
            for (int i = 0; i < depth; i++) text.Append(open);
            return text.ToString();
        }

        [Test]
        public void TheLimitIsMaxDepth()
        {
            Assert.IsTrue(PageJson.IsSafe(Nested("[", PageJson.MaxDepth)));
            Assert.IsFalse(PageJson.IsSafe(Nested("[", PageJson.MaxDepth + 1)));
            Assert.IsFalse(PageJson.IsSafe(Nested("{\"a\":", PageJson.MaxDepth + 1)));
            Assert.IsFalse(PageJson.IsSafe(Nested("[{\"a\":", PageJson.MaxDepth)));
        }

        [Test]
        public void DepthIsNesting_NotHowManyThereAre()
        {
            Assert.IsTrue(PageJson.IsSafe("[" + string.Join(",", new string[10000].Populate("[1,2,3]")) + "]"));
            Assert.IsTrue(PageJson.IsSafe(Nested("[]", CrashingDepth)));
        }

        [Test]
        public void ACommentCannotHideNesting()
        {
            Assert.IsFalse(PageJson.IsSafe("/*\"*/" + Nested("[", CrashingDepth)));
            Assert.IsFalse(PageJson.IsSafe("//\"\n" + Nested("[", CrashingDepth)));
            Assert.IsTrue(PageJson.IsSafe("{\"url\":\"https://world.example/a\"}"), "a slash in a string is only text");
        }

        [Test]
        public void BracketsInStringsDoNotCount()
        {
            var inAString = "\"" + Nested("[", CrashingDepth) + "\"";
            Assert.IsTrue(PageJson.IsSafe(inAString));
            Assert.IsTrue(PageJson.IsSafe("{\"a\":\"\\\"" + Nested("{", CrashingDepth) + "\"}"), "an escaped quote does not end the string");
            Assert.IsFalse(PageJson.IsSafe("\"closed\"" + Nested("[", CrashingDepth)), "after the string ends they count again");
        }

        [Test]
        public void HostileInputIsAnAnswer_NotAnException()
        {
            Assert.IsFalse(PageJson.IsSafe(null));
            Assert.IsTrue(PageJson.IsSafe(""));
            Assert.IsTrue(PageJson.IsSafe("]]]]}}}}"));
            Assert.IsTrue(PageJson.IsSafe("\"never closed \\"));
        }

        // ---- What real data needs: the shapes the page sends, built with the types that parse them.

        static int DepthOf(string json)
        {
            int depth = 0, deepest = 0;
            bool inString = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (inString) { if (c == '\\') i++; else if (c == '"') inString = false; }
                else if (c == '"') inString = true;
                else if (c == '{' || c == '[') deepest = Mathf.Max(deepest, ++depth);
                else if (c == '}' || c == ']') depth--;
            }
            return deepest;
        }

        [Test]
        public void RealGeometryData_IsFarFromTheLimit()
        {
            var points = JsonUtility.ToJson(new ParametricPoints { points = new[] { Vector3.one, Vector3.up } });
            var curve = JsonUtility.ToJson(new CurveDefinition
            {
                type = "Path",
                points = new[] { Vector3.one },
                segments = new[] { new CurveSegment { type = "Cubic", v0 = Vector3.up } },
            });
            var shape = JsonUtility.ToJson(new ShapeCommands { commands = new[] { new ShapeCommand { type = "M", x = 1 } } });
            const string edges = "[{\"id\":\"a\",\"x1\":1,\"color\":\"#fff\"}]";
            foreach (var json in new[] { points, curve, shape, edges })
            {
                Assert.IsTrue(PageJson.IsSafe(json), json);
                Assert.LessOrEqual(DepthOf(json), 4, json);
            }
            Assert.AreEqual(4, DepthOf(curve), "the deepest shape there is, and what MaxDepth's summary says");
        }

        // ---- The call sites refuse without parsing.

        [Test]
        public void ACurve_NestedTooDeep_IsRefused()
        {
            LogAssert.Expect(LogType.Warning, new Regex("curvePoints are nested too deep"));
            Assert.IsNull(CurveDefinition.Parse(Nested("[", CrashingDepth)));
        }

        [Test]
        public void AShape_NestedTooDeep_IsRefused()
        {
            LogAssert.Expect(LogType.Warning, new Regex("shapePoints are nested too deep"));
            Assert.IsNull(ShapeCommands.Parse(Nested("[", CrashingDepth)));
        }

        [Test]
        public void RealCurvesAndShapes_StillParse()
        {
            Assert.IsNotNull(CurveDefinition.Parse("{\"type\":\"CatmullRom\",\"points\":[{\"x\":0,\"y\":0,\"z\":0},{\"x\":1,\"y\":0,\"z\":0},{\"x\":1,\"y\":1,\"z\":0}]}"));
            Assert.IsNotNull(CurveDefinition.Parse("{\"type\":\"Path\",\"segments\":[{\"type\":\"Cubic\",\"v0\":{\"x\":0,\"y\":0,\"z\":0},\"v1\":{\"x\":1,\"y\":0,\"z\":0},\"v2\":{\"x\":1,\"y\":1,\"z\":0},\"v3\":{\"x\":0,\"y\":1,\"z\":0}}]}"));
            Assert.IsNotNull(ShapeCommands.Parse("{\"commands\":[{\"type\":\"M\",\"x\":0,\"y\":0},{\"type\":\"L\",\"x\":1,\"y\":0},{\"type\":\"L\",\"x\":1,\"y\":1}]}"));
        }

        [Test]
        public void ParametricPoints_NestedTooDeep_AreRefused()
        {
            var host = new GameObject("PageJsonTests");
            try
            {
                host.SetActive(false);
                var geometry = host.AddComponent<BSGeometry>();
                geometry.parametricType = ParametricGeometryType.Custom;
                geometry.parametricPoints = Nested("[", CrashingDepth);
                LogAssert.Expect(LogType.Warning, new Regex("parametricPoints are nested too deep"));
                Assert.IsNull(typeof(BSGeometry).GetMethod("BuildParametric", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(geometry, null));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void Edges_NestedTooDeep_AreRefused_AndRealOnesParse()
        {
            var layer = new BS.UI.Elements.BSUIEdgeLayer();
            var parse = typeof(BS.UI.Elements.BSUIEdgeLayer).GetMethod("ParseEdges", BindingFlags.Instance | BindingFlags.NonPublic);
            var edges = (System.Collections.ICollection)typeof(BS.UI.Elements.BSUIEdgeLayer).GetField("_edges", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(layer);

            parse.Invoke(layer, new object[] { "[{\"id\":\"a\",\"x1\":1,\"y1\":2,\"x2\":3,\"y2\":4,\"color\":\"#ff0000\"}]" });
            Assert.AreEqual(1, edges.Count);

            LogAssert.Expect(LogType.Warning, new Regex("edges are nested too deep"));
            parse.Invoke(layer, new object[] { Nested("[", CrashingDepth) });
            Assert.AreEqual(0, edges.Count);
        }
    }

    static class PageJsonTestExtensions
    {
        public static string[] Populate(this string[] items, string value)
        {
            for (int i = 0; i < items.Length; i++) items[i] = value;
            return items;
        }
    }
}
