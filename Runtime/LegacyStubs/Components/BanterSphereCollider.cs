using System;
using BS;
using UnityEngine;

namespace Banter.SDK
{
    /// <summary>Deprecated alias for <see cref="BSSphereCollider"/>, kept so existing scenes,
    /// asset bundles and Visual Scripting graphs keep resolving. Do not use in new code.</summary>
    [Obsolete("Renamed to BSSphereCollider. Kept for content compatibility.")]
    [RequireComponent(typeof(SphereCollider))]
    [RequireComponent(typeof(BSObjectId))]
    public class BanterSphereCollider : BSSphereCollider { }
}
