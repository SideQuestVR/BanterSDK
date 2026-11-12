using System;
using System.Collections.Generic;
using System.Linq;
using BS.Bridge;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace BS.BridgeApi.Tests
{
    /// <summary>
    /// Calls every generated member on a real object of its type, four times over: with default arguments, with negative
    /// ones, with the largest an argument may be and with ones past that. A refusal is a fine answer, and so is an exception
    /// from Unity's own rules. A member
    /// missing at runtime is not, and neither is one that makes Unity log an error.
    /// </summary>
    public class BridgeApiThunkSelfTest
    {
        enum Sweep { Defaults, Negative, Large, Huge }

        /// <summary>Errors a member may log, each with its reason. Nothing else may.</summary>
        static readonly (string message, string reason)[] Expected = { };

        BridgeApiFixture f;
        Type[] types;
        readonly Dictionary<int, UnityEngine.Object> byType = new Dictionary<int, UnityEngine.Object>();
        readonly List<string> errors = new List<string>();
        readonly List<string> warnings = new List<string>();
        int current;

        [SetUp]
        public void SetUp()
        {
            // Building the fixture only: an agent with no NavMesh logs as it is added. Each test makes its own check below.
            LogAssert.ignoreFailingMessages = true;
            f = new BridgeApiFixture();
            types = BridgeApiThunks.BuildTypes();
            var physics = f.Add("physics", typeof(Rigidbody), typeof(BoxCollider), typeof(SphereCollider), typeof(CapsuleCollider), typeof(MeshCollider),
                typeof(FixedJoint), typeof(HingeJoint), typeof(SpringJoint), typeof(ConfigurableJoint));
            var scene = f.Add("rendering", typeof(MeshFilter), typeof(MeshRenderer), typeof(Light), typeof(AudioSource), typeof(Animator));
            var mesh = f.Track(new Mesh { vertices = new[] { Vector3.zero, Vector3.up, Vector3.right }, triangles = new[] { 0, 1, 2 } });
            scene.GetComponent<MeshFilter>().sharedMesh = mesh;
            var parts = new[]
            {
                physics, scene, f.Add("particles", typeof(ParticleSystem)), f.Add("line", typeof(LineRenderer)), f.Add("trail", typeof(TrailRenderer)),
                f.Add("skin", typeof(SkinnedMeshRenderer)), f.Add("agent", typeof(NavMeshAgent)), f.Add("text", typeof(TextMeshPro)),
                f.Add("stream", typeof(BSAudioStream)),
            };
            for (int id = 1; id < types.Length; id++)
            {
                var t = types[id];
                if (t == null) continue;
                if (t == typeof(Mesh)) byType[id] = mesh;
                else if (t == typeof(GameObject) || t == typeof(UnityEngine.Object)) byType[id] = f.Thing;
                else if (t == typeof(Transform) || t == typeof(Component)) byType[id] = f.Thing.transform;
                else
                {
                    var found = parts.Select(p => p.GetComponent(t)).FirstOrDefault(c => c != null);
                    if (found != null) byType[id] = found;
                }
            }
        }

        [TearDown]
        public void TearDown()
        {
            f.Dispose();
            LogAssert.ignoreFailingMessages = false;
        }

        int HandleFor(int typeId) => f.Handle(byType[typeId]);

        static float Number(Sweep s) => s == Sweep.Defaults ? 0 : s == Sweep.Negative ? -1 : s == Sweep.Large ? BridgeValues.MaxNumber : 3e38f;
        static int Whole(Sweep s) => s == Sweep.Defaults ? 0 : s == Sweep.Negative ? -1 : s == Sweep.Large ? 1000000 : int.MaxValue;
        static string Text(Sweep s) => s == Sweep.Defaults ? "a" : s == Sweep.Negative ? "no such name" : new string('x', s == Sweep.Large ? 300 : 60000);

        void WriteArgument(ByteWriter w, string kind, Sweep s)
        {
            float n = Number(s);
            int count = s == Sweep.Defaults ? 0 : 1;
            switch (kind)
            {
                case "b": BridgeValues.Bool(w, s != Sweep.Defaults); return;
                case "i": BridgeValues.Int(w, Whole(s)); return;
                case "f": BridgeValues.Float(w, n); return;
                case "d": BridgeValues.Double(w, n); return;
                case "s": BridgeValues.String(w, Text(s)); return;
                case "v2": BridgeValues.Vector(w, ValueTag.Vec2, n, n); return;
                case "v3": BridgeValues.Vector(w, ValueTag.Vec3, n, n, n); return;
                case "v4": BridgeValues.Vector(w, ValueTag.Vec4, n, n, n, n); return;
                case "q":
                    if (s == Sweep.Defaults) BridgeValues.Vector(w, ValueTag.Quat, 0, 0, 0, 1);
                    else if (s == Sweep.Negative) BridgeValues.Vector(w, ValueTag.Quat, 1, 2, 3, 4);
                    else BridgeValues.Vector(w, ValueTag.Quat, 0, 0, 0, 0);
                    return;
                case "c": BridgeValues.Vector(w, ValueTag.Color, n, n, n, n); return;
                case "t": BridgeValues.Int(w, Array.IndexOf(types, s == Sweep.Defaults ? typeof(Transform) : typeof(Mesh))); return;
                case "f[]": BridgeValues.ArrayHeader(w, ValueTag.Float, count); if (count > 0) w.F32(n); return;
                case "i[]": BridgeValues.ArrayHeader(w, ValueTag.Int, count); if (count > 0) w.Varint(BridgeValues.Zigzag(Whole(s))); return;
                case "s[]": BridgeValues.ArrayHeader(w, ValueTag.String, count); if (count > 0) w.Str(Text(s)); return;
                case "v2[]": BridgeValues.ArrayHeader(w, ValueTag.Vec2, count); if (count > 0) BridgeValues.ElementVector(w, ValueTag.Vec2, n, n); return;
                case "v3[]": BridgeValues.ArrayHeader(w, ValueTag.Vec3, count); if (count > 0) BridgeValues.ElementVector(w, ValueTag.Vec3, n, n, n); return;
            }
            if (kind[0] == 'e') BridgeValues.Int(w, int.Parse(kind.Substring(1)));
            else if (kind.EndsWith("[]")) BridgeValues.ArrayHeader(w, ValueTag.Ref, 0);
            else if (kind[0] == 'r') BridgeValues.Ref(w, HandleFor(int.Parse(kind.Substring(1))), 0);
            else Assert.Fail("unknown parameter kind " + kind);
        }

        byte[] Call(BridgeMember m, Sweep s)
        {
            var w = new ByteWriter();
            w.Varint(1);
            w.Varint((uint)m.Id);
            w.Varint(m.IsStatic ? 0 : (uint)HandleFor(m.SelfType));
            w.Varint((uint)m.ArgCount);
            if (m.ArgCount > 0)
                foreach (var kind in m.Params.Split(',')) WriteArgument(w, kind, s);
            return w.ToArray();
        }

        void OnLog(string message, string stack, LogType type)
        {
            string line = "member " + current + " " + type + ": " + message;
            if (type == LogType.Warning || type == LogType.Log) warnings.Add(line);
            else if (!Expected.Any(e => message.StartsWith(e.message))) errors.Add(line);
        }

        /// <summary>
        /// Every member once, each log kept with the member that caused it. LogAssert only says something was logged; this
        /// says by whom, and fails on every error that is not in Expected, so it stands in for LogAssert here.
        /// </summary>
        int Run(Sweep sweep)
        {
            LogAssert.ignoreFailingMessages = true;
            int ok = 0;
            var problems = new List<string>();
            Application.logMessageReceived += OnLog;
            try
            {
                foreach (var m in BridgeApiThunks.Build())
                {
                    if (m == null || m.Thunk == null) continue;
                    if (!m.IsStatic && !byType.ContainsKey(m.SelfType)) { problems.Add("no object of type " + m.SelfType + " for member " + m.Id); continue; }
                    current = m.Id;
                    // Written before the call, so a member that never returns is named here.
                    System.IO.File.WriteAllText("Temp/bridgeapi-sweep.txt", sweep + " " + m.Id);
                    byte status = f.Invoke(Call(m, sweep));
                    current = 0;
                    if (status == ReplyStatus.Ok) { ok++; continue; }
                    if (status == ReplyStatus.UnknownMember || status == ReplyStatus.Malformed) problems.Add("member " + m.Id + ": status " + status + " " + f.Host.FailMessage);
                    string text = f.Host.FailMessage ?? "";
                    if (status == ReplyStatus.Internal && (text.Contains("MissingMethod") || text.Contains("TypeLoad") || text.Contains("EntryPointNotFound")))
                        problems.Add("member " + m.Id + ": " + text);
                    f.Thing.SetActive(true);
                }
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
            }
            foreach (var w in warnings.Distinct()) TestContext.WriteLine(w);
            TestContext.WriteLine(ok + " members answered OK");
            Assert.IsEmpty(problems, string.Join("\n", problems));
            Assert.IsEmpty(errors, string.Join("\n", errors));
            return ok;
        }

        [Test]
        public void EveryMemberIsConnected()
        {
            var table = BridgeApiThunks.Build();
            int live = 0;
            foreach (var m in table)
            {
                if (m == null) continue;
                Assert.AreEqual(m.Id, Array.IndexOf(table, m), "the table is indexed by id");
                if (m.Tier == BridgeTier.Denied) Assert.IsNull(m.Thunk, "member " + m.Id);
                else { Assert.IsNotNull(m.Thunk, "member " + m.Id); live++; }
            }
            Assert.Greater(live, 0);
        }

        [Test]
        public void EveryThunkBinds() => Assert.Greater(Run(Sweep.Defaults), 300, "most members work on default arguments");

        [Test]
        public void NegativeArgumentsLogNoErrors() => Run(Sweep.Negative);

        [Test]
        public void LargeArgumentsLogNoErrors() => Run(Sweep.Large);

        [Test]
        public void ArgumentsPastTheLargestLogNoErrors() => Run(Sweep.Huge);
    }
}
