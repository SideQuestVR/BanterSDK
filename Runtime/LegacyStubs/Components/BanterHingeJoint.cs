using System;
using UnityEngine;

namespace Banter.SDK
{
    /// <summary>Deprecated alias for <see cref="BSHingeJoint"/>, kept so existing scenes,
    /// asset bundles and Visual Scripting graphs keep resolving. Do not use in new code.</summary>
    [Obsolete("Renamed to BSHingeJoint. Kept for content compatibility.")]
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(HingeJoint))]
    [RequireComponent(typeof(BSObjectId))]
    public class BanterHingeJoint : BSHingeJoint { }
}
