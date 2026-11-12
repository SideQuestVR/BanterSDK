#if GREENFIELD_PROJECT
using System.Collections.Generic;
using UnityEngine;

namespace BS.Security
{
    /// <summary>
    /// Holds one value of every [SerializeReference] class content can use, so the layout generator's probe bundle
    /// stores each class's tree and hash (BundleScriptLayoutsGenerator). Editor tooling only, never content: the
    /// pre-load check denies it (BundleCheckPolicy.ExtraDeniedTypes). A runtime type because Unity writes a bundle's
    /// trees from the player assemblies.
    /// </summary>
    internal sealed class BundleLayoutProbe : ScriptableObject
    {
        [SerializeReference] internal List<object> Values = new List<object>();
    }
}
#endif
