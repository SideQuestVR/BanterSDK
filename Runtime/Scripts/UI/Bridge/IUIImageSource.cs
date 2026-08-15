using UnityEngine.UIElements;

namespace BS.UI.Bridge
{
    /// <summary>
    /// Resolves a <c>background-image</c> value that names something already present on the Unity
    /// side, rather than something to be fetched or decoded.
    /// </summary>
    /// <remarks>
    /// This exists so the bridge can serve images it has no business knowing about. The SDK owns the
    /// <c>res:</c> scheme itself because <c>Resources.Load</c> needs nothing but a path, but anything
    /// with its own index — an atlas set, a streamed catalogue — lives in the package that produced
    /// it, and that package sits ABOVE the SDK in the assembly graph. A provider registers itself at
    /// startup and the dependency points the right way.
    ///
    /// Resolution is deliberately synchronous. A provider that would have to await something should
    /// return false until it is ready rather than block the style write: <c>SetBackgroundImage</c> is
    /// <c>async void</c>, so an await there means the element paints unstyled for a frame and any
    /// exception is unobservable.
    /// </remarks>
    public interface IUIImageSource
    {
        /// <summary>
        /// The URI scheme this source answers for, without the colon — <c>"kit"</c> for
        /// <c>kit:cartooncubeworld/apple__3829793f</c>. Matched case-insensitively.
        /// </summary>
        string Scheme { get; }

        /// <summary>
        /// Resolve the part after the colon.
        /// </summary>
        /// <remarks>
        /// Returning a <see cref="StyleBackground"/> rather than a <c>Texture2D</c> is what lets a
        /// source hand back a <c>Sprite</c>, and a Sprite carries a sub-rect into a larger texture.
        /// That is the whole reason an atlas-backed source can exist at all.
        /// </remarks>
        /// <returns>False when the path is unknown, which the bridge logs and treats as no image.</returns>
        bool TryResolve(string path, out StyleBackground background);
    }
}
