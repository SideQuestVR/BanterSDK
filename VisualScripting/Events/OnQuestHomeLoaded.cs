using Unity.VisualScripting;
using BS;
using UnityEngine;
using UnityEngine.Events;

namespace BS.VisualScripting
{
    [UnitTitle("On Quest Home Loaded")]
    [UnitShortTitle("On Quest Home Loaded")]
    [UnitCategory("Events\\BS\\Space")]
    [TypeIcon(typeof(BSObjectId))]
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

        // Per graph instance: the BSQuestHome listened to and the listener, so StopListening can remove it.
        public class QuestHomeData : Data
        {
            public BSQuestHome questHome;
            public UnityAction<bool, string> onLoaded;
        }

        public override IGraphElementData CreateData()
        {
            return new QuestHomeData();
        }

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

            var data = stack.GetElementData<QuestHomeData>(this);
            if (data.onLoaded != null) return;

            var questHomeGo = Flow.FetchValue<GameObject>(questHomeObject, stack.ToReference());
            if (questHomeGo != null)
            {
                var questHome = questHomeGo.GetComponent<BSQuestHome>();
                if (questHome != null)
                {
                    // Subscribe to the loaded UnityEvent
                    data.questHome = questHome;
                    data.onLoaded = (bool loadSuccess, string message) =>
                    {
                        // Trigger the visual scripting event using EventBus
                        var args = new QuestHomeLoadedEventArgs
                        {
                            success = loadSuccess,
                            message = message,
                            gameObjectId = BSScene.UnityId(questHomeGo)
                        };

                        EventBus.Trigger("OnQuestHomeLoaded", args);
                    };
                    questHome.loaded.AddListener(data.onLoaded);
                }
                else
                {
                    Debug.LogWarning("[OnQuestHomeLoaded] No BSQuestHome component found on GameObject");
                }
            }
        }

        public override void StopListening(GraphStack stack)
        {
            base.StopListening(stack);

            var data = stack.GetElementData<QuestHomeData>(this);
            if (data.onLoaded == null) return;
            if (data.questHome != null)
            {
                data.questHome.loaded.RemoveListener(data.onLoaded);
            }
            data.questHome = null;
            data.onLoaded = null;
        }

        protected override bool ShouldTrigger(Flow flow, QuestHomeLoadedEventArgs args)
        {
            var questHomeGo = flow.GetValue<GameObject>(questHomeObject);
            if (questHomeGo == null) return false;

            // Only trigger if the event is for this specific GameObject
            return args.gameObjectId == BSScene.UnityId(questHomeGo);
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
