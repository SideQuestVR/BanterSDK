#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class CreatorConveniencesMenu
{
    private const string SpawnPointGuid = "c62a2d0bbf7d4fc7b5958e11c2f05501";
    private const string SpawnRangeGuid = "12b6892e0a2744c28aa7f7ad3a98e401";
    private const string SeatGuid = "fb1c6d01b9804fa9a77c30532a802802";
    private const string LocalTeleporterGuid = "f0fb9ff341064868a90e08695bd41a58";

    [MenuItem("GameObject/BS/Spawn Point", false, 10)]
    private static void CreateSpawnPoint(MenuCommand command) => Create(SpawnPointGuid, "Spawn Point", command);

    [MenuItem("GameObject/BS/Spawn Range", false, 15)]
    private static void CreateSpawnRange(MenuCommand command) => Create(SpawnRangeGuid, "Spawn Range", command);

    [MenuItem("GameObject/BS/Seat", false, 20)]
    private static void CreateSeat(MenuCommand command) => Create(SeatGuid, "Seat", command);

    [MenuItem("GameObject/BS/Local Space Teleporter", false, 25)]
    private static void CreateLocalTeleporter(MenuCommand command) => Create(LocalTeleporterGuid, "Local Space Teleporter", command);

    private static void Create(string guid, string displayName, MenuCommand command)
    {
        var path = AssetDatabase.GUIDToAssetPath(guid);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (!prefab)
        {
            Debug.LogError($"Creator SDK {displayName} prefab is missing. Reimport the Creator Conveniences sample.");
            return;
        }

        var parent = command.context as GameObject;
        var scene = parent ? parent.scene : SceneManager.GetActiveScene();
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        Undo.RegisterCreatedObjectUndo(instance, $"Create {displayName}");
        if (parent)
            GameObjectUtility.SetParentAndAlign(instance, parent);
        instance.name = $"BS {displayName}";
        Selection.activeGameObject = instance;
        Undo.SetCurrentGroupName($"Create {displayName}");
    }
}
#endif
