using System;
using BS;
using UnityEngine;

namespace Banter.SDK
{
    /// <summary>Deprecated alias for <see cref="BSCapsuleCollider"/>, kept so existing scenes,
    /// asset bundles and Visual Scripting graphs keep resolving. Do not use in new code.</summary>
    [Obsolete("Renamed to BSCapsuleCollider. Kept for content compatibility.")]
    [RequireComponent(typeof(CapsuleCollider))]
    [RequireComponent(typeof(BSObjectId))]
    public class BanterCapsuleCollider : BSCapsuleCollider { }
}
