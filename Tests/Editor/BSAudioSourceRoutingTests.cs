using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
using Object = UnityEngine.Object;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// A BSAudioSource made by a page plays under the player's volume sliders, as a world bundle's own sources do. Uses the
    /// client's mixer, and is ignored in a project without it (see <see cref="WorldAudioRoutingTests"/>).
    /// </summary>
    public class BSAudioSourceRoutingTests
    {
        const string AppMixer = "Assets/Audio/MainAudioMixer.mixer";

        static readonly FieldInfo SceneInstance = typeof(BSScene).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);

        GameObject root;
        AudioSource source;
        AudioMixerGroup gameplay, music;
        BSSceneSettings settings;
        object previousScene;

        [SetUp]
        public void SetUp()
        {
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(AppMixer);
            if (mixer == null) Assert.Ignore("This project has no " + AppMixer + ".");
            gameplay = mixer.FindMatchingGroups("Gameplay")[0];
            music = mixer.FindMatchingGroups("Music")[0];
            root = new GameObject("sound");
            root.SetActive(false); // no Awake/Start
            source = root.AddComponent<AudioSource>();
            settings = (BSSceneSettings)FormatterServices.GetUninitializedObject(typeof(BSSceneSettings));
            previousScene = SceneInstance.GetValue(null);
        }

        [TearDown]
        public void TearDown()
        {
            SceneInstance.SetValue(null, previousScene);
            if (root != null) Object.DestroyImmediate(root);
        }

        [Test]
        public void ASource_PlaysThroughTheWorldsGroup()
        {
            settings.SpaceAudioGroup = gameplay;
            BSAudioSource.RouteToApp(source, settings);
            Assert.AreSame(gameplay, source.outputAudioMixerGroup);
        }

        [Test]
        public void TheAppsSelector_ChoosesTheGroup()
        {
            settings.SpaceAudioGroup = gameplay;
            settings.AudioGroupSelector = _ => music;
            BSAudioSource.RouteToApp(source, settings);
            Assert.AreSame(music, source.outputAudioMixerGroup);
        }

        [Test]
        public void WithNoGroup_ASourceStaysAsItWas()
        {
            BSAudioSource.RouteToApp(source, settings);
            BSAudioSource.RouteToApp(source, null);
            Assert.IsNull(source.outputAudioMixerGroup);
        }

        [Test]
        public void ARoutedSource_StaysPut_WhenAskedAgain()
        {
            settings.SpaceAudioGroup = music;
            BSAudioSource.RouteToApp(source, settings);
            settings.SpaceAudioGroup = gameplay;
            BSAudioSource.RouteToApp(source, settings);
            Assert.AreSame(music, source.outputAudioMixerGroup);
        }

        [Test]
        public void TheComponent_RoutesTheSourceItMakes()
        {
            var scene = (BSScene)FormatterServices.GetUninitializedObject(typeof(BSScene));
            scene.settings = settings;
            SceneInstance.SetValue(null, scene);
            settings.SpaceAudioGroup = gameplay;
            Object.DestroyImmediate(source);

            var component = root.AddComponent<BSAudioSource>();
            component.UpdateCallback(new List<PropertyName>());

            Assert.AreSame(gameplay, root.GetComponent<AudioSource>().outputAudioMixerGroup);
        }
    }
}
