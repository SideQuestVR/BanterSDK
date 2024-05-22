using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Banter.SDK
{
    public class UnityComponentBase : BanterComponentBase
    {

        public override object CallMethod(string methodName, List<object> parameters)
        {
            throw new System.NotImplementedException();
        }

        public override void Deserialise(List<object> values)
        {
            throw new System.NotImplementedException();
        }

        public override void StartStuff()
        {
            // throw new NotImplementedException();
        }

        public override void DestroyStuff()
        {
            // throw new NotImplementedException();
        }

        public override void Init()
        {
            throw new System.NotImplementedException();
        }

        public override void ReSetup()
        {
            throw new System.NotImplementedException();
        }

        public override void SyncProperties(bool force, Action callback)
        {
            throw new System.NotImplementedException();
        }

        public override void WatchProperties(PropertyName[] properties)
        {
            throw new System.NotImplementedException();
        }
    }
}
