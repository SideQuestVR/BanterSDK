using System;
using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    /// <summary>
    /// Ported from three.js src/extras/core/Curve.js.
    ///
    /// three.js duck-types this: 2D curves return Vector2 and 3D curves return Vector3. C# has no
    /// equivalent without generics infecting every subclass, so everything returns Vector3 and the
    /// 2D curves leave z at 0. Unity's implicit Vector3-to-Vector2 conversion means callers that
    /// only want x and y stay clean.
    ///
    /// Curves are authored in three.js space. The geometry that consumes them is responsible for
    /// the handedness conversion, exactly as the hand-written builders are.
    /// </summary>
    public abstract class Curve
    {
        public int ArcLengthDivisions = 200;

        List<float> cacheArcLengths;
        bool needsUpdate;

        /// <summary>Point at parameter t in [0,1], which is NOT arc-length uniform.</summary>
        public abstract Vector3 GetPoint(float t);

        /// <summary>Point at u in [0,1] measured along the curve's length.</summary>
        public Vector3 GetPointAt(float u)
        {
            return GetPoint(GetUtoTmapping(u));
        }

        public List<Vector3> GetPoints(int divisions = 5)
        {
            divisions = Math.Max(1, divisions);
            var points = new List<Vector3>(divisions + 1);
            for (int d = 0; d <= divisions; d++)
            {
                points.Add(GetPoint(d / (float)divisions));
            }
            return points;
        }

        public List<Vector3> GetSpacedPoints(int divisions = 5)
        {
            divisions = Math.Max(1, divisions);
            var points = new List<Vector3>(divisions + 1);
            for (int d = 0; d <= divisions; d++)
            {
                points.Add(GetPointAt(d / (float)divisions));
            }
            return points;
        }

        public float GetLength()
        {
            var lengths = GetLengths();
            return lengths[lengths.Count - 1];
        }

        public virtual List<float> GetLengths(int divisions = -1)
        {
            if (divisions < 0)
            {
                divisions = ArcLengthDivisions;
            }

            if (cacheArcLengths != null && cacheArcLengths.Count == divisions + 1 && !needsUpdate)
            {
                return cacheArcLengths;
            }

            needsUpdate = false;

            var cache = new List<float>(divisions + 1) { 0f };
            var last = GetPoint(0);
            float sum = 0;

            for (int p = 1; p <= divisions; p++)
            {
                var current = GetPoint(p / (float)divisions);
                sum += Vector3.Distance(current, last);
                cache.Add(sum);
                last = current;
            }

            cacheArcLengths = cache;
            return cache;
        }

        public void UpdateArcLengths()
        {
            needsUpdate = true;
            GetLengths();
        }

        /// <summary>Maps a uniform-length u to the curve's own parameter t.</summary>
        public float GetUtoTmapping(float u, float distance = -1)
        {
            var arcLengths = GetLengths();

            int il = arcLengths.Count;
            float targetArcLength = distance >= 0 ? distance : u * arcLengths[il - 1];

            // binary search for the index with largest value <= target
            int low = 0, high = il - 1;
            float comparison;

            while (low <= high)
            {
                int i = low + (high - low) / 2;
                comparison = arcLengths[i] - targetArcLength;

                if (comparison < 0)
                {
                    low = i + 1;
                }
                else if (comparison > 0)
                {
                    high = i - 1;
                }
                else
                {
                    high = i;
                    break;
                }
            }

            int index = high;

            if (index < 0)
            {
                index = 0;
            }

            if (arcLengths[index] == targetArcLength)
            {
                return index / (float)(il - 1);
            }

            // interpolate between the two surrounding samples
            var lengthBefore = arcLengths[index];
            var lengthAfter = index + 1 < il ? arcLengths[index + 1] : lengthBefore;
            var segmentLength = lengthAfter - lengthBefore;

            var segmentFraction = segmentLength == 0 ? 0 : (targetArcLength - lengthBefore) / segmentLength;

            return (index + segmentFraction) / (il - 1);
        }

        /// <summary>
        /// Unit tangent, approximated by a small delta because most subclasses have no analytic
        /// derivative. Matches three.js rather than being exact.
        /// </summary>
        public virtual Vector3 GetTangent(float t)
        {
            const float delta = 0.0001f;
            var t1 = t - delta;
            var t2 = t + delta;

            if (t1 < 0) t1 = 0;
            if (t2 > 1) t2 = 1;

            var pt1 = GetPoint(t1);
            var pt2 = GetPoint(t2);

            var tangent = pt2 - pt1;
            return tangent.sqrMagnitude < 1e-20f ? Vector3.forward : tangent.normalized;
        }

        public Vector3 GetTangentAt(float u)
        {
            return GetTangent(GetUtoTmapping(u));
        }

        /// <summary>
        /// Rotation-minimising frames along the curve, used to sweep a cross-section without it
        /// spinning. Ported from three.js computeFrenetFrames.
        /// </summary>
        public void ComputeFrenetFrames(int segments, bool closed,
            out List<Vector3> tangents, out List<Vector3> normals, out List<Vector3> binormals)
        {
            tangents = new List<Vector3>(segments + 1);
            normals = new List<Vector3>(segments + 1);
            binormals = new List<Vector3>(segments + 1);

            for (int i = 0; i <= segments; i++)
            {
                tangents.Add(GetTangentAt(i / (float)segments));
            }

            // Pick an initial normal perpendicular to the first tangent, using its smallest
            // component so the cross product is well conditioned.
            var normal = Vector3.zero;
            var tx = Mathf.Abs(tangents[0].x);
            var ty = Mathf.Abs(tangents[0].y);
            var tz = Mathf.Abs(tangents[0].z);
            var min = float.MaxValue;

            if (tx <= min) { min = tx; normal = new Vector3(1, 0, 0); }
            if (ty <= min) { min = ty; normal = new Vector3(0, 1, 0); }
            if (tz <= min) { normal = new Vector3(0, 0, 1); }

            var vec = Vector3.Cross(tangents[0], normal).normalized;

            normals.Add(Vector3.Cross(tangents[0], vec));
            binormals.Add(Vector3.Cross(tangents[0], normals[0]));

            for (int i = 1; i <= segments; i++)
            {
                var prevNormal = normals[i - 1];
                var prevBinormal = binormals[i - 1];

                vec = Vector3.Cross(tangents[i - 1], tangents[i]);

                if (vec.magnitude > 1e-4f)
                {
                    vec = vec.normalized;
                    var dot = Mathf.Clamp(Vector3.Dot(tangents[i - 1], tangents[i]), -1f, 1f);
                    var theta = Mathf.Acos(dot);
                    prevNormal = Quaternion.AngleAxis(theta * Mathf.Rad2Deg, vec) * prevNormal;
                }

                normals.Add(prevNormal);
                binormals.Add(Vector3.Cross(tangents[i], prevNormal));
            }

            // A closed curve must not leave a seam, so spread the accumulated twist evenly.
            if (closed)
            {
                var theta = Mathf.Acos(Mathf.Clamp(Vector3.Dot(normals[0], normals[segments]), -1f, 1f));
                theta /= segments;

                if (Vector3.Dot(tangents[0], Vector3.Cross(normals[0], normals[segments])) > 0)
                {
                    theta = -theta;
                }

                for (int i = 1; i <= segments; i++)
                {
                    normals[i] = Quaternion.AngleAxis(theta * i * Mathf.Rad2Deg, tangents[i]) * normals[i];
                    binormals[i] = Vector3.Cross(tangents[i], normals[i]);
                }
            }
        }
    }
}
