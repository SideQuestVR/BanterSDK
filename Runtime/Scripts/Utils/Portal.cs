using UnityEngine;

namespace BS
{
    public class Portal : MonoBehaviour
    {
        bool CanActivate = false;
        public string url;
        BSSceneEvents sceneEvents;
        async void Start()
        {
            sceneEvents = BSScene.Instance().events;
            await new WaitForSeconds(0.5f);
            CanActivate = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("BSLocalCharacter") && CanActivate)
            {
#if UNITY_EDITOR && !GREENFIELD_PROJECT
                // SDK Play Mode tests the creator's own world, so a portal never leaves it; only the client follows one.
                Debug.Log($"[BSPortal] In the client, this portal takes the player to {(string.IsNullOrEmpty(url) ? "(no URL set)" : url)}. SDK Play Mode stays in your world.");
#else
                CanActivate = false;
                GetComponent<FaceTarget>().enabled = false;
                sceneEvents.OnPortalEnter.Invoke(url);
#endif
            }
        }
    }

}
