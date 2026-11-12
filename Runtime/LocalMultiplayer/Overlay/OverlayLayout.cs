using UnityEngine;

namespace BS.LocalMultiplayer.Overlay
{
    /// <summary>
    /// How big the overlay is and how it starts. Pure functions of the Game view size and the settings, so
    /// <see cref="OverlayCanvas"/> and <see cref="OverlayModule"/> share them with the tests, which have no canvas.
    /// </summary>
    internal static class OverlayLayout
    {
        /// <summary>The Game view size at which one canvas pixel is one screen pixel.</summary>
        public const float ReferenceWidth = 1200f;
        public const float ReferenceHeight = 900f;

        /// <summary>
        /// The smallest scale. A Multiplayer Play Mode player's window is often around 800x600, where the 430 px
        /// panel at full size would cover most of the view and take the clicks and grabs under it.
        /// </summary>
        public const float MinScale = 0.6f;

        /// <summary>The largest scale, so the panel stays legible on a 4K screen without taking it over.</summary>
        public const float MaxScale = 2f;

        /// <summary>
        /// The canvas scale for a Game view of <paramref name="width"/> by <paramref name="height"/> pixels: 1 at
        /// 1200x900, following whichever side is tighter, clamped to <see cref="MinScale"/>..<see cref="MaxScale"/>.
        /// </summary>
        public static float ScaleFactor(int width, int height)
        {
            return Mathf.Clamp(Mathf.Min(height / ReferenceHeight, width / ReferenceWidth), MinScale, MaxScale);
        }

        /// <summary>
        /// Whether the panel, rather than the one-line pill, shows when Play starts. Only the main editor follows
        /// <see cref="LocalMultiplayerSettings.overlayVisibleOnStart"/>: a Multiplayer Play Mode player's Game view
        /// is small, so a clone always starts with the pill, and the overlay key or a click on it opens the panel.
        /// </summary>
        public static bool PanelVisibleOnStart(LocalMultiplayerSettings settings, bool isClone)
        {
            return !isClone && (settings == null || settings.overlayVisibleOnStart);
        }
    }
}
