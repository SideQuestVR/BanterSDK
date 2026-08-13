using System;
using BS;
using UnityEngine;

namespace Banter.SDK
{
    /// <summary>Deprecated alias for <see cref="BSBoxCollider"/>, kept so existing scenes,
    /// asset bundles and Visual Scripting graphs keep resolving. Do not use in new code.</summary>
    [Obsolete("Renamed to BSBoxCollider. Kept for content compatibility.")]
    [RequireComponent(typeof(BoxCollider))]
    [RequireComponent(typeof(BSObjectId))]
    public class BanterBoxCollider : BSBoxCollider { }
}
