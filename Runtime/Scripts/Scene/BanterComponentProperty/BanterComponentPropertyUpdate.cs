using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Banter.SDK
{
    [System.Serializable]
    public class BanterComponentPropertyUpdate : BanterComponentPropertyBase
    {
        public int oid;
        public int cid;
        public string objName;
        public Action callback;
    }
}
