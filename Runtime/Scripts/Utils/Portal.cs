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
            if (other.CompareTag("__BA_LocalPlayer") && CanActivate)
            {
                CanActivate = false;
                GetComponent<FaceTarget>().enabled = false;
                sceneEvents.OnPortalEnter.Invoke(url);
            }
        }
    }

}
