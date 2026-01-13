#if BANTER_VISUAL_SCRIPTING
using UnityEngine;
using Unity.VisualScripting;
using Banter.SDK;

namespace Banter.VisualScripting
{
    [UnitTitle("Load Quest Home")]
    [UnitShortTitle("Load Quest Home")]
    [UnitCategory("Banter\\Space")]
    [TypeIcon(typeof(BanterObjectId))]
    public class LoadQuestHome : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ControlOutput invalid;

        [DoNotSerialize]
        public ValueInput url;

        [DoNotSerialize]
        public ValueInput addColliders;

        [DoNotSerialize]
        public ValueInput climbable;

        [DoNotSerialize]
        public ValueOutput questHomeObject;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var _url = flow.GetValue<string>(url);
                var _addColliders = flow.GetValue<bool>(addColliders);
                var _climbable = flow.GetValue<bool>(climbable);

                // Validate URL format before doing anything
                if (!SideQuestUrlResolver.ValidateUrlFormat(_url))
                {
                    Debug.LogWarning($"[LoadQuestHome] Invalid URL format: {_url}");
                    return invalid;
                }

                // Get the parent GameObject (visual script's GameObject)
                GameObject parentGameObject = flow.stack.gameObject;

                // Find existing Quest Home child (if any)
                BanterQuestHome oldQuestHome = null;
                foreach (Transform child in parentGameObject.transform)
                {
                    var questHome = child.GetComponent<BanterQuestHome>();
                    if (questHome != null)
                    {
                        oldQuestHome = questHome;
                        Debug.Log($"[LoadQuestHome] Found existing Quest Home, will swap after new one loads");
                        break;
                    }
                }

                // Create new child GameObject for Quest Home
                GameObject questHomeGo = new GameObject($"QuestHome_{System.DateTime.Now.Ticks}");
                questHomeGo.transform.SetParent(parentGameObject.transform, false);

                // Add components
                var questHomeComponent = questHomeGo.AddComponent<BanterQuestHome>();
                questHomeGo.AddComponent<AudioSource>();

                // Set up cleanup handlers for both success and failure cases
                GameObject oldGameObject = oldQuestHome != null ? oldQuestHome.gameObject : null;

                // Subscribe to loaded event to handle success/failure
                questHomeComponent.loaded.AddListener((success, message) => {
                    if (success)
                    {
                        // New Quest Home loaded successfully
                        if (oldGameObject != null)
                        {
                            Debug.Log("[LoadQuestHome] New Quest Home loaded successfully, destroying old one");
                            Object.Destroy(oldGameObject);
                        }
                        else
                        {
                            Debug.Log("[LoadQuestHome] Quest Home loaded successfully");
                        }
                    }
                    else
                    {
                        // New Quest Home failed to load
                        Debug.LogWarning($"[LoadQuestHome] New Quest Home failed to load: {message}");

                        if (oldGameObject != null)
                        {
                            Debug.Log("[LoadQuestHome] Keeping old Quest Home active and destroying failed one");
                        }
                        else
                        {
                            Debug.Log("[LoadQuestHome] No previous Quest Home to keep, destroying failed one");
                        }

                        // Always destroy the failed Quest Home GameObject to clean up
                        Debug.Log("[LoadQuestHome] Destroying failed Quest Home GameObject");
                        Object.Destroy(questHomeGo);
                    }
                });

                // Set properties (starts async loading)
                questHomeComponent.Url = _url;
                questHomeComponent.AddColliders = _addColliders;
                questHomeComponent.Climbable = _climbable;

                // Return new Quest Home GameObject
                flow.SetValue(questHomeObject, questHomeGo);

                Debug.Log($"[LoadQuestHome] Started loading Quest Home from {_url}");

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            invalid = ControlOutput("Invalid");
            url = ValueInput("URL", "https://sidequestvr.com/app/167567/canyon-environment");
            addColliders = ValueInput("Add Colliders", true);
            climbable = ValueInput("Climbable", false);
            questHomeObject = ValueOutput<GameObject>("Quest Home Object");
        }
    }
}
#endif
