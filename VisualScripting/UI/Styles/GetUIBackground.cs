#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using Banter.UI.Bridge;
using Banter.UI.Core;
using UnityEngine;
using UnityEngine.UIElements;
using Banter.VisualScripting.UI.Helpers;

namespace Banter.VisualScripting
{
    [UnitTitle("Get UI Background")]
    [UnitShortTitle("Get UI Background")]
    [UnitCategory("Banter\\UI\\Styles\\Appearance")]
    [TypeIcon(typeof(BanterObjectId))]
    public class GetUIBackground : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        public ValueInput elementId;

        [DoNotSerialize]
        public ValueInput elementName;

        [DoNotSerialize]
        public ValueOutput elementIdOutput;

        [DoNotSerialize]
        public ValueOutput backgroundColor;

        [DoNotSerialize]
        public ValueOutput backgroundTexture;

        [DoNotSerialize]
        public ValueOutput backgroundSprite;

        [DoNotSerialize]
        public ValueOutput backgroundVectorImage;

        [DoNotSerialize]
        public ValueOutput backgroundRenderTexture;

        [DoNotSerialize]
        public ValueOutput tintColor;

        [DoNotSerialize]
        public ValueOutput hasBackground;

        [DoNotSerialize]
        public ValueOutput backgroundType;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var targetId = flow.GetValue<string>(elementId);
                var targetName = flow.GetValue<string>(elementName);

                // Resolve element name to ID if needed
                string elemId = UIElementResolverHelper.ResolveElementIdOrName(targetId, targetName);

                if (!UIPanelExtensions.ValidateElementForOperation(elemId, "GetUIBackground"))
                {
                    SetDefaultValues(flow, elemId);
                    return outputTrigger;
                }

                try
                {
                    // Get the panel and element directly
                    var panel = UIPanelExtensions.GetPanelByElementId(elemId);
                    if (panel == null)
                    {
                        Debug.LogError($"[GetUIBackground] Could not find panel for element '{elemId}'");
                        SetDefaultValues(flow, elemId);
                        return outputTrigger;
                    }

                    var bridge = GetUIElementBridge(panel);
                    if (bridge == null)
                    {
                        Debug.LogError($"[GetUIBackground] Could not get UIElementBridge from panel");
                        SetDefaultValues(flow, elemId);
                        return outputTrigger;
                    }

                    var element = bridge.GetElement(elemId);
                    if (element == null)
                    {
                        Debug.LogError($"[GetUIBackground] Element '{elemId}' not found");
                        SetDefaultValues(flow, elemId);
                        return outputTrigger;
                    }

                    // Set element ID for chaining
                    flow.SetValue(elementIdOutput, elemId);

                    // Get background color
                    var bgColor = element.resolvedStyle.backgroundColor;
                    flow.SetValue(backgroundColor, bgColor);

                    // Get tint color
                    var tint = element.resolvedStyle.unityBackgroundImageTintColor;
                    flow.SetValue(tintColor, tint);

                    // Get background image
                    var bgImage = element.resolvedStyle.backgroundImage;

                    bool hasBg = false;
                    string bgTypeStr = "None";

                    // Check sprite first
                    if (bgImage.sprite != null)
                    {
                        hasBg = true;
                        flow.SetValue(backgroundSprite, bgImage.sprite);
                        flow.SetValue(backgroundTexture, null);
                        flow.SetValue(backgroundRenderTexture, null);
                        flow.SetValue(backgroundVectorImage, null);
                        bgTypeStr = "Sprite";
                    }
                    // Check vector image
                    else if (bgImage.vectorImage != null)
                    {
                        hasBg = true;
                        flow.SetValue(backgroundVectorImage, bgImage.vectorImage);
                        flow.SetValue(backgroundTexture, null);
                        flow.SetValue(backgroundRenderTexture, null);
                        flow.SetValue(backgroundSprite, null);
                        bgTypeStr = "VectorImage";
                    }
                    // Check render texture
                    else if (bgImage.renderTexture != null)
                    {
                        hasBg = true;
                        flow.SetValue(backgroundRenderTexture, bgImage.renderTexture);
                        flow.SetValue(backgroundTexture, null);
                        flow.SetValue(backgroundSprite, null);
                        flow.SetValue(backgroundVectorImage, null);
                        bgTypeStr = "RenderTexture";
                    }
                    // Check texture2D
                    else if (bgImage.texture != null)
                    {
                        hasBg = true;
                        flow.SetValue(backgroundTexture, bgImage.texture);
                        flow.SetValue(backgroundRenderTexture, null);
                        flow.SetValue(backgroundSprite, null);
                        flow.SetValue(backgroundVectorImage, null);
                        bgTypeStr = "Texture2D";
                    }
                    else
                    {
                        // No background image, just color
                        flow.SetValue(backgroundTexture, null);
                        flow.SetValue(backgroundRenderTexture, null);
                        flow.SetValue(backgroundSprite, null);
                        flow.SetValue(backgroundVectorImage, null);

                        // Check if there's a background color
                        if (bgColor.a > 0)
                        {
                            hasBg = true;
                            bgTypeStr = "Color";
                        }
                    }

                    flow.SetValue(hasBackground, hasBg);
                    flow.SetValue(backgroundType, bgTypeStr);

                    Debug.Log($"[GetUIBackground] Got background for element '{elemId}': Type={bgTypeStr}, Color={bgColor}, Tint={tint}");
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[GetUIBackground] Failed to get background: {e.Message}");
                    SetDefaultValues(flow, elemId);
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            elementId = ValueInput<string>("Element ID", "");
            elementName = ValueInput<string>("Element Name", "");

            // Outputs
            elementIdOutput = ValueOutput<string>("Element ID");
            backgroundColor = ValueOutput<Color>("Background Color");
            backgroundTexture = ValueOutput<Texture2D>("Texture");
            backgroundSprite = ValueOutput<Sprite>("Sprite");
            backgroundVectorImage = ValueOutput<VectorImage>("Vector Image");
            backgroundRenderTexture = ValueOutput<RenderTexture>("Render Texture");
            tintColor = ValueOutput<Color>("Tint Color");
            hasBackground = ValueOutput<bool>("Has Background");
            backgroundType = ValueOutput<string>("Background Type");
        }

        private void SetDefaultValues(Flow flow, string elemId)
        {
            flow.SetValue(elementIdOutput, elemId);
            flow.SetValue(backgroundColor, Color.clear);
            flow.SetValue(backgroundTexture, null);
            flow.SetValue(backgroundSprite, null);
            flow.SetValue(backgroundVectorImage, null);
            flow.SetValue(backgroundRenderTexture, null);
            flow.SetValue(tintColor, Color.white);
            flow.SetValue(hasBackground, false);
            flow.SetValue(backgroundType, "None");
        }

        /// <summary>
        /// Gets the UIElementBridge from a BanterUIPanel using reflection
        /// </summary>
        private UIElementBridge GetUIElementBridge(BanterUIPanel panel)
        {
            try
            {
                var panelType = typeof(BanterUIPanel);
                var bridgeField = panelType.GetField("uiElementBridge", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                return bridgeField?.GetValue(panel) as UIElementBridge;
            }
            catch
            {
                return null;
            }
        }
    }
}
#endif
