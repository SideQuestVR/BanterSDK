using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Banter
{
    [RequireComponent(typeof(BanterObjectId))]
    public abstract class BanterComponentBase : MonoBehaviour
    {
        public abstract void Deserialise(List<object> values);
        public abstract void SyncProperties(bool force, Action callback = null);
        public abstract void WatchProperties(PropertyName[] properties);
        public abstract void Init();
        public abstract void ReSetup();
        public abstract void StartStuff();
        public abstract void DestroyStuff();
        public abstract object CallMethod(string methodName, List<object> parameters);
        [HideInInspector] public UnityEvent<float> progress = new UnityEvent<float>();
        [HideInInspector] public UnityEvent<bool, string> loaded = new UnityEvent<bool, string>();
        [HideInInspector] public bool _loaded;
        [HideInInspector] public float percentage;
        [HideInInspector] public int oid;
        [HideInInspector] public int cid;

        public void SetLoadedIfNot(bool success = true, string message = "Loaded ok.") {
            if(!_loaded) {
                _loaded = true;
                if(!success){
                    LogLine.Do(Color.red, LogTag.Banter, "Failed to load: " + message);
                }
                loaded.Invoke(success, message);
            }
        }
    }
}
