#if BANTER_VISUAL_SCRIPTING
using UnityEngine;
using UnityEngine.Events;

namespace Banter.VisualScripting
{
    public enum EventDataType
    {
        None,
        Float,
        Int,
        Bool,
        String,
        Vector2,
        Vector3,
        Color,
        Texture,
        Texture2D,
        GameObject,
        Object
    }

    [AddComponentMenu("Banter/Visual Scripting Relay")]
    public class VisualScriptingEventTyped : MonoBehaviour
    {
        public EventDataType dataType = EventDataType.None;

        // All typed events - custom editor shows only the matching one
        public UnityEvent OnEvent;
        public UnityEvent<float> OnFloatEvent;
        public UnityEvent<int> OnIntEvent;
        public UnityEvent<bool> OnBoolEvent;
        public UnityEvent<string> OnStringEvent;
        public UnityEvent<Vector2> OnVector2Event;
        public UnityEvent<Vector3> OnVector3Event;
        public UnityEvent<Color> OnColorEvent;
        public UnityEvent<Texture> OnTextureEvent;
        public UnityEvent<Texture2D> OnTexture2DEvent;
        public UnityEvent<GameObject> OnGameObjectEvent;
        public UnityEvent<Object> OnObjectEvent;

        public void TriggerEvent() => OnEvent?.Invoke();
        public void TriggerFloat(float v) => OnFloatEvent?.Invoke(v);
        public void TriggerInt(int v) => OnIntEvent?.Invoke(v);
        public void TriggerBool(bool v) => OnBoolEvent?.Invoke(v);
        public void TriggerString(string v) => OnStringEvent?.Invoke(v);
        public void TriggerVector2(Vector2 v) => OnVector2Event?.Invoke(v);
        public void TriggerVector3(Vector3 v) => OnVector3Event?.Invoke(v);
        public void TriggerColor(Color v) => OnColorEvent?.Invoke(v);
        public void TriggerTexture(Texture v) => OnTextureEvent?.Invoke(v);
        public void TriggerTexture2D(Texture2D v) => OnTexture2DEvent?.Invoke(v);
        public void TriggerGameObject(GameObject v) => OnGameObjectEvent?.Invoke(v);
        public void TriggerObject(Object v) => OnObjectEvent?.Invoke(v);
    }
}
#endif
