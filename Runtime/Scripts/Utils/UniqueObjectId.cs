using UnityEngine;
using UnityEditor;

[ExecuteInEditMode]
public class UniqueObjectId : MonoBehaviour
{
    public string Id;

    [HideInInspector]
    public Transform lastObject;

    void Start()
    {
        Gen();
    }

    public void Gen()
    {
        // Always generate a new ID
        if(string.IsNullOrEmpty(Id)) {
            Id = System.Guid.NewGuid().ToString();
        }
    }

    public void ButtonGen() {
        Id = null;
        Gen();
    }
}