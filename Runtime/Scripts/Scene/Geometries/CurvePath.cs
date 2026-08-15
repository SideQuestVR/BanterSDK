using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    // Ported from three.js src/extras/core/CurvePath.js. A curve made of curves; t is distributed
    // by arc length so a path of mixed segment types is traversed at a steady rate.
    public class CurvePath : Curve
    {
        public readonly List<Curve> curves = new List<Curve>();
        public bool autoClose;

        List<float> cacheLengths;

        public void Add(Curve curve)
        {
            curves.Add(curve);
            cacheLengths = null;
        }

        public override Vector3 GetPoint(float t)
        {
            if (curves.Count == 0)
            {
                return Vector3.zero;
            }

            var d = t * GetLength();
            var curveLengths = GetCurveLengths();

            for (int i = 0; i < curveLengths.Count; i++)
            {
                if (curveLengths[i] >= d)
                {
                    var diff = curveLengths[i] - d;
                    var curve = curves[i];
                    var segmentLength = curve.GetLength();
                    var u = segmentLength == 0 ? 0 : 1 - diff / segmentLength;
                    return curve.GetPointAt(u);
                }
            }

            return curves[curves.Count - 1].GetPointAt(1);
        }

        public override List<float> GetLengths(int divisions = -1)
        {
            // Length comes from the sub-curves rather than from resampling this one, so a path
            // built of exact primitives keeps their exact lengths.
            var lengths = GetCurveLengths();
            var result = new List<float> { 0f };
            result.AddRange(lengths);
            return result;
        }

        public List<float> GetCurveLengths()
        {
            if (cacheLengths != null && cacheLengths.Count == curves.Count)
            {
                return cacheLengths;
            }

            var lengths = new List<float>(curves.Count);
            float sums = 0;

            for (int i = 0; i < curves.Count; i++)
            {
                sums += curves[i].GetLength();
                lengths.Add(sums);
            }

            cacheLengths = lengths;
            return lengths;
        }
    }
}
