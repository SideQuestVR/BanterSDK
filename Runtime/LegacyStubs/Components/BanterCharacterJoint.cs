using System;
using BS;
using UnityEngine;

namespace Banter.SDK
{
    /// <summary>Deprecated alias for <see cref="BSCharacterJoint"/>, kept so existing scenes,
    /// asset bundles and Visual Scripting graphs keep resolving. Do not use in new code.</summary>
    [Obsolete("Renamed to BSCharacterJoint. Kept for content compatibility.")]
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(CharacterJoint))]
    [RequireComponent(typeof(BSObjectId))]
    public class BanterCharacterJoint : BSCharacterJoint { }
}
