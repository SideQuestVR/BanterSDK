using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ObjectSpawner : MonoBehaviour
{
    public void SpawnObject(GameObject obj)
    {
        Instantiate(obj, transform.position, transform.rotation);
    }
}
