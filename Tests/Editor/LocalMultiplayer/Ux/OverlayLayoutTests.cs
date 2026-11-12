using BS.LocalMultiplayer.Overlay;
using NUnit.Framework;

namespace BS.LocalMultiplayer.Tests
{
    /// <summary>
    /// How big the overlay is and how it starts: it shrinks in a small Multiplayer Play Mode player window, so
    /// the panel leaves the world clickable, and a clone starts with the one-line pill.
    /// </summary>
    public class OverlayLayoutTests
    {
        const float Tolerance = 1e-4f;

        [TestCase(1200, 900, 1f)]
        [TestCase(1920, 1080, 1.2f)]
        [TestCase(2560, 1440, 1.6f)]
        [TestCase(800, 600, 0.6666667f)]
        [TestCase(1920, 600, 0.6666667f)]
        [TestCase(900, 1080, 0.75f)]
        [TestCase(640, 480, 0.6f)]
        [TestCase(0, 0, 0.6f)]
        [TestCase(3840, 2160, 2f)]
        public void ScaleFactor_FollowsTheTighterSide(int width, int height, float expected)
        {
            Assert.AreEqual(expected, OverlayLayout.ScaleFactor(width, height), Tolerance);
        }

        [Test]
        public void ScaleFactor_ShrinksBelowOne_InASmallPlayerWindow()
        {
            // The old rule never went below 1, so a 430 px panel covered most of an 800x600 clone.
            Assert.Less(OverlayLayout.ScaleFactor(800, 600), 1f);
            Assert.Less(OverlayLayout.ScaleFactor(1024, 768), 1f);
        }

        [Test]
        public void ScaleFactor_StaysInBounds_AndGrowsWithTheView()
        {
            var sizes = new[] { 0, 1, 320, 480, 600, 720, 800, 900, 1080, 1200, 1440, 2160, 4320, 10000 };
            foreach (var width in sizes)
            {
                var previousInRow = 0f;
                foreach (var height in sizes)
                {
                    var scale = OverlayLayout.ScaleFactor(width, height);
                    var label = width + "x" + height;
                    Assert.GreaterOrEqual(scale, OverlayLayout.MinScale, label);
                    Assert.LessOrEqual(scale, OverlayLayout.MaxScale, label);
                    Assert.GreaterOrEqual(scale, previousInRow, label);
                    Assert.LessOrEqual(scale, OverlayLayout.ScaleFactor(width + 100, height), label);
                    previousInRow = scale;
                }
            }
        }

        [Test]
        public void MainEditor_FollowsTheSetting()
        {
            Assert.IsTrue(OverlayLayout.PanelVisibleOnStart(new LocalMultiplayerSettings(), false));
            Assert.IsTrue(OverlayLayout.PanelVisibleOnStart(new LocalMultiplayerSettings { overlayVisibleOnStart = true }, false));
            Assert.IsFalse(OverlayLayout.PanelVisibleOnStart(new LocalMultiplayerSettings { overlayVisibleOnStart = false }, false));
            // No settings: the default, which shows the panel.
            Assert.IsTrue(OverlayLayout.PanelVisibleOnStart(null, false));
        }

        [Test]
        public void Clone_AlwaysStartsWithThePill()
        {
            Assert.IsFalse(OverlayLayout.PanelVisibleOnStart(new LocalMultiplayerSettings { overlayVisibleOnStart = true }, true));
            Assert.IsFalse(OverlayLayout.PanelVisibleOnStart(new LocalMultiplayerSettings { overlayVisibleOnStart = false }, true));
            Assert.IsFalse(OverlayLayout.PanelVisibleOnStart(null, true));
        }
    }
}
