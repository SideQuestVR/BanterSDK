/***
* This code is adapted from
* https://github.com/gree/unity-webview/blob/master/dist/package/Assets/Plugins/Editor/UnityWebViewPostprocessBuild.cs
**/

#if UNITY_EDITOR
using System.IO;
using System.Text;
using System.Xml;
using UnityEngine;
using UnityEditor.Android;

namespace TLab.WebView.Editor
{
#if UNITY_2018_1_OR_NEWER
    public class UnityWebViewPostprocessBuild : IPostGenerateGradleAndroidProject
    {
        //// for android/unity 2018.1 or newer
        //// cf. https://forum.unity.com/threads/android-hardwareaccelerated-is-forced-false-in-all-activities.532786/
        //// cf. https://github.com/Over17/UnityAndroidManifestCallback

        // This function called from unity when Android Gradle project is generated and before building begins.
        public void OnPostGenerateGradleAndroidProject(string basePath)
        {
            // ----------------------------------------------------------------------------------
            // Rewrite AndroidManifest.xml
            //

            var changed = false;
            var androidManifest = new AndroidManifest(GetManifestPath(basePath));

            changed = androidManifest.AddApplicationElement("supportsRtl", true) || changed;

            changed = androidManifest.AddApplicationElement("allowBackup", true) || changed;

            changed = androidManifest.SetExported(true) || changed;

            changed = androidManifest.SetHardwareAccelerated(true) || changed;

#if UNITYWEBVIEW_ANDROID_USES_CLEARTEXT_TRAFFIC
            changed = androidManifest.SetUsesCleartextTraffic(true) || changed;
#endif

#if UNITYWEBVIEW_ANDROID_ENABLE_CAMERA
            changed = androidManifest.AddCamera() || changed;
            changed = androidManifest.AddGallery() || changed;
#endif

#if UNITYWEBVIEW_ANDROID_ENABLE_MICROPHONE
            changed = androidManifest.AddMicrophone() || changed;
#endif

            if (changed)
            {
                var result = androidManifest.Save();
                Debug.Log("AndroidManifest.xml overwrite complete: " + result);
            }
        }


        public int callbackOrder
        {
            get
            {
                return 1;
            }
        }

        // ----------------------------------------------------------------------------------
        // Get each file
        //

        private string GetManifestPath(string basePath)
        {
            var pathBuilder = new StringBuilder(basePath);
            pathBuilder.Append(Path.DirectorySeparatorChar).Append("src");
            pathBuilder.Append(Path.DirectorySeparatorChar).Append("main");
            pathBuilder.Append(Path.DirectorySeparatorChar).Append("AndroidManifest.xml");
            var pathString = pathBuilder.ToString();
            Debug.Log("android custom manifest path: " + pathString);
            return pathString;
        }
    }

    internal class AndroidXmlDocument : XmlDocument
    {
        private string m_Path;
        protected XmlNamespaceManager nsMgr;
        public readonly string AndroidXmlNamespace = "http://schemas.android.com/apk/res/android";

        public AndroidXmlDocument(string path)
        {
            m_Path = path;
            using (var reader = new XmlTextReader(m_Path))
            {
                reader.Read();
                Load(reader);
            }
            nsMgr = new XmlNamespaceManager(NameTable);
            nsMgr.AddNamespace("android", AndroidXmlNamespace);
        }

        public string Save()
        {
            return SaveAs(m_Path);
        }

        public string SaveAs(string path)
        {
            using (var writer = new XmlTextWriter(path, new UTF8Encoding(false)))
            {
                writer.Formatting = Formatting.Indented;
                Save(writer);
            }
            return path;
        }
    }

    internal class AndroidManifest : AndroidXmlDocument
    {
        private readonly XmlElement ManifestElement;
        private readonly XmlElement ApplicationElement;

        public AndroidManifest(string path) : base(path)
        {
            ManifestElement = SelectSingleNode("/manifest") as XmlElement;
            ApplicationElement = SelectSingleNode("/manifest/application") as XmlElement;
        }

        private XmlAttribute CreateAndroidAttribute(string key, string value)
        {
            XmlAttribute attr = CreateAttribute("android", key, AndroidXmlNamespace);
            attr.Value = value;
            return attr;
        }

        internal XmlNode GetActivityWithLaunchIntent()
        {
            return
                SelectSingleNode(
                    "/manifest/application/activity[intent-filter/action/@android:name='android.intent.action.MAIN' and "
                    + "intent-filter/category/@android:name='android.intent.category.LAUNCHER']",
                    nsMgr);
        }

        internal bool AddApplicationElement(string attribute, bool enabled)
        {
            var changed = false;
            var value = enabled ? "true" : "false";

            var attributeResult = ApplicationElement.GetAttribute(attribute, AndroidXmlNamespace);

            if (attributeResult == null || attributeResult == "" || attributeResult != value)
            {
                ApplicationElement.SetAttribute(attribute, AndroidXmlNamespace, value);
                changed = true;
            }

            Debug.Log($"Succeed in {nameof(ApplicationElement.SetAttribute)}: " + attribute);
            return changed;
        }

        internal bool SetUsesCleartextTraffic(bool enabled)
        {
            var changed = false;
            var value = enabled ? "true" : "false";

#if UNITY_2022_1_OR_NEWER
            changed = changed || AddApplicationElement("usesCleartextTraffic", true);
#elif UNITY_2018_1_OR_NEWER
            var attributeResult = ApplicationElement.GetAttribute("usesCleartextTraffic", AndroidXmlNamespace);
            if (attributeResult != value)
            {
                ApplicationElement.SetAttribute("usesCleartextTraffic", AndroidXmlNamespace, value);
                changed = true;
            }
#endif
            return changed;
        }

        internal bool SetAttributeInLaunchIntent(bool enabled, string key)
        {
            bool changed = false;
            var value = enabled ? "true" : "false";

            var activity = GetActivityWithLaunchIntent() as XmlElement;
            if (activity != null)
            {
                var attributeResult = activity.HasAttribute(key) ? activity.GetAttribute(key, AndroidXmlNamespace) : null;
                if (attributeResult != value)
                {
                    activity.SetAttribute(key, AndroidXmlNamespace, value);
                    changed = true;
                }
            }
            else
            {
                Debug.LogWarning("Activity not found");
            }
            return changed;
        }

        // For API level 33
        internal bool SetExported(bool enabled) => 
            SetAttributeInLaunchIntent(enabled, "exported");

        internal bool SetHardwareAccelerated(bool enabled) =>
            SetAttributeInLaunchIntent(enabled, "hardwareAccelerated");

        internal bool AddCamera()
        {
            bool changed = false;
            if (SelectNodes("/manifest/uses-permission[@android:name='android.permission.CAMERA']", nsMgr).Count == 0)
            {
                var elem = CreateElement("uses-permission");
                elem.Attributes.Append(CreateAndroidAttribute("name", "android.permission.CAMERA"));
                ManifestElement.AppendChild(elem);
                changed = true;
            }
            if (SelectNodes("/manifest/uses-feature[@android:name='android.hardware.camera']", nsMgr).Count == 0)
            {
                var elem = CreateElement("uses-feature");
                elem.Attributes.Append(CreateAndroidAttribute("name", "android.hardware.camera"));
                ManifestElement.AppendChild(elem);
                changed = true;
            }
            // cf. https://developer.android.com/training/data-storage/shared/media#media-location-permission
            if (SelectNodes("/manifest/uses-permission[@android:name='android.permission.ACCESS_MEDIA_LOCATION']", nsMgr).Count == 0)
            {
                var elem = CreateElement("uses-permission");
                elem.Attributes.Append(CreateAndroidAttribute("name", "android.permission.ACCESS_MEDIA_LOCATION"));
                ManifestElement.AppendChild(elem);
                changed = true;
            }
            // cf. https://developer.android.com/training/package-visibility/declaring
            if (SelectNodes("/manifest/queries", nsMgr).Count == 0)
            {
                var elem = CreateElement("queries");
                ManifestElement.AppendChild(elem);
                changed = true;
            }
            if (SelectNodes("/manifest/queries/intent/action[@android:name='android.media.action.IMAGE_CAPTURE']", nsMgr).Count == 0)
            {
                var action = CreateElement("action");
                action.Attributes.Append(CreateAndroidAttribute("name", "android.media.action.IMAGE_CAPTURE"));
                var intent = CreateElement("intent");
                intent.AppendChild(action);
                var queries = SelectSingleNode("/manifest/queries") as XmlElement;
                queries.AppendChild(intent);
                changed = true;
            }
            return changed;
        }

        internal bool AddGallery()
        {
            bool changed = false;
            // for api level 33
            if (SelectNodes("/manifest/uses-permission[@android:name='android.permission.READ_MEDIA_IMAGES']", nsMgr).Count == 0)
            {
                var elem = CreateElement("uses-permission");
                elem.Attributes.Append(CreateAndroidAttribute("name", "android.permission.READ_MEDIA_IMAGES"));
                ManifestElement.AppendChild(elem);
                changed = true;
            }
            if (SelectNodes("/manifest/uses-permission[@android:name='android.permission.READ_MEDIA_VIDEO']", nsMgr).Count == 0)
            {
                var elem = CreateElement("uses-permission");
                elem.Attributes.Append(CreateAndroidAttribute("name", "android.permission.READ_MEDIA_VIDEO"));
                ManifestElement.AppendChild(elem);
                changed = true;
            }
            if (SelectNodes("/manifest/uses-permission[@android:name='android.permission.READ_MEDIA_AUDIO']", nsMgr).Count == 0)
            {
                var elem = CreateElement("uses-permission");
                elem.Attributes.Append(CreateAndroidAttribute("name", "android.permission.READ_MEDIA_AUDIO"));
                ManifestElement.AppendChild(elem);
                changed = true;
            }
            // cf. https://developer.android.com/training/package-visibility/declaring
            if (SelectNodes("/manifest/queries", nsMgr).Count == 0)
            {
                var elem = CreateElement("queries");
                ManifestElement.AppendChild(elem);
                changed = true;
            }
            if (SelectNodes("/manifest/queries/intent/action[@android:name='android.media.action.GET_CONTENT']", nsMgr).Count == 0)
            {
                var action = CreateElement("action");
                action.Attributes.Append(CreateAndroidAttribute("name", "android.media.action.GET_CONTENT"));
                var intent = CreateElement("intent");
                intent.AppendChild(action);
                var queries = SelectSingleNode("/manifest/queries") as XmlElement;
                queries.AppendChild(intent);
                changed = true;
            }
            return changed;
        }

        internal bool AddMicrophone()
        {
            bool changed = false;
            if (SelectNodes("/manifest/uses-permission[@android:name='android.permission.MICROPHONE']", nsMgr).Count == 0)
            {
                var elem = CreateElement("uses-permission");
                elem.Attributes.Append(CreateAndroidAttribute("name", "android.permission.MICROPHONE"));
                ManifestElement.AppendChild(elem);
                changed = true;
            }
            if (SelectNodes("/manifest/uses-feature[@android:name='android.hardware.microphone']", nsMgr).Count == 0)
            {
                var elem = CreateElement("uses-feature");
                elem.Attributes.Append(CreateAndroidAttribute("name", "android.hardware.microphone"));
                ManifestElement.AppendChild(elem);
                changed = true;
            }
            // cf. https://github.com/gree/unity-webview/issues/679
            // cf. https://github.com/fluttercommunity/flutter_webview_plugin/issues/138#issuecomment-559307558
            // cf. https://stackoverflow.com/questions/38917751/webview-webrtc-not-working/68024032#68024032
            // cf. https://stackoverflow.com/questions/40236925/allowing-microphone-accesspermission-in-webview-android-studio-java/47410311#47410311
            if (SelectNodes("/manifest/uses-permission[@android:name='android.permission.MODIFY_AUDIO_SETTINGS']", nsMgr).Count == 0)
            {
                var elem = CreateElement("uses-permission");
                elem.Attributes.Append(CreateAndroidAttribute("name", "android.permission.MODIFY_AUDIO_SETTINGS"));
                ManifestElement.AppendChild(elem);
                changed = true;
            }
            if (SelectNodes("/manifest/uses-permission[@android:name='android.permission.RECORD_AUDIO']", nsMgr).Count == 0)
            {
                var elem = CreateElement("uses-permission");
                elem.Attributes.Append(CreateAndroidAttribute("name", "android.permission.RECORD_AUDIO"));
                ManifestElement.AppendChild(elem);
                changed = true;
            }
            return changed;
        }
    }
#endif
}
#endif