using UnityEngine;
using Unity.VisualScripting;
using BS;
using UnityEngine.Events;

namespace BS.VisualScripting
{
    [AddComponentMenu("")]
    public class VisualScriptingEvent : MonoBehaviour
    {
        public UnityEvent OnCustomEvent;
        public void TriggerEvent()
        {
            OnCustomEvent?.Invoke();
        }
    }
}
