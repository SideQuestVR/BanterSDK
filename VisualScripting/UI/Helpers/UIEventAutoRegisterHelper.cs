#if BANTER_VISUAL_SCRIPTING
using Banter.SDK;
using Banter.UI.Bridge;
using Banter.UI.Core;
using System.Collections;
using UnityEngine;

namespace Banter.VisualScripting.UI.Helpers
{
    /// <summary>
    /// Helper class for auto-registering UI events from Visual Scripting nodes
    /// </summary>
    public static class UIEventAutoRegisterHelper
    {
        private const string LogPrefix = "[UIEventAutoRegisterHelper]";

        /// <summary>
        /// Attempts to auto-register a Change event for the specified element with delayed retry
        /// </summary>
        /// <param name="elementId">The element ID to register the event for</param>
        /// <param name="nodeName">The name of the node requesting registration (for logging)</param>
        public static void TryRegisterChangeEventWithRetry(string elementId, string nodeName)
        {
            var runner = CoroutineRunner.Instance;
            runner.StartCoroutine(RegisterChangeEventCoroutine(elementId, nodeName));
        }

        /// <summary>
        /// Coroutine that retries registration until successful or max attempts reached
        /// </summary>
        private static IEnumerator RegisterChangeEventCoroutine(string elementId, string nodeName)
        {
            LogVerbose(nodeName, $"RegisterChangeEventCoroutine started for element '{elementId}'");

            // Wait for up to 5 seconds, checking every 0.1 seconds
            const float maxWaitTime = 5f;
            const float checkInterval = 0.1f;
            float elapsedTime = 0f;
            int attemptNumber = 0;

            while (elapsedTime < maxWaitTime)
            {
                attemptNumber++;
                LogVerbose(nodeName, $"Registration attempt {attemptNumber} (elapsed: {elapsedTime:F1}s) for element '{elementId}'");

                if (TryRegisterChangeEventImmediate(elementId, nodeName))
                {
                    LogVerbose(nodeName, $"Auto-registration succeeded on attempt {attemptNumber} after {elapsedTime:F1}s for element '{elementId}'");
                    yield break;
                }

                LogVerbose(nodeName, $"Attempt {attemptNumber} failed, waiting {checkInterval}s before retry...");
                yield return new WaitForSeconds(checkInterval);
                elapsedTime += checkInterval;
            }

            Debug.LogWarning($"[{nodeName}] Auto-registration failed after {attemptNumber} attempts ({maxWaitTime}s) for element '{elementId}'. Element may not exist or panel not initialized.");
        }

        /// <summary>
        /// Attempts immediate registration without retry
        /// </summary>
        private static bool TryRegisterChangeEventImmediate(string elementId, string nodeName)
        {
            LogVerbose(nodeName, $"TryRegisterChangeEventImmediate: Starting with elementId='{elementId}'");

            if (string.IsNullOrEmpty(elementId))
            {
                Debug.LogWarning($"[{nodeName}] TryRegisterChangeEventImmediate: elementId is null or empty, returning false");
                return false;
            }

            try
            {
                // Try to find ANY panel that's initialized
                var allPanels = UnityEngine.Object.FindObjectsOfType<BanterUIPanel>();
                LogVerbose(nodeName, $"TryRegisterChangeEventImmediate: Found {allPanels.Length} BanterUIPanel(s)");

                foreach (var panel in allPanels)
                {
                    try
                    {
                        // Check if panel has UIElementBridge component (indicates initialization is complete)
                        var bridge = panel.GetComponent<UIElementBridge>();
                        if (bridge == null)
                        {
                            LogVerbose(nodeName, $"TryRegisterChangeEventImmediate: Panel '{panel.name}' has no UIElementBridge yet, skipping");
                            continue;
                        }

                        // Try to get the formatted panel ID
                        var panelId = panel.GetFormattedPanelId();
                        LogVerbose(nodeName, $"TryRegisterChangeEventImmediate: Panel '{panel.name}' has ID '{panelId}' and bridge is ready");

                        // Skip if panel ID is still default (panel_0_0)
                        if (panelId == "panel_0_0")
                        {
                            LogVerbose(nodeName, $"TryRegisterChangeEventImmediate: Panel '{panel.name}' still has default ID panel_0_0, skipping");
                            continue;
                        }

                        // Try to register the event directly - HandleMessage will validate internally
                        var message = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.REGISTER_UI_EVENT}{MessageDelimiters.PRIMARY}{elementId}{MessageDelimiters.SECONDARY}change";
                        LogVerbose(nodeName, $"TryRegisterChangeEventImmediate: Attempting HandleMessage with: '{message}'");

                        UIElementBridge.HandleMessage(message);

                        // If we got here without exception, assume success
                        LogVerbose(nodeName, $"TryRegisterChangeEventImmediate: HandleMessage completed successfully for panel '{panel.name}'");
                        return true;
                    }
                    catch (System.Exception ex)
                    {
                        LogVerbose(nodeName, $"TryRegisterChangeEventImmediate: Failed for panel '{panel.name}': {ex.Message}");
                        // Continue to next panel
                    }
                }

                LogVerbose(nodeName, "TryRegisterChangeEventImmediate: No panels succeeded, returning false");
                return false;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[{nodeName}] TryRegisterChangeEventImmediate: Exception occurred: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// Attempts to auto-register any UI event type for the specified element with delayed retry
        /// </summary>
        /// <param name="elementId">The element ID to register the event for</param>
        /// <param name="eventType">The event type to register</param>
        /// <param name="nodeName">The name of the node requesting registration (for logging)</param>
        public static void TryRegisterEventWithRetry(string elementId, UIEventType eventType, string nodeName)
        {
            CoroutineRunner.Instance.StartCoroutine(RegisterEventCoroutine(elementId, eventType, nodeName));
        }

        /// <summary>
        /// Coroutine that retries event registration until successful or max attempts reached
        /// </summary>
        private static IEnumerator RegisterEventCoroutine(string elementId, UIEventType eventType, string nodeName)
        {
            // Wait for up to 5 seconds, checking every 0.1 seconds (same as change events)
            const float maxWaitTime = 5f;
            const float checkInterval = 0.1f;
            float elapsedTime = 0f;
            int attemptNumber = 0;

            while (elapsedTime < maxWaitTime)
            {
                attemptNumber++;

                if (TryRegisterEventImmediate(elementId, eventType, nodeName))
                {
                    LogVerbose(nodeName, $"Auto-registration of '{eventType.ToEventName()}' succeeded on attempt {attemptNumber} after {elapsedTime:F1}s for element '{elementId}'");
                    yield break;
                }

                yield return new WaitForSeconds(checkInterval);
                elapsedTime += checkInterval;
            }

            var eventNameFinal = eventType.ToEventName();
            Debug.LogWarning($"[{nodeName}] Auto-registration of '{eventNameFinal}' failed after {attemptNumber} attempts ({maxWaitTime}s) for element '{elementId}'. Element may not exist or panel not initialized.");
        }

        /// <summary>
        /// Attempts immediate event registration without retry
        /// </summary>
        private static bool TryRegisterEventImmediate(string elementId, UIEventType eventType, string nodeName)
        {
            if (string.IsNullOrEmpty(elementId))
            {
                return false;
            }

            try
            {
                // Try to find ANY panel that's initialized
                var allPanels = UnityEngine.Object.FindObjectsOfType<BanterUIPanel>();

                foreach (var panel in allPanels)
                {
                    try
                    {
                        // Check if panel has UIElementBridge component (indicates initialization is complete)
                        var bridge = panel.GetComponent<UIElementBridge>();
                        if (bridge == null)
                        {
                            continue;
                        }

                        // Try to get the formatted panel ID
                        var panelId = panel.GetFormattedPanelId();

                        // Skip if panel ID is still default (panel_0_0)
                        if (panelId == "panel_0_0")
                        {
                            continue;
                        }

                        // Convert event type to string name
                        var eventName = eventType.ToEventName();

                        // Try to register the event directly - HandleMessage will validate internally
                        var message = $"{panelId}{MessageDelimiters.PRIMARY}{UICommands.REGISTER_UI_EVENT}{MessageDelimiters.PRIMARY}{elementId}{MessageDelimiters.SECONDARY}{eventName}";
                        UIElementBridge.HandleMessage(message);

                        // If we got here without exception, assume success
                        return true;
                    }
                    catch (System.Exception)
                    {
                        // Continue to next panel
                    }
                }

                return false;
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        private static void LogVerbose(string nodeName, string message)
        {
#if BANTER_UI_DEBUG
            Debug.Log($"{LogPrefix} [{nodeName}] {message}");
#endif
        }
    }
}
#endif

