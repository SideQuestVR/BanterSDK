using System;
using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    /// <summary>
    /// Ported from three.js ExtrudeGeometry - a 2D shape given depth along Z, with caps at both
    /// ends. Keep this a literal transcription; handedness is handled once by
    /// ConvertToUnityHandedness.
    ///
    /// Bevelling is not implemented. three.js's bevel offsets each contour inward with mitre
    /// handling for reflex vertices and is the fiddliest part of the whole port, so it is
    /// deliberately left out rather than shipped half-right - <see cref="ExtrudeGeometry"/>
    /// callers get a flat extrusion and the bevel fields are absent from the component, so the
    /// wire contract does not promise something the mesh does not do.
    /// </summary>
    public class ExtrudeGeometry : Geometry
    {
        public ExtrudeGeometry(Shape shape, float depth = 1, int steps = 1, int curveSegments = 12, Curve extrudePath = null)
        {
            indices = new List<int>();
            vertices = new List<Vector3>();
            normals = new List<Vector3>();
            uvs = new List<Vector2>();

            if (shape == null)
            {
                return;
            }

            steps = Math.Max(1, steps);

            AddShape(shape, depth, steps, curveSegments, extrudePath);

            ConvertToUnityHandedness();
            Recentre();
        }

        void AddShape(Shape shape, float depth, int steps, int curveSegments, Curve extrudePath)
        {
            shape.ExtractPoints(curveSegments, out var outline, out var holes);

            // three.js normalises the outline counter-clockwise and holes clockwise before
            // triangulating, so a shape authored either way extrudes the same.
            if (ShapeUtils.IsClockWise(outline))
            {
                outline.Reverse();
            }

            for (int h = 0; h < holes.Count; h++)
            {
                if (!ShapeUtils.IsClockWise(holes[h]))
                {
                    holes[h].Reverse();
                }
            }

            var faces = ShapeUtils.TriangulateShape(outline, holes);

            // one flat vertex list: outline first, then each hole in order
            var contour = new List<Vector2>(outline);
            foreach (var hole in holes)
            {
                contour.AddRange(hole);
            }

            int vlen = contour.Count;

            // If an extrude path is given the shape is swept along it using rotation-minimising
            // frames; otherwise it is a straight push along Z.
            List<Vector3> spine = null;
            List<Vector3> spineNormals = null;
            List<Vector3> spineBinormals = null;

            if (extrudePath != null)
            {
                spine = extrudePath.GetSpacedPoints(steps);
                extrudePath.ComputeFrenetFrames(steps, false, out _, out spineNormals, out spineBinormals);
            }

            // ---- vertices ----
            for (int s = 0; s <= steps; s++)
            {
                var t = s / (float)steps;

                for (int i = 0; i < vlen; i++)
                {
                    var p = contour[i];

                    if (extrudePath != null)
                    {
                        var pos = spine[s];
                        var n = spineNormals[s] * p.x;
                        var b = spineBinormals[s] * p.y;
                        vertices.Add(new Vector3(pos.x + n.x + b.x, pos.y + n.y + b.y, pos.z + n.z + b.z));
                    }
                    else
                    {
                        vertices.Add(new Vector3(p.x, p.y, depth * t));
                    }

                    uvs.Add(new Vector2(p.x, p.y));
                    normals.Add(Vector3.zero); // replaced below
                }
            }

            // ---- caps ----
            // front cap faces -Z, back cap faces +Z, so the front is wound in reverse.
            int lastLayer = steps * vlen;

            foreach (var f in faces)
            {
                indices.Add(f);
            }
            ReverseLastTriangles(faces.Count);

            foreach (var f in faces)
            {
                indices.Add(f + lastLayer);
            }

            // ---- walls ----
            int layerStart = 0;
            AddWalls(outline.Count, 0, steps, vlen);
            layerStart += outline.Count;

            foreach (var hole in holes)
            {
                AddWalls(hole.Count, layerStart, steps, vlen);
                layerStart += hole.Count;
            }

            RecomputeSmoothNormals();
        }

        /// <summary>Flips the winding of the last <paramref name="count"/> indices.</summary>
        void ReverseLastTriangles(int count)
        {
            int start = indices.Count - count;
            for (int i = start; i + 2 < indices.Count; i += 3)
            {
                int first = indices[i];
                indices[i] = indices[i + 2];
                indices[i + 2] = first;
            }
        }

        /// <summary>Quads joining consecutive layers around one closed contour.</summary>
        void AddWalls(int count, int offset, int steps, int vlen)
        {
            for (int s = 0; s < steps; s++)
            {
                for (int i = 0; i < count; i++)
                {
                    int j = (i + 1) % count;

                    int a = s * vlen + offset + i;
                    int b = s * vlen + offset + j;
                    int c = (s + 1) * vlen + offset + j;
                    int d = (s + 1) * vlen + offset + i;

                    indices.Add(a);
                    indices.Add(b);
                    indices.Add(d);

                    indices.Add(b);
                    indices.Add(c);
                    indices.Add(d);
                }
            }
        }

    }
}
