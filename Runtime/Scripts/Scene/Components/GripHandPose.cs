using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
#endif

namespace BS
{
    /// <summary>Fits an owned, CPU-only copy of the authored hand to a local grab surface.</summary>
    internal sealed class GripHandPose : IDisposable
    {
        const float FingerRadius = .006f;
        static readonly string[] Fingers = { "index", "middle", "ring", "pinky", "thumb" };
        readonly GameObject rig;
        readonly SkinnedMeshRenderer renderer;
        readonly Transform[] joints = new Transform[15];
        readonly Transform[] tips = new Transform[5];
        readonly Quaternion[] rest = new Quaternion[15];
        readonly float[] signs = new float[15];
        readonly Matrix4x4 sourceToCanonical, meshToCanonical;
        readonly Mesh mesh;
#if UNITY_EDITOR
        readonly Scene previewScene;
#endif

        internal GripHandPose()
        {
            var source = Resources.Load<GameObject>("CreatorConveniences/Models/GripHand");
            if (source == null) return;
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                previewScene = EditorSceneManager.NewPreviewScene();
                rig = (GameObject)PrefabUtility.InstantiatePrefab(source, previewScene);
            }
            else
#endif
            {
                rig = UnityEngine.Object.Instantiate(source);
                UnityEngine.Object.DontDestroyOnLoad(rig);
            }
            rig.name = "Local grip pose";
            rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            rig.transform.localScale = Vector3.one;
            foreach (var transform in rig.GetComponentsInChildren<Transform>(true))
                transform.gameObject.hideFlags = HideFlags.HideAndDontSave;
            foreach (var animator in rig.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
            foreach (var visual in rig.GetComponentsInChildren<Renderer>(true)) visual.enabled = false;
            renderer = rig.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (renderer == null) return;
            var transforms = rig.GetComponentsInChildren<Transform>(true);
            var wrist = Array.Find(transforms, t => t.name == "hand.r");
            if (wrist == null) return;
            for (var finger = 0; finger < Fingers.Length; finger++)
            {
                for (var joint = 0; joint < 3; joint++)
                {
                    var index = finger * 3 + joint;
                    joints[index] = Array.Find(transforms, t => t.name == "c_" + Fingers[finger] + (joint + 1) + ".r");
                    if (joints[index] == null) return;
                    rest[index] = joints[index].localRotation;
                }
                tips[finger] = Array.Find(transforms, t => t.name == "c_" + Fingers[finger] + "3.r_end");
                if (tips[finger] == null) return;
            }
            var forward = ((joints[0].position + joints[3].position + joints[6].position + joints[9].position) * .25f - wrist.position).normalized;
            var thumbSide = Vector3.ProjectOnPlane(joints[0].position - joints[9].position, forward).normalized;
            var frame = Quaternion.LookRotation(forward, thumbSide);
            sourceToCanonical = Matrix4x4.TRS(wrist.position, frame, Vector3.one).inverse;
            meshToCanonical = sourceToCanonical * renderer.transform.localToWorldMatrix;
            for (var i = 0; i < joints.Length; i++)
                signs[i] = Vector3.Dot(Vector3.Cross(joints[i].right, joints[i].up), frame * Vector3.left) >= 0f ? 1f : -1f;
            mesh = new Mesh { name = "Fitted grip hand", hideFlags = HideFlags.HideAndDontSave };
            mesh.MarkDynamic();
        }

        internal Mesh Fit(Pose wristPose, bool left, Collider collider, bool triggerReady)
        {
            if (rig == null || renderer == null || mesh == null) return null;
            for (var i = 0; i < joints.Length; i++) joints[i].localRotation = rest[i];
            var toWorld = Matrix4x4.TRS(wristPose.position, wristPose.rotation, new Vector3(left ? -1f : 1f, 1f, 1f)) * sourceToCanonical;
            if (collider != null)
                for (var finger = 0; finger < Fingers.Length; finger++)
                    for (var joint = 0; joint < 3; joint++)
                    {
                        // Preserve the authored thumb opposition; finger-sized bends fold it back into the wrist.
                        var limit = finger == 4 ? (joint == 0 ? 30f : joint == 1 ? 55f : 65f)
                            : triggerReady && finger == 0 ? (joint == 0 ? 5f : joint == 1 ? 10f : 8f)
                            : joint == 0 ? 90f : joint == 1 ? 100f : 80f;
                        Curl(finger, joint, limit, collider, toWorld);
                    }
            renderer.BakeMesh(mesh);
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            for (var i = 0; i < vertices.Length; i++) vertices[i] = meshToCanonical.MultiplyPoint3x4(vertices[i]);
            for (var i = 0; i < normals.Length; i++) normals[i] = meshToCanonical.MultiplyVector(normals[i]).normalized;
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.RecalculateBounds();
            return mesh;
        }

        void Curl(int finger, int joint, float limit, Collider collider, Matrix4x4 toWorld)
        {
            var index = finger * 3 + joint;
            SetCurl(index, 0f);
            if (Touches(finger, joint, collider, toWorld)) return;
            var clear = 0f;
            while (clear < limit)
            {
                var next = Mathf.Min(clear + 5f, limit);
                SetCurl(index, next);
                if (Touches(finger, joint, collider, toWorld))
                {
                    for (var step = 0; step < 5; step++)
                    {
                        var middle = (clear + next) * .5f;
                        SetCurl(index, middle);
                        if (Touches(finger, joint, collider, toWorld)) next = middle;
                        else clear = middle;
                    }
                    SetCurl(index, clear);
                    return;
                }
                clear = next;
            }
        }

        void SetCurl(int index, float angle) => joints[index].localRotation = rest[index]
            * Quaternion.AngleAxis(signs[index] * angle, Vector3.right);

        bool Touches(int finger, int firstJoint, Collider collider, Matrix4x4 toWorld)
        {
            var nativeClosestPoint = collider is BoxCollider || collider is SphereCollider || collider is CapsuleCollider
                || collider is MeshCollider meshCollider && meshCollider.convex;
            for (var joint = firstJoint; joint < 3; joint++)
            {
                var from = joints[finger * 3 + joint].position;
                var to = joint == 2 ? tips[finger].position : joints[finger * 3 + joint + 1].position;
                for (var sample = 1; sample <= 3; sample++)
                {
                    var point = toWorld.MultiplyPoint3x4(Vector3.Lerp(from, to, sample / 3f));
                    // shortcut: Concave colliders use bounds; add triangle sampling if precise concave hints are needed.
                    var closest = nativeClosestPoint ? collider.ClosestPoint(point) : collider.bounds.ClosestPoint(point);
                    if ((point - closest).sqrMagnitude <= FingerRadius * FingerRadius) return true;
                }
            }
            return false;
        }

        public void Dispose()
        {
            Destroy(rig);
            Destroy(mesh);
#if UNITY_EDITOR
            if (previewScene.IsValid()) EditorSceneManager.ClosePreviewScene(previewScene);
#endif
        }

        static void Destroy(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
