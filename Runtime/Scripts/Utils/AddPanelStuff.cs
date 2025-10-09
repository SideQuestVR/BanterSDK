using System;
using UnityEngine;
using UnityEngine.UIElements;

public class AddPanelStuff : MonoBehaviour
{
    [SerializeField] private UIDocument uIDocument;
    
    private PanelRaycaster _raycaster;


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
        if (uIDocument == null)
            uIDocument = GetComponent<UIDocument>();
        if (uIDocument != null)
        {
            _raycaster = GetComponent<PanelRaycaster>();
            if (_raycaster != null)
            {
                Debug.Log("[AddPanelStuff] Assigning PanelRaycaster's panel to UIDocument's runtimePanel.");
                _raycaster.panel = uIDocument.runtimePanel;
            }

            var eventHandler = GetComponent<PanelEventHandler>();
            if (eventHandler != null)
            {
                Debug.Log("[AddPanelStuff] Assigning PanelEventHandler's panel to UIDocument's runtimePanel.");
                eventHandler.panel = uIDocument.runtimePanel;
            }

            uIDocument.runtimePanel.selectableGameObject = gameObject;
        }
    }

    private void Update()
    {
        if (!_raycaster.enabled)
            _raycaster.enabled = true;
    }
}
