using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Banter.SDK
{
    public class UnityComponentBase : BanterComponentBase
    {

        internal override object CallMethod(string methodName, List<object> parameters)
        {
            throw new System.NotImplementedException();
        }

        internal override void Deserialise(List<object> values)
        {
            throw new System.NotImplementedException();
        }

        internal override void StartStuff()
        {
            // throw new NotImplementedException();
        }

        internal override void DestroyStuff()
        {
            // throw new NotImplementedException();
        }
        

        internal override void UpdateStuff()
        {
            
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            throw new System.NotImplementedException();
        }

        internal override void ReSetup()
        {
            throw new System.NotImplementedException();
        }

        internal override string GetSignature()
        {
            throw new System.NotImplementedException();
        }

        internal override void SyncProperties(bool force, Action callback)
        {
            throw new System.NotImplementedException();
        }

        internal override void WatchProperties(PropertyName[] properties)
        {
            throw new System.NotImplementedException();
        }
    }
}
