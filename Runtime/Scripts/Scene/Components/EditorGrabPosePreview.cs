#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using SideQuest.FlexaBody;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BS
{
    // Isolated copies let the editor use the real grab solver without touching authored physics.
    public sealed class EditorGrabPosePreview : IDisposable
    {
        readonly Scene scene;
        readonly Collider source, collider;
        readonly List<(Transform source, Transform copy)> transforms = new List<(Transform, Transform)>();
        readonly GrabHandle handle;
        readonly PhysicsHand hand;
        readonly Rigidbody palm;
        readonly Rigidbody body;
        readonly Rigidbody sourceBody;

        public EditorGrabPosePreview(Collider source)
        {
            this.source = source;
            sourceBody = source.attachedRigidbody;
            scene = EditorSceneManager.NewPreviewScene();
            var chain = new Stack<Transform>();
            for (var t = source.transform; t != null; t = t.parent) chain.Push(t);
            Transform parent = null;
            while (chain.Count > 0)
            {
                var original = chain.Pop();
                var go = new GameObject(original.name);
                SceneManager.MoveGameObjectToScene(go, scene);
                go.transform.SetParent(parent, false);
                transforms.Add((original, go.transform));
                parent = go.transform;
                if (source.attachedRigidbody != null && original == source.attachedRigidbody.transform)
                {
                    body = go.AddComponent<Rigidbody>();
                    body.isKinematic = true;
                    body.useGravity = false;
                }
            }
            collider = (Collider)parent.gameObject.AddComponent(source.GetType());
            handle = parent.gameObject.AddComponent<GrabHandle>();
            handle.Col = collider;
            if (body != null)
            {
                handle.WorldObj = body.gameObject.AddComponent<WorldObject>();
                handle.WorldObj.RB = body;
            }
            var handObject = new GameObject("Preview wrist");
            SceneManager.MoveGameObjectToScene(handObject, scene);
            palm = handObject.AddComponent<Rigidbody>();
            palm.isKinematic = true;
            palm.useGravity = false;
            hand = handObject.AddComponent<PhysicsHand>();
            var settings = new SerializedObject(hand);
            settings.FindProperty("_rb").objectReferenceValue = palm;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        public static Vector3 PalmOffset(bool left) => new Vector3(left ? .028f : -.028f, 0f, .073f);

        public bool IsCurrent
        {
            get
            {
                if (source == null || source.attachedRigidbody != sourceBody) return false;
                for (var i = 0; i < transforms.Count; i++)
                    if (transforms[i].source == null || transforms[i].source.parent != (i == 0 ? null : transforms[i - 1].source))
                        return false;
                return true;
            }
        }

        public Pose Evaluate(BSGrabType type, float radius, bool left, Pose approach, BSPointGrabPose custom = null)
        {
            foreach (var pair in transforms)
            {
                pair.copy.localPosition = pair.source.localPosition;
                pair.copy.localRotation = pair.source.localRotation;
                pair.copy.localScale = pair.source.localScale;
            }
            EditorUtility.CopySerialized(source, collider);
            if (body != null)
            {
                body.position = body.transform.position;
                body.rotation = body.transform.rotation;
            }
            handle.GrabType = (GrabType)type;
            handle._grabRadius = radius;
            handle.UseCustomPointPose = false;
            custom?.Apply(handle);
            var settings = new SerializedObject(hand);
            settings.FindProperty("_handId").intValue = (int)(left ? HandID.Left : HandID.Right);
            settings.ApplyModifiedPropertiesWithoutUndo();
            palm.position = approach.position;
            palm.rotation = approach.rotation;
            return BSGripHologram.GetWristPose(handle, hand, PalmOffset(left));
        }

        public void Dispose()
        {
            if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
#endif
