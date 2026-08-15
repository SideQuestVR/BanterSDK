using System;
using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    // Ported from three.js TubeGeometry. Keep this a literal transcription; handedness is handled
    // once by ConvertToUnityHandedness.
    public class Tube : Geometry
    {
        readonly List<Vector3> frameTangents;
        readonly List<Vector3> frameNormals;
        readonly List<Vector3> frameBinormals;

        public Tube(Curve path, int tubularSegments = 64, float radius = 0.5f, int radialSegments = 8, bool closed = false)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            tubularSegments = Math.Max(1, tubularSegments);
            radialSegments = Math.Max(3, radialSegments);

            path.ComputeFrenetFrames(tubularSegments, closed, out frameTangents, out frameNormals, out frameBinormals);

            indices = new List<int>();
            vertices = new List<Vector3>();
            normals = new List<Vector3>();
            uvs = new List<Vector2>();

            GenerateBufferData(path, tubularSegments, radius, radialSegments, closed);

            ConvertToUnityHandedness();

            // A swept tube's bounding box has no reason to be centred on the curve's own origin.
            Recentre();
        }

        void GenerateBufferData(Curve path, int tubularSegments, float radius, int radialSegments, bool closed)
        {
            for (int i = 0; i < tubularSegments; i++)
            {
                GenerateSegment(path, tubularSegments, radius, radialSegments, closed, i);
            }

            // The last vertex ring duplicates the first only in position; it carries uv 1 rather
            // than 0, which is what keeps the texture from wrapping back on itself.
            GenerateSegment(path, tubularSegments, radius, radialSegments, closed, closed ? 0 : tubularSegments);

            GenerateUVs(tubularSegments, radialSegments);
            GenerateIndices(tubularSegments, radialSegments);
        }

        void GenerateSegment(Curve path, int tubularSegments, float radius, int radialSegments, bool closed, int i)
        {
            var P = path.GetPointAt(i / (float)tubularSegments);

            var N = frameNormals[Math.Min(i, frameNormals.Count - 1)];
            var B = frameBinormals[Math.Min(i, frameBinormals.Count - 1)];

            for (int j = 0; j <= radialSegments; j++)
            {
                var v = j / (float)radialSegments * Mathf.PI * 2;

                var sin = Mathf.Sin(v);
                var cos = -Mathf.Cos(v);

                var normal = new Vector3(
                    cos * N.x + sin * B.x,
                    cos * N.y + sin * B.y,
                    cos * N.z + sin * B.z).normalized;

                normals.Add(normal);
                vertices.Add(P + radius * normal);
            }
        }

        void GenerateUVs(int tubularSegments, int radialSegments)
        {
            for (int i = 0; i <= tubularSegments; i++)
            {
                for (int j = 0; j <= radialSegments; j++)
                {
                    uvs.Add(new Vector2(i / (float)tubularSegments, j / (float)radialSegments));
                }
            }
        }

        void GenerateIndices(int tubularSegments, int radialSegments)
        {
            for (int j = 1; j <= tubularSegments; j++)
            {
                for (int i = 1; i <= radialSegments; i++)
                {
                    var a = (radialSegments + 1) * (j - 1) + (i - 1);
                    var b = (radialSegments + 1) * j + (i - 1);
                    var c = (radialSegments + 1) * j + i;
                    var d = (radialSegments + 1) * (j - 1) + i;

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
