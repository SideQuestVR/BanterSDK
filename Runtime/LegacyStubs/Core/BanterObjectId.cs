using System;
using BS;
using UnityEngine;

namespace Banter.SDK
{
    /// <summary>Deprecated alias for <see cref="BSObjectId"/>, kept so existing scenes,
    /// prefabs and asset bundles keep resolving. Do not use in new code.</summary>
    [Obsolete("Renamed to BSObjectId. Kept for content compatibility.")]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-1)]
    public class BanterObjectId : BSObjectId { }
}
