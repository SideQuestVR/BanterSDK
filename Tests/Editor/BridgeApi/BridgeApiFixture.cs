using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using BS.Bridge;
using UnityEngine;

namespace BS.BridgeApi.Tests
{
    /// <summary>
    /// A small world: a root with objects under it, and objects that are not the world's. The scope is the real one with
    /// its Unity lookups swapped, because in edit mode BSWorldScope accepts everything.
    /// </summary>
    internal sealed class BridgeApiFixture : IDisposable
    {
        public readonly GameObject Root = new GameObject("World Root");
        public readonly GameObject Thing = new GameObject("Thing");
        public readonly GameObject Outside = new GameObject("Outside");
        public readonly HashSet<GameObject> Registered = new HashSet<GameObject>();
        public readonly HashSet<GameObject> Persistent = new HashSet<GameObject>();
        public readonly HashSet<GameObject> NotTheWorldsScene = new HashSet<GameObject>();
        /// <summary>The world has no root yet.</summary>
        public bool NoRoot;
        public readonly BridgeWorldScope Scope;
        public readonly UnityHandles Handles;
        public readonly BridgeCallHost Host;
        readonly List<UnityEngine.Object> made = new List<UnityEngine.Object>();
        uint nextRequest = 1;
        public int Frame = 1;

        public BridgeApiFixture()
        {
            Thing.transform.SetParent(Root.transform);
            made.Add(Root);
            made.Add(Outside);
            Scope = new BridgeWorldScope(() => NoRoot ? null : Root.transform, Registered.Contains, go => !Persistent.Contains(go), go => !NotTheWorldsScene.Contains(go));
            Handles = new UnityHandles(Scope, id => FindRegistered(id), BridgeApiThunks.IsReadOnlyAsset, BridgeApiThunks.BuildTypes());
            Host = new BridgeCallHost(BridgeApiThunks.Build(), Handles, () => Frame);
        }

        GameObject FindRegistered(int id)
        {
            foreach (var go in Registered) if (BSScene.UnityId(go) == id) return go;
            return null;
        }

        /// <summary>An object of the world, under its root.</summary>
        public GameObject Add(string name, params Type[] components)
        {
            var go = new GameObject(name, components);
            go.transform.SetParent(Root.transform);
            return go;
        }

        /// <summary>Makes an object for the test, removed afterwards.</summary>
        public T Track<T>(T o) where T : UnityEngine.Object { made.Add(o); return o; }

        /// <summary>The handle a world would get for this object: minted, so the scope has passed it.</summary>
        public int Handle(UnityEngine.Object o)
        {
            int h = Handles.Mint(o, out _);
            if (h <= 0) throw new InvalidOperationException(o + " is not in the world");
            return h;
        }

        public byte Call(int member, int self, params Action<ByteWriter>[] args)
        {
            var w = new ByteWriter();
            w.Varint(nextRequest++);
            w.Varint((uint)member);
            w.Varint((uint)self);
            w.Varint((uint)args.Length);
            foreach (var a in args) a(w);
            return Invoke(w.ToArray());
        }

        public byte Invoke(byte[] call)
        {
            var c = new PayloadCursor(call);
            c.Varint();
            return Host.Invoke(ref c, call.Length);
        }

        public BridgeValue Result()
        {
            var c = new PayloadCursor(Host.Result);
            var v = default(BridgeValue);
            if (BridgeValues.Read(ref c, ref v, false) != ReplyStatus.Ok || !c.Done) throw new InvalidOperationException("the result is not one value");
            return v;
        }

        public static Action<ByteWriter> Vec3(Vector3 v) => w => BridgeValues.Vector(w, ValueTag.Vec3, v.x, v.y, v.z);
        public static Action<ByteWriter> Float(float v) => w => BridgeValues.Float(w, v);
        public static Action<ByteWriter> Int(int v) => w => BridgeValues.Int(w, v);
        public static Action<ByteWriter> Bool(bool v) => w => BridgeValues.Bool(w, v);
        public static Action<ByteWriter> Str(string v) => w => BridgeValues.String(w, v);
        public static Action<ByteWriter> Color(Color v) => w => BridgeValues.Vector(w, ValueTag.Color, v.r, v.g, v.b, v.a);
        public static Action<ByteWriter> Ref(int handle) => w => BridgeValues.Ref(w, handle, 0);
        public static readonly Action<ByteWriter> Null = w => BridgeValues.Null(w);

        public void Dispose()
        {
            Host.WorldUnloaded();
            foreach (var o in made) if (o != null) UnityEngine.Object.DestroyImmediate(o);
        }

        // ---------------------------------------------------------------------------------------- ids from the policy

        static Dictionary<string, int> ids;

        /// <summary>A member's id by its signature, or a curated one by "function/argument count".</summary>
        public static int Id(string name)
        {
            if (ids == null)
            {
                ids = new Dictionary<string, int>();
                var text = File.ReadAllText(Path.GetFullPath("Packages/com.sidequest.creator-sdk/Bridge~/unity-api.policy.json"));
                foreach (Match m in Regex.Matches(text, "\\{ *\"id\": (\\d+), \"sig\": \"([^\"]+)\"")) ids[m.Groups[2].Value] = int.Parse(m.Groups[1].Value);
                foreach (Match m in Regex.Matches(text, "\\{ *\"id\": (\\d+), \"curated\": \"([^\"]+)\", \"on\": \"[^\"]+\", \"js\": \"[^\"]+\", \"params\": \\[(.*?)\\], \"tier\""))
                    ids[m.Groups[2].Value + "/" + Regex.Matches(m.Groups[3].Value, "\\[\"").Count] = int.Parse(m.Groups[1].Value);
            }
            return ids[name];
        }
    }
}
