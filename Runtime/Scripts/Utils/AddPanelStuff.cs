using System;
using UnityEngine;
using UnityEngine.UIElements;

public class AddPanelStuff : MonoBehaviour
{
    [SerializeField] private UIDocument uIDocument;

    private PanelRaycaster _raycaster;

    /// <summary>
    /// Fired when a UITK panel's runtime panel becomes ready. Subscribers receive the IPanel.
    /// Used by UIInputLock to auto-register panels for text field focus monitoring and by
    /// VirtualKeyboard to auto-wire TextFields.
    /// </summary>
    public static event Action<IPanel> PanelReady;

    /// <summary>
    /// Announce a panel that was bound manually (without an AddPanelStuff component),
    /// e.g. the Greenfield menu, so PanelReady subscribers still hear about it.
    /// </summary>
    public static void NotifyPanelReady(IPanel panel) => PanelReady?.Invoke(panel);

    void Start()
    {
        Invoke(nameof(WaitForRuntimePanel),1f);
    }

    public void Reset()
    {
        WaitForRuntimePanel();
    }

    private void WaitForRuntimePanel()
    {
        if (uIDocument == null)
            uIDocument = GetComponent<UIDocument>();

        if (uIDocument.runtimePanel == null)
        {
            Invoke(nameof(WaitForRuntimePanel),0.5f);
            return;
        }

        if (uIDocument != null)
        {
            _raycaster = GetComponent<PanelRaycaster>();
            if (_raycaster != null)
            {
                _raycaster.panel = uIDocument.runtimePanel;
            }

            var eventHandler = GetComponent<PanelEventHandler>();
            if (eventHandler != null)
            {
                eventHandler.panel = uIDocument.runtimePanel;
            }

            uIDocument.runtimePanel.selectableGameObject = gameObject;

            PanelReady?.Invoke(uIDocument.runtimePanel);
        }
    }
}
