using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Banter.SDK
{
    [RequireComponent(typeof(BanterObjectId))]
    public abstract class BanterComponentBase : MonoBehaviour
    {
        internal abstract void Deserialise(List<object> values);
        internal abstract void SyncProperties(bool force, Action callback = null);
        internal abstract void WatchProperties(PropertyName[] properties);
        internal abstract void Init(List<object> constructorProperties = null);
        internal abstract void ReSetup();
        internal abstract void StartStuff();
        internal abstract void DestroyStuff();
        internal abstract object CallMethod(string methodName, List<object> parameters);
        [HideInInspector] public UnityEvent<float> progress { get; private set; } = new UnityEvent<float>();
        [HideInInspector] public UnityEvent<bool, string> loaded { get; private set; } = new UnityEvent<bool, string>();
        public bool _loaded { get; internal set; }
        public float percentage { get; internal set; }
        public int oid { get; internal set; }
        public int cid { get; internal set; }

        protected void SetLoadedIfNot(bool success = true, string message = "Loaded ok.")
        {
            if (!_loaded)
            {
                _loaded = true;
                if (!success)
                {
                    LogLine.Do(Color.red, LogTag.Banter, "Failed to load: " + message);
                }
                loaded.Invoke(success, message);
            }
        }
    }
}
