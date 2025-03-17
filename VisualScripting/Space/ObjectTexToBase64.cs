#if BANTER_VISUAL_SCRIPTING
using UnityEngine;
using Unity.VisualScripting;
using Banter.SDK;

namespace Banter.VisualScripting
{
    [UnitTitle("GameObject texture to Base64")]
    [UnitShortTitle("ObjectTextureToBase64")]
    [UnitCategory("Banter\\AI")]
    [TypeIcon(typeof(BanterObjectId))]
    public class ObjectTexToBase64 : Unit
    {
        [DoNotSerialize]
        public ValueInput gameObject;

        [DoNotSerialize]
        public ValueInput material;

        [DoNotSerialize]
        public ValueOutput base64;


        protected override void Definition()
        {
            material = ValueInput("Material Index", 0);
            gameObject = ValueInput<GameObject>(nameof(gameObject), null).NullMeansSelf();
            base64 = ValueOutput<string>("Base64", flow => {
                return BanterScene.Instance().GameObjectTextureToBase64(flow.GetValue<GameObject>(gameObject), flow.GetValue<int>(material));
            });
        }
    }
}
#endif
