using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// What a Unity editor can show of BSAudioStream without its audio thread: which sources exist, where they sit and how
    /// they are set up. Timing against the audio engine is BSAudioStreamPlayModeTests.
    /// </summary>
    public class BSAudioStreamTests
    {
        GameObject root;
        BSAudioStream stream;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("stream");
            root.SetActive(false);
            stream = root.AddComponent<BSAudioStream>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(root);

        SerializedObject Serialized() => new SerializedObject(stream);

        void SetRow(BSAudioStreamRole role, bool create, AudioSource source)
        {
            var so = Serialized();
            var row = so.FindProperty("channels").GetArrayElementAtIndex((int)role);
            row.FindPropertyRelative("create").boolValue = create;
            row.FindPropertyRelative("source").objectReferenceValue = source;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        [Test]
        public void EveryRoleHasARowInRoleOrder()
        {
            var channels = Serialized().FindProperty("channels");
            Assert.AreEqual(8, channels.arraySize);
            for (var i = 0; i < 8; i++)
            {
                Assert.AreEqual(i, channels.GetArrayElementAtIndex(i).FindPropertyRelative("role").enumValueIndex);
                Assert.IsTrue(channels.GetArrayElementAtIndex(i).FindPropertyRelative("create").boolValue);
            }
        }

        [TestCase(BSAudioStreamLayout.Mono, 1)]
        [TestCase(BSAudioStreamLayout.Stereo, 2)]
        [TestCase(BSAudioStreamLayout.Quad, 4)]
        [TestCase(BSAudioStreamLayout.Surround51, 6)]
        public void ALayoutOffersItsChannelCount(BSAudioStreamLayout layout, int count)
        {
            Assert.AreEqual(count, BSAudioStream.RolesOf(layout).Length);
        }

        [Test]
        public void StereoIsFrontLeftAndRight()
        {
            CollectionAssert.AreEqual(new[] { BSAudioStreamRole.FrontLeft, BSAudioStreamRole.FrontRight }, BSAudioStream.RolesOf(BSAudioStreamLayout.Stereo));
        }

        [Test]
        public void ARowWithNoSourceGetsAChildWithItsOwnAudioSource()
        {
            var source = stream.SourceFor(BSAudioStreamRole.FrontLeft, false);
            Assert.IsNotNull(source);
            Assert.AreEqual(root.transform, source.transform.parent);
            Assert.IsFalse(source.playOnAwake);
            Assert.AreEqual(1f, source.spatialBlend);
        }

        [Test]
        public void TheSameRoleKeepsItsCreatedSource()
        {
            Assert.AreSame(stream.SourceFor(BSAudioStreamRole.Center, false), stream.SourceFor(BSAudioStreamRole.Center, false));
        }

        [Test]
        public void AnAssignedSourceIsUsedAsItIs()
        {
            var own = new GameObject("mine").AddComponent<AudioSource>();
            own.spatialBlend = 0.25f;
            own.minDistance = 7f;
            SetRow(BSAudioStreamRole.FrontRight, true, own);
            Assert.AreSame(own, stream.SourceFor(BSAudioStreamRole.FrontRight, false));
            Assert.AreEqual(0.25f, own.spatialBlend);
            Assert.AreEqual(7f, own.minDistance);
            Assert.AreEqual(0, root.transform.childCount, "nothing created");
            Object.DestroyImmediate(own.gameObject);
        }

        [Test]
        public void AnUntickedChannelGetsNoSourceEvenWithOneAssigned()
        {
            var own = new GameObject("mine").AddComponent<AudioSource>();
            SetRow(BSAudioStreamRole.SurroundLeft, false, own);
            Assert.IsNull(stream.SourceFor(BSAudioStreamRole.SurroundLeft, false));
            SetRow(BSAudioStreamRole.SurroundLeft, false, null);
            Assert.IsNull(stream.SourceFor(BSAudioStreamRole.SurroundLeft, false));
            Assert.AreEqual(0, root.transform.childCount);
            Object.DestroyImmediate(own.gameObject);
        }

        [Test]
        public void TheLfeIsNotSpatial()
        {
            Assert.AreEqual(0f, stream.SourceFor(BSAudioStreamRole.Lfe, false).spatialBlend);
        }

        [Test]
        public void CreatedSourcesKeepTheStreamsRateWhateverTheTemplateSays()
        {
            var template = new GameObject("template").AddComponent<AudioSource>();
            template.pitch = 2f;
            template.dopplerLevel = 1f;
            template.volume = 0.3f;
            template.maxDistance = 42f;
            var so = Serialized();
            so.FindProperty("sourceTemplate").objectReferenceValue = template;
            so.ApplyModifiedPropertiesWithoutUndo();

            var source = stream.SourceFor(BSAudioStreamRole.Center, false);
            Assert.AreEqual(1f, source.pitch);
            Assert.AreEqual(0f, source.dopplerLevel);
            Assert.AreEqual(0.3f, source.volume, 1e-6f);
            Assert.AreEqual(42f, source.maxDistance);
            Object.DestroyImmediate(template.gameObject);
        }

        [TestCase(BSAudioStreamRole.FrontLeft, false, -30f)]
        [TestCase(BSAudioStreamRole.FrontRight, false, 30f)]
        [TestCase(BSAudioStreamRole.Center, false, 0f)]
        [TestCase(BSAudioStreamRole.SurroundLeft, false, -110f)]
        [TestCase(BSAudioStreamRole.SurroundRight, false, 110f)]
        [TestCase(BSAudioStreamRole.SurroundLeft, true, -90f)]
        [TestCase(BSAudioStreamRole.BackRight, true, 150f)]
        public void SpeakersSitAtTheStandardAngles(BSAudioStreamRole role, bool hasBack, float degrees)
        {
            Assert.AreEqual(degrees, BSAudioStream.Azimuth(role, hasBack));
            var so = Serialized();
            so.FindProperty("speakerRadius").floatValue = 3f;
            so.ApplyModifiedPropertiesWithoutUndo();
            var position = stream.SourceFor(role, hasBack).transform.localPosition;
            Assert.AreEqual(3f, position.magnitude, 1e-4f);
            Assert.AreEqual(degrees, Mathf.Atan2(position.x, position.z) * Mathf.Rad2Deg, 1e-3f);
        }

        [Test]
        public void ItIsAPlainComponentNotABSComponent()
        {
            Assert.IsFalse(typeof(BSComponent).IsAssignableFrom(typeof(BSAudioStream)));
            Assert.AreEqual(typeof(MonoBehaviour), typeof(BSAudioStream).BaseType);
        }

        [Test]
        public void ItsSettingsAreWrittenThroughPropertiesNotFields()
        {
            var writable = typeof(BSAudioStream).GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => !typeof(UnityEngine.Events.UnityEventBase).IsAssignableFrom(f.FieldType)).Select(f => f.Name).ToArray();
            CollectionAssert.IsEmpty(writable);
        }

        [Test]
        public void SettersKeepValuesItCanUse()
        {
            stream.PrebufferSeconds = 0f;
            Assert.AreEqual(0.1f, stream.PrebufferSeconds);
            stream.PrebufferSeconds = 99f;
            Assert.AreEqual(10f, stream.PrebufferSeconds);
            stream.SpeakerRadius = -3f;
            Assert.AreEqual(0f, stream.SpeakerRadius);
            stream.Url = null;
            Assert.AreEqual("", stream.Url);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => stream.MaxChannels = (BSAudioStreamLayout)3);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => stream.SetChannelEnabled((BSAudioStreamRole)8, true));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => stream.MaxChannels = (BSAudioStreamLayout)8, "7.1 is not offered");
            stream.MaxChannels = BSAudioStreamLayout.Surround51;
            Assert.AreEqual(BSAudioStreamLayout.Surround51, stream.MaxChannels);
        }

        [Test]
        public void ChannelSettersWriteTheInspectorRows()
        {
            var own = new GameObject("own speaker").AddComponent<AudioSource>();
            own.transform.SetParent(root.transform);
            stream.SetChannelEnabled(BSAudioStreamRole.Center, false);
            stream.SetChannelSource(BSAudioStreamRole.FrontLeft, own);
            var rows = Serialized().FindProperty("channels");
            Assert.IsFalse(rows.GetArrayElementAtIndex((int)BSAudioStreamRole.Center).FindPropertyRelative("create").boolValue);
            Assert.AreSame(own, rows.GetArrayElementAtIndex((int)BSAudioStreamRole.FrontLeft).FindPropertyRelative("source").objectReferenceValue);
            Assert.IsFalse(stream.GetChannelEnabled(BSAudioStreamRole.Center));
            Assert.AreSame(own, stream.GetChannelSource(BSAudioStreamRole.FrontLeft));
            Assert.IsNull(stream.SourceFor(BSAudioStreamRole.Center, false));
            Assert.AreSame(own, stream.SourceFor(BSAudioStreamRole.FrontLeft, false));
        }

        [Test]
        public void TheTemplateDoesNotPassOnSettingsThatStepOutsideThePlayersOwn()
        {
            var template = new GameObject("template").AddComponent<AudioSource>();
            template.transform.SetParent(root.transform);
            template.volume = 0.5f;
            template.ignoreListenerVolume = true;
            template.ignoreListenerPause = true;
            template.bypassListenerEffects = true;
            stream.SourceTemplate = template;
            var created = stream.SourceFor(BSAudioStreamRole.FrontLeft, false);
            Assert.AreEqual(0.5f, created.volume, "the rest of the template is copied");
            Assert.IsFalse(created.ignoreListenerVolume);
            Assert.IsFalse(created.ignoreListenerPause);
            Assert.IsFalse(created.bypassListenerEffects);
        }

        [Test]
        public void APlaySoonAfterAnotherWaitsAndStopCancelsIt()
        {
            var restart = BSAudioStream.MinRestartSeconds;
            BSAudioStream.MinRestartSeconds = 60;
            try
            {
                // No address: the first session ends on its own thread at once, without a request.
                stream.Play();
                Assert.IsFalse(stream.StartHeld, "the first Play starts at once");
                stream.Play();
                Assert.IsTrue(stream.StartHeld, "a second within the restart time waits for it");
                Assert.AreEqual(BSAudioStreamState.Connecting, stream.State);
                var calls = 0;
                stream.stopped.AddListener(() => calls++);
                stream.Stop();
                Assert.IsFalse(stream.StartHeld);
                Assert.AreEqual(1, calls, "a waiting Play counts as playing");
                Assert.AreEqual(BSAudioStreamState.Stopped, stream.State);
            }
            finally
            {
                BSAudioStream.MinRestartSeconds = restart;
            }
        }

        [Test]
        public void AChannelSwitchedOffWhilePlayingKeepsItsSourceUntilTheNextPlay()
        {
            // No address: the session ends on its own thread, but stays the running one until Update or Stop.
            stream.Play();
            stream.SetChannelEnabled(BSAudioStreamRole.FrontRight, false);
            Assert.IsNotNull(stream.SourceFor(BSAudioStreamRole.FrontRight, false), "the running session chose its channels at Play");
            stream.Stop();
            Assert.IsNull(stream.SourceFor(BSAudioStreamRole.FrontRight, false), "the next one goes by the row");
        }

        [Test]
        public void CreatedSourcesAreSetUpAgainAtEachPlay()
        {
            var first = stream.SourceFor(BSAudioStreamRole.FrontLeft, false);
            Assert.AreEqual(2f, first.minDistance, "the default radius");
            stream.SpeakerRadius = 4f;
            Assert.AreEqual(4f, stream.SourceFor(BSAudioStreamRole.FrontLeft, false).minDistance);
            var template = new GameObject("template").AddComponent<AudioSource>();
            template.transform.SetParent(root.transform);
            template.volume = 0.3f;
            stream.SourceTemplate = template;
            var again = stream.SourceFor(BSAudioStreamRole.FrontLeft, false);
            Assert.AreSame(first, again, "the same source");
            Assert.AreEqual(0.3f, again.volume, "with the template that is set now");
        }

        [Test]
        public void WarningsAreRationed()
        {
            var seen = 0;
            void Count(string message, string stack, LogType type)
            {
                if (message.StartsWith("[BSAudioStream]"))
                {
                    seen++;
                }
            }
            Application.logMessageReceived += Count;
            try
            {
                for (var i = 0; i < 50; i++)
                {
                    stream.Warn("[BSAudioStream] warning " + i);
                }
            }
            finally
            {
                Application.logMessageReceived -= Count;
            }
            Assert.AreEqual(9, seen, "a burst of 8, then one line saying the rest are held back");
        }

        [Test]
        public void StoppingWhenNothingIsPlayingDoesNothing()
        {
            var calls = 0;
            stream.stopped.AddListener(() => calls++);
            stream.Stop();
            Assert.AreEqual(0, calls);
            Assert.AreEqual(BSAudioStreamState.Stopped, stream.State);
        }

        [Test]
        public void ANewComponentIsStopped()
        {
            Assert.AreEqual(BSAudioStreamState.Stopped, stream.State);
            Assert.AreEqual("", stream.Codec);
            Assert.AreEqual(0, stream.SampleRate);
        }
    }
}
