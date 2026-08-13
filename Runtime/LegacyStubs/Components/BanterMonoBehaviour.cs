using System;
using UnityEngine;

namespace Banter.SDK
{
    /// <summary>Deprecated alias for <see cref="BSMonoBehaviour"/>, kept so existing scenes,
    /// asset bundles and Visual Scripting graphs keep resolving. Do not use in new code.</summary>
    [Obsolete("Renamed to BSMonoBehaviour. Kept for content compatibility.")]
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    public class BanterMonoBehaviour : BSMonoBehaviour { }
}
