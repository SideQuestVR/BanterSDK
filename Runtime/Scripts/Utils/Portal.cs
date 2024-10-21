using UnityEngine;

namespace Banter.SDK
{
    public class Portal : MonoBehaviour
    {
        bool CanActivate = false;
        public string url;
        BanterSceneEvents sceneEvents;
        async void Start()
        {
            sceneEvents = BanterScene.Instance().events;
            await new WaitForSeconds(0.5f);
            CanActivate = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("__BA_LocalPlayer") && CanActivate)
            {
                CanActivate = false;
                GetComponent<FaceTarget>().enabled = false;
                sceneEvents.OnPortalEnter.Invoke(url);
            }
        }
    }

}
