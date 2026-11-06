using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem.UI;
using UnityEngine.Networking;
namespace BS
{
    // Deprecated: kept for content that already uses it and for scripts (the snippet runtime adds one), but
    // not offered in Add Component.
    [AddComponentMenu("")]
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]
    public partial class BSKitItem : BSComponentBase
    {
        [Tooltip("The location of the prefab in the kit object. Must match the path in the asset bundle (always lowercase).")]
        [See(initial = "")][SerializeField] internal string path = "";
        [See(initial = "false")] public bool resetTransform = false;

        GameObject item;
        public AssetBundle KitBundle;

        /*
         * Sentinel path meaning "the first prefab of MY bundle" — the BSAssetBundle on this same
         * GameObject, not the scene-wide KitPaths registry (a sentinel is never registered there,
         * and the registry can hold many bundles). Used by the injection runtime's <bs-snippet
         * asset="..."> element, which adds an AssetBundle then a KitItem("*") to one object.
         */
        public const string FirstPrefabPath = "*";

        private async Task SetupKitItem()
        {
            string loadPath = path;
            if (path == FirstPrefabPath)
            {
                // No scene.bundlesLoaded gate here: snippets are added after scene load and must
                // not depend on the initial-load latch.
                var bundleComponent = GetComponent<BSAssetBundle>();
                if (bundleComponent == null)
                {
                    Debug.LogWarning($"[BSKitItem] '{name}' path is '{FirstPrefabPath}' but no BSAssetBundle sits on this object.");
                    SetLoadedIfNot(false, "KitItem '*' requires a sibling BSAssetBundle.");
                    return;
                }
                await new WaitUntil(() => bundleComponent.IsLoaded); // latches on success AND failure
                KitBundle = bundleComponent.assetBundle;
                if (KitBundle == null)
                {
                    // Same-bytes-already-loaded case: a second snippet sharing this asset URL
                    // fails its own load; fall back to the already-loaded twin (identical
                    // signature = identical URLs). Visual Scripting or C# can get here at Play
                    // start, before the page's first load created the settings.
                    if (this == null || !await scene.WhenSettingsReady("Kit item '" + name + "'") || this == null) return;
                    KitBundle = scene.settings.KitBundles.FirstOrDefault(b =>
                        b != bundleComponent && b.assetBundle != null &&
                        b.GetSignature() == bundleComponent.GetSignature())?.assetBundle;
                }
                if (KitBundle == null)
                {
                    // The latch on this component reports only once, ever — warn as well so later
                    // failures stay visible (see BSKitAsset for the precedent).
                    Debug.LogWarning($"[BSKitItem] '{name}' sibling bundle failed to load - cannot resolve first prefab.");
                    SetLoadedIfNot(false, "KitItem '*': sibling bundle failed to load.");
                    return;
                }
                loadPath = KitBundle.GetAllAssetNames()
                    .Where(p => p.EndsWith(".prefab"))
                    .OrderBy(p => p, StringComparer.Ordinal) // GetAllAssetNames order is unspecified; make "first" deterministic
                    .FirstOrDefault();
                if (loadPath == null)
                {
                    Debug.LogWarning($"[BSKitItem] '{name}' bundle contains no .prefab assets.");
                    SetLoadedIfNot(false, "KitItem '*': bundle contains no prefabs.");
                    return;
                }
            }
            else
            {
                if (!scene.bundlesLoaded)
                {
                    // Bounded: this item gone, the load it started in replaced by another or failed (bundlesLoaded is the
                    // new load's, and may stay false after a cancelled one), or the backstop, which doesn't run out while
                    // an asset bundle is still pending (those bound themselves). No load captured (Play start, before the
                    // first): no identity to compare.
                    var load = scene.loadUrlTaskCompletionSource;
                    var deadline = Time.realtimeSinceStartup + KitPathWait.LimitSeconds;
                    await new WaitUntil(() => this == null || scene.bundlesLoaded
                        || (load != null && !ReferenceEquals(scene.loadUrlTaskCompletionSource, load))
                        || scene.HasLoadFailed()
                        || (Time.realtimeSinceStartup > deadline && !scene.AnyCriticalPending()));
                    if (this == null) return;
                }
                if (KitBundle == null && scene.settings == null)
                {
                    // The space's settings went while this waited (Play stopping).
                    SetLoadedIfNot(false, "Kititem not found at path: " + path);
                    return;
                }
                if (KitBundle == null)
                {
                    if (!scene.settings.KitPaths.ContainsKey(path))
                    {
                        // scene.bundlesLoaded is only recomputed by the space's load poll, and a kit bundle placed in the
                        // world registers its prefabs only once its own Start has loaded it, which can come after this
                        // item's Start: wait while a kit bundle may still register the path, then look once more.
                        var settings = scene.settings;
                        // The page's creations still queued may include the kit bundle itself.
                        var wait = new KitPathWait(() => settings.KitPaths.ContainsKey(path), BSAssetBundle.AnyKitStillLoading,
                            () => scene.pendingCreates.Count > 0, scene.HasLoadFailed, Time.realtimeSinceStartup);
                        await new WaitUntil(() => this == null || wait.Done(Time.realtimeSinceStartup));
                        if (this == null) return;
                    }
                    if (scene.settings != null && scene.settings.KitPaths.TryGetValue(path, out var kit))
                    {
                        KitBundle = kit.assetBundle;
                    }
                    else
                    {
                        SetLoadedIfNot(false, "Kititem not found at path: " + path);
                        return; // previously fell through into a NullReferenceException on LoadAsset
                    }
                }
            }
            if (item != null)
            {
                Destroy(item);
            }
            try
            {
                GameObject asset = KitBundle.LoadAsset<GameObject>(loadPath);
                // Judged on the asset, before any copy exists, so every instance inherits it (BSUnityEventGuard); a
                // prefab that fails throws, refusing the item. A reloaded asset is a new object and is judged again.
                BSUnityEventGuard.SanitizePrefab(asset, loadPath);
                if (resetTransform)
                {
                    asset.transform.localPosition = Vector3.zero;
                    asset.transform.localRotation = Quaternion.identity;
                }
                item = Instantiate(asset, transform, false);
                BSUnityEventGuard.SanitizeInstance(item, loadPath); // what only a live copy shows (legacy Animation states)
                scene.kitItems.Add(item);

                foreach (Transform transform in item.GetComponentsInChildren<Transform>(true))
                {
                    BSText.RepairFont(transform.gameObject.GetComponent<TMP_Text>());
                    var canvas = transform.gameObject.GetComponent<Canvas>();
                    if (canvas != null)
                    {
                        if (canvas.renderMode == RenderMode.WorldSpace)
                        {
                            canvas.worldCamera = Camera.main;
                            if (!canvas.GetComponent<CanvasCameraBinder>())
                                canvas.gameObject.AddComponent<CanvasCameraBinder>();
                            if (!canvas.GetComponent<BoxCollider>())
                            {
                                var box = canvas.gameObject.AddComponent<BoxCollider>();
                                var rt = canvas.GetComponent<RectTransform>();
                                box.size = new Vector3(rt.rect.width, rt.rect.height, 0.01f);
                                box.center = new Vector3(0f, 0f, 0.015f);
                            }
                            var trackedDeviceRaycaster = canvas.gameObject.GetComponent<TrackedDeviceRaycaster>();
                            if (trackedDeviceRaycaster)
                                Destroy(trackedDeviceRaycaster);
                        }
                    }
                }

                SetLoadedIfNot();
            }
            catch (Exception e)
            {
                SetLoadedIfNot(false, e.Message);
            }
        }

        internal override void DestroyStuff()
        {
            if (item != null && scene.kitItems.Contains(item))
            {
                scene.kitItems.Remove(item);
            }
        }

        internal override void UpdateStuff()
        {

        }
        internal override void StartStuff()
        {
            if (!valuesApplied)
            {
                // Placed in the Inspector, so nothing has loaded the path set there yet (see
                // valuesApplied). Without one there is nothing to load, and it counts as loaded
                // rather than failing the space's load.
                if (string.IsNullOrEmpty(path))
                {
                    SetLoadedIfNot();
                }
                else
                {
                    ReSetup();
                }
            }
        }
        internal void UpdateCallback(List<PropertyName> changedProperties)
        {
            valuesApplied = true;
            _ = SetupKitItem();
        }
    }
}