#if UNITY_EDITOR
using System;
using BS;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// GameObject > BS: objects that are ready to use. The player conveniences are prefabs, loaded by GUID;
/// the grab presets and component objects are built here. Fields are set through SerializedObject, never
/// the components' setters, which would run their runtime setup in edit mode.
/// </summary>
internal static class CreatorConveniencesMenu
{
    private const string Root = "GameObject/BS/";
    private const int UILayer = 5;
    private const int GrabbableLayer = 20;

    private const string SpawnPointGuid = "c62a2d0bbf7d4fc7b5958e11c2f05501";
    private const string SpawnRangeGuid = "12b6892e0a2744c28aa7f7ad3a98e401";
    private const string SeatGuid = "fb1c6d01b9804fa9a77c30532a802802";
    private const string TeleporterGuid = "f0fb9ff341064868a90e08695bd41a58";

    // ------------------------------------------------------------------ Player
    [MenuItem("GameObject/BS/Objects/BSStarterUpper", false, 10)]
    static void CreateBanterStarterUpper(MenuCommand menuCommand)
    {
        var exists = GameObject.FindObjectOfType<BSStarterUpper>();
        if (exists != null)
        {
            Debug.LogWarning("BSStarterUpper already exists in the scene.", exists);
            return;
        }
        // Create a custom game object
        GameObject go = new GameObject("BSStarterUpper");
        go.AddComponent<BSStarterUpper>();
        // Ensure it gets reparented if this was a context click (otherwise does nothing)
        GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);
        // Register the creation in the undo system
        Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
        Selection.activeObject = go;
    }
    [MenuItem(Root + "Player/Spawn Point", false, 10)]
    private static void CreateSpawnPoint(MenuCommand command) => CreatePrefab(SpawnPointGuid, "Spawn Point", command);

    [MenuItem(Root + "Player/Spawn Range", false, 11)]
    private static void CreateSpawnRange(MenuCommand command) => CreatePrefab(SpawnRangeGuid, "Spawn Range", command);

    [MenuItem(Root + "Player/Seat", false, 12)]
    private static void CreateSeat(MenuCommand command) => CreatePrefab(SeatGuid, "Seat", command);

    [MenuItem(Root + "Player/Teleporter", false, 13)]
    private static void CreateTeleporter(MenuCommand command) => CreatePrefab(TeleporterGuid, "Teleporter", command);

    [MenuItem(Root + "Player/Scene Settings", false, 14)]
    private static void CreateSceneSettings(MenuCommand command)
    {
        var parent = command.context as GameObject;
        var scene = parent ? parent.scene : SceneManager.GetActiveScene();
        foreach (var existing in Object.FindObjectsByType<BSSettings>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (existing.gameObject.scene != scene)
                continue;
            var sceneName = string.IsNullOrEmpty(scene.name) ? "This scene" : $"'{scene.name}'";
            Debug.LogWarning($"{sceneName} already has scene settings on '{existing.name}'; a scene uses one.", existing);
            Selection.activeGameObject = existing.gameObject;
            EditorGUIUtility.PingObject(existing);
            return;
        }

        var go = Begin("Scene Settings", command);
        Add<BSSettings>(go);
        Finish(go, "Scene Settings");
    }

    // ------------------------------------------------------------------ Grab
    // Each preset is one grab type, set up the way the SDK samples are: a rigidbody root with
    // BSWorldObject (and BSSyncedObject, so everyone sees it move), and a Grabbable-layer (20) collider
    // carrying the BSGrabHandle that says how the hand holds it.

    [MenuItem(Root + "Grab/Point (Handle)", false, 20)]
    private static void CreateGrabPoint(MenuCommand command)
    {
        // The hand snaps to one pose: the GrabHandle transform (grip along its Y, pointing along its Z).
        var root = GrabbableRoot("Grab Point", command, 0.5f);
        Primitive(PrimitiveType.Cylinder, "Grip", root.transform, Vector3.zero, new Vector3(0.035f, 0.07f, 0.035f));
        Primitive(PrimitiveType.Cube, "Head", root.transform, new Vector3(0f, 0.09f, 0.03f), new Vector3(0.06f, 0.05f, 0.14f));
        PointHandle(root.transform);
        Finish(root, "Grab Point");
    }

    [MenuItem(Root + "Grab/Point (Gun)", false, 21)]
    private static void CreateGrabGun(MenuCommand command)
    {
        // A Point grip with held events: the trigger fires (onGunTrigger / OnGunTrigger), and is kept
        // from the player's own trigger actions while held.
        var root = GrabbableRoot("Grab Gun", command, 0.8f);
        var grip = Primitive(PrimitiveType.Cube, "Grip", root.transform, Vector3.zero, new Vector3(0.03f, 0.1f, 0.045f));
        // The bottom raked back, like a pistol grip.
        grip.transform.localRotation = Quaternion.Euler(15f, 0f, 0f);
        Primitive(PrimitiveType.Cube, "Barrel", root.transform, new Vector3(0f, 0.065f, 0.06f), new Vector3(0.035f, 0.045f, 0.2f));
        Child("Muzzle", root.transform).transform.localPosition = new Vector3(0f, 0.065f, 0.165f);

        var handle = PointHandle(root.transform);
        var held = Add<BSHeldEvents>(handle);
        Set(held, so =>
        {
            so.FindProperty("blockLeftTrigger").boolValue = true;
            so.FindProperty("blockRightTrigger").boolValue = true;
        });
        Finish(root, "Grab Gun");
    }

    [MenuItem(Root + "Grab/Cylinder (Stick)", false, 22)]
    private static void CreateGrabCylinder(MenuCommand command)
    {
        // Held anywhere along its length (the handle's Y axis), like a bat, sword or pole.
        var root = GrabbableRoot("Grab Stick", command, 1f);
        var stick = Primitive(PrimitiveType.Cylinder, "Stick", root.transform, Vector3.zero, new Vector3(0.04f, 0.5f, 0.04f));
        MakeHandle(stick, BSGrabType.Cylinder, 0.02f);
        Finish(root, "Grab Stick");
    }

    [MenuItem(Root + "Grab/Ball", false, 23)]
    private static void CreateGrabBall(MenuCommand command)
    {
        // Held anywhere on its surface, palm towards the centre. The grab radius is the ball's.
        var root = GrabbableRoot("Grab Ball", command, 0.5f);
        var ball = Primitive(PrimitiveType.Sphere, "Ball", root.transform, Vector3.zero, Vector3.one * 0.25f);
        MakeHandle(ball, BSGrabType.Ball, 0.125f);
        Finish(root, "Grab Ball");
    }

    [MenuItem(Root + "Grab/Soft (Any Shape)", false, 24)]
    private static void CreateGrabSoft(MenuCommand command)
    {
        // Held wherever the hand touches the collider's surface: for props of any shape.
        var root = GrabbableRoot("Grab Soft", command, 1f);
        var box = Primitive(PrimitiveType.Cube, "Box", root.transform, Vector3.zero, Vector3.one * 0.3f);
        MakeHandle(box, BSGrabType.Soft, 0.01f);
        Finish(root, "Grab Soft");
    }

    [MenuItem(Root + "Grab/Climbable Handhold", false, 25)]
    private static void CreateHandhold(MenuCommand command)
    {
        // No rigidbody: a hand that grabs it holds on to the world, so players can climb it.
        // The bar is on the object's origin, so it lands where the menu places things, like every other preset.
        var root = Begin("Handhold", command);
        var bar = Primitive(PrimitiveType.Cylinder, "Bar", root.transform, Vector3.zero, new Vector3(0.05f, 0.3f, 0.05f));
        bar.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        MakeHandle(bar, BSGrabType.Cylinder, 0.025f);
        Finish(root, "Handhold");
    }

    // ------------------------------------------------------------------ Objects

    [MenuItem(Root + "Objects/Mirror", false, 30)]
    private static void CreateMirror(MenuCommand command) => CreateComponentObject<BSMirror>("Mirror", command);

    [MenuItem(Root + "Objects/Browser", false, 31)]
    private static void CreateBrowser(MenuCommand command) =>
        CreateComponentObject<BSBrowser>("Browser", command, so => so.FindProperty("url").stringValue = "https://sidequestvr.com");

    [MenuItem(Root + "Objects/Video Player", false, 32)]
    private static void CreateVideoPlayer(MenuCommand command)
    {
        // The video plays on this object's renderer: a 16:9 screen.
        var screen = Primitive(PrimitiveType.Quad, "BS Video Player", null, Vector3.zero, new Vector3(1.6f, 0.9f, 1f), keepCollider: false);
        Place(screen, command);
        Add<BSVideoPlayer>(screen);
        Finish(screen, "Video Player");
    }

    [MenuItem(Root + "Objects/Text", false, 33)]
    private static void CreateText(MenuCommand command) =>
        CreateComponentObject<BSText>("Text", command, so => so.FindProperty("text").stringValue = "Hello World");

    [MenuItem(Root + "Objects/Audio Source", false, 34)]
    private static void CreateAudioSource(MenuCommand command) =>
        // Heard from where it is, like most sound in a space.
        CreateComponentObject<BSAudioSource>("Audio Source", command, so => so.FindProperty("spatialBlend").floatValue = 1f);

    [MenuItem(Root + "Objects/Portal", false, 35)]
    private static void CreatePortal(MenuCommand command) => CreateComponentObject<BSPortal>("Portal", command);

    [MenuItem(Root + "Objects/GLTF Model", false, 36)]
    private static void CreateGltf(MenuCommand command) =>
        CreateComponentObject<BSGLTF>("GLTF Model", command, so => so.FindProperty("addColliders").boolValue = true);

    [MenuItem(Root + "Objects/Synced Object", false, 37)]
    private static void CreateSyncedObject(MenuCommand command)
    {
        var go = Primitive(PrimitiveType.Cube, "BS Synced Object", null, Vector3.zero, Vector3.one * 0.3f);
        Place(go, command);
        var body = Add<Rigidbody>(go);
        body.interpolation = RigidbodyInterpolation.Interpolate;
        Add<BSSyncedObject>(go);
        Finish(go, "Synced Object");
    }

    [MenuItem(Root + "Objects/UI Panel", false, 38)]
    private static void CreateUIPanel(MenuCommand command) => CreateComponentObject<BSUIPanel>("UI Panel", command);

    [MenuItem(Root + "Objects/Billboard", false, 40)]
    private static void CreateBillboard(MenuCommand command)
    {
        // A quad that turns to face the player.
        var go = Primitive(PrimitiveType.Quad, "BS Billboard", null, Vector3.zero, Vector3.one, keepCollider: false);
        Place(go, command);
        Add<BSBillboard>(go);
        Finish(go, "Billboard");
    }

    [MenuItem(Root + "Objects/Collider Events", false, 41)]
    private static void CreateColliderEvents(MenuCommand command)
    {
        // A trigger volume that reports enter/exit to the page's script and to Visual Scripting.
        var go = Begin("Collider Events", command);
        var box = Add<BoxCollider>(go);
        box.isTrigger = true;
        box.size = new Vector3(2f, 2f, 2f);
        box.center = new Vector3(0f, 1f, 0f);
        Add<BSColliderEvents>(go);
        Finish(go, "Collider Events");
    }

    // ------------------------------------------------------------------ Helpers

    private static void CreatePrefab(string guid, string displayName, MenuCommand command)
    {
        var path = AssetDatabase.GUIDToAssetPath(guid);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (!prefab)
        {
            Debug.LogError($"Creator SDK {displayName} prefab is missing. Reinstall the Creator SDK package.");
            return;
        }

        var parent = command.context as GameObject;
        var scene = parent ? parent.scene : SceneManager.GetActiveScene();
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        Undo.RegisterCreatedObjectUndo(instance, $"Create {displayName}");
        Place(instance, command);
        instance.name = $"BS {displayName}";
        Finish(instance, displayName);
    }

    private static void CreateComponentObject<T>(string displayName, MenuCommand command, Action<SerializedObject> setup = null)
        where T : Component
    {
        var go = Begin(displayName, command);
        var component = Add<T>(go);
        if (setup != null)
            Set(component, setup);
        Finish(go, displayName);
    }

    private static GameObject Begin(string displayName, MenuCommand command)
    {
        var go = ObjectFactory.CreateGameObject($"BS {displayName}");
        Place(go, command);
        return go;
    }

    // Where Unity's own GameObject menu puts a new object, so these land like a cube would: under the object the
    // menu was opened on, under the default parent, at the world origin or the Scene view's pivot (as Preferences >
    // Scene View says), and in the prefab being edited. Parenting there copies the parent's layer onto the whole new
    // hierarchy and resets the root's scale, though, and seats, grab handles and the video screen depend on theirs,
    // so both are put back.
    private static void Place(GameObject go, MenuCommand command)
    {
        var transforms = go.GetComponentsInChildren<Transform>(true);
        var layers = new int[transforms.Length];
        for (var i = 0; i < transforms.Length; i++)
            layers[i] = transforms[i].gameObject.layer;
        var scale = go.transform.localScale;

        ObjectFactory.PlaceGameObject(go, command.context as GameObject);

        for (var i = 0; i < transforms.Length; i++)
            transforms[i].gameObject.layer = layers[i];
        go.transform.localScale = scale;
    }

    private static void Finish(GameObject go, string displayName)
    {
        Selection.activeGameObject = go;
        Undo.SetCurrentGroupName($"Create {displayName}");
    }

    private static T Add<T>(GameObject go) where T : Component => ObjectFactory.AddComponent<T>(go);

    private static GameObject Child(string name, Transform parent)
    {
        var go = ObjectFactory.CreateGameObject(name);
        go.transform.SetParent(parent, false);
        return go;
    }

    private static void Set(Object target, Action<SerializedObject> set)
    {
        var so = new SerializedObject(target);
        set(so);
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 localScale, bool keepCollider = true)
    {
        var go = ObjectFactory.CreatePrimitive(type);
        go.name = name;
        if (parent)
            go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localScale = localScale;
        if (!keepCollider)
            Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    private static GameObject GrabbableRoot(string displayName, MenuCommand command, float mass)
    {
        var root = Begin(displayName, command);
        var body = Add<Rigidbody>(root);
        body.mass = mass;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        Add<BSWorldObject>(root);
        Add<BSSyncedObject>(root);
        return root;
    }

    // Makes this object's own collider the grab handle, on the Grabbable layer.
    private static void MakeHandle(GameObject go, BSGrabType type, float radius)
    {
        go.layer = GrabbableLayer;
        var handle = Add<BSGrabHandle>(go);
        Set(handle, so =>
        {
            so.FindProperty("grabType").intValue = (int)type;
            so.FindProperty("grabRadius").floatValue = radius;
        });
    }

    // A trigger capsule around the grip, as the SDK's SimpleGun has it.
    private static GameObject PointHandle(Transform root)
    {
        var handle = Child("GrabHandle", root);
        var capsule = Add<CapsuleCollider>(handle);
        capsule.isTrigger = true;
        capsule.radius = 0.06f;
        capsule.height = 0.18f;
        capsule.direction = 1;
        MakeHandle(handle, BSGrabType.Point, 0.01f);
        return handle;
    }
}
#endif
