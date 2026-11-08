using System;
using System.Collections.Generic;
using BS.Bridge;
using UnityEngine;

namespace BS.BridgeApi
{
    /// <summary>
    /// Members that can't be a plain thunk: they clamp, cap, filter or go through a property block. The policy names each
    /// one and declares its signature ("curated" entries); the compiler checks the two agree. Functions taking the context
    /// write their own result.
    /// </summary>
    internal static class BridgeApiCurated
    {
        public const int MaxHits = 128;
        /// <summary>What Unity hands back before out-of-world hits are dropped, so a crowd of them can't hide the world's own.</summary>
        const int HitBuffer = 512;
        public const int MaxMeshVertices = 65535;
        public const int MaxMeshIndices = 196608;
        public const int MaxLinePositions = 16384;
        /// <summary>Shader.PropertyToID keeps every name it sees for the life of the process, so a world gets this many.</summary>
        public const int MaxPropertyNames = 1024;
        /// <summary>The same across worlds, since those names stay too.</summary>
        internal static int MaxProcessPropertyNames = 8192;

        static readonly RaycastHit[] hits = new RaycastHit[HitBuffer];
        static readonly Collider[] colliders = new Collider[HitBuffer];
        static readonly Type[] componentTypes = BridgeApiThunks.BuildComponentTypes();
        static readonly HashSet<string> processPropertyNames = new HashSet<string>();
        static MaterialPropertyBlock scratchBlock;
        static readonly Dictionary<string, int> propertyIds = new Dictionary<string, int>();
        static readonly List<Component> components = new List<Component>();

        static MaterialPropertyBlock Block() => scratchBlock ??= new MaterialPropertyBlock();

        /// <summary>The world has gone.</summary>
        internal static void Reset() => propertyIds.Clear();

        // ------------------------------------------------------------------------------------------- hierarchy

        /// <summary>Null is the world root. Ok means the object moved.</summary>
        public static void Transform_SetParent(BridgeCallContext c, Transform self, Transform parent, bool worldPositionStays = true)
        {
            var target = parent != null ? parent : ((UnityHandles)c.Handles).WorldRoot;
            if (target == null) { c.Fail(ReplyStatus.NotReady, "there is no world root to move to"); return; }
            if (target.IsChildOf(self)) { c.Fail(ReplyStatus.Type, "an object can't move under itself"); return; }
            self.SetParent(target, worldPositionStays);
            if (self.parent != target) { c.Fail(ReplyStatus.Internal, "the object did not move"); return; }
            BridgeApiArgs.TransformsMoved = true;
        }

        /// <summary>A plain comparison, so any string is a fair question: Unity's own reports a tag the project does not define.</summary>
        public static bool Component_CompareTag(Component self, string tag) => self.tag == tag;
        public static bool GameObject_CompareTag(GameObject self, string tag) => self.tag == tag;

        static Type ComponentType(BridgeCallContext c, int typeId)
        {
            var t = typeId > 0 && typeId < componentTypes.Length ? componentTypes[typeId] : null;
            if (t == null) c.Fail(ReplyStatus.Type, "not a component class");
            return t;
        }

        public static void GameObject_GetComponent(BridgeCallContext c, GameObject self, int typeId)
        {
            var t = ComponentType(c, typeId);
            if (!c.Failed) c.RetRef(self.GetComponent(t));
        }

        public static void GameObject_GetComponents(BridgeCallContext c, GameObject self, int typeId)
        {
            var t = ComponentType(c, typeId);
            if (c.Failed) return;
            components.Clear();
            self.GetComponents(t, components);
            c.RetRefs(components, BridgeApiArgs.MaxRefs);
            components.Clear();
        }

        public static void GameObject_GetComponentInChildren(BridgeCallContext c, GameObject self, int typeId, bool includeInactive = false)
        {
            var t = ComponentType(c, typeId);
            if (!c.Failed) c.RetRef(self.GetComponentInChildren(t, includeInactive));
        }

        public static void GameObject_GetComponentInParent(BridgeCallContext c, GameObject self, int typeId, bool includeInactive = false)
        {
            var t = ComponentType(c, typeId);
            if (!c.Failed) c.RetRef(self.GetComponentInParent(t, includeInactive));
        }

        public static void GameObject_GetComponentsInChildren(BridgeCallContext c, GameObject self, int typeId, bool includeInactive = false)
        {
            var t = ComponentType(c, typeId);
            if (!c.Failed) c.RetRefs(self.GetComponentsInChildren(t, includeInactive), BridgeApiArgs.MaxRefs);
        }

        // ------------------------------------------------------------------------------------------- physics

        const int DefaultMask = Physics.DefaultRaycastLayers;
        const QueryTriggerInteraction UseGlobal = QueryTriggerInteraction.UseGlobal;

        static void Sync()
        {
            if (!BridgeApiArgs.TransformsMoved) return;
            BridgeApiArgs.TransformsMoved = false;
            Physics.SyncTransforms();
        }

        /// <summary>Hits on what the world may see, nearest first, at most MaxHits.</summary>
        static void RetHits(BridgeCallContext c, int count)
        {
            Array.Sort(hits, 0, count, HitsByDistance.Instance);
            var w = new List<(int handle, int type, int index)>(Math.Min(count, MaxHits));
            for (int i = 0; i < count && w.Count < MaxHits; i++)
            {
                int h = c.Handles.Mint(hits[i].collider, out int type);
                if (h < 0) { c.Fail(ReplyStatus.Limit, "too many objects held by this world"); return; }
                if (h > 0) w.Add((h, type, i));
            }
            BridgeValues.ArrayHeader(c.Out, ValueTag.Record, w.Count);
            foreach (var (h, type, i) in w) WriteHit(c, h, type, hits[i], true);
        }

        static void WriteHit(BridgeCallContext c, int handle, int type, in RaycastHit hit, bool element)
        {
            BridgeValues.RecordHeader(c.Out, RecordType.RaycastHit, 4, element);
            BridgeValues.Ref(c.Out, handle, type);
            BridgeApiArgs.Ret(c, hit.point);
            BridgeApiArgs.Ret(c, hit.normal);
            BridgeValues.Float(c.Out, hit.distance);
        }

        /// <summary>The nearest hit the world may see, or null.</summary>
        static void RetNearest(BridgeCallContext c, int count)
        {
            Array.Sort(hits, 0, count, HitsByDistance.Instance);
            for (int i = 0; i < count; i++)
            {
                int h = c.Handles.Mint(hits[i].collider, out int type);
                if (h < 0) { c.Fail(ReplyStatus.Limit, "too many objects held by this world"); return; }
                if (h == 0) continue;
                WriteHit(c, h, type, hits[i], false);
                return;
            }
            c.RetNull();
        }

        sealed class HitsByDistance : IComparer<RaycastHit>
        {
            public static readonly HitsByDistance Instance = new HitsByDistance();
            public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
        }

        static void RetColliders(BridgeCallContext c, int count)
        {
            var list = new List<Collider>(count);
            for (int i = 0; i < count; i++) list.Add(colliders[i]);
            c.RetRefs(list, MaxHits);
        }

        static int Cast(Vector3 origin, Vector3 direction, float maxDistance, int layerMask, QueryTriggerInteraction triggers)
        {
            Sync();
            return Physics.RaycastNonAlloc(origin, direction, hits, maxDistance, layerMask, triggers);
        }

        public static void Physics_Raycast(BridgeCallContext c, Vector3 origin, Vector3 direction, float maxDistance = float.PositiveInfinity, int layerMask = DefaultMask, QueryTriggerInteraction triggers = UseGlobal)
            => RetNearest(c, Cast(origin, direction, maxDistance, layerMask, triggers));

        public static void Physics_RaycastAll(BridgeCallContext c, Vector3 origin, Vector3 direction, float maxDistance = float.PositiveInfinity, int layerMask = DefaultMask, QueryTriggerInteraction triggers = UseGlobal)
            => RetHits(c, Cast(origin, direction, maxDistance, layerMask, triggers));

        public static void Physics_Linecast(BridgeCallContext c, Vector3 start, Vector3 end, int layerMask = DefaultMask, QueryTriggerInteraction triggers = UseGlobal)
        {
            var d = end - start;
            RetNearest(c, Cast(start, d, d.magnitude, layerMask, triggers));
        }

        static int CastSphere(Vector3 origin, float radius, Vector3 direction, float maxDistance, int layerMask, QueryTriggerInteraction triggers)
        {
            Sync();
            return Physics.SphereCastNonAlloc(origin, radius, direction.normalized, hits, maxDistance, layerMask, triggers);
        }

        public static void Physics_SphereCast(BridgeCallContext c, Vector3 origin, float radius, Vector3 direction, float maxDistance = float.PositiveInfinity, int layerMask = DefaultMask, QueryTriggerInteraction triggers = UseGlobal)
            => RetNearest(c, CastSphere(origin, radius, direction, maxDistance, layerMask, triggers));

        public static void Physics_SphereCastAll(BridgeCallContext c, Vector3 origin, float radius, Vector3 direction, float maxDistance = float.PositiveInfinity, int layerMask = DefaultMask, QueryTriggerInteraction triggers = UseGlobal)
            => RetHits(c, CastSphere(origin, radius, direction, maxDistance, layerMask, triggers));

        public static void Physics_CapsuleCastAll(BridgeCallContext c, Vector3 point1, Vector3 point2, float radius, Vector3 direction, float maxDistance = float.PositiveInfinity, int layerMask = DefaultMask, QueryTriggerInteraction triggers = UseGlobal)
        {
            Sync();
            RetHits(c, Physics.CapsuleCastNonAlloc(point1, point2, radius, direction.normalized, hits, maxDistance, layerMask, triggers));
        }

        public static void Physics_OverlapSphere(BridgeCallContext c, Vector3 position, float radius, int layerMask = Physics.AllLayers, QueryTriggerInteraction triggers = UseGlobal)
        {
            Sync();
            RetColliders(c, Physics.OverlapSphereNonAlloc(position, radius, colliders, layerMask, triggers));
        }

        public static void Physics_OverlapBox(BridgeCallContext c, Vector3 center, Vector3 halfExtents) => Physics_OverlapBox(c, center, halfExtents, Quaternion.identity);

        public static void Physics_OverlapBox(BridgeCallContext c, Vector3 center, Vector3 halfExtents, Quaternion orientation, int layerMask = Physics.AllLayers, QueryTriggerInteraction triggers = UseGlobal)
        {
            Sync();
            RetColliders(c, Physics.OverlapBoxNonAlloc(center, halfExtents, colliders, orientation, layerMask, triggers));
        }

        public static void Physics_OverlapCapsule(BridgeCallContext c, Vector3 point0, Vector3 point1, float radius, int layerMask = Physics.AllLayers, QueryTriggerInteraction triggers = UseGlobal)
        {
            Sync();
            RetColliders(c, Physics.OverlapCapsuleNonAlloc(point0, point1, radius, colliders, layerMask, triggers));
        }

        // ------------------------------------------------------------------------------------------- material properties

        /// <summary>The id of a shader property name, or -1 once the world has used up its names.</summary>
        static int PropertyId(BridgeCallContext c, string name)
        {
            if (name.Length == 0) { c.Fail(ReplyStatus.Type, "a material property needs a name"); return -1; }
            if (propertyIds.TryGetValue(name, out int id)) return id;
            if (propertyIds.Count >= MaxPropertyNames)
            {
                c.Fail(ReplyStatus.Limit, "too many material property names in this world");
                return -1;
            }
            if (!processPropertyNames.Contains(name))
            {
                if (processPropertyNames.Count >= MaxProcessPropertyNames)
                {
                    c.Fail(ReplyStatus.Limit, "too many material property names");
                    return -1;
                }
                processPropertyNames.Add(name);
            }
            id = Shader.PropertyToID(name);
            propertyIds[name] = id;
            return id;
        }

        enum PropertyKind { Float, Integer, Vector }

        // All material writes go through a property block on this renderer: the material itself may be shared with other
        // objects, and a block survives the material being swapped under it. materialIndex -1 means the whole renderer.
        /// <summary>The block to write a property into, or null when the call has failed. A property keeps its kind in a block.</summary>
        static MaterialPropertyBlock OpenBlock(BridgeCallContext c, Renderer r, string name, int materialIndex, PropertyKind kind, out int id)
        {
            id = PropertyId(c, name);
            if (c.Failed || !IndexOk(c, r, materialIndex)) return null;
            var block = Block();
            if (materialIndex >= 0) r.GetPropertyBlock(block, materialIndex);
            else r.GetPropertyBlock(block);
            bool clash = kind == PropertyKind.Float ? block.HasInteger(id) || block.HasVector(id)
                : kind == PropertyKind.Integer ? block.HasFloat(id) || block.HasVector(id)
                : block.HasFloat(id) || block.HasInteger(id);
            if (!clash) return block;
            c.Fail(ReplyStatus.Type, "the property " + name + " has another type on this renderer");
            return null;
        }

        static void CloseBlock(Renderer r, MaterialPropertyBlock block, int materialIndex)
        {
            if (materialIndex >= 0) r.SetPropertyBlock(block, materialIndex);
            else r.SetPropertyBlock(block);
        }

        static bool IndexOk(BridgeCallContext c, Renderer self, int materialIndex)
        {
            if (materialIndex >= -1 && materialIndex < self.sharedMaterials.Length) return true;
            c.Fail(ReplyStatus.Type, "no material at index " + materialIndex);
            return false;
        }

        public static void Renderer_SetMaterialFloat(BridgeCallContext c, Renderer self, string name, float value, int materialIndex = -1)
        {
            var block = OpenBlock(c, self, name, materialIndex, PropertyKind.Float, out int id);
            if (block == null) return;
            block.SetFloat(id, value);
            CloseBlock(self, block, materialIndex);
        }

        public static void Renderer_SetMaterialInteger(BridgeCallContext c, Renderer self, string name, int value, int materialIndex = -1)
        {
            var block = OpenBlock(c, self, name, materialIndex, PropertyKind.Integer, out int id);
            if (block == null) return;
            block.SetInteger(id, value);
            CloseBlock(self, block, materialIndex);
        }

        public static void Renderer_SetMaterialVector(BridgeCallContext c, Renderer self, string name, Vector4 value, int materialIndex = -1)
        {
            var block = OpenBlock(c, self, name, materialIndex, PropertyKind.Vector, out int id);
            if (block == null) return;
            block.SetVector(id, value);
            CloseBlock(self, block, materialIndex);
        }

        public static void Renderer_SetMaterialColor(BridgeCallContext c, Renderer self, string name, Color value, int materialIndex = -1)
        {
            var block = OpenBlock(c, self, name, materialIndex, PropertyKind.Vector, out int id);
            if (block == null) return;
            block.SetColor(id, value);
            CloseBlock(self, block, materialIndex);
        }

        /// <summary>The block's value if it has one, otherwise the material's.</summary>
        public static void Renderer_GetMaterialFloat(BridgeCallContext c, Renderer self, string name)
        {
            int id = PropertyId(c, name);
            if (c.Failed) return;
            var block = Block();
            self.GetPropertyBlock(block);
            var m = self.sharedMaterial;
            c.RetFloat(block.HasFloat(id) ? block.GetFloat(id) : m != null && m.HasFloat(id) ? m.GetFloat(id) : 0);
        }

        public static void Renderer_GetMaterialVector(BridgeCallContext c, Renderer self, string name)
        {
            int id = PropertyId(c, name);
            if (c.Failed) return;
            var block = Block();
            self.GetPropertyBlock(block);
            var m = self.sharedMaterial;
            BridgeApiArgs.Ret(c, block.HasVector(id) ? block.GetVector(id) : m != null && m.HasVector(id) ? m.GetVector(id) : Vector4.zero);
        }

        public static void Renderer_GetMaterialColor(BridgeCallContext c, Renderer self, string name)
        {
            int id = PropertyId(c, name);
            if (c.Failed) return;
            var block = Block();
            self.GetPropertyBlock(block);
            var m = self.sharedMaterial;
            BridgeApiArgs.Ret(c, block.HasColor(id) ? block.GetColor(id) : m != null && m.HasColor(id) ? m.GetColor(id) : Color.clear);
        }

        public static void Renderer_HasMaterialProperty(BridgeCallContext c, Renderer self, string name)
        {
            int id = PropertyId(c, name);
            if (c.Failed) return;
            var m = self.sharedMaterial;
            c.RetBool(m != null && m.HasProperty(id));
        }

        public static void Renderer_ClearMaterialProperties(Renderer self)
        {
            self.SetPropertyBlock(null);
            for (int i = 0; i < self.sharedMaterials.Length; i++) self.SetPropertyBlock(null, i);
        }

        public static int Renderer_GetMaterialCount(Renderer self) => self.sharedMaterials.Length;

        // ------------------------------------------------------------------------------------------- particles

        public static void ParticleSystem_SetStartColor(ParticleSystem self, Color value) { var m = self.main; m.startColor = value; }
        public static void ParticleSystem_SetStartSize(ParticleSystem self, float value) { var m = self.main; m.startSize = value; }
        public static void ParticleSystem_SetStartSpeed(ParticleSystem self, float value) { var m = self.main; m.startSpeed = value; }
        public static void ParticleSystem_SetStartLifetime(ParticleSystem self, float value) { var m = self.main; m.startLifetime = value; }
        public static void ParticleSystem_SetGravityModifier(ParticleSystem self, float value) { var m = self.main; m.gravityModifier = value; }
        public static void ParticleSystem_SetSimulationSpeed(ParticleSystem self, float value) { var m = self.main; m.simulationSpeed = value; }
        public static void ParticleSystem_SetLoop(ParticleSystem self, bool value) { var m = self.main; m.loop = value; }
        public static bool ParticleSystem_GetLoop(ParticleSystem self) => self.main.loop;
        public static void ParticleSystem_SetMaxParticles(ParticleSystem self, int value) { var m = self.main; m.maxParticles = value; }
        public static int ParticleSystem_GetMaxParticles(ParticleSystem self) => self.main.maxParticles;
        public static void ParticleSystem_SetEmissionEnabled(ParticleSystem self, bool value) { var e = self.emission; e.enabled = value; }
        public static bool ParticleSystem_GetEmissionEnabled(ParticleSystem self) => self.emission.enabled;
        public static void ParticleSystem_SetEmissionRate(ParticleSystem self, float value) { var e = self.emission; e.rateOverTime = value; }
        public static float ParticleSystem_GetEmissionRate(ParticleSystem self) => self.emission.rateOverTime.constant;

        // ------------------------------------------------------------------------------------------- lines and trails

        public static void LineRenderer_GetPositions(BridgeCallContext c, LineRenderer self)
        {
            int n = self.positionCount;
            if (n > MaxLinePositions) { c.Fail(ReplyStatus.Limit, "more than " + MaxLinePositions + " positions"); return; }
            var a = new Vector3[n];
            self.GetPositions(a);
            BridgeApiArgs.Ret(c, a);
        }

        public static void TrailRenderer_GetPositions(BridgeCallContext c, TrailRenderer self)
        {
            int n = self.positionCount;
            if (n > MaxLinePositions) { c.Fail(ReplyStatus.Limit, "more than " + MaxLinePositions + " positions"); return; }
            var a = new Vector3[n];
            self.GetPositions(a);
            BridgeApiArgs.Ret(c, a);
        }

        // ------------------------------------------------------------------------------------------- joints

        public static void HingeJoint_SetMotor(HingeJoint self, float targetVelocity, float force, bool freeSpin)
            => self.motor = new JointMotor { targetVelocity = targetVelocity, force = force, freeSpin = freeSpin };

        public static void HingeJoint_SetLimits(HingeJoint self, float min, float max, float bounciness)
        {
            var l = self.limits;
            l.min = min;
            l.max = max;
            l.bounciness = bounciness;
            self.limits = l;
        }

        public static void HingeJoint_SetSpring(HingeJoint self, float spring, float damper, float targetPosition)
            => self.spring = new JointSpring { spring = spring, damper = damper, targetPosition = targetPosition };

        // ------------------------------------------------------------------------------------------- mesh reads

        static bool Readable(BridgeCallContext c, Mesh self)
        {
            if (!self.isReadable) { c.Fail(ReplyStatus.Unsupported, "this mesh can't be read: it was imported without Read/Write"); return false; }
            if (self.vertexCount <= MaxMeshVertices) return true;
            c.Fail(ReplyStatus.Limit, "more than " + MaxMeshVertices + " vertices");
            return false;
        }

        public static void Mesh_GetVertices(BridgeCallContext c, Mesh self) { if (Readable(c, self)) BridgeApiArgs.Ret(c, self.vertices); }
        public static void Mesh_GetNormals(BridgeCallContext c, Mesh self) { if (Readable(c, self)) BridgeApiArgs.Ret(c, self.normals); }
        public static void Mesh_GetUv(BridgeCallContext c, Mesh self) { if (Readable(c, self)) BridgeApiArgs.Ret(c, self.uv); }

        public static void Mesh_GetTriangles(BridgeCallContext c, Mesh self)
        {
            if (!Readable(c, self)) return;
            long n = 0;
            for (int i = 0; i < self.subMeshCount; i++) n += self.GetIndexCount(i);
            if (n > MaxMeshIndices) { c.Fail(ReplyStatus.Limit, "more than " + MaxMeshIndices + " indices"); return; }
            BridgeApiArgs.Ret(c, self.triangles);
        }

        // ------------------------------------------------------------------------------------------- audio streams

        /// <summary>A count past int.MaxValue reads as int.MaxValue.</summary>
        public static int AudioStream_GetUnderruns(BSAudioStream self) => (int)Math.Min(self.Underruns, int.MaxValue);
    }
}
