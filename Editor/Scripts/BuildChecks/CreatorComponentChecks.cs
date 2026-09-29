using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BS.SDKEditor.BuildChecks
{
    /// <summary>Seats are sat on by clicking their colliders.</summary>
    sealed class SeatCheck : BuildCheck
    {
        const int UILayer = 5;
        const int MenuLayer = 22;

        public override string Id => "bs.seat";
        public override string Title => "Seats";
        public override int Order => 300;

        public override void Run(BuildCheckContext context, List<BuildCheckIssue> issues)
        {
            var nothingToClick = new List<BSSeat>();
            var notClickableInSdk = new List<BSSeat>();
            foreach (var seat in context.Components<BSSeat>())
            {
                var colliders = ClickColliders(seat).ToList();
                if (colliders.Count == 0)
                    nothingToClick.Add(seat);
                else if (!colliders.Any(col => IsClickLayer(col.gameObject.layer)))
                    notClickableInSdk.Add(seat);
            }

            if (nothingToClick.Count > 0)
            {
                issues.Add(new BuildCheckIssue(Id, BuildCheckSeverity.Warning,
                        $"{nothingToClick.Count} seat(s) have nothing to click",
                        "A seat is sat on by clicking a collider on it or its children. Add one; a trigger box around the chair " +
                        "on the UI layer works everywhere.")
                    .WithTargets(context.TargetsOf(nothingToClick)));
            }
            if (notClickableInSdk.Count > 0)
            {
                issues.Add(new BuildCheckIssue(Id, BuildCheckSeverity.Warning,
                        $"{notClickableInSdk.Count} seat(s) can't be clicked in SDK Play Mode",
                        "The SDK's desktop player only clicks colliders on the UI (5) or Menu (22) layer. The client clicks any " +
                        "layer, but put the seat's click collider on the UI layer to test it here.")
                    .WithTargets(context.TargetsOf(notClickableInSdk))
                    .WithFix("Move the seats' colliders to the UI layer", issue =>
                    {
                        var changed = false;
                        foreach (var seat in issue.Resolve<GameObject>().Select(go => go.GetComponent<BSSeat>()).Where(seat => seat != null))
                        {
                            foreach (var col in ClickColliders(seat))
                            {
                                Undo.RecordObject(col.gameObject, "Move seat collider to UI layer");
                                col.gameObject.layer = UILayer;
                                changed = true;
                            }
                        }
                        return changed;
                    }));
            }
        }

        static bool IsClickLayer(int layer) => layer == UILayer || layer == MenuLayer;

        // The colliders BSSeat listens to: its own and its children's, but not a nested seat's.
        static IEnumerable<Collider> ClickColliders(BSSeat seat) =>
            seat.GetComponentsInChildren<Collider>(true).Where(col => col.GetComponentInParent<BSSeat>(true) == seat);
    }

    /// <summary>Hands only find grab handles on the Grabbable layer, through the handle's own collider.</summary>
    sealed class GrabHandlesCheck : BuildCheck
    {
        const int GrabbableLayer = 20;

        public override string Id => "bs.grab-handles";
        public override string Title => "Grab handles";
        public override int Order => 310;

        public override void Run(BuildCheckContext context, List<BuildCheckIssue> issues)
        {
            var handles = context.Components<BSGrabHandle>().ToList();
            var wrongLayer = handles.Where(handle => handle.gameObject.layer != GrabbableLayer).ToList();
            var noCollider = handles.Where(handle => handle.GetComponent<Collider>() == null).ToList();

            if (wrongLayer.Count > 0)
            {
                issues.Add(new BuildCheckIssue(Id, BuildCheckSeverity.Warning,
                        $"{wrongLayer.Count} grab handle(s) aren't on the Grabbable layer",
                        "Hands only look for grab handles on layer 20 (Grabbable). Anywhere else, these can't be picked up.")
                    .WithTargets(context.TargetsOf(wrongLayer))
                    .WithFix("Move them to the Grabbable layer", issue =>
                    {
                        var changed = false;
                        foreach (var go in issue.Resolve<GameObject>())
                        {
                            Undo.RecordObject(go, "Move grab handle to Grabbable layer");
                            go.layer = GrabbableLayer;
                            changed = true;
                        }
                        return changed;
                    }));
            }
            if (noCollider.Count > 0)
            {
                issues.Add(new BuildCheckIssue(Id, BuildCheckSeverity.Warning,
                        $"{noCollider.Count} grab handle(s) have no collider",
                        "A grab handle is held through a collider on the same object. Add one (a trigger is fine).")
                    .WithTargets(context.TargetsOf(noCollider)));
            }
        }
    }

    /// <summary>BSGrababble configures itself only when a page script sets its properties.</summary>
    sealed class GrababbleCheck : BuildCheck
    {
        public override string Id => "bs.grababble";
        public override string Title => "Grababble";
        public override int Order => 320;

        public override void Run(BuildCheckContext context, List<BuildCheckIssue> issues)
        {
            var grababbles = context.Components<BSGrababble>().ToList();
            if (grababbles.Count == 0)
                return;
            issues.Add(new BuildCheckIssue(Id, BuildCheckSeverity.Warning,
                    $"{grababbles.Count} object(s) use BSGrababble, which doesn't set itself up in a scene",
                    "BSGrababble adds its grab handle only when a page script sets its properties, so placed in the editor it " +
                    "can't be picked up. Use a preset from GameObject > BS > Grab, or a BSGrabHandle on a Grabbable-layer " +
                    "collider with BSWorldObject on the rigidbody.")
                .WithTargets(context.TargetsOf(grababbles)));
        }
    }

    /// <summary>One settings component, and nothing on the page replacing it.</summary>
    sealed class SceneSettingsCheck : BuildCheck
    {
        static readonly string[] PageExtensions = { ".html", ".htm", ".js", ".mjs" };
        const long LargestPageFile = 2 * 1024 * 1024;

        public override string Id => "bs.settings";
        public override string Title => "Scene settings";
        public override int Order => 330;

        public override void Run(BuildCheckContext context, List<BuildCheckIssue> issues)
        {
            var settings = context.Components<BSSettings>().ToList();
            if (settings.Count > 1)
            {
                issues.Add(new BuildCheckIssue(Id, BuildCheckSeverity.Warning,
                        $"{settings.Count} Scene Settings components",
                        "Each applies its values when the space loads, so which one wins is down to load order. Keep one.")
                    .WithTargets(context.TargetsOf(settings)));
            }
            if (settings.Count == 0 || settings.Any(IsFullyLocked))
                return;
            var page = FindPageCallingSetSettings(Path.Combine(Application.dataPath, "WebRoot"));
            if (page == null)
                return;
            issues.Add(new BuildCheckIssue(Id, BuildCheckSeverity.Warning,
                    "The page's script also sets the scene settings",
                    $"{page} calls SetSettings, which sends every setting and replaces the Scene Settings component's values if " +
                    "it runs after them. Remove the call, or tick both locks on the component (Is Settings Locked, Is Physics " +
                    "Settings Locked).")
                .WithTargets(context.TargetsOf(settings)));
        }

        static bool IsFullyLocked(BSSettings settings)
        {
            var serialized = new SerializedObject(settings);
            return serialized.FindProperty("isSettingsLocked").boolValue
                   && serialized.FindProperty("isPhysicsSettingsLocked").boolValue;
        }

        internal static string FindPageCallingSetSettings(string webRoot)
        {
            if (!Directory.Exists(webRoot))
                return null;
            foreach (var file in Directory.EnumerateFiles(webRoot, "*", SearchOption.AllDirectories))
            {
                if (!PageExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()) || new FileInfo(file).Length > LargestPageFile)
                    continue;
                if (File.ReadAllText(file).Contains("SetSettings("))
                    return "Assets/WebRoot/" + Path.GetRelativePath(webRoot, file).Replace('\\', '/');
            }
            return null;
        }
    }

    /// <summary>Where players arrive.</summary>
    sealed class SpawnCheck : BuildCheck
    {
        public override string Id => "bs.spawn";
        public override string Title => "Spawn points";
        public override int Order => 340;

        public override void Run(BuildCheckContext context, List<BuildCheckIssue> issues)
        {
            var spawns = context.Components<BSSpawn>().ToList();
            if (spawns.Count == 0)
            {
                issues.Add(new BuildCheckIssue(Id, BuildCheckSeverity.Info, "No spawn points",
                    "Players arrive at the world origin, or wherever the page's script teleports them. Add spawn points with " +
                    "GameObject > BS > Player > Spawn Point."));
                return;
            }
            if (!spawns.Any(spawn => spawn.enabled && spawn.gameObject.activeInHierarchy))
            {
                issues.Add(new BuildCheckIssue(Id, BuildCheckSeverity.Warning, "None of the spawn points are active",
                        "Only active spawn points are picked from, so players arrive at the world origin.")
                    .WithTargets(context.TargetsOf(spawns)));
            }
            var negative = spawns.Where(spawn => new SerializedObject(spawn).FindProperty("radius").floatValue < 0f).ToList();
            if (negative.Count > 0)
            {
                issues.Add(new BuildCheckIssue(Id, BuildCheckSeverity.Warning,
                        $"{negative.Count} spawn point(s) have a negative radius",
                        "A negative radius lands players exactly on the spawn point. Use 0 for that, or a positive radius to spread them out.")
                    .WithTargets(context.TargetsOf(negative)));
            }
        }
    }

    /// <summary>Teleporters need a trigger to walk into and somewhere else to land.</summary>
    sealed class TeleporterCheck : BuildCheck
    {
        public override string Id => "bs.teleporter";
        public override string Title => "Teleporters";
        public override int Order => 350;

        public override void Run(BuildCheckContext context, List<BuildCheckIssue> issues)
        {
            var noTrigger = new List<BSTeleporter>();
            var noDestination = new List<BSTeleporter>();
            var landsInside = new List<BSTeleporter>();
            foreach (var teleporter in context.Components<BSTeleporter>())
            {
                var destination = new SerializedObject(teleporter).FindProperty("destination").objectReferenceValue as Transform;
                var trigger = teleporter.GetComponents<Collider>().FirstOrDefault(col => col.isTrigger);
                if (trigger == null)
                    noTrigger.Add(teleporter);
                if (destination == null)
                    noDestination.Add(teleporter);
                else if (trigger != null && Contains(trigger, destination.position))
                    landsInside.Add(teleporter);
            }

            if (noTrigger.Count > 0)
            {
                issues.Add(new BuildCheckIssue(Id, BuildCheckSeverity.Warning,
                        $"{noTrigger.Count} teleporter(s) have no trigger collider",
                        "A teleporter fires when the player walks into a collider with Is Trigger ticked on the same object.")
                    .WithTargets(context.TargetsOf(noTrigger)));
            }
            if (noDestination.Count > 0)
            {
                issues.Add(new BuildCheckIssue(Id, BuildCheckSeverity.Warning,
                        $"{noDestination.Count} teleporter(s) have no destination",
                        "Set Destination to the transform players should land on.")
                    .WithTargets(context.TargetsOf(noDestination)));
            }
            if (landsInside.Count > 0)
            {
                issues.Add(new BuildCheckIssue(Id, BuildCheckSeverity.Warning,
                        $"{landsInside.Count} teleporter(s) land players inside their own trigger",
                        "Players arrive back in the trigger, so they are teleported again every time the cooldown ends. Move the " +
                        "destination outside the trigger.")
                    .WithTargets(context.TargetsOf(landsInside)));
            }
        }

        internal static bool Contains(Collider trigger, Vector3 point)
        {
            var local = trigger.transform.InverseTransformPoint(point);
            switch (trigger)
            {
                case BoxCollider box:
                    var offset = local - box.center;
                    var half = box.size * 0.5f;
                    return Math.Abs(offset.x) <= half.x && Math.Abs(offset.y) <= half.y && Math.Abs(offset.z) <= half.z;
                case SphereCollider sphere:
                    return (local - sphere.center).magnitude <= sphere.radius;
                default:
                    return trigger.enabled && trigger.gameObject.activeInHierarchy && trigger.bounds.Contains(point);
            }
        }
    }
}
