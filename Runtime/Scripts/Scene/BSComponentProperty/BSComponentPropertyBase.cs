using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.VisualScripting;

namespace Banter.SDK
{
    [RenamedFrom("Banter.SDK.BanterComponentPropertyBase")]
    public class BSComponentPropertyBase
    {
        [System.NonSerialized] public PropertyName name;
        [System.NonSerialized] public ComponentType componentType;
        [System.NonSerialized] public PropertyType type;
        [System.NonSerialized] public object value;
    }
}
