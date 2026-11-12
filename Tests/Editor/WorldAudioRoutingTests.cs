using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
using Object = UnityEngine.Object;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// A world's AudioSource ends up under the app's mixer, keeping a creator's mixer where the sliders allow it. This uses
    /// the client's mixer (ignored in a project without it) and a temporary asset for the creator's, which only the
    /// editor can make: whether a mixer loaded from a bundle takes the same output needs a built world.
    /// </summary>
    public class WorldAudioRoutingTests
    {
        const string AppMixer = "Assets/Audio/MainAudioMixer.mixer";
        const string CreatorMixer = "Assets/WorldAudioRoutingTests.mixer";

        GameObject root;
        AudioSource source;
        AudioMixerGroup gameplay;
        AudioMixerGroup music;

        [SetUp]
        public void SetUp()
        {
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(AppMixer);
            if (mixer == null) Assert.Ignore("This project has no " + AppMixer + ".");
            gameplay = mixer.FindMatchingGroups("Gameplay")[0];
            music = mixer.FindMatchingGroups("Music")[0];
            root = new GameObject("sound");
            source = root.AddComponent<AudioSource>();
        }

        [TearDown]
        public void TearDown()
        {
            if (root != null) Object.DestroyImmediate(root);
            AssetDatabase.DeleteAsset(CreatorMixer);
        }

        static AudioMixer MakeCreatorsMixer()
        {
            var create = typeof(Editor).Assembly.GetType("UnityEditor.Audio.AudioMixerController")
                ?.GetMethod("CreateMixerControllerAtPath", BindingFlags.Public | BindingFlags.Static);
            if (create == null) Assert.Ignore("This editor can't make a mixer from code.");
            return (AudioMixer)create.Invoke(null, new object[] { CreatorMixer });
        }

        AudioSource Source(string name, AudioMixerGroup group)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform);
            var added = go.AddComponent<AudioSource>();
            added.outputAudioMixerGroup = group;
            return added;
        }

        [Test]
        public void ASourceWithNoGroup_PlaysThroughTheAppsGroup()
        {
            BSAssetBundle.RouteToApp(source, gameplay);
            Assert.AreSame(gameplay, source.outputAudioMixerGroup);
        }

        [Test]
        public void WithNoAppGroup_TheSourceKeepsItsOwn()
        {
            source.outputAudioMixerGroup = music;
            BSAssetBundle.RouteToApp(source, null);
            Assert.AreSame(music, source.outputAudioMixerGroup);
        }

        [Test]
        public void ACreatorsMixer_GoesUnderTheFirstGroupAsked()
        {
            var mixer = MakeCreatorsMixer();
            var own = mixer.FindMatchingGroups("Master")[0];
            source.outputAudioMixerGroup = own;

            BSAssetBundle.RouteToApp(source, music);

            Assert.AreSame(own, source.outputAudioMixerGroup);
            Assert.AreSame(music, mixer.outputAudioMixerGroup);
        }

        [Test]
        public void ASourceForTheSameSlider_StaysOnTheCreatorsMixer()
        {
            var mixer = MakeCreatorsMixer();
            var own = mixer.FindMatchingGroups("Master")[0];
            BSAssetBundle.RouteToApp(Source("first", own), music);
            var other = Source("second", own);

            BSAssetBundle.RouteToApp(other, music);

            Assert.AreSame(own, other.outputAudioMixerGroup);
        }

        [Test]
        public void ASourceForAnotherSlider_SkipsTheCreatorsMixer()
        {
            var mixer = MakeCreatorsMixer();
            var own = mixer.FindMatchingGroups("Master")[0];
            BSAssetBundle.RouteToApp(Source("music", own), music);
            var effects = Source("effects", own);

            BSAssetBundle.RouteToApp(effects, gameplay);

            Assert.AreSame(gameplay, effects.outputAudioMixerGroup);
            Assert.AreSame(music, mixer.outputAudioMixerGroup);
        }

        [Test]
        public void ASourceAlreadyUnderTheAppsMixer_StaysWhereItIs()
        {
            source.outputAudioMixerGroup = music;
            BSAssetBundle.RouteToApp(source, gameplay);
            Assert.AreSame(music, source.outputAudioMixerGroup);
        }
    }
}
