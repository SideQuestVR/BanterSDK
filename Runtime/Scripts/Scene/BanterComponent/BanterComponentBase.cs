using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Banter.SDK
{
    [RequireComponent(typeof(BSObjectId))]
    public abstract class BanterComponentBase : MonoBehaviour
    {
        public string jsId;
        internal abstract void Deserialise(List<object> values);
        internal abstract void SyncProperties(bool force, Action callback = null);
        internal abstract void WatchProperties(PropertyName[] properties);
        internal abstract void Init(List<object> constructorProperties = null);
        internal abstract void ReSetup();
        internal abstract string GetSignature();
        public void Refresh() => ReSetup();
        internal abstract void StartStuff();
        internal abstract void DestroyStuff();
        internal abstract void UpdateStuff();
        internal abstract object CallMethod(string methodName, List<object> parameters);

        /// <summary>
        /// Override this method to return a specific object reference for asset resolution.
        /// For UnityComponents, this returns the underlying Unity component.
        /// For BanterComponents, override to return a custom field if needed.
        /// </summary>
        public virtual UnityEngine.Object GetReferenceObject()
        {
            return this;
        }

        // NonSerialized: listeners are only ever added from code (HideInInspector since forever),
        // and serializing it clashes with generated subclass fields named "progress"
        // (e.g. BSAOBaking) — Unity logs "same field name serialized multiple times" per build.
        [NonSerialized][HideInInspector] public UnityEvent<float> progress = new UnityEvent<float>();
        [HideInInspector]public UnityEvent<bool, string> loaded = new UnityEvent<bool, string>();
        internal bool _loaded;
        public bool IsLoaded => _loaded;
        internal float percentage;
        internal int oid;
        internal int cid;

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
