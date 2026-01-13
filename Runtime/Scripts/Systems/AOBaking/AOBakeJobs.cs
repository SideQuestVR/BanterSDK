using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace Banter.SDK
{
    /// <summary>
    /// Burst-compiled job to prepare RaycastCommands for AO baking.
    /// Runs in parallel across all vertex/sample combinations.
    /// </summary>
    [BurstCompile]
    public struct PrepareAORaycastsJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Vector3> WorldVertices;
        [ReadOnly] public NativeArray<Vector3> WorldNormals;
        [ReadOnly] public NativeArray<Vector3> HemisphereSamples;

        public float AOBias;
        public float AORadius;
        public int SampleCount;

        [WriteOnly] public NativeArray<RaycastCommand> Commands;

        public void Execute(int index)
        {
            int vertexIndex = index / SampleCount;
            int sampleIndex = index % SampleCount;

            Vector3 worldNormal = WorldNormals[vertexIndex];
            Vector3 origin = WorldVertices[vertexIndex] + worldNormal * AOBias;

            // Rotate hemisphere sample to align with vertex normal
            Vector3 sampleDir = RotateSampleToNormal(HemisphereSamples[sampleIndex], worldNormal);

            // Create raycast command
            Commands[index] = new RaycastCommand(
                origin,
                sampleDir,
                QueryParameters.Default,
                AORadius
            );
        }

        /// <summary>
        /// Rotates a sample direction from Z-up hemisphere to align with the given normal
        /// </summary>
        private Vector3 RotateSampleToNormal(Vector3 sample, Vector3 normal)
        {
            // Create rotation from Z-forward to normal direction
            // Using Rodrigues' rotation formula for Burst compatibility

            Vector3 from = new Vector3(0, 0, 1); // Z-up in our hemisphere samples

            float dot = Vector3.Dot(from, normal);

            // Handle parallel/anti-parallel cases
            if (dot > 0.9999f)
                return sample;
            if (dot < -0.9999f)
                return new Vector3(sample.x, sample.y, -sample.z);

            Vector3 axis = Vector3.Cross(from, normal);
            float axisLength = axis.magnitude;
            if (axisLength < 0.0001f)
                return sample;

            axis /= axisLength; // Normalize

            float angle = Mathf.Acos(Mathf.Clamp(dot, -1f, 1f));
            float sinAngle = Mathf.Sin(angle);
            float cosAngle = dot;

            // Rodrigues' rotation formula
            Vector3 rotated = sample * cosAngle +
                              Vector3.Cross(axis, sample) * sinAngle +
                              axis * Vector3.Dot(axis, sample) * (1f - cosAngle);

            return rotated;
        }
    }

    /// <summary>
    /// Burst-compiled job to process raycast results and compute AO per vertex.
    /// </summary>
    [BurstCompile]
    public struct ProcessAOResultsJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<RaycastHit> RaycastResults;

        public int SampleCount;
        public float AOIntensity;

        [WriteOnly] public NativeArray<Color32> VertexColors;

        public void Execute(int vertexIndex)
        {
            int startIndex = vertexIndex * SampleCount;
            int hitCount = 0;

            for (int i = 0; i < SampleCount; i++)
            {
                // Check if raycast hit something (colliderInstanceID != 0 means hit)
                if (RaycastResults[startIndex + i].colliderInstanceID != 0)
                {
                    hitCount++;
                }
            }

            // Calculate AO: more hits = more occlusion = darker
            float occlusion = (float)hitCount / SampleCount;
            byte ao = (byte)(255 * (1f - Mathf.Clamp01(occlusion * AOIntensity)));

            VertexColors[vertexIndex] = new Color32(ao, ao, ao, 255);
        }
    }

    /// <summary>
    /// Helper struct to hold all NativeArrays for an async AO bake operation
    /// </summary>
    public struct AOBakeJobData : System.IDisposable
    {
        public NativeArray<Vector3> WorldVertices;
        public NativeArray<Vector3> WorldNormals;
        public NativeArray<Vector3> HemisphereSamples;
        public NativeArray<RaycastCommand> RaycastCommands;
        public NativeArray<RaycastHit> RaycastResults;
        public NativeArray<Color32> VertexColors;

        public bool IsCreated => WorldVertices.IsCreated;

        public void Allocate(int vertexCount, int sampleCount)
        {
            int totalRays = vertexCount * sampleCount;

            WorldVertices = new NativeArray<Vector3>(vertexCount, Allocator.Persistent);
            WorldNormals = new NativeArray<Vector3>(vertexCount, Allocator.Persistent);
            HemisphereSamples = new NativeArray<Vector3>(sampleCount, Allocator.Persistent);
            RaycastCommands = new NativeArray<RaycastCommand>(totalRays, Allocator.Persistent);
            RaycastResults = new NativeArray<RaycastHit>(totalRays, Allocator.Persistent);
            VertexColors = new NativeArray<Color32>(vertexCount, Allocator.Persistent);
        }

        public void Dispose()
        {
            if (WorldVertices.IsCreated) WorldVertices.Dispose();
            if (WorldNormals.IsCreated) WorldNormals.Dispose();
            if (HemisphereSamples.IsCreated) HemisphereSamples.Dispose();
            if (RaycastCommands.IsCreated) RaycastCommands.Dispose();
            if (RaycastResults.IsCreated) RaycastResults.Dispose();
            if (VertexColors.IsCreated) VertexColors.Dispose();
        }
    }
}
