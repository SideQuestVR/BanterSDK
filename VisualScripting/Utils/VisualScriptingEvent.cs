#if BANTER_VISUAL_SCRIPTING
using UnityEngine;
using Unity.VisualScripting;
using Banter.SDK;
using UnityEngine.Events;

namespace Banter.VisualScripting
{
    public class VisualScriptingEvent : MonoBehaviour
    {
        public UnityEvent OnCustomEvent;
        public void TriggerEvent()
        {
            OnCustomEvent?.Invoke();
        }
    }
}
#endif
