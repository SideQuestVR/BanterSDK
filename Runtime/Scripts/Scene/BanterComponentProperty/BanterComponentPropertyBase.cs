using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Banter.SDK
{
    public class BanterComponentPropertyBase
    {
        [System.NonSerialized] public PropertyName name;
        [System.NonSerialized] public ComponentType componentType;
        [System.NonSerialized] public PropertyType type;
        [System.NonSerialized] public object value;
    }
}
