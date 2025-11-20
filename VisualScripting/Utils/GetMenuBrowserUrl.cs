#if BANTER_VISUAL_SCRIPTING
using System;
using System.Reflection;
using UnityEngine;
using Unity.VisualScripting;
using Banter.SDK;

namespace Banter.VisualScripting
{
    /// <summary>
    /// Visual Scripting node that gets the current URL of the menu browser.
    /// The menu browser is the main UI browser used for navigation, settings, and space browsing.
    /// Use this to detect when users navigate to specific pages like SideQuest listings.
    /// Uses reflection to avoid assembly dependencies.
    /// </summary>
    [UnitTitle("Get Menu Browser URL")]
    [UnitShortTitle("Menu Browser URL")]
    [UnitCategory("Banter\\Browser")]
    [TypeIcon(typeof(BanterObjectId))]
    public class GetMenuBrowserUrl : Unit
    {
        [DoNotSerialize]
        public ValueOutput url;

        protected override void Definition()
        {
            url = ValueOutput<string>("URL", flow => {
                try
                {
                    // Use reflection to find MenuBrowserMessager without compile-time reference
                    Type menuBrowserMessagerType = Type.GetType("MenuBrowserMessager, Assembly-CSharp");
                    if (menuBrowserMessagerType == null)
                    {
                        return string.Empty;
                    }

                    // Find instance of MenuBrowserMessager
                    var menuBrowserMessager = UnityEngine.Object.FindObjectOfType(menuBrowserMessagerType);
                    if (menuBrowserMessager == null)
                    {
                        return string.Empty;
                    }

                    // Get webViewPrefab field
                    FieldInfo webViewPrefabField = menuBrowserMessagerType.GetField("webViewPrefab");
                    if (webViewPrefabField == null)
                    {
                        return string.Empty;
                    }

                    var webViewPrefab = webViewPrefabField.GetValue(menuBrowserMessager);
                    if (webViewPrefab == null)
                    {
                        return string.Empty;
                    }

                    // Get WebView property
                    PropertyInfo webViewProperty = webViewPrefab.GetType().GetProperty("WebView");
                    if (webViewProperty == null)
                    {
                        return string.Empty;
                    }

                    var webView = webViewProperty.GetValue(webViewPrefab);
                    if (webView == null)
                    {
                        return string.Empty;
                    }

                    // Get Url property from WebView
                    PropertyInfo urlProperty = webView.GetType().GetProperty("Url");
                    if (urlProperty == null)
                    {
                        return string.Empty;
                    }

                    return urlProperty.GetValue(webView) as string ?? string.Empty;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[GetMenuBrowserUrl] Failed to get menu browser URL: {e.Message}");
                }
                return string.Empty;
            });
        }
    }
}
#endif
