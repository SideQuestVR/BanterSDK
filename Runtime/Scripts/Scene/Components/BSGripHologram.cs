using SideQuest.FlexaBody;
using UnityEngine;
using UnityEngine.Rendering;

namespace BS
{
    /// <summary>One local hand's grip hint, fitted to the actual grab collider.</summary>
    public sealed class BSGripHologram : System.IDisposable
    {
        static readonly int OpacityProperty = Shader.PropertyToID("_Opacity");
        static Material material, depthMaterial;
        GripHandPose fitter;
        Collider fittedCollider;
        Vector3 fittedPosition, fittedBounds, fittedCenter, fittedScale;
        Quaternion fittedRotation;
        bool fittedLeft, fittedTrigger;
        double lastFit;
        MaterialPropertyBlock properties;
        Pose wrist;
        Mesh mesh;
        bool left;
        GrabHandle target;
        GrabHandle visualTarget;
        float opacity;

        public GrabHandle Target => target;
        public float Opacity => opacity;
        public bool IsVisible => opacity > 0f && mesh != null;

        public static Material SharedMaterial
        {
            get
            {
                if (material == null)
                    material = Resources.Load<Material>("CreatorConveniences/Materials/GripHologram");
                return material;
            }
        }

        internal static Material SharedDepthMaterial
        {
            get
            {
                if (depthMaterial == null)
                    depthMaterial = Resources.Load<Material>("CreatorConveniences/Materials/GripHologramDepth");
                return depthMaterial;
            }
        }

        internal Mesh Fit(Pose pose, bool isLeft, Collider collider, bool triggerReady)
        {
            var position = collider.transform.InverseTransformPoint(pose.position);
            var rotation = Quaternion.Inverse(collider.transform.rotation) * pose.rotation;
            var bounds = collider.bounds.size;
            var center = collider.transform.InverseTransformPoint(collider.bounds.center);
            var scale = collider.transform.lossyScale;
            var changed = mesh == null || fittedCollider != collider || fittedLeft != isLeft || fittedTrigger != triggerReady;
            var moved = (position - fittedPosition).sqrMagnitude > .000004f || Quaternion.Angle(rotation, fittedRotation) > 2f
                || (bounds - fittedBounds).sqrMagnitude > .000001f || (center - fittedCenter).sqrMagnitude > .000001f || scale != fittedScale;
            if (changed || moved && Time.realtimeSinceStartupAsDouble - lastFit >= .05)
            {
                if (fitter == null) fitter = new GripHandPose();
                mesh = fitter.Fit(pose, isLeft, collider, triggerReady);
                fittedCollider = collider;
                fittedPosition = position;
                fittedRotation = rotation;
                fittedBounds = bounds;
                fittedCenter = center;
                fittedScale = scale;
                fittedLeft = isLeft;
                fittedTrigger = triggerReady;
                lastFit = Time.realtimeSinceStartupAsDouble;
            }
            return mesh;
        }

        public static Pose GetWristPose(GrabHandle handle, PhysicsHand hand, Vector3 grabPoint)
        {
            var pose = handle.GetGrabPose(hand, grabPoint);
            var body = handle.RB;
            var rotation = (body != null ? body.rotation : handle.transform.rotation) * Quaternion.Inverse(pose.rotation);
            var anchor = body != null ? body.transform.TransformPoint(pose.position) : pose.position;
            return new Pose(anchor - rotation * grabPoint, rotation);
        }

        public void Update(GrabHandle candidate, PhysicsHand hand, Vector3 grabPoint, float deltaTime)
        {
            if (candidate != null && (hand == null || hand.RB == null || candidate.Col == null
                || !candidate.Col.enabled || !candidate.gameObject.activeInHierarchy
                || candidate.WorldObj != null && candidate.RB == null))
                candidate = null;
            // Keep the fading source so disabling its hint also removes a departing hand immediately.
            var visualSource = candidate != null ? candidate : visualTarget;
            if (visualSource != null && !RuntimeVisualEnabled(visualSource))
            {
                Clear();
                return;
            }
            if (candidate != null)
            {
                if (candidate != target) opacity = 0f;
                visualTarget = candidate;
                wrist = GetWristPose(candidate, hand, grabPoint);
                left = hand.HandId == HandID.Left;
                var held = candidate.GetComponent<BSHeldEvents>();
                Fit(wrist, left, candidate.Col, held != null && (left ? held.blockLeftTrigger : held.blockRightTrigger));
            }
            target = candidate;
            opacity = Mathf.MoveTowards(opacity, candidate != null ? 1f : 0f, Mathf.Max(0f, deltaTime) * 8f);
            if (opacity == 0f) visualTarget = null;
            if (!IsVisible || SharedMaterial == null || SharedDepthMaterial == null) return;
            if (properties == null) properties = new MaterialPropertyBlock();
            properties.SetFloat(OpacityProperty, opacity);
            var parameters = new RenderParams(SharedMaterial)
            {
                layer = 0,
                matProps = properties,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                lightProbeUsage = LightProbeUsage.Off,
                reflectionProbeUsage = ReflectionProbeUsage.Off
            };
            var matrix = Matrix(wrist, left);
            Graphics.RenderMesh(parameters, mesh, 0, matrix);
            // Populate depth after the world renders, then blend only the nearest hand surface.
            // shortcut: Custom transparency after queue 3100 can be masked; use a private depth target if required.
            parameters.material = SharedDepthMaterial;
            Graphics.RenderMesh(parameters, mesh, 0, matrix);
        }

        public void Clear()
        {
            target = null;
            visualTarget = null;
            opacity = 0f;
            mesh = null;
        }

        static bool RuntimeVisualEnabled(GrabHandle handle)
        {
            var settings = handle.GetComponent<BSGrabHandle>();
            var grabbable = handle.GetComponent<BSGrababble>();
            return (settings == null || settings.ShowRuntimeVisual)
                && (grabbable == null || grabbable.ShowRuntimeVisual);
        }

        public void Dispose()
        {
            Clear();
            fitter?.Dispose();
            fitter = null;
            fittedCollider = null;
        }

        static Matrix4x4 Matrix(Pose pose, bool left) =>
            Matrix4x4.TRS(pose.position, pose.rotation, new Vector3(left ? -1f : 1f, 1f, 1f));

#if UNITY_EDITOR
        public void DrawEditorPreview(Pose pose, bool left, Collider collider, bool triggerReady = false)
        {
            var mesh = Fit(pose, left, collider, triggerReady);
            if (mesh == null || SharedMaterial == null || SharedDepthMaterial == null) return;
            SharedDepthMaterial.SetPass(0);
            Graphics.DrawMeshNow(mesh, Matrix(pose, left));
            SharedMaterial.SetPass(0);
            Graphics.DrawMeshNow(mesh, Matrix(pose, left));
        }
#endif
    }
}
