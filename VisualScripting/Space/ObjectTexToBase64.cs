#if BANTER_VISUAL_SCRIPTING
using UnityEngine;
using Unity.VisualScripting;
using Banter.SDK;
using Banter.Utilities.Async;

namespace Banter.VisualScripting
{
    [UnitTitle("GameObject texture to Base64")]
    [UnitShortTitle("ObjectTextureToBase64")]
    [UnitCategory("Banter")]
    [TypeIcon(typeof(BanterObjectId))]
    public class ObjectTexToBase64 : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;
    
        [DoNotSerialize]
        public ControlOutput outputTrigger;
        [DoNotSerialize]
        [NullMeansSelf]
        public ValueInput gameObject;

        [DoNotSerialize]
        public ValueInput material;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var _gameObject = flow.GetValue<GameObject>(gameObject);
                var _material = flow.GetValue<int>(material);
                UnityMainThreadTaskScheduler.Default.Enqueue(async () =>
                {
                    var data = await BanterScene.Instance().GameObjectTextureToBase64(_gameObject, _material);
#if BANTER_VISUAL_SCRIPTING
                    EventBus.Trigger("OnTextureToBase64", new CustomEventArgs("texture", new object[] { data }));
#endif
                });
                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
            material = ValueInput("Material Index", 0);
            gameObject = ValueInput<GameObject>(nameof(gameObject), null).NullMeansSelf();
        }
    }
}
#endif
