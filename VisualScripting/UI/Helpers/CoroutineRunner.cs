#if BANTER_VISUAL_SCRIPTING
using UnityEngine;

namespace Banter.VisualScripting.UI.Helpers
{
    /// <summary>
    /// Singleton MonoBehaviour for running coroutines from static contexts
    /// </summary>
    public class CoroutineRunner : MonoBehaviour
    {
        private static CoroutineRunner _instance;

        /// <summary>
        /// Gets or creates the singleton instance
        /// </summary>
        public static CoroutineRunner Instance
        {
            get
            {
                if (_instance == null)
                {
                    // Create a new GameObject with CoroutineRunner
                    var go = new GameObject("UIEventCoroutineRunner");
                    _instance = go.AddComponent<CoroutineRunner>();
                    DontDestroyOnLoad(go);
#if BANTER_UI_DEBUG
                    Debug.Log("[CoroutineRunner] Created singleton instance");
#endif
                }
                return _instance;
            }
        }

        private void Awake()
        {
            // Ensure only one instance exists
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }
    }
}
#endif
