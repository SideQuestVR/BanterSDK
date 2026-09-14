using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BS.SDKEditor
{
    /// <summary>
    /// Finds MeshColliders that have Convex ticked on large static geometry, and blocks a publish
    /// until the creator has been told what it does to players.
    ///
    /// Convex makes PhysX discard the triangle mesh and collide against its convex hull instead. On
    /// a floor the hull can sit a long way from the surface the player can see. A published world
    /// was measured with a 17-19 cm gap between where a ray found the floor and where a rolling
    /// sphere could rest on it; that is wider than the player rig's ground probe can reach while
    /// standing still, so players ended up standing on solid ground while the game believed they
    /// were falling, and stayed stuck in that state. Unticking Convex closed the gap to zero.
    ///
    /// Unity only *requires* convex in two cases: the collider is on a moving (non-kinematic)
    /// Rigidbody, or it is a trigger. Both are excluded below, so the rule cannot flag a legitimate
    /// use. Everything else is static world geometry, where convex only discards accuracy.
    /// </summary>
    public static class ConvexColliderValidation
    {
        const string Tag = "[Colliders]";

        /// <summary>
        /// Horizontal size in metres at or above which a convex hull is worth complaining about.
        /// Below this the hull and the mesh are close enough that players will not notice; above it
        /// the collider is essentially always a floor, wall or structure. Tune here.
        /// </summary>
        public const float LargeColliderSize = 4f;

        /// <summary>How many offenders to name in the dialog before summarising the rest.</summary>
        const int MaxListedInDialog = 8;

        public struct Finding
        {
            public MeshCollider Collider;
            public string Path;
            public Vector3 WorldSize;
            public int Triangles;
        }

        /// <summary>
        /// True when this collider has Convex ticked for no reason Unity requires, and is big
        /// enough for the hull to diverge noticeably from the mesh.
        /// </summary>
        public static bool IsSuspect(MeshCollider mc)
        {
            if (mc == null) return false;
            if (!mc.convex) return false;
            if (mc.sharedMesh == null) return false;

            // Triggers must be convex — Unity forces the flag on when isTrigger is set.
            if (mc.isTrigger) return false;

            // A MeshCollider on a moving body must be convex. includeInactive: true matters, because
            // a collider on a deactivated object would otherwise not see its own Rigidbody and would
            // be reported as a false positive.
            if (mc.GetComponentInParent<Rigidbody>(true) != null) return false;

            Vector3 size = WorldSize(mc);
            return size.x >= LargeColliderSize || size.z >= LargeColliderSize;
        }

        /// <summary>
        /// Core pass. Takes roots rather than reaching for the active scene, so edit-mode tests can
        /// feed hand-built hierarchies. Appends a human-readable report and returns the offender count.
        /// </summary>
        public static int FindInto(IEnumerable<GameObject> roots, StringBuilder report, List<Finding> findings = null)
        {
            var found = new List<Finding>();
            if (roots != null)
            {
                foreach (GameObject root in roots)
                {
                    if (root == null) continue;

                    // Include inactive: excluded or platform-filtered trees may start deactivated,
                    // and they still ship in the bundle.
                    foreach (MeshCollider mc in root.GetComponentsInChildren<MeshCollider>(true))
                    {
                        if (!IsSuspect(mc)) continue;
                        found.Add(new Finding
                        {
                            Collider = mc,
                            Path = HierarchyPath(mc.transform),
                            WorldSize = WorldSize(mc),
                            Triangles = TriangleCount(mc.sharedMesh),
                        });
                    }
                }
            }

            found.Sort((a, b) => Largest(b.WorldSize).CompareTo(Largest(a.WorldSize)));

            if (report != null)
            {
                if (found.Count == 0)
                {
                    report.AppendLine("No convex mesh colliders on large static geometry.");
                }
                else
                {
                    report.AppendLine($"{found.Count} mesh collider(s) have Convex ticked on static geometry "
                                      + $"at least {LargeColliderSize} m across:");
                    foreach (Finding f in found)
                    {
                        report.AppendLine($"  {f.Path}");
                        report.AppendLine($"      {f.WorldSize.x:F1} x {f.WorldSize.y:F1} x {f.WorldSize.z:F1} m, "
                                          + $"{f.Triangles} tris");
                    }
                }
            }

            findings?.AddRange(found);
            return found.Count;
        }

        /// <summary>Every root GameObject across the loaded scenes.</summary>
        public static IEnumerable<GameObject> OpenSceneRoots()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (GameObject go in scene.GetRootGameObjects())
                    yield return go;
            }
        }

        /// <summary>
        /// Publish gate. Returns true to let the build proceed. Mirrors
        /// <see cref="ValidateVisualScripting.CheckVsNodes"/>'s bool contract so it can sit beside it.
        /// </summary>
        public static bool CheckConvexColliders()
        {
            var report = new StringBuilder();
            var findings = new List<Finding>();
            int count = FindInto(OpenSceneRoots(), report, findings);

            if (count == 0)
                return true;

            // Durable record either way — the dialog is the decision, the log is the evidence.
            Debug.LogWarning($"{Tag} {report}");

            // Never prompt headlessly: a modal dialog wedges a batch run, and EditorDialogSuppressions
            // exists in this repo precisely because that has happened. Fail instead.
            if (Application.isBatchMode)
            {
                Debug.LogError($"{Tag} Publish blocked: {count} convex mesh collider(s) on static geometry. "
                               + "Untick Convex on them, or run Altspace > Tools > Fix Convex Colliders.");
                return false;
            }

            return EditorUtility.DisplayDialog("Convex colliders on static geometry", Message(count, findings),
                                               "Publish anyway", "Cancel");
        }

        static string Message(int count, List<Finding> findings)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{count} mesh collider(s) in this scene have Convex ticked on static geometry.");
            sb.AppendLine();
            sb.AppendLine("Convex throws the mesh away and collides against its convex hull instead, so players "
                          + "hit a shape that can sit well away from the surface they can see. On a floor this is "
                          + "severe: a player can end up standing on solid ground while the game thinks they are "
                          + "falling, and get stuck there. That has already happened in a published world.");
            sb.AppendLine();
            sb.AppendLine("Convex is only needed when a mesh collider is on a moving Rigidbody, or is a trigger. "
                          + "Neither applies to these. It is usually ticked to silence Unity's \"Non-convex "
                          + "MeshCollider with non-kinematic Rigidbody is not supported\" error, then left behind "
                          + "once the Rigidbody is gone.");
            sb.AppendLine();

            int shown = Mathf.Min(findings.Count, MaxListedInDialog);
            for (int i = 0; i < shown; i++)
                sb.AppendLine($"  {findings[i].Collider.name}  ({findings[i].WorldSize.x:F0} x "
                              + $"{findings[i].WorldSize.z:F0} m)");
            if (findings.Count > shown)
                sb.AppendLine($"  ...and {findings.Count - shown} more (full list in the console)");

            sb.AppendLine();
            sb.Append("To fix: untick Convex on each Mesh Collider, or run Altspace > Tools > Fix Convex Colliders.");
            return sb.ToString();
        }

        [MenuItem("Altspace/Tools/Validate Colliders")]
        public static void ValidateMenu()
        {
            var report = new StringBuilder();
            int count = FindInto(OpenSceneRoots(), report, null);
            if (count == 0) Debug.Log($"{Tag} {report}");
            else Debug.LogWarning($"{Tag} {report}");
        }

        /// <summary>
        /// Unticks Convex on everything the validator flags. Deliberately a separate, explicit action
        /// rather than something the publish gate does on its own, because it changes collision shape.
        /// </summary>
        [MenuItem("Altspace/Tools/Fix Convex Colliders")]
        public static void FixMenu()
        {
            var findings = new List<Finding>();
            int count = FindInto(OpenSceneRoots(), null, findings);
            if (count == 0)
            {
                Debug.Log($"{Tag} Nothing to fix.");
                return;
            }

            var report = new StringBuilder();
            report.AppendLine($"Unticked Convex on {count} mesh collider(s):");
            foreach (Finding f in findings)
            {
                Undo.RecordObject(f.Collider, "Untick Convex");
                f.Collider.convex = false;
                EditorUtility.SetDirty(f.Collider);
                EditorSceneManager.MarkSceneDirty(f.Collider.gameObject.scene);
                report.AppendLine($"  {f.Path}");
            }
            Debug.Log($"{Tag} {report}");
        }

        static float Largest(Vector3 v) => Mathf.Max(v.x, v.z);

        /// <summary>
        /// Mesh bounds scaled into world space. Taken from the mesh rather than Collider.bounds so it
        /// works on inactive objects and on colliders whose physics shape has not been cooked, which
        /// is the case in edit-mode tests.
        /// </summary>
        static Vector3 WorldSize(MeshCollider mc)
        {
            Mesh mesh = mc.sharedMesh;
            if (mesh == null) return Vector3.zero;
            Vector3 s = mesh.bounds.size;
            Vector3 l = mc.transform.lossyScale;
            return new Vector3(Mathf.Abs(s.x * l.x), Mathf.Abs(s.y * l.y), Mathf.Abs(s.z * l.z));
        }

        /// <summary>
        /// Index metadata only. Reading mesh.triangles would throw on any mesh imported without
        /// Read/Write enabled, which is most of them.
        /// </summary>
        static int TriangleCount(Mesh mesh)
        {
            if (mesh == null) return 0;
            long indices = 0;
            for (int i = 0; i < mesh.subMeshCount; i++)
                indices += (long)mesh.GetIndexCount(i);
            return (int)(indices / 3);
        }

        static string HierarchyPath(Transform t)
        {
            var sb = new StringBuilder(t.name);
            for (Transform p = t.parent; p != null; p = p.parent)
                sb.Insert(0, p.name + "/");
            return sb.ToString();
        }
    }
}
