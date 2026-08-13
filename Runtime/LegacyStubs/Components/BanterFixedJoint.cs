using System;
using UnityEngine;

namespace Banter.SDK
{
    /// <summary>Deprecated alias for <see cref="BSFixedJoint"/>, kept so existing scenes,
    /// asset bundles and Visual Scripting graphs keep resolving. Do not use in new code.</summary>
    [Obsolete("Renamed to BSFixedJoint. Kept for content compatibility.")]
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(FixedJoint))]
    [RequireComponent(typeof(BanterObjectId))]
    public class BanterFixedJoint : BSFixedJoint { }
}
