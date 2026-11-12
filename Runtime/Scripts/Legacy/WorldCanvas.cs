using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WorldCanvas : MonoBehaviour
{
    void Start()
    {
        var canvas = GetComponent<Canvas>();
        if (canvas != null)
        {
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
            if (!canvas.GetComponent<BS.CanvasCameraBinder>())
                canvas.gameObject.AddComponent<BS.CanvasCameraBinder>();
        }
    }

}
