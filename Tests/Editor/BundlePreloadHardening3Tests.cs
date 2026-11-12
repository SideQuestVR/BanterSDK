using System;
using System.Collections.Generic;
using System.Linq;
using BS.Security;
using NUnit.Framework;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// Round 5 of the pre-load check's hardening: the app's script layout table (BundleScriptLayouts). Unity reads a
    /// script object with the app's compiled layout when its stored hash is the app's (StreamedBinaryRead), so a stored
    /// tree must then be the app's own; [SerializeReference] values read that way likewise (the U10 crash class). The
    /// tables here are written by hand: BundleCheckUnityBehaviourTests U14 checks the same against Unity's own output.
    /// Built with the fixture writer in BundlePreloadCheckTests.
    /// </summary>
    public partial class BundlePreloadCheckTests
    {
        static readonly byte[] appHash = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();
        static readonly byte[] otherHash = Enumerable.Range(101, 16).Select(i => (byte)i).ToArray();
        static readonly byte[] refAppHash = Enumerable.Range(201, 16).Select(i => (byte)i).ToArray();

        /// <summary>A fixture tree as the checker's digest sees it (the writer stores version 1, the array flag and 0x4000 for alignment).</summary>
        static IEnumerable<BundleScriptLayouts.LayoutNode> Layout(N node, int level = 0)
        {
            yield return new BundleScriptLayouts.LayoutNode
            {
                Level = level, TypeFlags = node.IsArray ? 1 : 0, Version = 1, Size = node.Size, Align = node.Align, Type = node.Type, Name = node.Name,
            };
            foreach (var child in node.Children)
            {
                foreach (var nested in Layout(child, level + 1)) yield return nested;
            }
        }

        static byte[] DigestOf(N tree) => BundleScriptLayouts.Digest(Layout(tree));

        static BundleCheckOptions WithTable(BundleScriptLayouts.Table table)
        {
            var options = BundleCheckOptions.World();
            options.ScriptLayouts = table;
            return options;
        }

        static Type ButtonType()
        {
            var button = BundleTypeResolver.ResolveScript("UnityEngine.UI.dll", "UnityEngine.UI", "Button");
            if (button == null) Assert.Ignore("UGUI not loaded");
            return button;
        }

        /// <summary>A Button-scripted component whose type entry stores <paramref name="hash"/> (null: zeroes).</summary>
        static byte[] HashedWorld(N tree, byte[] hash, Action<Writer> fields, FileOptions options = null)
        {
            var behaviour = Behaviour(3, 2, tree, fields);
            behaviour.TypeHash = hash;
            return Bundle(("BuildPlayer-Test", SerializedFile(new[]
            {
                GameObject(1, "Panel"),
                Script(2, "UnityEngine.UI.dll", "UnityEngine.UI", "Button"),
                behaviour,
            }, options ?? new FileOptions())));
        }

        static bool LayoutRefused(BundleVerdict verdict) => verdict.Reasons.Any(r => r.Rule == BundleRule.TypeTreeShape);

        [Test]
        public void AForgedTreeUnderTheAppsHashIsRefused()
        {
            var table = new BundleScriptLayouts.Table();
            table.Scripts[BundleScriptLayouts.KeyOf(ButtonType())] = new BundleScriptLayouts.Entry(appHash, DigestOf(buttonTree));
            var options = WithTable(table);
            // U2's forgery: the group renamed, so the checker would see no calls while Unity reads its own m_PersistentCalls.
            var forged = MonoBehaviourBase(new N("ButtonClickedEvent", "m_OnClick", -1, false, Renamed(PersistentCalls(), "m_PersistentCallz")));
            AssertRefused(Check(HashedWorld(forged, appHash, w => WriteCalls(w, new Call[0])), options), BundleRule.TypeTreeShape);
            // A zero hash is never stored for a real script: treated as the app's.
            AssertRefused(Check(HashedWorld(forged, null, w => WriteCalls(w, new Call[0])), options), BundleRule.TypeTreeShape);
            // Another hash: Unity converts by name with the stored tree, which is what the checker reads.
            Assert.IsFalse(LayoutRefused(Check(HashedWorld(forged, otherHash, w => WriteCalls(w, new Call[0])), options)));
            // The app's own tree under the app's hash.
            AssertPassed(Check(HashedWorld(buttonTree, appHash, w => WriteCalls(w, new Call[0])), options));
            // A table without the script: taken as it is.
            Assert.IsFalse(LayoutRefused(Check(HashedWorld(forged, appHash, w => WriteCalls(w, new Call[0])), WithTable(new BundleScriptLayouts.Table()))));
        }

        [Test]
        public void SerializeReferenceValuesReadWithTheAppsLayoutMustBeTheAppsOwn()
        {
            var button = ButtonType();
            var eventType = typeof(UnityEngine.Events.UnityEvent);
            var appValue = new N("UnityEvent", "Base", -1, false, PersistentCalls());
            var forgedValue = new N("UnityEvent", "Base", -1, false, Int("m_Value"));
            var host = MonoBehaviourBase(new N("ManagedReferencesRegistry", "references", -1, false, Int("version")));
            byte[] World(N storedValue, byte[] hostHash, byte[] refHash)
            {
                var options = new FileOptions();
                options.RefTypes.Add((storedValue, "UnityEvent", "UnityEngine.Events", "UnityEngine.CoreModule"));
                if (refHash != null) options.RefTypeHashes["UnityEvent"] = refHash;
                return HashedWorld(host, hostHash, w =>
                {
                    w.I32(2); // registry version
                    w.I32(1); // one value
                    w.I64(1); // rid
                    w.Str("UnityEvent");
                    w.Str("UnityEngine.Events");
                    w.Str("UnityEngine.CoreModule");
                    w.I32(0); // no calls, or m_Value
                }, options);
            }
            var table = new BundleScriptLayouts.Table();
            table.Scripts[BundleScriptLayouts.KeyOf(button)] = new BundleScriptLayouts.Entry(appHash, DigestOf(host));
            table.References[BundleScriptLayouts.KeyOf(eventType)] = new BundleScriptLayouts.Entry(refAppHash, DigestOf(appValue));
            var options = WithTable(table);
            Assert.IsFalse(LayoutRefused(Check(World(appValue, appHash, null), options)), "the app's layouts");
            // The host is read with the app's layout, so its registry is too (the U10 crash class), whatever the ref
            // type's own hash (Unity writes zeroes).
            AssertRefused(Check(World(forgedValue, appHash, null), options), BundleRule.TypeTreeShape);
            AssertRefused(Check(World(forgedValue, appHash, otherHash), options), BundleRule.TypeTreeShape);
            // A host converted by name converts its values by name with their stored trees, whatever their hashes.
            Assert.IsFalse(LayoutRefused(Check(World(forgedValue, otherHash, null), options)), "converted by name");
            Assert.IsFalse(LayoutRefused(Check(World(forgedValue, otherHash, refAppHash), options)), "converted by name, ref hash ignored");
            // A class the table does not hold, read with the app's layout, cannot be compared.
            var withoutClass = new BundleScriptLayouts.Table();
            withoutClass.Scripts[BundleScriptLayouts.KeyOf(button)] = new BundleScriptLayouts.Entry(appHash, DigestOf(host));
            AssertRefused(Check(World(appValue, appHash, null), WithTable(withoutClass)), BundleRule.TypeTreeShape);
            Assert.IsFalse(LayoutRefused(Check(World(appValue, otherHash, null), WithTable(withoutClass))), "not held, but converted by name");
            Assert.IsTrue(BundleRules.IsStructural(BundleRule.TypeTreeShape));
        }

        [Test]
        public void TwoRefTypesForOneClassAreRefused()
        {
            var value = new N("UnityEvent", "Base", -1, false, PersistentCalls());
            var options = new FileOptions();
            options.RefTypes.Add((value, "UnityEvent", "UnityEngine.Events", "UnityEngine.CoreModule"));
            options.RefTypes.Add((new N("UnityEvent", "Base", -1, false, Int("m_Value")), "UnityEvent", "UnityEngine.Events", "UnityEngine.CoreModule"));
            var world = HashedWorld(MonoBehaviourBase(new N("ManagedReferencesRegistry", "references", -1, false, Int("version"))), otherHash, w =>
            {
                w.I32(2);
                w.I32(1);
                w.I64(1);
                w.Str("UnityEvent");
                w.Str("UnityEngine.Events");
                w.Str("UnityEngine.CoreModule");
                w.I32(0);
            }, options);
            AssertRefused(Check(world), BundleRule.TypeTreeShape);
        }

        [Test]
        public void TheGeneratorsCollectorAgreesWithTheCheck()
        {
            var button = ButtonType();
            var observed = BundleChecker.CollectLayouts(HashedWorld(buttonTree, appHash, w => WriteCalls(w, new Call[0])));
            var entry = observed.Single(o => o.Kind == 'S');
            Assert.AreEqual(BundleScriptLayouts.KeyOf(button), entry.Key);
            CollectionAssert.AreEqual(appHash, entry.Hash);
            CollectionAssert.AreEqual(DigestOf(buttonTree), entry.Digest, "the collector's digest is the one the check compares");
            Assert.IsTrue(observed.Any(o => o.Kind == 'N' && o.Key == "class 115"), "native type entries are collected too");
        }

        [Test]
        public void TheLayoutTableRoundTripsAndKeysTheVerdictCache()
        {
            var digest = Enumerable.Repeat((byte)7, 16).ToArray();
            var problems = new List<string>();
            var table = BundleScriptLayouts.Build(new[]
            {
                new BundleScriptLayouts.Observed { Kind = 'S', Key = "A|One", Hash = appHash, Digest = digest },
                new BundleScriptLayouts.Observed { Kind = 'S', Key = "A|One", Hash = appHash, Digest = digest },
                new BundleScriptLayouts.Observed { Kind = 'R', Key = "A|Value", Hash = refAppHash, Digest = digest },
                new BundleScriptLayouts.Observed { Kind = 'S', Key = "A|Two", Hash = appHash, Digest = digest },
                new BundleScriptLayouts.Observed { Kind = 'S', Key = "A|Two", Hash = otherHash, Digest = digest },
                new BundleScriptLayouts.Observed { Kind = 'N', Key = "class 115", Hash = appHash, Digest = digest },
            }, "6000.3.21f1", "Android", "inputs", problems);
            Assert.IsTrue(table.Scripts.ContainsKey("A|One"));
            Assert.IsFalse(table.Scripts.ContainsKey("A|Two"), "a key with two layouts is left out");
            Assert.IsTrue(table.References.ContainsKey("A|Value"));
            Assert.AreEqual(1, problems.Count);
            var text = BundleScriptLayouts.Format(table);
            var parsed = BundleScriptLayouts.Parse(text, out var problem);
            Assert.IsNotNull(parsed, problem);
            Assert.AreEqual(table.Fingerprint, parsed.Fingerprint);
            Assert.AreEqual("Android", parsed.Target);
            CollectionAssert.AreEqual(appHash, parsed.Scripts["A|One"].Hash);
            CollectionAssert.AreEqual(digest, parsed.References["A|Value"].Digest);
            Assert.IsNull(BundleScriptLayouts.Parse("not a table", out _));
            Assert.IsNull(BundleScriptLayouts.Parse(text.Replace("\nS ", "\nX "), out _), "unknown line");
            Assert.IsNull(BundleScriptLayouts.Parse(text + text.Substring(text.IndexOf("\nS ", StringComparison.Ordinal) + 1), out _), "repeated key");
            StringAssert.Contains("/t" + table.Fingerprint + "/", BundleCheckPolicy.Versions(WithTable(table), "test"));
            StringAssert.Contains("/t" + parsed.Fingerprint + "/", Check(ButtonWorld(), WithTable(parsed)).Versions);
        }
    }
}
