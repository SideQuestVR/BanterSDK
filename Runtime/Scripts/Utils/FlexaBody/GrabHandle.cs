// SDK port of FlexaBody's GrabHandle, compiled when the FlexaBody package is not available.
// The grab pose maths is the client's, unchanged, so a handle grabs the same way in the SDK
// desktop controller as it does in Banter.

#if !BANTER_FLEX

using UnityEngine;

namespace SideQuest.FlexaBody
{
    /// <summary> Interactable Handle </summary>
    [AddComponentMenu("")]
    public class GrabHandle : MonoBehaviour
    {
        public WorldObject WorldObj;
        public Collider Col;
        public GrabType GrabType;
        public float _grabRadius = 0.01f;

        public bool UseCustomPointPose;
        public Pose RightPointOffset = Pose.identity;
        public Pose LeftPointOffset = Pose.identity;
        public HandPose RightPointHandPose;
        public HandPose LeftPointHandPose;

        public HandPose GetCustomPointHandPose(HandID side)
        {
            if (!UseCustomPointPose || GrabType != GrabType.Point || !isActiveAndEnabled ||
                (side != HandID.Left && side != HandID.Right)) return null;
            HandPose pose = side == HandID.Left ? LeftPointHandPose : RightPointHandPose;
            Pose offset = side == HandID.Left ? LeftPointOffset : RightPointOffset;
            Quaternion rotation = offset.rotation;
            if (pose == null || !Finite(pose.thumb) || !Finite(pose.index) || !Finite(pose.mid) ||
                !Finite(pose.ring) || !Finite(pose.pinky) || !Finite(offset.position) ||
                !Finite(new Vector4(rotation.x, rotation.y, rotation.z, rotation.w)) ||
                Quaternion.Dot(rotation, rotation) < 0.000001f) return null;
            return pose;
        }

        static bool Finite(Vector4 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                !float.IsNaN(value.z) && !float.IsInfinity(value.z) &&
                !float.IsNaN(value.w) && !float.IsInfinity(value.w);
        }

        public HandleFunction[] _handleFunctions;

        public void OnGenerateGrabHandle(Collider grabCol)
        {
            GrabType = GrabType.Soft;
            Col = grabCol;
            ValidateGrabHandle();
        }

        public void ValidateGrabHandle()
        {
            if (WorldObj)
                return;

            Rigidbody r = Col.attachedRigidbody;
            if (r)
            {
                WorldObj = r.GetComponent<WorldObject>();
                if (!WorldObj)
                {
                    WorldObj = r.gameObject.AddComponent<WorldObject>();
                    WorldObj.RB = r;
                }
            }
        }

        void Start()
        {
            if (!WorldObj)
                WorldObj = GetComponentInParent<WorldObject>();
        }

        public Rigidbody RB
        {
            get
            {
                if (WorldObj)
                    return WorldObj.RB;
                else
                    return null;
            }
        }

        public Pose GetGrabPose(PhysicsHand physHand, Vector3 grabPos)
        {
            switch (GrabType)
            {
                case GrabType.Point: return GetGrabPose_Point(physHand);
                case GrabType.Cylinder: return GetGrabPose_Cylinder(physHand, grabPos);
                case GrabType.Ball: return GetGrabPose_Ball(physHand, grabPos);
                case GrabType.Soft: return GetGrabPose_Soft(physHand, grabPos);
            }

            return Pose.identity;
        }

        public void OnGrab(HandData handData)
        {
            if (WorldObj)
                WorldObj.OnGrab(handData);

            for (int i = 0; i < _handleFunctions?.Length; i++) _handleFunctions?[i].OnGrab(handData);
        }

        public void OnHeld(HandData handData)
        {
            for (int i = 0; i < _handleFunctions?.Length; i++) _handleFunctions?[i].OnHeld(handData);
        }

        public void OnRelease(HandData handData)
        {
            if (WorldObj)
                WorldObj.OnRelease(handData);

            for (int i = 0; i < _handleFunctions?.Length; i++) _handleFunctions?[i].OnRelease(handData);
        }

        Pose GetGrabPose_Point(PhysicsHand physHand)
        {
            Pose pose = new Pose();
            bool custom = GetCustomPointHandPose(physHand.HandId) != null;
            Pose offset = physHand.HandId == HandID.Left ? LeftPointOffset : RightPointOffset;

            if (WorldObj && WorldObj.RB)
            {
                // The joint's connected anchor/rotation are in the RIGIDBODY's local space, so take the handle's
                // pose relative to the rigidbody — not transform.localPosition/localRotation, which are relative
                // to the handle's parent and only coincide when the handle is a direct child of the rigidbody.
                // A handle ON the rigidbody (e.g. a ball) was anchored at its position in the scene, so it hung
                // a random distance from the hand depending on where it lay when grabbed.
                Transform body = WorldObj.RB.transform;
                Vector3 handlePos = body.InverseTransformPoint(transform.position);
                Quaternion handleRot = Quaternion.Inverse(body.rotation) * transform.rotation;
                pose.position = handlePos + handleRot * ((physHand.HandId == HandID.Right ? 1f : -1f) * _grabRadius * Vector3.right);
                pose.rotation = Quaternion.Inverse(handleRot);
                if (custom)
                {
                    pose.position = body.InverseTransformPoint(transform.position + transform.rotation *
                        ((physHand.HandId == HandID.Right ? 1f : -1f) * _grabRadius * Vector3.right + offset.position));
                    pose.rotation = Quaternion.Inverse(handleRot * offset.rotation.normalized);
                }
            }
            else
            {
                pose.position = transform.position + transform.rotation * ((physHand.HandId == HandID.Right ? 1f : -1f) * _grabRadius * Vector3.right);
                pose.rotation = Quaternion.identity;
                if (custom)
                {
                    pose.position += transform.rotation * offset.position;
                    pose.rotation = Quaternion.Inverse(offset.rotation.normalized);
                }
            }


            return pose;
        }

        Pose GetGrabPose_Cylinder(PhysicsHand physHand, Vector3 grabPos)
        {
            Pose pose = new Pose();
            Rigidbody r = physHand.RB;

            Vector3 worldGrabPos;
            worldGrabPos = r.TransformPoint(grabPos);

            Vector3 toSurfaceDir = transform.InverseTransformDirection(transform.position - worldGrabPos);
            toSurfaceDir.y = 0f;
            toSurfaceDir = transform.TransformDirection(toSurfaceDir);

            Vector3 closestPoint = worldGrabPos + toSurfaceDir.normalized * (toSurfaceDir.magnitude - _grabRadius);

            Quaternion invRot = Quaternion.Inverse(r.rotation);     // Use for localising rotations
            toSurfaceDir = invRot * toSurfaceDir;                   // Localise toSurfaceRot
            Quaternion palmToSurfaceRot = Quaternion.FromToRotation(toSurfaceDir, physHand.GetPalmDirLocal());

            Quaternion localObjRot = invRot * transform.rotation;   // Localise R2 Rotation
            Quaternion finalRot = palmToSurfaceRot * localObjRot;

            Vector3 grabUp = finalRot * Vector3.up;

            if (Vector3.Dot(grabUp, Vector3.up) > 0f)
                palmToSurfaceRot = Quaternion.FromToRotation(grabUp, Vector3.up);
            else
                palmToSurfaceRot = Quaternion.FromToRotation(-grabUp, Vector3.up);
            finalRot = palmToSurfaceRot * finalRot;

            if (WorldObj)
            {
                Rigidbody r2 = WorldObj.RB;

                pose.position = r2.InverseTransformPoint(closestPoint);
                pose.rotation = Quaternion.Euler(40f, 0f, 0f) * finalRot * Quaternion.Inverse(transform.localRotation);
            }
            else
            {
                pose.position = closestPoint;
                pose.rotation = Quaternion.Euler(40f, 0f, 0f) * finalRot;
            }

            return pose;
        }

        Pose GetGrabPose_Ball(PhysicsHand physHand, Vector3 grabPos)
        {
            Pose pose = new Pose();
            Rigidbody r = physHand.RB;

            Vector3 worldGrabPos;
            worldGrabPos = r.TransformPoint(grabPos);
            Vector3 toSurfaceDir = transform.position - worldGrabPos;
            Vector3 closestPoint = transform.position - toSurfaceDir.normalized * _grabRadius;

            Quaternion palmToSurfaceRot = Quaternion.FromToRotation(toSurfaceDir, physHand.GetPalmDir());

            if (WorldObj)
            {
                Rigidbody r2 = WorldObj.RB;

                pose.position = r2.InverseTransformPoint(closestPoint);

                Quaternion finalRot = palmToSurfaceRot * r2.rotation;
                pose.rotation = Quaternion.Inverse(r.rotation) * finalRot;
            }
            else
            {
                pose.position = closestPoint;

                Quaternion finalRot = palmToSurfaceRot * transform.rotation;
                pose.rotation = Quaternion.Inverse(r.rotation) * finalRot;
            }

            return pose;
        }

        Pose GetGrabPose_Soft(PhysicsHand physHand, Vector3 grabPos)
        {
            Pose pose = new Pose();
            Rigidbody r = physHand.RB;

            Vector3 worldGrabPos;
            worldGrabPos = r.TransformPoint(grabPos);

            bool isConvex;
            if (Col is MeshCollider)
            {
                MeshCollider mc = Col as MeshCollider;
                isConvex = mc.convex;
            }
            else
                isConvex = true;

            Vector3 closestPoint;
            if (isConvex)
                closestPoint = Col.ClosestPoint(worldGrabPos);
            else
            {
                // Try a few raycasts from the palm to get a close surface;
                RaytraceNonConvex();
            }

            void RaytraceNonConvex()
            {
                // World GrabPos (in Palm)
                closestPoint = r.TransformPoint(grabPos);

                // Raycast out from Palm
                Vector3 baseDir = physHand.HandId == HandID.Left ? r.Right() : r.Left();
                Vector3 dir = baseDir;
                if (Col.Raycast(new Ray { origin = closestPoint - dir * 0.1f, direction = dir }, out RaycastHit hit, 0.4f))
                {
                    closestPoint = hit.point;
                    return;
                }

                // Raycast back from Palm
                dir *= -1f;
                if (Col.Raycast(new Ray { origin = closestPoint - dir * 0.1f, direction = dir }, out hit, 0.4f))
                {
                    closestPoint = hit.point;
                    return;
                }

                // Raycast forward
                dir = r.Forward();
                if (Col.Raycast(new Ray { origin = closestPoint - dir * 0.1f, direction = dir }, out hit, 0.4f))
                {
                    closestPoint = hit.point;
                    return;
                }

                // Raycast up-forward
                dir = (r.Forward() + r.Up()).normalized;
                if (Col.Raycast(new Ray { origin = closestPoint - dir * 0.1f, direction = dir }, out hit, 0.4f))
                {
                    closestPoint = hit.point;
                    return;
                }

                // Raycast down-forward
                dir = (r.Forward() + r.Down()).normalized;
                if (Col.Raycast(new Ray { origin = closestPoint - dir * 0.1f, direction = dir }, out hit, 0.4f))
                {
                    closestPoint = hit.point;
                    return;
                }

                closestPoint += baseDir * 0.03f;
            }

            if (WorldObj)
            {
                Rigidbody r2 = WorldObj.RB;
                pose.position = r2.InverseTransformPoint(closestPoint);

                Vector3 colliderCenter = Col.bounds.center;
                Vector3 toSurfaceDir = colliderCenter - worldGrabPos;
                Vector3 toClosest = closestPoint - worldGrabPos;
                float dot = Vector3.Dot(toClosest.normalized, toSurfaceDir.normalized);
                if (dot < 0f)
                    toClosest *= -1f;

                Quaternion palmToSurfaceRot = Quaternion.FromToRotation(toClosest, physHand.GetPalmDir());
                Quaternion finalRot = palmToSurfaceRot * r2.rotation;
                pose.rotation = Quaternion.Inverse(r.rotation) * finalRot;
            }
            else
            {
                pose.position = closestPoint;

                if (isConvex)
                {
                    Vector3 toSurfaceDir = transform.position - worldGrabPos;
                    Quaternion palmToSurfaceRot = Quaternion.FromToRotation(toSurfaceDir, physHand.GetPalmDir());
                    Quaternion finalRot = palmToSurfaceRot * transform.rotation;
                    pose.rotation = Quaternion.Inverse(r.rotation) * finalRot;
                }
                else
                    pose.rotation = Quaternion.Inverse(physHand.RB.rotation) * Col.transform.rotation;
            }

            return pose;
        }
    }
}

#endif
