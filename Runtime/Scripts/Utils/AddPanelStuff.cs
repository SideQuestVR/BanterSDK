using UnityEngine;
using UnityEngine.UIElements;

public class AddPanelStuff : MonoBehaviour
{
    [SerializeField] private UIDocument uIDocument;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if (uIDocument == null)
            uIDocument = GetComponent<UIDocument>();
        if (uIDocument != null)
        {
            var raycaster = GetComponent<PanelRaycaster>();
            if (raycaster != null)
            {
                raycaster.panel = uIDocument.runtimePanel;
            }

            var eventHandler = GetComponent<PanelEventHandler>();
            if (eventHandler != null)
            {
                eventHandler.panel = uIDocument.runtimePanel;
            }
        }
    }
}
