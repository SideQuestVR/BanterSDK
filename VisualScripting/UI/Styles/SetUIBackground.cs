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
    public enum BackgroundType
    {
        Color,
        Texture2D,
        RenderTexture,
        Sprite,
        VectorImage
    }

    [UnitTitle("Set UI Background")]
    [UnitShortTitle("Set UI Background")]
    [UnitCategory("Banter\\UI\\Styles\\Appearance")]
    [TypeIcon(typeof(BanterObjectId))]
    public class SetUIBackground : Unit
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
        public ValueInput backgroundType;

        [DoNotSerialize]
        public ValueInput texture;

        [DoNotSerialize]
        public ValueInput renderTexture;

        [DoNotSerialize]
        public ValueInput sprite;

        [DoNotSerialize]
        public ValueInput vectorImage;

        [DoNotSerialize]
        public ValueInput color;

        [DoNotSerialize]
        public ValueInput tintColor;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var targetId = flow.GetValue<string>(elementId);
                var targetName = flow.GetValue<string>(elementName);

                // Resolve element name to ID if needed
                string elemId = UIElementResolverHelper.ResolveElementIdOrName(targetId, targetName);
                var bgType = flow.GetValue<BackgroundType>(backgroundType);

                if (!UIPanelExtensions.ValidateElementForOperation(elemId, "SetUIBackground"))
                {
                    return outputTrigger;
                }

                try
                {
                    // Get the panel and element directly
                    var panel = UIPanelExtensions.GetPanelByElementId(elemId);
                    if (panel == null)
                    {
                        Debug.LogError($"[SetUIBackground] Could not find panel for element '{elemId}'");
                        return outputTrigger;
                    }

                    var bridge = GetUIElementBridge(panel);
                    if (bridge == null)
                    {
                        Debug.LogError($"[SetUIBackground] Could not get UIElementBridge from panel");
                        return outputTrigger;
                    }

                    var element = bridge.GetElement(elemId);
                    if (element == null)
                    {
                        Debug.LogError($"[SetUIBackground] Element '{elemId}' not found");
                        return outputTrigger;
                    }

                    // Apply background based on type
                    switch (bgType)
                    {
                        case BackgroundType.Color:
                            var colorValue = flow.GetValue<Color>(color);
                            element.style.backgroundColor = colorValue;
                            Debug.Log($"[SetUIBackground] Set background color for element '{elemId}'");
                            break;

                        case BackgroundType.Texture2D:
                            var tex2D = flow.GetValue<Texture2D>(texture);
                            if (tex2D != null)
                            {
                                element.style.backgroundImage = new StyleBackground(tex2D);
                                ApplyTintColor(element, flow);
                                Debug.Log($"[SetUIBackground] Set Texture2D background for element '{elemId}'");
                            }
                            else
                            {
                                Debug.LogWarning($"[SetUIBackground] Texture2D is null for element '{elemId}'");
                            }
                            break;

                        case BackgroundType.RenderTexture:
                            var renderTex = flow.GetValue<RenderTexture>(renderTexture);
                            if (renderTex != null)
                            {
                                element.style.backgroundImage = Background.FromRenderTexture(renderTex);
                                ApplyTintColor(element, flow);
                                Debug.Log($"[SetUIBackground] Set RenderTexture background for element '{elemId}'");
                            }
                            else
                            {
                                Debug.LogWarning($"[SetUIBackground] RenderTexture is null for element '{elemId}'");
                            }
                            break;

                        case BackgroundType.Sprite:
                            var spriteValue = flow.GetValue<Sprite>(sprite);
                            if (spriteValue != null)
                            {
                                element.style.backgroundImage = new StyleBackground(spriteValue);
                                ApplyTintColor(element, flow);
                                Debug.Log($"[SetUIBackground] Set Sprite background for element '{elemId}'");
                            }
                            else
                            {
                                Debug.LogWarning($"[SetUIBackground] Sprite is null for element '{elemId}'");
                            }
                            break;

                        case BackgroundType.VectorImage:
                            var vectorImg = flow.GetValue<VectorImage>(vectorImage);
                            if (vectorImg != null)
                            {
                                element.style.backgroundImage = new StyleBackground(vectorImg);
                                ApplyTintColor(element, flow);
                                Debug.Log($"[SetUIBackground] Set VectorImage background for element '{elemId}'");
                            }
                            else
                            {
                                Debug.LogWarning($"[SetUIBackground] VectorImage is null for element '{elemId}'");
                            }
                            break;
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[SetUIBackground] Failed to set background: {e.Message}");
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            elementId = ValueInput<string>("Element ID", "");
            elementName = ValueInput<string>("Element Name", "");
            backgroundType = ValueInput("Background Type", BackgroundType.Color);
            texture = ValueInput<Texture2D>("Texture", null);
            renderTexture = ValueInput<RenderTexture>("Render Texture", null);
            sprite = ValueInput<Sprite>("Sprite", null);
            vectorImage = ValueInput<VectorImage>("Vector Image", null);
            color = ValueInput("Color", Color.white);
            tintColor = ValueInput("Tint Color", Color.white);
        }

        private void ApplyTintColor(VisualElement element, Flow flow)
        {
            var tint = flow.GetValue<Color>(tintColor);
            element.style.unityBackgroundImageTintColor = tint;
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
