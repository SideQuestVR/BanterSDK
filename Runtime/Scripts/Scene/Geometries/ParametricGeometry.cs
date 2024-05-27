using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ParametricGeometry : Geometry
{

    public static Func<float, float, Vector3> Klein = (v, u) =>
    {
        u *= Mathf.PI;
        v *= Mathf.PI * 2;
        u = u * 2;
        float x, y, z;
        if (u < Mathf.PI)
        {
            x = 3f * Mathf.Cos(u) * (1f + Mathf.Sin(u)) + (2f * (1f - Mathf.Cos(u) / 2f)) * Mathf.Cos(u) * Mathf.Cos(v);
            z = -8f * Mathf.Sin(u) - 2f * (1f - Mathf.Cos(u) / 2f) * Mathf.Sin(u) * Mathf.Cos(v);
        }
        else
        {
            x = 3f * Mathf.Cos(u) * (1f + Mathf.Sin(u)) + (2f * (1f - Mathf.Cos(u) / 2f)) * Mathf.Cos(v + Mathf.PI);
            z = -8f * Mathf.Sin(u);
        }
        y = -2f * (1f - Mathf.Cos(u) / 2f) * Mathf.Sin(v);
        var vector = new Vector3(x, y, z);
        vector.x /= 4;
        vector.y /= 4;
        vector.z /= 4;
        return vector;
    };

    public static Func<float, float, Vector3> InvertedKlein = (v, u) =>
    {
        return Klein(u, v);
    };

    public static Func<float, float, Vector3> Mobius = (u, t) =>
    {
        // flat mobius strip
        // http://www.wolframalpha.com/input/?i=M%C3%B6bius+strip+parametric+equations&lk=1&a=ClashPrefs_*Surface.MoebiusStrip.SurfaceProperty.ParametricEquations-
        u = u - 0.5f;
        var v = 2 * Mathf.PI * t;
        var a = 2;
        var x = Mathf.Cos(v) * (a + u * Mathf.Cos(v / 2));
        var y = Mathf.Sin(v) * (a + u * Mathf.Cos(v / 2));
        var z = u * Mathf.Sin(v / 2);
        return new Vector3(x, y, z);
    };

    public static Func<float, float, Vector3> Mobius3d = (u, t) =>
    {
        // volumetric mobius strip
        u *= Mathf.PI;
        t *= Mathf.PI * 2;

        u = u * 2;
        float phi = u / 2, major = 2.25f, a = 0.125f, b = 0.65f;
        var x = a * Mathf.Cos(t) * Mathf.Cos(phi) - b * Mathf.Sin(t) * Mathf.Sin(phi);
        var z = a * Mathf.Cos(t) * Mathf.Sin(phi) + b * Mathf.Sin(t) * Mathf.Cos(phi);
        var y = (major + x) * Mathf.Sin(u);
        x = (major + x) * Mathf.Cos(u);
        return new Vector3(x, y, z);
    };

    public static Func<float, float, Vector3> InvertedMobius3d = (v, u) =>
    {
        return Mobius3d(u, v);
    };


    public static Func<float, float, Vector3> Apple = (u, v) =>
    {
        u = u * 2 * Mathf.PI;
        v = (v * 2 * Mathf.PI) - Mathf.PI;
        var x = Mathf.Cos(u) * (4 + 3.8 * Mathf.Cos(v));
        var y = Mathf.Sin(u) * (4 + 3.8 * Mathf.Cos(v));
        var z = (Mathf.Cos(v) + Mathf.Sin(v) - 1) * (1 + Mathf.Sin(v)) * Mathf.Log(1 - Mathf.PI * v / 10) + 7.5 * Mathf.Sin(v);
        return new Vector3((float)x, (float)y, (float)z);
    };

    public static Func<float, float, Vector3> InvertedApple = (v, u) =>
    {
        return Apple(u, v);
    };

    public static Func<float, float, Vector3> Snail = (u, v) =>
    {
        u = u * 2 * Mathf.PI;
        v = (v * 4 * Mathf.PI) - Mathf.PI * 2;
        float x = u * Mathf.Cos(v) * Mathf.Sin(u);
        float y = u * Mathf.Cos(u) * Mathf.Cos(v);
        float z = -u * (float)Math.Sin(v);
        return new Vector3(x, y, z);
    };

    public static Func<float, float, Vector3> InvertedSnail = (v, u) =>
    {
        return Snail(u, v);
    };

    public static Func<float, float, Vector3> Spiral = (u, v) =>
    {
        u = u * 2 * -Mathf.PI;
        v = v * 2 * -Mathf.PI;
        var n = 4;
        var a = 0.1;
        var b = 1;
        var c = 0.3;
        var x = a * (1 - v / 2 * Mathf.PI) * Mathf.Cos(n * v) * (1 - Mathf.Cos(u)) + c * Mathf.Cos(n * v);
        var y = a * (1 - v / 2 * Mathf.PI) * Mathf.Sin(n * v) * (1 - Mathf.Cos(u)) + c * Mathf.Sin(n * v);
        var z = b * v / 2 * Mathf.PI + a * (1 - v / 2 * Mathf.PI) * Mathf.Sin(u);
        return new Vector3((float)x, (float)y, (float)z);
    };

    public static Func<float, float, Vector3> InvertedSpiral = (v, u) =>
    {
        return Spiral(u, v);
    };

    public static Func<float, float, Vector3> Fermet = (u, v) =>
    {
        u = u * 16 - 8;
        var abs_u = (u < 0) ? -u : u;
        v *= 1;
        var a = (u < 0) ? -1 : 1;
        var H = 0.8;
        var x = a * Mathf.Sqrt(abs_u) * Mathf.Cos(abs_u);
        var z = H * v;
        var y = a * Mathf.Sqrt(abs_u) * Mathf.Sin(abs_u);
        return new Vector3(x, y, (float)z);
    };

    public static Func<float, float, Vector3> Helicoid = (u, v) =>
    {
        u = u * 6 * -Mathf.PI;
        v = (v * 2 * Mathf.PI) - Mathf.PI;
        var c = 2;
        var x = c * v * Mathf.Cos(u);
        var y = c * v * Mathf.Sin(u);
        var z = u;
        return new Vector3(x, y, z);
    };

    public static Func<float, float, Vector3> Horn = (u, v) =>
    {
        v = v * 2 * Mathf.PI;
        var x = (2 + u * Mathf.Cos(v)) * Mathf.Cos(2 * Mathf.PI * u) + 2 * u;
        var y = (2 + u * Mathf.Cos(v)) * Mathf.Sin(2 * Mathf.PI * u);
        var z = u * Mathf.Sin(v);
        return new Vector3(x, y, z);
    };

    public static Func<float, float, Vector3> InvertedHorn = (v, u) =>
    {
        return Horn(u, v);
    };

    public static Func<float, float, Vector3> Pillow = (u, v) =>
    {
        u = (u * Mathf.PI) - Mathf.PI;
        v = (v * 2 * Mathf.PI) - Mathf.PI;
        var x = Mathf.Cos(u);
        var y = Mathf.Cos(v);
        var z = Mathf.Sin(u) * Math.Sin(v);
        return new Vector3(x, y, (float)z);
    };

    public static Func<float, float, Vector3> InvertedPillow = (v, u) =>
    {
        return Pillow(u, v);
    };

    public static Func<float, float, Vector3> Spring = (u, v) =>
    {
        u = u * 6 * -Mathf.PI;
        v = (v * 2 * Mathf.PI) - Mathf.PI;
        var r1 = 0.3;
        var r2 = 0.3;
        var periodlength = 1.2;
        // var cycles = 3;

        var x = (1 - r1 * Mathf.Cos(v)) * Math.Cos(u);
        var y = (1 - r1 * Mathf.Cos(v)) * Math.Sin(u);
        var z = r2 * (Mathf.Sin(v) + periodlength * u / Mathf.PI);
        return new Vector3((float)x, (float)y, (float)z);
    };

    public static Func<float, float, Vector3> InvertedSpring = (v, u) =>
    {
        return Spring(u, v);
    };

    public static Func<float, float, Vector3> Scherk = (u, v) =>
    {
        v = (v * Mathf.PI) - Mathf.PI / 2;
        u = (u * Mathf.PI) - Mathf.PI / 2;
        var c = 0.9;
        var x = v;
        var y = u;
        var z = Mathf.Log(Mathf.Cos((float)c * u) / (float)Math.Cos(c * v)) / c;
        return new Vector3(x, y, (float)z);
    };

    public static Func<float, float, Vector3> Catenoid = (u, v) =>
    {
        u = u * 2 * Mathf.PI;
        v = (v * 2 * Mathf.PI) - Mathf.PI;
        var c = 2;
        var x = c * System.Math.Cosh(v / c) * Mathf.Sin(u);
        var y = c * System.Math.Cosh(v / c) * Mathf.Cos(u);
        var z = v;
        return new Vector3((float)x, (float)y, z);
    };

    public static Func<float, float, Vector3> Natica = (u, v) =>
    {
        u = u * 21 - 20;
        v *= (float)Math.PI * 2;
        var a = 2.6;
        var b = 2.4;
        var c = 1.0;
        var h = 1.25;
        var k = -2.8;
        var w = 0.18;
        var R = 1;
        var exp_wu = Mathf.Exp((float)w * u);
        var sin_v = Mathf.Sin(v);
        var cos_v = Mathf.Cos(v);
        var sin_cu = Mathf.Sin((float)c * u);
        var cos_cu = Mathf.Cos((float)c * u);
        var x = exp_wu * (h + a * cos_v) * cos_cu;
        var y = R * exp_wu * (h + a * cos_v) * sin_cu;
        var z = exp_wu * (k + b * sin_v);
        return new Vector3((float)x, (float)y, (float)z);
    };

    public static Func<float, float, Vector3> InvertedNatica = (v, u) =>
    {
        return Natica(u, v);
    };

    public ParametricGeometry(int slices, int stacks, Func<float, float, Vector3> func = null, Vector3[] points = null)
    {
        indices = new List<int>();
        vertices = new List<Vector3>();
        normals = new List<Vector3>();
        uvs = new List<Vector2>();

        var stackCount = stacks + 1;
        var sliceCount = slices + 1;

        if (points != null && points.Length != stackCount * sliceCount)
        {
            throw new Exception("Not enough points, the points does not equal the number of slices/stacks.");
        }

        for (var i = 0f; i <= stacks; i++)
        {
            var v = i / stacks;
            for (var j = 0f; j <= slices; j++)
            {
                var u = j / slices;
                int _i = (int)i * (stacks + 1) + (int)j;
                var p = points == null ? func(u, v) : points[_i];
                vertices.Add(p);
                normals.Add(p.normalized);
                uvs.Add(new Vector2(u, v));
            }
        }

        for (var i = 0; i < stacks; i++)
        {
            for (var j = 0; j < slices; j++)
            {
                var a = i * sliceCount + j;
                var b = i * sliceCount + j + 1;
                var c = (i + 1) * sliceCount + j + 1;
                var d = (i + 1) * sliceCount + j;


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