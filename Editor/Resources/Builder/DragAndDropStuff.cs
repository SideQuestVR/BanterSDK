using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public class DragAndDropStuff
{
    private VisualElement _dropArea;
    private Action<bool, string, string[]> _onDrop;
    public VisualElement SetupDropArea(VisualElement element, Action<bool, string, string[]> onDrop)
    {
        _onDrop = onDrop;
        _dropArea = element;
        _dropArea.RegisterCallback<DragUpdatedEvent>(OnDragUpdate);
        _dropArea.RegisterCallback<DragPerformEvent>(OnDragPerform);
        DragAndDrop.objectReferences = new UnityEngine.Object[] { };
        return _dropArea;
    }

    void OnDragUpdate(DragUpdatedEvent _)
    {
        DragAndDrop.visualMode = DragAndDropVisualMode.Generic;
    }
    void OnDragPerform(DragPerformEvent _)
    {
        if (DragAndDrop.paths.Length < 1)
        {
            return;
        }
        var sceneFileDrop = DragAndDrop.paths.FirstOrDefault(x => x.EndsWith(".unity"));
        bool isScene = sceneFileDrop != null;
        _onDrop?.Invoke(isScene, sceneFileDrop, DragAndDrop.paths);
    }

    public void Disable()
    {
        if (_dropArea != null)
        {
            _dropArea.UnregisterCallback<DragUpdatedEvent>(OnDragUpdate);
            _dropArea.UnregisterCallback<DragPerformEvent>(OnDragPerform);
        }
    }
}
