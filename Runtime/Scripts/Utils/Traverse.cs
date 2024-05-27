using System;
using UnityEngine;
public class Traverse
{
    public static void Do(Transform parent, Action<Transform> callback)
    {
        foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
        {
            callback(child);
        }
    }
}
