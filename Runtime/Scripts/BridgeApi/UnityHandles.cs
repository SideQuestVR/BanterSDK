using System;
using System.Collections.Generic;
using BS.Bridge;
using UnityEngine;

namespace BS.BridgeApi
{
    /// <summary>
    /// The current world's handle table. A handle is the object's unityId, which is never reused, so a stale handle can only
    /// find its own dead object. Every object in or out goes through the world's scope.
    /// </summary>
    internal sealed class UnityHandles : IBridgeHandles
    {
        public const int MaxHandles = 65536;

        readonly BridgeWorldScope scope;
        readonly Func<int, GameObject> findRegistered;
        readonly Func<object, bool> isReadOnlyAsset;
        readonly Type[] typesById;
        readonly Dictionary<int, UnityEngine.Object> table = new Dictionary<int, UnityEngine.Object>();
        readonly Dictionary<Type, int> typeIds = new Dictionary<Type, int>();

        public UnityHandles(BridgeWorldScope scope, Func<int, GameObject> findRegistered, Func<object, bool> isReadOnlyAsset, Type[] typesById)
        {
            this.scope = scope;
            this.findRegistered = findRegistered;
            this.isReadOnlyAsset = isReadOnlyAsset;
            this.typesById = typesById;
            for (int id = 0; id < typesById.Length; id++)
                if (typesById[id] != null) typeIds[typesById[id]] = id;
        }

        public int Count => table.Count;
        public Transform WorldRoot => scope.Root;

        public object Resolve(int handle, out byte status)
        {
            if (table.TryGetValue(handle, out var o))
            {
                if (o == null) { status = ReplyStatus.Destroyed; return null; }
                // Asked every time: it may have left the world since the handle was given out.
                if (!InScope(o)) { status = ReplyStatus.Denied; return null; }
                status = ReplyStatus.Ok;
                return o;
            }
            var go = findRegistered(handle);
            if (go == null) { status = ReplyStatus.UnknownHandle; return null; }
            if (!InScope(go)) { status = ReplyStatus.Denied; return null; }
            if (!Room()) { status = ReplyStatus.Limit; return null; }
            table[handle] = go;
            status = ReplyStatus.Ok;
            return go;
        }

        public int Mint(object obj, out int typeId)
        {
            typeId = 0;
            var o = obj as UnityEngine.Object;
            if (o == null || !InScope(o)) return 0;
            int handle = BSScene.UnityId(o);
            if (!table.ContainsKey(handle))
            {
                if (!Room()) return -1;
                table[handle] = o;
            }
            typeId = TypeIdOf(o);
            return handle;
        }

        bool InScope(UnityEngine.Object o)
        {
            if (o is GameObject go) return scope.Contains(go);
            if (o is Component c) return scope.Contains(c.gameObject);
            return isReadOnlyAsset(o);
        }

        /// <summary>A full table sheds its dead entries first; the live ones are what the world is holding.</summary>
        bool Room()
        {
            if (table.Count < MaxHandles) return true;
            var dead = new List<int>();
            foreach (var e in table) if (e.Value == null) dead.Add(e.Key);
            foreach (int key in dead) table.Remove(key);
            return table.Count < MaxHandles;
        }

        /// <summary>The nearest listed type: a world's subclass of Collider shows as the Collider it is.</summary>
        int TypeIdOf(UnityEngine.Object o)
        {
            var t = o.GetType();
            if (typeIds.TryGetValue(t, out int id)) return id;
            for (var b = t.BaseType; b != null; b = b.BaseType)
            {
                if (!typeIds.TryGetValue(b, out id)) continue;
                typeIds[t] = id;
                return id;
            }
            return 0;
        }

        public void Clear()
        {
            table.Clear();
            scope.Clear();
            BridgeApiCurated.Reset();
            BridgeApiGuards.Reset();
            BridgeApiArgs.TransformsMoved = false;
        }
    }
}
