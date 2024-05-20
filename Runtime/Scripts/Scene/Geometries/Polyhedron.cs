using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class Polyhedron : Geometry
{

    List<Vector3> vertexBuffer = new List<Vector3>();
    List<Vector2> uvBuffer = new List<Vector2>();

    public Polyhedron(List<Vector3> vertices, List<int> indices, float radius, float detail)
    {
        this.vertices = vertices;
        this.indices = indices;
        this.uvs = new List<Vector2>();
        this.normals = new List<Vector3>();

        // the subdivision creates the vertex buffer data

        subdivide(detail);

        // all vertices should lie on a conceptual sphere with a given radius

        applyRadius(radius);

        // finally, create the uv data

        generateUVs();

        for (int i = 0; i < vertexBuffer.Count; i++)
        {
            normals.Add(new Vector3(0, 0, 1));
        }
        this.vertices = vertexBuffer;
        Debug.Log("indices" + indices.Count);
        Debug.Log("vertices" + vertices.Count);
        Debug.Log("uvs" + uvs.Count);
        Debug.Log("normals" + normals.Count);
    }



    void subdivide(float detail)
    {

        var a = new Vector3();
        var b = new Vector3();
        var c = new Vector3();

        // iterate over all faces and apply a subdivison with the given detail value

        for (int i = 0; i < indices.Count; i += 3)
        {

            // get the vertices of the face
            a = vertices[indices[i + 0]];
            // getVertexByIndex( indices[ i + 0 ], a );
            b = vertices[indices[i + 1]];
            // getVertexByIndex( indices[ i + 1 ], b );
            c = vertices[indices[i + 2]];
            // getVertexByIndex( indices[ i + 2 ], c );

            // perform subdivision

            subdivideFace(a, b, c, detail);

        }

    }

    void subdivideFace(Vector3 a, Vector3 b, Vector3 c, float detail)
    {

        var cols = detail + 1;

        // we use this multidimensional array as a data structure for creating the subdivision

        var v = new List<List<Vector3>>();

        // construct all of the vertices for this subdivision

        for (int i = 0; i <= cols; i++)
        {

            v.Add(new List<Vector3>());


            var aj = Vector3.Lerp(a, c, i / cols); // a.clone().lerp( c, i / cols );
            var bj = Vector3.Lerp(b, c, i / cols); // b.clone().lerp( c, i / cols );

            var rows = cols - i;

            for (int j = 0; j <= rows; j++)
            {

                if (j == 0 && i == cols)
                {

                    v[i].Add(aj);

                }
                else
                {

                    v[i].Add(Vector3.Lerp(aj, bj, j / rows)); // aj.clone().lerp( bj, j / rows );

                }

            }

        }

        // construct all of the faces

        for (int i = 0; i < cols; i++)
        {

            for (int j = 0; j < 2 * (cols - i) - 1; j++)
            {

                var k = (int)Mathf.Floor(j / 2);

                if (j % 2 == 0)
                {

                    pushVertex(v[i][k + 1]);
                    pushVertex(v[i + 1][k]);
                    pushVertex(v[i][k]);

                }
                else
                {

                    pushVertex(v[i][k + 1]);
                    pushVertex(v[i + 1][k + 1]);
                    pushVertex(v[i + 1][k]);

                }

            }

        }

    }

    void applyRadius(float radius)
    {

        //var vertex = new Vector3();

        // iterate over the entire buffer and apply the radius to each vertex

        for (int i = 0; i < vertexBuffer.Count; i++)
        {
            Vector3.Normalize(vertexBuffer[i]);
            vertexBuffer[i] = vertexBuffer[i] * radius;

            // vertex.x = vertexBuffer[ i + 0 ];
            // vertex.y = vertexBuffer[ i + 1 ];
            // vertex.z = vertexBuffer[ i + 2 ];

            // Vector3.Normalize(vertex);
            // vertex = vertex * radius;
            // // vertex.normalize().multiplyScalar( radius );

            // vertexBuffer[ i + 0 ] = vertex.x;
            // vertexBuffer[ i + 1 ] = vertex.y;
            // vertexBuffer[ i + 2 ] = vertex.z;

        }

    }

    void generateUVs()
    {

        // var vertex = new Vector3();

        for (int i = 0; i < vertexBuffer.Count; i++)
        {

            // vertex.x = vertexBuffer[ i + 0 ];
            // vertex.y = vertexBuffer[ i + 1 ];
            // vertex.z = vertexBuffer[ i + 2 ];

            var u = azimuth(vertexBuffer[i]) / 2 / Mathf.PI + 0.5f;
            var v = inclination(vertexBuffer[i]) / Mathf.PI + 0.5f;
            uvBuffer.Add(new Vector2(u, 1 - v));
            // uvBuffer.Add( u );
            // uvBuffer.Add(1 - v);
        }
        Debug.Log("#uvs: " + uvBuffer.Count);
        Debug.Log("#vertexBuffer.Count: " + vertexBuffer.Count);

        correctUVs();

        correctSeam();
        this.uvs = uvBuffer;
        // for(int i = 0; i < uvBuffer.Count; i++) {
        //     this.uvs.Add(uvBuffer[i]);
        // }
    }

    void correctSeam()
    {

        // handle case when face straddles the seam, see #3269

        for (int i = 0; i < uvBuffer.Count; i += 3)
        {

            // uv data of a single face

            var x0 = uvBuffer[i + 0].x;
            var x1 = uvBuffer[i + 1].x;
            var x2 = uvBuffer[i + 2].x;

            var max = Mathf.Max(x0, x1, x2);
            var min = Mathf.Min(x0, x1, x2);

            // 0.9 is somewhat arbitrary

            if (max > 0.9 && min < 0.1)
            {

                if (x0 < 0.2)
                {
                    // uvBuffer[ i + 0 ].x = uvBuffer[ i + 0 ].x + 1;
                    var uv = uvBuffer.ElementAt(i + 0);
                    uv.x += 1;
                }
                if (x1 < 0.2)
                {
                    // uvBuffer[ i + 1 ].x = uvBuffer[ i + 1 ].x + 1;
                    var uv = uvBuffer.ElementAt(i + 1);
                    uv.x += 1;
                }
                if (x2 < 0.2)
                {
                    // uvBuffer[ i + 2 ].x = uvBuffer[ i + 2 ].x + 1;
                    var uv = uvBuffer.ElementAt(i + 2);
                    uv.x += 1;
                }

            }

        }

    }

    void pushVertex(Vector3 vertex)
    {

        vertexBuffer.Add(vertex);
        // vertexBuffer.Add( vertex.y );
        // vertexBuffer.Add( vertex.z );

    }

    // void getVertexByIndex( int index, Vector3 vertex ) {

    // 	// var stride = index * 3;
    //     vertex = vertices[ index ];
    // 	// vertex.x = vertices[ stride + 0 ];
    // 	// vertex.y = vertices[ stride + 1 ];
    // 	// vertex.z = vertices[ stride + 2 ];

    // }

    void correctUVs()
    {

        var a = new Vector3();
        var b = new Vector3();
        var c = new Vector3();

        var centroid = new Vector3();

        var uvA = new Vector2();
        var uvB = new Vector2();
        var uvC = new Vector2();

        for (int i = 0, j = 0; i < vertexBuffer.Count; i += 3, j += 3)
        {
            a = vertexBuffer[i];
            // a.set( vertexBuffer[ i + 0 ], vertexBuffer[ i + 1 ], vertexBuffer[ i + 2 ] );
            b = vertexBuffer[i + 1];
            // b.set( vertexBuffer[ i + 3 ], vertexBuffer[ i + 4 ], vertexBuffer[ i + 5 ] );
            c = vertexBuffer[i + 2];
            //c.set( vertexBuffer[ i + 6 ], vertexBuffer[ i + 7 ], vertexBuffer[ i + 8 ] );

            // uvA.set( uvBuffer[ j + 0 ], uvBuffer[ j + 1 ] );
            uvA.x = uvBuffer[j + 0].x; uvA.y = uvBuffer[j + 0].y;
            // uvB.set( uvBuffer[ j + 2 ], uvBuffer[ j + 3 ] );
            uvB.x = uvBuffer[j + 1].x; uvB.y = uvBuffer[j + 1].y;
            // uvC.set( uvBuffer[ j + 4 ], uvBuffer[ j + 5 ] );
            uvC.x = uvBuffer[j + 2].x; uvC.y = uvBuffer[j + 2].y;

            // centroid.copy( a ).add( b ).add( c ).divideScalar( 3 );
            centroid = (a + b + c) / 3;

            float azi = azimuth(centroid);

            correctUV(uvA, j + 0, a, azi);
            correctUV(uvB, j + 1, b, azi);
            correctUV(uvC, j + 2, c, azi);

        }

    }

    void correctUV(Vector2 uv, int stride, Vector3 vector, float azimuth)
    {
        var _uv = uvBuffer[stride];
        if ((azimuth < 0) && (uv.x == 1))
        {

            _uv.x = uv.x - 1;

        }

        if ((vector.x == 0) && (vector.z == 0))
        {

            _uv.x = azimuth / 2 / Mathf.PI + 0.5f;

        }

    }

    // Angle around the Y axis, counter-clockwise when looking from above.

    float azimuth(Vector3 vector)
    {

        return Mathf.Atan2(vector.z, -vector.x);

    }


    // Angle above the XZ plane.

    float inclination(Vector3 vector)
    {

        return Mathf.Atan2(-vector.y, Mathf.Sqrt((vector.x * vector.x) + (vector.z * vector.z)));

    }

}
