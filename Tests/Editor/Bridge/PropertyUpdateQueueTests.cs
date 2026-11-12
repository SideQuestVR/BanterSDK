using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using NUnit.Framework;

namespace BS.Bridge.Tests
{
    /// <summary>
    /// A property update whose object isn't registered is retried, then dropped. It used to go round for as long as the
    /// world stayed open, logging a stack trace every frame, and whoever waited on it was never answered.
    /// </summary>
    public class PropertyUpdateQueueTests
    {
        const BindingFlags Instance = BindingFlags.NonPublic | BindingFlags.Instance;

        [Test]
        public void AnUpdateForAnObjectThatNeverArrivesIsDroppedAtTheCapAndAnswersItsCaller()
        {
            var max = (int)typeof(BSScene).GetField("MaxPropertyUpdateRetries", BindingFlags.NonPublic | BindingFlags.Static).GetRawConstantValue();
            Assert.Greater(max, 0);
            var scene = BareScene();
            var process = typeof(BSScene).GetMethod("DoPropertyUpdateQueue", Instance);
            Assert.IsNotNull(process, "BSScene.DoPropertyUpdateQueue");

            var answered = 0;
            var update = new BSComponentPropertyUpdate { oid = 987654, cid = 987655, callback = () => answered++ };
            var batch = new List<BSComponentPropertyUpdate> { update };

            for (var pass = 1; pass <= max; pass++)
            {
                process.Invoke(scene, new object[] { batch });
                Assert.AreEqual(0, answered, "still waiting for its object on pass " + pass);
            }
            process.Invoke(scene, new object[] { batch });
            Assert.AreEqual(1, answered, "dropped on the pass after the last retry");
        }

        // BSScene's constructor has side effects (input actions, a static instance), so a bare one gets what these paths use.
        // The queue swallows the retries the scheduler hands back: each pass here stands in for one of them.
        static BSScene BareScene()
        {
            var scene = (BSScene)FormatterServices.GetUninitializedObject(typeof(BSScene));
            typeof(BSScene).GetField("objects", Instance).SetValue(scene, new ConcurrentDictionary<int, UnityAndBanterObject>());
            typeof(BSScene).GetField("banterComponents", Instance).SetValue(scene, new ConcurrentDictionary<int, BSComponent>());
            typeof(BSScene).GetField("propertyUpdates", Instance).SetValue(scene,
                new DrainQueue<BSComponentPropertyUpdate>(_ => { }, drain => drain(), e => throw e));
            return scene;
        }
    }
}
