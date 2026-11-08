using System.Collections.Generic;
using BS.Bridge;
using UnityEngine;
using UnityEngine.AI;

namespace BS.BridgeApi
{
    /// <summary>
    /// Checks a member makes before Unity sees its arguments: what Unity would only log about is answered instead, so the
    /// page gets the reason and the log stays quiet. The policy names each one ("guard"); it runs after the arguments are
    /// read and before the member is called.
    /// </summary>
    internal static class BridgeApiGuards
    {
        static readonly Dictionary<RuntimeAnimatorController, Dictionary<string, AnimatorControllerParameterType>> parameters =
            new Dictionary<RuntimeAnimatorController, Dictionary<string, AnimatorControllerParameterType>>();

        const int MaxSimplifyPositions = 4096;

        /// <summary>The world has gone.</summary>
        internal static void Reset() => parameters.Clear();

        static bool Enabled(Behaviour b) => b.enabled && b.gameObject.activeInHierarchy;

        // ------------------------------------------------------------------------------------------- transforms and physics

        public static void NotZero(BridgeCallContext c, Transform self, Vector3 direction)
        {
            if (direction.sqrMagnitude < 1e-12f) c.Fail(ReplyStatus.Type, "the direction can't be zero");
        }

        public static void NotNegative(BridgeCallContext c, Rigidbody self, Vector3 value)
        {
            if (value.x < 0 || value.y < 0 || value.z < 0) c.Fail(ReplyStatus.Type, "the value can't be negative");
        }

        public static void JointBody(BridgeCallContext c, Joint self, Rigidbody body)
        {
            if (body != null && body.gameObject == self.gameObject) c.Fail(ReplyStatus.Type, "a joint can't connect to its own body");
        }

        public static void Dynamic(BridgeCallContext c, Rigidbody self)
        {
            if (self.isKinematic) c.Fail(ReplyStatus.NotReady, "this body is kinematic");
        }

        public static void MayMove(BridgeCallContext c, Rigidbody self, bool kinematic)
        {
            if (kinematic) return;
            foreach (var mesh in self.GetComponentsInChildren<MeshCollider>(true))
            {
                if (!mesh.enabled || mesh.convex || mesh.sharedMesh == null || mesh.attachedRigidbody != self) continue;
                c.Fail(ReplyStatus.Type, "a body with a concave mesh collider stays kinematic");
                return;
            }
        }

        // ------------------------------------------------------------------------------------------- animators

        public static void AnimatorRunning(BridgeCallContext c, Animator self)
        {
            if (Enabled(self) && self.runtimeAnimatorController != null && self.isInitialized) return;
            c.Fail(ReplyStatus.NotReady, "this animator is not running a controller");
        }

        static Dictionary<string, AnimatorControllerParameterType> ParametersOf(Animator self)
        {
            var controller = self.runtimeAnimatorController;
            if (parameters.TryGetValue(controller, out var map)) return map;
            map = new Dictionary<string, AnimatorControllerParameterType>();
            foreach (var p in self.parameters) map[p.name] = p.type;
            parameters[controller] = map;
            return map;
        }

        static void Parameter(BridgeCallContext c, Animator self, string name, AnimatorControllerParameterType type, AnimatorControllerParameterType also)
        {
            AnimatorRunning(c, self);
            if (c.Failed) return;
            if (!ParametersOf(self).TryGetValue(name, out var found)) c.Fail(ReplyStatus.Type, "this animator has no parameter " + name);
            else if (found != type && found != also) c.Fail(ReplyStatus.Type, "the parameter " + name + " is a " + found);
        }

        public static void AnimatorFloat(BridgeCallContext c, Animator self, string name) => Parameter(c, self, name, AnimatorControllerParameterType.Float, AnimatorControllerParameterType.Float);
        public static void AnimatorInteger(BridgeCallContext c, Animator self, string name) => Parameter(c, self, name, AnimatorControllerParameterType.Int, AnimatorControllerParameterType.Int);
        public static void AnimatorBool(BridgeCallContext c, Animator self, string name) => Parameter(c, self, name, AnimatorControllerParameterType.Bool, AnimatorControllerParameterType.Trigger);
        public static void AnimatorTrigger(BridgeCallContext c, Animator self, string name) => Parameter(c, self, name, AnimatorControllerParameterType.Trigger, AnimatorControllerParameterType.Trigger);

        public static void AnimatorAnyParameter(BridgeCallContext c, Animator self, string name)
        {
            AnimatorRunning(c, self);
            if (!c.Failed && !ParametersOf(self).ContainsKey(name)) c.Fail(ReplyStatus.Type, "this animator has no parameter " + name);
        }

        public static void AnimatorLayer(BridgeCallContext c, Animator self, int layer)
        {
            AnimatorRunning(c, self);
            if (!c.Failed && (layer < 0 || layer >= self.layerCount)) c.Fail(ReplyStatus.Type, "this animator has no layer " + layer);
        }

        /// <summary>layer -1: any layer, as Play takes it.</summary>
        public static void AnimatorState(BridgeCallContext c, Animator self, string name, int layer = -1)
        {
            AnimatorRunning(c, self);
            if (c.Failed) return;
            int count = self.layerCount;
            if (layer < -1 || layer >= count) { c.Fail(ReplyStatus.Type, "this animator has no layer " + layer); return; }
            int state = Animator.StringToHash(name);
            for (int i = layer < 0 ? 0 : layer; i < (layer < 0 ? count : layer + 1); i++)
                if (self.HasState(i, state)) return;
            c.Fail(ReplyStatus.Type, "this animator has no state " + name);
        }

        public static void AnimatorCrossFade(BridgeCallContext c, Animator self, string name, float duration, int layer = -1) => AnimatorState(c, self, name, layer);

        // ------------------------------------------------------------------------------------------- audio

        public static void HasClip(BridgeCallContext c, AudioSource self)
        {
            if (self.clip == null) c.Fail(ReplyStatus.NotReady, "this audio source has no clip");
        }

        public static void AudioEnabled(BridgeCallContext c, AudioSource self)
        {
            if (!Enabled(self)) c.Fail(ReplyStatus.NotReady, "this audio source is disabled");
        }

        public static void AudioTime(BridgeCallContext c, AudioSource self, float time)
        {
            HasClip(c, self);
            if (!c.Failed && (time < 0 || time > self.clip.length)) c.Fail(ReplyStatus.Denied, "the time is outside the clip");
        }

        public static void AudioSamples(BridgeCallContext c, AudioSource self, int samples)
        {
            HasClip(c, self);
            if (!c.Failed && (samples < 0 || samples > self.clip.samples)) c.Fail(ReplyStatus.Denied, "the position is outside the clip");
        }

        // ------------------------------------------------------------------------------------------- particles

        public static void ParticleStopped(BridgeCallContext c, ParticleSystem self)
        {
            if (!self.isStopped) c.Fail(ReplyStatus.NotReady, "stop this system first");
        }

        // ------------------------------------------------------------------------------------------- indexes

        public static void LineIndex(BridgeCallContext c, LineRenderer self, int index)
        {
            if (index < 0 || index >= self.positionCount) c.Fail(ReplyStatus.Type, "no position at index " + index);
        }

        public static void TrailIndex(BridgeCallContext c, TrailRenderer self, int index)
        {
            if (index < 0 || index >= self.positionCount) c.Fail(ReplyStatus.Type, "no position at index " + index);
        }

        public static void LineSimplify(BridgeCallContext c, LineRenderer self)
        {
            if (self.positionCount > MaxSimplifyPositions) c.Fail(ReplyStatus.Limit, "more than " + MaxSimplifyPositions + " positions");
        }

        public static void TrailHasRoom(BridgeCallContext c, TrailRenderer self)
        {
            if (self.positionCount >= BridgeApiCurated.MaxLinePositions) c.Fail(ReplyStatus.Limit, "this trail is full");
        }

        public static void TrailHasRoomFor(BridgeCallContext c, TrailRenderer self, Vector3[] positions)
        {
            if (self.positionCount + positions.Length > BridgeApiCurated.MaxLinePositions) c.Fail(ReplyStatus.Limit, "this trail would be too long");
        }

        public static void BlendShape(BridgeCallContext c, SkinnedMeshRenderer self, int index)
        {
            var mesh = self.sharedMesh;
            if (mesh == null || index < 0 || index >= mesh.blendShapeCount) c.Fail(ReplyStatus.Type, "no blend shape at index " + index);
        }

        // ------------------------------------------------------------------------------------------- navigation

        public static void OnNavMesh(BridgeCallContext c, NavMeshAgent self)
        {
            if (!Enabled(self) || !self.isOnNavMesh) c.Fail(ReplyStatus.NotReady, "this agent is not on a NavMesh");
        }
    }
}
