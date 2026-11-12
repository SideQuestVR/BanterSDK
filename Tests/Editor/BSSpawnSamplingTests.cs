using NUnit.Framework;
using UnityEngine;

namespace BS.SDKEditor.Tests
{
    /// <summary>Where BSSpawn lands players: a uniform point on the horizontal disc of its radius.</summary>
    public class BSSpawnSamplingTests
    {
        static readonly Vector3 Center = new Vector3(3f, 1.5f, -2f);
        const float Tolerance = 1e-4f;

        static float Distance(Vector3 point) => Vector2.Distance(new Vector2(point.x, point.z), new Vector2(Center.x, Center.z));

        [TestCase(0f)]
        [TestCase(-2f)]
        public void NoRadius_LandsOnTheSpawn(float radius)
        {
            Assert.AreEqual(Center, BSSpawn.SamplePoint(Center, radius, 0.7f, 123f));
        }

        [TestCase(0f)]
        [TestCase(90f)]
        [TestCase(200f)]
        public void LargestSample_LandsOnTheEdge(float angle)
        {
            Assert.AreEqual(4f, Distance(BSSpawn.SamplePoint(Center, 4f, 1f, angle)), Tolerance);
        }

        [Test]
        public void QuarterSample_IsHalfwayOut()
        {
            // Uniform over the area: a quarter of the points fall within half the radius.
            Assert.AreEqual(2f, Distance(BSSpawn.SamplePoint(Center, 4f, 0.25f, 45f)), Tolerance);
        }

        [Test]
        public void Angle_TurnsFromXTowardsZ()
        {
            var point = BSSpawn.SamplePoint(Vector3.zero, 1f, 1f, 90f);
            Assert.AreEqual(0f, point.x, Tolerance);
            Assert.AreEqual(1f, point.z, Tolerance);
        }

        [Test]
        public void RandomSamples_StayLevel_AndInside()
        {
            var random = new System.Random(1234);
            for (var i = 0; i < 1000; i++)
            {
                var point = BSSpawn.SamplePoint(Center, 5f, (float)random.NextDouble(), (float)random.NextDouble() * 360f);
                Assert.AreEqual(Center.y, point.y);
                Assert.LessOrEqual(Distance(point), 5f + Tolerance);
            }
        }
    }
}
