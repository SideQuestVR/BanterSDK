using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;


class DepthHeader
{
    public ushort HeaderSize;
    public ushort NumberOfPlanes;
    public ushort Width;
    public ushort Height;
    public ushort Offset;
}
class DepthData
{
    public DepthPlane[] planes;
    public ushort[] indices;
    public DepthData(int indicesSize, int planesSize) {
        planes = new DepthPlane[planesSize];
        indices = new ushort[indicesSize];
    }
}
class DepthPlane
{
    public double d;
    public Vector3 n;
}


public class Neighbour
{
    public string Panoid; // "x0G4coU6yvs_y5JIeCm5Ew"
    public double[] LatLng;
}

public class PhotoMeta
{
    public string Panoid;
    public Vector3 Rotation;
    public int Resolution;
    public float Elavation;
    public float Latitude;
    public float Longitude;
    public float TileWidth;
    public float TileHeight;
    public List<Neighbour> Neighbours = new List<Neighbour>();

    private JArray Root;
    private byte[] DepthData;
    private Action<Vector3[], Vector3[], Vector2[], List<int>> DepthCallback;

    public PhotoMeta(string response, Action<Vector3[], Vector3[], Vector2[], List<int>> DepthCallback) {
        this.DepthCallback = DepthCallback;
        Root = JArray.Parse(response.Substring(4));
        try{
            if (((string)Root[1][0][6][7][0]).Equals("")) {
                Debug.LogWarning("This panorama is invalid.");
            } else {
                ParseMetaData();
                ParseDepth();
                ParseNeighbours();
            }
        }catch(Exception e){
            Debug.LogWarning("This panorama is invalid." + e.Message);
            Debug.Log(response);
        }
    }

    void ParseMetaData() {
        var rotation = (JArray)Root[1][0][5][0][1][2];
        Panoid = (string)Root[1][0][1][1];
        Rotation = new Vector3(-(float)rotation[1], (float)rotation[2], (float)rotation[0]);
        Resolution = (int)Root[1][0][2][2][0];
        Elavation = (float)Root[1][0][5][0][1][1][0];
        Latitude = (float)Root[1][0][5][0][1][0][2];
        Longitude = (float)Root[1][0][5][0][1][0][3];
        TileWidth = (int)Root[1][0][2][3][1][1];
        TileHeight = (int)Root[1][0][2][3][1][0];
    }

    void ParseNeighbours() {
        foreach (JArray PanoidData in (JArray)Root[1][0][5][0][3][0]) {
            var NextPanoid = (string)PanoidData[0][1];
            var Latitude = (double)PanoidData[2][0][2];
            var Longitude = (double)PanoidData[2][0][3];
            if (NextPanoid != Panoid && Neighbours.Count < 6) {
                Neighbours.Add(new Neighbour() {
                    Panoid = NextPanoid,
                    LatLng = new double[2] { Latitude, Longitude },
                });
            }
        }
    }

    void ParseDepth() {
        string base64Depth = (string)Root[1][0][5][0][5][1][2];
        while (base64Depth.Length % 4 != 0) {
            base64Depth += '=';
        }
        base64Depth = base64Depth.Replace('-', '+');
        base64Depth = base64Depth.Replace('_', '/');
        DepthData = Convert.FromBase64String(base64Depth);
        Thread thread = new Thread(() => {
            var header = new DepthHeader();

            // parse header
            header.HeaderSize = BitConverter.ToUInt16(DepthData, 0);
            header.NumberOfPlanes = BitConverter.ToUInt16(DepthData, 1);
            header.Width = BitConverter.ToUInt16(DepthData, 3);
            header.Height = BitConverter.ToUInt16(DepthData, 5);
            header.Offset = BitConverter.ToUInt16(DepthData, 7);

            var data = ParsePlanes(header, DepthData);
            Vector3[] vertices = new Vector3[header.Width * header.Height];
            Vector3[] normals = new Vector3[header.Width * header.Height];
            Vector2[] uvs = new Vector2[header.Width * header.Height];

            //int index = 0;
            List<int[]> grid = new List<int[]>();

            List<int> tris = new List<int>();
            for (int y = 0; y < header.Height; y++) {

                for (int x = 0; x < header.Width; x++) {

                    float rad_azimuth = x / (float)(header.Width - 1.0f) * Mathf.PI * 2;
                    float rad_elevation = y / (float)(header.Height - 1.0f) * Mathf.PI;

                    //Calculate the cartesian position of this vertex (if it was at unit distance)
                    Vector3 xyz;
                    xyz.x = Mathf.Sin(rad_elevation) * Mathf.Sin(rad_azimuth);
                    xyz.y = Mathf.Sin(rad_elevation) * Mathf.Cos(rad_azimuth);
                    xyz.z = Mathf.Cos(rad_elevation);
                    double distance = 1;

                    //Value that is safe to use to retrieve stuff from the index arrays 
                    var current = y * header.Width + x;
                    //Calculate distance of point according to the depth map data.
                    int depthMapIndex = data.indices[current];
                    if (depthMapIndex == 0) {
                        //Distance of sky
                        distance = 50D;
                    } else {
                        DepthPlane plane = data.planes[depthMapIndex];
                        distance = -plane.d / (plane.n[0] * xyz.x + plane.n[1] * xyz.y + -plane.n[2] * xyz.z);
                    }
                    var currentPosition = new Vector3(-xyz.x * (float)distance, xyz.y * (float)distance, xyz.z * (float)distance);
                    vertices[current] = currentPosition;
                    normals[current] = Vector3.forward;
                    uvs[current] = new Vector2(x / (float)header.Width, 1 - y / (float)header.Height);

                    // particleSystem.Emit(emitParams, 1);
                }
            }

            for (int iy = 0; iy < header.Height - 1; iy++) {

                for (int ix = 0; ix < header.Width - 1; ix++) {
                    var current = iy * header.Width + ix;
                    tris.Add(current + 512);
                    tris.Add(current);
                    tris.Add(current + 513);

                    tris.Add(current + 513);
                    tris.Add(current);
                    tris.Add(current + 1);
                }

            }
             UnityMainThreadDispatcher.Instance().Enqueue(() => DepthCallback(vertices, normals, uvs, tris));
        });
        thread.Start();
    }

    DepthData ParsePlanes(DepthHeader header, byte[] depthMap) {
        var data = new DepthData(header.Width * header.Height, header.NumberOfPlanes);

        for (int i = 0; i < header.Width * header.Height; i++) {
            data.indices[i] = depthMap[header.Offset + i];
        }

        for (int i = 0; i < header.NumberOfPlanes; i++) {
            var byteOffset = header.Offset + (header.Width * header.Height) + (i * 4 * 4);
            var plane = new DepthPlane();
            plane.n[0] = BitConverter.ToSingle(depthMap, byteOffset);
            plane.n[1] = BitConverter.ToSingle(depthMap, byteOffset + 4);
            plane.n[2] = BitConverter.ToSingle(depthMap, byteOffset + 8);
            plane.d = BitConverter.ToSingle(depthMap, byteOffset + 12);
            data.planes[i] = plane;
        }
        return data;
    }
}
