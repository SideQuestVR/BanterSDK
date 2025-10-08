#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using UnityEngine;
using UnityEngine.UIElements;

namespace Banter.VisualScripting
{
    [UnitTitle("Load UXML Asset")]
    [UnitShortTitle("Load UXML Asset")]
    [UnitCategory("Banter\\UI\\UXML")]
    [TypeIcon(typeof(BanterObjectId))]
    public class LoadUXMLAsset : Unit
    {
        private const string LogPrefix = "[LoadUXMLAsset]";

        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        [PortLabelHidden]
        [NullMeansSelf]
        public ValueInput gameObject;

        [DoNotSerialize]
        public ValueInput visualTreeAsset;

        [DoNotSerialize]
        public ValueInput resourcePath;

        [DoNotSerialize]
        public ValueOutput uiDocument;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var target = flow.GetValue<GameObject>(gameObject);
                var vta = flow.GetValue<VisualTreeAsset>(visualTreeAsset);
                var resPath = flow.GetValue<string>(resourcePath);

                try
                {
                    // Get BanterUIPanel component
                    var panel = target?.GetComponent<BanterUIPanel>();
                    if (panel == null)
                    {
                        Debug.LogError("[LoadUXMLAsset] BanterUIPanel component not found on target GameObject");
                        flow.SetValue(uiDocument, null);
                        return outputTrigger;
                    }

                    // Get existing UIDocument or create one
                    var document = target.GetComponent<UIDocument>();
                    if (document == null)
                    {
                        document = target.AddComponent<UIDocument>();
#if BANTER_UI_DEBUG
                        Debug.Log($"{LogPrefix} Created new UIDocument component");
#endif
                    }

                    VisualTreeAsset assetToLoad = vta;

                    // Load from resources if no direct asset provided
                    if (assetToLoad == null && !string.IsNullOrEmpty(resPath))
                    {
                        assetToLoad = Resources.Load<VisualTreeAsset>(resPath);
                        if (assetToLoad == null)
                        {
                            Debug.LogError($"[LoadUXMLAsset] Failed to load UXML asset from Resources: {resPath}");
                            flow.SetValue(uiDocument, null);
                            return outputTrigger;
                        }
#if BANTER_UI_DEBUG
                        Debug.Log($"{LogPrefix} Loaded UXML asset from Resources: {resPath}");
#endif
                    }

                    if (assetToLoad == null)
                    {
                        Debug.LogError("[LoadUXMLAsset] No VisualTreeAsset provided either directly or via resource path");
                        flow.SetValue(uiDocument, null);
                        return outputTrigger;
                    }

                    // Assign the visual tree asset to the document
                    document.visualTreeAsset = assetToLoad;

                    // Only assign panel settings if the document doesn't already have them
                    if (document.panelSettings == null)
                    {
                        // Get panel settings from the BanterUIPanel if available
                        var panelSettings = GetPanelSettings(panel);
                        if (panelSettings != null)
                        {
                            document.panelSettings = panelSettings;
#if BANTER_UI_DEBUG
                        Debug.Log($"{LogPrefix} Applied panel settings from BanterUIPanel");
#endif
                    }
                    else
                    {
#if BANTER_UI_DEBUG
                        Debug.Log($"{LogPrefix} No panel settings found, document will use its own or default settings");
#endif
                    }
                }
                else
                {
#if BANTER_UI_DEBUG
                    Debug.Log($"{LogPrefix} UIDocument already has panel settings, preserving existing configuration");
#endif
                }

                    // Force rebuild of the UI
                    if (document.rootVisualElement != null)
                    {
                        document.rootVisualElement.MarkDirtyRepaint();
                    }

#if BANTER_UI_DEBUG
                    Debug.Log($"{LogPrefix} Successfully loaded UXML asset and assigned to UIDocument");
#endif

                    flow.SetValue(uiDocument, document);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[LoadUXMLAsset] Failed to load UXML asset: {e.Message}");
                    flow.SetValue(uiDocument, null);
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("");
            gameObject = ValueInput<GameObject>(nameof(gameObject), null).NullMeansSelf();
            visualTreeAsset = ValueInput<VisualTreeAsset>("UXML Asset", null);
            resourcePath = ValueInput<string>("Resource Path", "");
            uiDocument = ValueOutput<UIDocument>("UI Document");
        }

        /// <summary>
        /// Gets the PanelSettings from a BanterUIPanel using reflection
        /// </summary>
        private PanelSettings GetPanelSettings(BanterUIPanel panel)
        {
            try
            {
                var panelType = typeof(BanterUIPanel);
                var panelSettingsField = panelType.GetField("panelSettings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                return panelSettingsField?.GetValue(panel) as PanelSettings;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[LoadUXMLAsset] Failed to get PanelSettings: {e.Message}");
                return null;
            }
        }
    }
}
#endif
