using System;
using UnityEngine;

namespace Banter.SDK
{
    /// <summary>Deprecated alias for <see cref="BSConfigurableJoint"/>, kept so existing scenes,
    /// asset bundles and Visual Scripting graphs keep resolving. Do not use in new code.</summary>
    [Obsolete("Renamed to BSConfigurableJoint. Kept for content compatibility.")]
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(ConfigurableJoint))]
    [RequireComponent(typeof(BSObjectId))]
    public class BanterConfigurableJoint : BSConfigurableJoint { }
}
