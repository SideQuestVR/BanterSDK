using NUnit.Framework;
using UnityEngine;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// BSPlatformFilter: the platform half (PC / Android) and the VR half (VR / Non-VR), combined with AND, and which
    /// filters a bundle build has to keep for the device to decide.
    /// </summary>
    public class PlatformFilterTests
    {
        // The four kinds of player, as (isAndroid, isVR).
        static readonly (bool android, bool vr)[] Devices =
        {
            (false, false), // PC, flat screen
            (false, true),  // PC VR
            (true, false),  // Android, flat screen
            (true, true),   // Quest
        };

        static bool Show((bool android, bool vr) device, bool mobile, bool desktop, bool inVR, bool inNonVR) =>
            BSPlatformFilter.ShouldShow(device.android, device.vr, mobile, desktop, inVR, inNonVR);

        [Test]
        public void TheDefaults_ShowEverywhere()
        {
            foreach (var device in Devices)
                Assert.IsTrue(Show(device, true, true, true, true), $"hidden on {device}");
        }

        [Test]
        public void AndroidPlusVrOnly_ShowsOnlyInAQuestHeadset()
        {
            Assert.IsFalse(Show((false, false), true, false, true, false), "PC flat");
            Assert.IsFalse(Show((false, true), true, false, true, false), "PC VR");
            Assert.IsFalse(Show((true, false), true, false, true, false), "Android flat");
            Assert.IsTrue(Show((true, true), true, false, true, false), "Quest");
        }

        [Test]
        public void PcOnly_ShowsOnPcInAndOutOfVr()
        {
            Assert.IsTrue(Show((false, false), false, true, true, true));
            Assert.IsTrue(Show((false, true), false, true, true, true));
            Assert.IsFalse(Show((true, false), false, true, true, true));
            Assert.IsFalse(Show((true, true), false, true, true, true));
        }

        [Test]
        public void NonVrOnly_ShowsOnFlatScreensOnEitherPlatform()
        {
            Assert.IsTrue(Show((false, false), true, true, false, true));
            Assert.IsFalse(Show((false, true), true, true, false, true));
            Assert.IsTrue(Show((true, false), true, true, false, true));
            Assert.IsFalse(Show((true, true), true, true, false, true));
        }

        [Test]
        public void AGroupWithNothingTicked_HidesEverywhere()
        {
            foreach (var device in Devices)
            {
                Assert.IsFalse(Show(device, false, false, true, true), $"no platform, shown on {device}");
                Assert.IsFalse(Show(device, true, true, false, false), $"no mode, shown on {device}");
            }
        }

        [Test]
        public void EveryCombination_IsThePlatformHalfAndTheModeHalf()
        {
            for (int bits = 0; bits < 16; bits++)
            {
                bool mobile = (bits & 1) != 0, desktop = (bits & 2) != 0, inVR = (bits & 4) != 0, inNonVR = (bits & 8) != 0;
                foreach (var device in Devices)
                {
                    bool platform = device.android ? mobile : desktop;
                    bool mode = device.vr ? inVR : inNonVR;
                    Assert.AreEqual(platform && mode, Show(device, mobile, desktop, inVR, inNonVR),
                        $"mobile={mobile} desktop={desktop} vr={inVR} nonVr={inNonVR} on {device}");
                }
            }
        }

        [Test]
        public void ThePlatformHalf_IsWhatTheBuildUses()
        {
            Assert.IsTrue(BSPlatformFilter.IsPlatformIncluded(true, includeOnMobile: true, includeOnDesktop: false));
            Assert.IsFalse(BSPlatformFilter.IsPlatformIncluded(false, includeOnMobile: true, includeOnDesktop: false));
            Assert.IsTrue(BSPlatformFilter.IsPlatformIncluded(false, includeOnMobile: false, includeOnDesktop: true));
            Assert.IsFalse(BSPlatformFilter.IsPlatformIncluded(true, includeOnMobile: false, includeOnDesktop: true));
        }

        [Test]
        public void ANewFilter_ShowsEverywhere_AndNeedsNoDeviceCheck()
        {
            var go = new GameObject("PlatformFilterTests");
            try
            {
                var filter = go.AddComponent<BSPlatformFilter>();
                Assert.IsTrue(filter.includeOnMobile && filter.includeOnDesktop && filter.includeInVR && filter.includeInNonVR);
                Assert.IsFalse(filter.NeedsRuntimeCheck);
                Assert.IsNotNull(go.GetComponent<BSObjectId>(), "RequireComponent(BSObjectId)");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [TestCase(true, true, true, true, false)]
        [TestCase(false, true, true, true, false)] // platform only: each platform's bundle already decided
        [TestCase(true, false, true, true, false)]
        [TestCase(true, true, false, true, true)]
        [TestCase(true, true, true, false, true)]
        [TestCase(true, false, true, false, true)] // Android + VR only
        public void OnlyAVrRestriction_NeedsTheDeviceToDecide(bool mobile, bool desktop, bool inVR, bool inNonVR, bool expected)
        {
            var go = new GameObject("PlatformFilterTests");
            try
            {
                var filter = go.AddComponent<BSPlatformFilter>();
                filter.includeOnMobile = mobile;
                filter.includeOnDesktop = desktop;
                filter.includeInVR = inVR;
                filter.includeInNonVR = inNonVR;
                Assert.AreEqual(expected, filter.NeedsRuntimeCheck);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // The device-side half: Start applies the filter to the GameObject, a property change re-applies it, and the
        // filter never switches on an object something else switched off. In the Windows editor the scene answers
        // PC; IsVR comes from the hook these tests set.

        static void AsStarted(BSPlatformFilter filter)
        {
            // What Init does before Deserialise, without registering with a page that is not there.
            typeof(BSPlatformFilter).GetField("alreadyStarted", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(filter, true);
        }

        static void OnDevice(bool vr, System.Action body)
        {
            if (Application.platform != RuntimePlatform.WindowsEditor)
                Assert.Ignore("The device-side tests assume the Windows editor (PC, not Android).");
            var scene = BSScene.Instance();
            var hook = scene.events.IsVR;
            scene.events.IsVR = () => vr;
            try { body(); }
            finally { scene.events.IsVR = hook; }
        }

        [Test]
        public void OnStart_APcExcludedObjectIsSwitchedOff_AndComesBackWhenPcIsTicked()
        {
            OnDevice(false, () =>
            {
                var go = new GameObject("PlatformFilterTests");
                try
                {
                    var filter = go.AddComponent<BSPlatformFilter>();
                    filter.includeOnDesktop = false;
                    filter.StartStuff();
                    Assert.IsFalse(go.activeSelf, "Android-only object still active on PC");
                    Assert.IsTrue(filter.IsLoaded, "a space's load waits for every component to report loaded");

                    AsStarted(filter);
                    filter.IncludeOnDesktop = true;
                    Assert.IsTrue(go.activeSelf, "ticking PC did not bring it back");
                }
                finally
                {
                    Object.DestroyImmediate(go);
                }
            });
        }

        [Test]
        public void OnStart_TheVrHalfDecidesInAHeadset()
        {
            OnDevice(true, () =>
            {
                var go = new GameObject("PlatformFilterTests");
                try
                {
                    var filter = go.AddComponent<BSPlatformFilter>();
                    filter.includeInVR = false;
                    filter.StartStuff();
                    Assert.IsFalse(go.activeSelf, "a flat-screen-only object stayed active in VR");
                }
                finally
                {
                    Object.DestroyImmediate(go);
                }
            });
        }

        [Test]
        public void HidingDuringAPagesAddComponent_StillReportsLoaded()
        {
            // A page's AddComponent runs Init (and Deserialise) before any Start; a filter that hides its object
            // there never gets a Start, so the load report cannot wait for one.
            OnDevice(false, () =>
            {
                var go = new GameObject("PlatformFilterTests");
                try
                {
                    var filter = go.AddComponent<BSPlatformFilter>();
                    AsStarted(filter);
                    filter.IncludeOnDesktop = false;
                    Assert.IsFalse(go.activeSelf);
                    Assert.IsTrue(filter.IsLoaded, "hidden before Start and never reported loaded");
                }
                finally
                {
                    Object.DestroyImmediate(go);
                }
            });
        }

        [Test]
        public void TheFilter_NeverSwitchesOnWhatItDidNotSwitchOff()
        {
            OnDevice(false, () =>
            {
                var go = new GameObject("PlatformFilterTests");
                try
                {
                    var filter = go.AddComponent<BSPlatformFilter>();
                    go.SetActive(false); // someone else's choice
                    filter.StartStuff();
                    AsStarted(filter);
                    filter.IncludeInNonVR = true; // a re-apply that says "show"
                    Assert.IsFalse(go.activeSelf, "the filter switched on an object it never hid");
                }
                finally
                {
                    Object.DestroyImmediate(go);
                }
            });
        }

        [Test]
        public void TheWireIds_AreAppendedNotInserted()
        {
            // ComponentType and PropertyName ordinals are the JS <-> Unity protocol: append-only.
            Assert.AreEqual(79, (int)ComponentType.SpawnerObject);
            Assert.AreEqual(80, (int)ComponentType.PlatformFilter);
            Assert.AreEqual((int)PropertyName.forceKinematic + 1, (int)PropertyName.includeOnMobile);
            Assert.AreEqual((int)PropertyName.includeOnMobile + 3, (int)PropertyName.includeInNonVR);
        }
    }
}
