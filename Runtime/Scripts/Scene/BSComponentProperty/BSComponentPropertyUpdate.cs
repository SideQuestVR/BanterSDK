using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.VisualScripting;

namespace BS
{
    [System.Serializable]
    [RenamedFrom("Banter.SDK.BanterComponentPropertyUpdate")]
    public class BSComponentPropertyUpdate : BSComponentPropertyBase
    {
        public int oid;
        public int cid;
        public string objName;
        public Action callback;
    }
}
