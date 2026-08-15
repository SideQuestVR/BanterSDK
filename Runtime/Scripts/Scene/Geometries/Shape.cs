using System.Collections.Generic;
using UnityEngine;

namespace BS
{
    /// <summary>
    /// Ported from three.js src/extras/core/Shape.js - a closed Path2D plus any number of holes.
    ///
    /// Note this is three.js's Shape (a 2D outline), not its ShapeGeometry (a mesh). The mesh
    /// lives in <see cref="ShapeGeometry"/>.
    /// </summary>
    public class Shape : Path2D
    {
        public readonly List<Path2D> holes = new List<Path2D>();

        public Shape(List<Vector2> points = null) : base(points)
        {
        }

        /// <summary>Outline and holes flattened to polylines at the given curve resolution.</summary>
        public void ExtractPoints(int divisions, out List<Vector2> shape, out List<List<Vector2>> holePoints)
        {
            shape = GetPoints2D(divisions);
            holePoints = new List<List<Vector2>>(holes.Count);

            foreach (var hole in holes)
            {
                holePoints.Add(hole.GetPoints2D(divisions));
            }
        }
    }
}
