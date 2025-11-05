using System.Collections.Generic;
using System.IO;
using UnityEngine;


public class ObjectScreenshotter
{
    public static void CaptureGameObject(
        GameObject target, 
        string outputPath, 
        int resolution = 512, 
        bool angled45 = true,
        float spotlightIntensity = 0f,
        int isolateLayer = -1)
    {
        if (target == null)
        {
            Debug.LogError("No target provided for screenshot.");
            return;
        }

        List<ParticleSystem> disabledParticles;
        Bounds bounds = CalculateObjectBounds(target, out disabledParticles);
        if (bounds.size == Vector3.zero)
        {
            Debug.LogError("Target object has no renderers (excluding particles).");
            return;
        }

        // --- Store and override layers if requested ---
        Dictionary<Renderer, int> originalLayers = new Dictionary<Renderer, int>();
        if (isolateLayer >= 0)
        {
            Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
            foreach (var r in renderers)
            {
                originalLayers[r] = r.gameObject.layer;
                r.gameObject.layer = isolateLayer;
            }
        }

        // --- Setup temporary camera ---
        GameObject camGO = new GameObject("TempScreenshotCamera");
        Camera cam = camGO.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0, 0, 0, 0);
        cam.orthographic = false;
        cam.enabled = false;

        if (isolateLayer >= 0)
            cam.cullingMask = 1 << isolateLayer;

        // --- Position camera ---
        Vector3 center = bounds.center;
        float radius = bounds.extents.magnitude;

        Vector3 forwardDir = target.transform.forward;
        Vector3 camDir = angled45
            ? Quaternion.AngleAxis(-45f, target.transform.up) * forwardDir
            : forwardDir;

        // Estimate distance from bounds size
        float objectRadius = bounds.extents.magnitude;
        float distance = objectRadius * 1.2f; // start a little out

        Vector3 camPos = center + camDir.normalized * distance;
        cam.transform.position = camPos;
        cam.transform.LookAt(center, target.transform.up);

        // --- Add optional spotlight ---
        if (spotlightIntensity > 0f)
        {
            Light spot = camGO.AddComponent<Light>();
            spot.type = LightType.Spot;
            spot.intensity = spotlightIntensity;
            spot.range = radius * 4f;
            spot.spotAngle = 60f;
        }

        // --- Adjust FOV to fit bounds tightly ---
        Vector3 extents = bounds.extents;
        float maxSize = Mathf.Max(extents.x, extents.y, extents.z);
        float requiredFov = 2f * Mathf.Atan((maxSize * (1.1f)) / distance) * Mathf.Rad2Deg;
        cam.fieldOfView = Mathf.Max(requiredFov, 1f);

        // --- Render to texture ---
        RenderTexture rt = new RenderTexture(resolution, resolution, 24, RenderTextureFormat.ARGB32);
        rt.antiAliasing = 8;
        cam.targetTexture = rt;

        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;

        cam.Render();

        Texture2D tex = new Texture2D(resolution, resolution, TextureFormat.ARGB32, false);
        tex.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
        tex.Apply();

        byte[] pngData = tex.EncodeToPNG();
        File.WriteAllBytes(outputPath, pngData);
        Debug.Log($"Saved screenshot to {outputPath}");

        // --- Cleanup ---
        RenderTexture.active = prev;
        cam.targetTexture = null;
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(camGO);
        
        // Restore particle systems
        foreach (var ps in disabledParticles)
        {
            if (ps != null) ps.gameObject.SetActive(true);
        }

        // Restore original layers
        if (isolateLayer >= 0)
        {
            foreach (var kvp in originalLayers)
            {
                if (kvp.Key != null)
                    kvp.Key.gameObject.layer = kvp.Value;
            }
        }
    }

    private static Bounds CalculateObjectBounds(GameObject go, out List<ParticleSystem> disabledParticles)
    {
        disabledParticles = new List<ParticleSystem>();

        // Temporarily disable all ParticleSystems so they don't affect bounds
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>())
        {
            if (ps.gameObject.activeInHierarchy && ps.GetComponent<Renderer>() != null)
            {
                if (ps.enableEmission) // legacy check for emission module
                {
                    disabledParticles.Add(ps);
                    ps.gameObject.SetActive(false);
                }
            }
        }

        Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return new Bounds(go.transform.position, Vector3.zero);

        Bounds bounds = renderers[0].bounds;
        foreach (Renderer r in renderers)
        {
            // Skip particle system renderers completely
            if (r is ParticleSystemRenderer) continue;
            bounds.Encapsulate(r.bounds);
        }

        return bounds;
    }
}

