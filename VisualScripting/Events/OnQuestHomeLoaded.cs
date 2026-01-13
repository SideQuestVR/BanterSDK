#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using UnityEngine;

namespace Banter.VisualScripting
{
    [UnitTitle("On Quest Home Loaded")]
    [UnitShortTitle("On Quest Home Loaded")]
    [UnitCategory("Events\\Banter\\Space")]
    [TypeIcon(typeof(BanterObjectId))]
    public class OnQuestHomeLoaded : EventUnit<QuestHomeLoadedEventArgs>
    {
        [DoNotSerialize]
        [PortLabelHidden]
        public ValueInput questHomeObject { get; private set; }

        [DoNotSerialize]
        public ValueOutput success { get; private set; }

        [DoNotSerialize]
        public ValueOutput errorMessage { get; private set; }

        protected override bool register => true;

        public override EventHook GetHook(GraphReference reference)
        {
            return new EventHook("OnQuestHomeLoaded");
        }

        protected override void Definition()
        {
            base.Definition();
            questHomeObject = ValueInput<GameObject>("Quest Home Object", null);
            success = ValueOutput<bool>("Success");
            errorMessage = ValueOutput<string>("Error Message");
        }

        public override void StartListening(GraphStack stack)
        {
            base.StartListening(stack);

            var questHomeGo = Flow.FetchValue<GameObject>(questHomeObject, stack.ToReference());
            if (questHomeGo != null)
            {
                var questHome = questHomeGo.GetComponent<BanterQuestHome>();
                if (questHome != null)
                {
                    // Subscribe to the loaded UnityEvent
                    questHome.loaded.AddListener((bool loadSuccess, string message) =>
                    {
                        // Trigger the visual scripting event using EventBus
                        var args = new QuestHomeLoadedEventArgs
                        {
                            success = loadSuccess,
                            message = message,
                            gameObjectId = questHomeGo.GetInstanceID()
                        };

                        EventBus.Trigger("OnQuestHomeLoaded", args);
                    });
                }
                else
                {
                    Debug.LogWarning("[OnQuestHomeLoaded] No BanterQuestHome component found on GameObject");
                }
            }
        }

        protected override bool ShouldTrigger(Flow flow, QuestHomeLoadedEventArgs args)
        {
            var questHomeGo = flow.GetValue<GameObject>(questHomeObject);
            if (questHomeGo == null) return false;

            // Only trigger if the event is for this specific GameObject
            return args.gameObjectId == questHomeGo.GetInstanceID();
        }

        protected override void AssignArguments(Flow flow, QuestHomeLoadedEventArgs args)
        {
            flow.SetValue(success, args.success);
            flow.SetValue(errorMessage, args.message);
        }
    }

    /// <summary>
    /// Event args for Quest Home loaded event
    /// </summary>
    public class QuestHomeLoadedEventArgs
    {
        public bool success;
        public string message;
        public int gameObjectId;
    }
}
#endif
