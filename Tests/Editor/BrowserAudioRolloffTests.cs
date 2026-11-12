using System.Collections.Generic;
using NUnit.Framework;
using SideQuest.Ora;
using UnityEngine;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// BSBrowser's distance fade: what each rolloff setting does to a speaker, and which AudioSources count as the
    /// view's speakers (found again whenever Ora rebuilds them).
    /// </summary>
    public class BrowserAudioRolloffTests
    {
        readonly List<GameObject> created = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in created)
                if (go != null) Object.DestroyImmediate(go);
            created.Clear();
        }

        GameObject New(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            created.Add(go);
            return go;
        }

        // What OraAudioEmitter.CreateSpeaker builds: a child with an AudioSource and a filter pointing back at it.
        AudioSource AddSpeaker(OraAudioEmitter emitter, OraAudioEmitter filterEmitter = null)
        {
            var go = New("OraSpeaker", emitter.transform);
            var source = go.AddComponent<AudioSource>();
            go.AddComponent<OraSpeakerFilter>().emitter = filterEmitter ?? emitter;
            return source;
        }

        static float Fade(AudioSource s, float t) => s.GetCustomCurve(AudioSourceCurveType.CustomRolloff).Evaluate(t);

        [Test]
        public void Flat_IsHowOraSetsUpItsOwnSpeakers()
        {
            var s = New("s").AddComponent<AudioSource>();
            BSBrowser.ApplyRolloff(s, AudioRolloff.Flat, 1f, 500f);
            Assert.AreEqual(AudioRolloffMode.Custom, s.rolloffMode);
            Assert.AreEqual(1f, Fade(s, 0f));
            Assert.AreEqual(1f, Fade(s, 0.5f));
            Assert.AreEqual(1f, Fade(s, 1f));
            Assert.AreEqual(1f, s.minDistance);
            Assert.AreEqual(500f, s.maxDistance);
        }

        [Test]
        public void LogarithmicAndLinear_UseUnitysOwnFades()
        {
            var s = New("s").AddComponent<AudioSource>();
            BSBrowser.ApplyRolloff(s, AudioRolloff.Logarithmic, 2f, 30f);
            Assert.AreEqual(AudioRolloffMode.Logarithmic, s.rolloffMode);
            Assert.AreEqual(2f, s.minDistance);
            Assert.AreEqual(30f, s.maxDistance);
            BSBrowser.ApplyRolloff(s, AudioRolloff.Linear, 3f, 40f);
            Assert.AreEqual(AudioRolloffMode.Linear, s.rolloffMode);
            Assert.AreEqual(3f, s.minDistance);
            Assert.AreEqual(40f, s.maxDistance);
        }

        [Test]
        public void BackToFlat_PutsTheFlatCurveBack()
        {
            var s = New("s").AddComponent<AudioSource>();
            s.rolloffMode = AudioRolloffMode.Custom;
            s.SetCustomCurve(AudioSourceCurveType.CustomRolloff, AnimationCurve.Linear(0f, 1f, 1f, 0f));
            BSBrowser.ApplyRolloff(s, AudioRolloff.Linear, 1f, 10f);
            BSBrowser.ApplyRolloff(s, AudioRolloff.Flat, 1f, 10f);
            Assert.AreEqual(AudioRolloffMode.Custom, s.rolloffMode);
            Assert.AreEqual(1f, Fade(s, 0.5f));
            Assert.AreEqual(1f, Fade(s, 1f));
        }

        [Test]
        public void DistancesPastTheOldOnes_AreBothSet()
        {
            var s = New("s").AddComponent<AudioSource>();
            BSBrowser.ApplyRolloff(s, AudioRolloff.Logarithmic, 1f, 5f);
            BSBrowser.ApplyRolloff(s, AudioRolloff.Logarithmic, 50f, 80f);
            Assert.AreEqual(50f, s.minDistance, "min above the old max");
            Assert.AreEqual(80f, s.maxDistance);
            BSBrowser.ApplyRolloff(s, AudioRolloff.Logarithmic, 0.5f, 2f);
            Assert.AreEqual(0.5f, s.minDistance, "max below the old min");
            Assert.AreEqual(2f, s.maxDistance);
        }

        [TestCase(float.NaN, float.NaN)]
        [TestCase(-5f, -1f)]
        [TestCase(float.PositiveInfinity, float.NegativeInfinity)]
        [TestCase(20f, 10f)]
        public void BadDistances_StayUsable(float min, float max)
        {
            var s = New("s").AddComponent<AudioSource>();
            BSBrowser.ApplyRolloff(s, AudioRolloff.Linear, min, max);
            Assert.IsTrue(s.minDistance > 0f && !float.IsInfinity(s.minDistance) && !float.IsNaN(s.minDistance), $"min {s.minDistance}");
            Assert.IsTrue(s.maxDistance > s.minDistance && !float.IsInfinity(s.maxDistance), $"max {s.maxDistance}");
        }

        [Test]
        public void AnUnknownMode_PlaysFlat()
        {
            var s = New("s").AddComponent<AudioSource>();
            BSBrowser.ApplyRolloff(s, (AudioRolloff)7, 1f, 500f);
            Assert.AreEqual(AudioRolloffMode.Custom, s.rolloffMode);
            Assert.AreEqual(1f, Fade(s, 1f));
        }

        [Test]
        public void Tracker_FindsOnlyThisEmittersSpeakers()
        {
            var emitter = New("view").AddComponent<OraAudioEmitter>();
            var other = New("other").AddComponent<OraAudioEmitter>();
            var mine = AddSpeaker(emitter);
            AddSpeaker(emitter, other);                                    // another emitter's filter
            New("plain", emitter.transform).AddComponent<AudioSource>();   // an AudioSource that isn't a speaker

            var tracker = new BSBrowser.SpeakerTracker();
            Assert.IsTrue(tracker.Refresh(emitter));
            CollectionAssert.AreEqual(new[] { mine }, tracker.Speakers);
        }

        [Test]
        public void Tracker_LeavesACustomEmittersOwnSourcesAlone()
        {
            // Greenfield's streaming browser: a Custom emitter playing through its own AudioSource, with its own fade.
            var emitter = New("streaming").AddComponent<OraAudioEmitter>();
            emitter.speakerMode = OraSpeakerMode.Custom;
            emitter.gameObject.AddComponent<AudioSource>();
            emitter.gameObject.AddComponent<OraSpeakerFilter>().emitter = emitter;
            AddSpeaker(emitter);

            var tracker = new BSBrowser.SpeakerTracker();
            Assert.IsFalse(tracker.Refresh(emitter));
            Assert.IsEmpty(tracker.Speakers);

            emitter.speakerMode = OraSpeakerMode.Managed;
            Assert.IsTrue(tracker.Refresh(emitter), "back to Ora's own speakers");
        }

        [Test]
        public void Tracker_ReportsSpeakersOnlyWhenTheyMayHaveChanged()
        {
            var emitter = New("view").AddComponent<OraAudioEmitter>();
            var tracker = new BSBrowser.SpeakerTracker();
            Assert.IsFalse(tracker.Refresh(null), "no view audio yet");
            Assert.IsFalse(tracker.Refresh(emitter), "an emitter without speakers");

            var first = AddSpeaker(emitter);
            Assert.IsTrue(tracker.Refresh(emitter), "speaker built");
            Assert.IsFalse(tracker.Refresh(emitter), "nothing changed");

            // Ora rebuilds its speakers (an output-rate change): the old one goes, a new one comes.
            Object.DestroyImmediate(first.gameObject);
            var second = AddSpeaker(emitter);
            Assert.IsTrue(tracker.Refresh(emitter), "rebuilt");
            CollectionAssert.AreEqual(new[] { second }, tracker.Speakers);

            var wide = AddSpeaker(emitter);
            Assert.IsTrue(tracker.Refresh(emitter), "wide stereo pair");
            CollectionAssert.AreEquivalent(new[] { second, wide }, tracker.Speakers);

            Assert.IsFalse(tracker.Refresh(null), "view audio gone");
            Assert.IsEmpty(tracker.Speakers);
        }
    }
}
