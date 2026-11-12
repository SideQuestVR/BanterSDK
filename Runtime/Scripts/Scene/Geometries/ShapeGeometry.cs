using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    // Ported from three.js ShapeGeometry - a flat, triangulated 2D outline with holes, lying in
    // the XY plane. Keep this a literal transcription; handedness is handled once by
    // ConvertToUnityHandedness.
    public class ShapeGeometry : Geometry
    {
        public ShapeGeometry(Shape shape, int curveSegments = 12)
        {
            indices = new List<int>();
            vertices = new List<Vector3>();
            normals = new List<Vector3>();
            uvs = new List<Vector2>();

            if (shape != null)
            {
                AddShape(shape, curveSegments);
            }

            ConvertToUnityHandedness();
        }

        void AddShape(Shape shape, int curveSegments)
        {
            int indexOffset = vertices.Count;

            shape.ExtractPoints(curveSegments, out var outline, out var holes);

            // three.js normalises the outline to counter-clockwise and the holes to clockwise
            // before triangulating, so a shape authored either way comes out the same.
            if (ShapeUtils.IsClockWise(outline))
            {
                outline.Reverse();
            }

            for (int i = 0; i < holes.Count; i++)
            {
                if (!ShapeUtils.IsClockWise(holes[i]))
                {
                    holes[i].Reverse();
                }
            }

            var faces = ShapeUtils.TriangulateShape(outline, holes);

            // the hole points follow the outline in one flat vertex list
            var all = new List<Vector2>(outline);
            foreach (var hole in holes)
            {
                all.AddRange(hole);
            }

            foreach (var p in all)
            {
                vertices.Add(new Vector3(p.x, p.y, 0));
                normals.Add(new Vector3(0, 0, 1));
                uvs.Add(new Vector2(p.x, p.y));
            }

            foreach (var f in faces)
            {
                indices.Add(f + indexOffset);
            }
        }
    }
}
