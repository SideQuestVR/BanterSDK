using System;
using System.Reflection;
using NUnit.Framework;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// Encrypted (.bee) worlds download through Basis (Get.EncryptedAssetBundleWithSection). Its download loop only
    /// stopped for a cancellation token, and nothing passes one, so a stalled download held the space's load for good.
    /// It now gives up once no new bytes have arrived for 60 s, and Get.AssetBundle (raw bundles) uses the same watch.
    /// This assembly doesn't reference Basis, so the watch is reached by name.
    ///
    /// Worst case before the load fails: about 180 s for an uncached world whose server never sends a body byte (the
    /// connector prefetch's header, the main download's header, then Basis' one retry, each waiting 60 s). A stall in
    /// the middle of the world's section fails after 60 s, and the retry downloads the whole world again from the
    /// start (ranges are downloaded to memory, so nothing of the first attempt is kept).
    /// </summary>
    public class BeeDownloadStallTests
    {
        static readonly Type IO = Type.GetType("BasisIOManagement, BasisBundleManagement", throwOnError: true);

        sealed class Watch
        {
            static readonly Type type = IO.GetNestedType("DownloadStallWatch");
            static readonly MethodInfo isStalled = type.GetMethod("IsStalled");
            static readonly PropertyInfo lastBytes = type.GetProperty("LastBytes");
            readonly object boxed; // calls on the box keep its state

            internal Watch(double stallSeconds, double now) => boxed = Activator.CreateInstance(type, stallSeconds, now);
            internal bool IsStalled(ulong bytes, double now) => (bool)isStalled.Invoke(boxed, new object[] { bytes, now });
            internal ulong LastBytes => (ulong)lastBytes.GetValue(boxed);

            /// <summary>Polls every half second (as the download loops do, and more often) from <paramref name="from"/> to <paramref name="to"/>.</summary>
            internal bool StalledBy(ulong bytes, double from, double to)
            {
                var stalled = false;
                for (var t = from; t <= to + 1e-9; t += 0.5) stalled = IsStalled(bytes, t);
                return stalled;
            }
        }

        [Test]
        public void BeeDownloads_UseGetAssetBundlesStallTime()
        {
            var bee = (double)IO.GetField("DownloadStallSeconds").GetValue(null);
            var raw = (float)typeof(Get).GetField("LARGE_DOWNLOAD_STALL_SECONDS", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            Assert.AreEqual((double)raw, bee, "raw and encrypted world downloads give up after the same silence");
        }

        [Test]
        public void ADownloadThatNeverReceivesAByte_StallsAfter60Seconds()
        {
            var watch = new Watch(60, 1000);
            Assert.IsFalse(watch.StalledBy(0, 1000.5, 1060), "exactly 60 s is still waiting, as before");
            Assert.IsTrue(watch.IsStalled(0, 1060.1), "a half-open connection that never answers");
            Assert.IsFalse(new Watch(0, 0).StalledBy(0, 0.5, 600), "0 turns the check off");
        }

        [Test]
        public void TheClockRunsFromTheLastNewBytes_NotFromTheStart()
        {
            var slow = new Watch(60, 0);
            ulong bytes = 0;
            for (double t = 0.5; t <= 3600; t += 0.5)
            {
                if (t % 59 < 0.5) bytes++; // a few bytes a minute
                Assert.IsFalse(slow.IsStalled(bytes, t), "a slow download that still moves is never abandoned");
            }

            var stopped = new Watch(60, 0);
            Assert.IsFalse(stopped.StalledBy(5000, 0.5, 50));
            Assert.IsFalse(stopped.StalledBy(9000, 50.5, 110), "the last bytes came at 50.5 s");
            Assert.IsTrue(stopped.IsStalled(9000, 111));
            Assert.AreEqual(9000UL, stopped.LastBytes, "the log says how far it got");
        }

        [Test]
        public void APause_TimesTheSilenceAgain_InsteadOfFailingAtOnce()
        {
            var watch = new Watch(60, 0);
            Assert.IsFalse(watch.StalledBy(1000, 0.5, 10));
            // The headset was off for five minutes, and the clock ran on.
            Assert.IsFalse(watch.IsStalled(1000, 310), "the first poll after a pause");
            Assert.IsFalse(watch.StalledBy(1000, 310.5, 370));
            Assert.IsTrue(watch.IsStalled(1000, 370.5), "60 s of silence after the wake");
        }
    }
}
