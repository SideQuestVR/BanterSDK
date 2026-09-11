using System;
using System.Threading.Tasks;
using UnityEngine;

namespace BS
{
    /// <summary>
    /// Resolves texture references of the form <c>&lt;scheme&gt;:&lt;path&gt;</c> for <see cref="BSMaterial"/>,
    /// so a material property can name a texture without carrying a URL.
    /// </summary>
    /// <remarks>
    /// Registered through <see cref="BSMaterial.RegisterTextureSource"/>, normally from a
    /// <c>[RuntimeInitializeOnLoadMethod]</c> in the package that owns the scheme (the cc0 texture
    /// library registers <c>cc0</c> and maps <c>cc0:{slug}/{map}/{size}</c> to a CDN file id). The SDK
    /// never depends on those packages; a reference whose scheme is not registered falls through to
    /// the plain URL path and, being no URL, is ignored.
    ///
    /// Schemes are matched case-insensitively and must not shadow <c>http</c>, <c>https</c>,
    /// <c>file</c>, <c>data</c> or <c>asset</c>.
    /// </remarks>
    public interface IBSTextureSource
    {
        /// <summary>The scheme without the colon, e.g. <c>cc0</c>.</summary>
        string Scheme { get; }

        /// <summary>
        /// Resolve <paramref name="path"/> (everything after the colon) to a texture.
        /// </summary>
        /// <param name="path">Reference body, e.g. <c>bark_11/basecolor/1024</c>.</param>
        /// <param name="linear">True for data maps (normal, roughness, AO) that must not be sRGB-decoded.</param>
        /// <param name="mipmaps">Whether the final texture should carry a mip chain.</param>
        /// <param name="preview">
        /// May be invoked at most once, synchronously or later, with a low-resolution stand-in that is
        /// available immediately (an in-build thumbnail). The caller shows it until the task completes.
        /// Sources with no preview simply never call it.
        /// </param>
        /// <returns>The final texture, or null when the reference cannot be resolved.</returns>
        Task<Texture2D> Resolve(string path, bool linear, bool mipmaps, Action<Texture2D> preview);
    }
}
