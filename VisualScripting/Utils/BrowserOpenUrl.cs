#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using System;

namespace Banter.VisualScripting
{
    [UnitTitle("Menu Browser Open URL")]
    [UnitShortTitle("Menu Browser Nav")]
    [UnitCategory("Banter\\Browser")]
    [TypeIcon(typeof(BanterObjectId))]
    public class MenuOpenUrl : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput url;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var urlVal = flow.GetValue<string>(url);

                BanterScene.Instance().OpenPage(urlVal, 0);

                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
            url = ValueInput("Url", string.Empty);
        }
    }

    [UnitTitle("World Browser Open URL")]
    [UnitShortTitle("World Browser Nav")]
    [UnitCategory("Banter")]
    [Obsolete("Use BanterBrowser Set Url")]
    [TypeIcon(typeof(BanterObjectId))]
    public class WorldOpenUrl : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput url;

        [DoNotSerialize]
        [NullMeansSelf]
        public ValueInput browserComponent;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var urlVal = flow.GetValue<string>(url);
                var browser = flow.GetValue<BanterBrowser>(browserComponent);

                browser.Url = urlVal;

                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
            url = ValueInput("Url", string.Empty);
            browserComponent = ValueInput<BanterBrowser>("Banter Browser", null);
            browserComponent.SetDefaultValue(null);
            browserComponent.NullMeansSelf();
        }
    }
}
#endif
