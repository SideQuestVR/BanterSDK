using System;
using BS;
using UnityEngine;

namespace Banter.SDK
{
    /// <summary>Deprecated alias for <see cref="BSMeshCollider"/>, kept so existing scenes,
    /// asset bundles and Visual Scripting graphs keep resolving. Do not use in new code.</summary>
    [Obsolete("Renamed to BSMeshCollider. Kept for content compatibility.")]
    [RequireComponent(typeof(MeshCollider))]
    [RequireComponent(typeof(BSObjectId))]
    public class BanterMeshCollider : BSMeshCollider { }
}
