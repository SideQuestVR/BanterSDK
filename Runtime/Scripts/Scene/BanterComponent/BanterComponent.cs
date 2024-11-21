using Banter.Utilities.Async;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Banter.SDK
{
    public class BanterComponent
    {
        public int cid;
        public ComponentType type;
        public ConcurrentDictionary<PropertyName, BanterComponentProperty> componentProperties = new ConcurrentDictionary<PropertyName, BanterComponentProperty>();
        public BanterObject banterObject;
        public float progress;
        public bool loaded;
        public void SetProperty(PropertyName name, PropertyType type, object value, Action callback = null)
        {
            BanterComponentProperty prop;
            try
            {
                if (componentProperties.TryGetValue(name, out prop))
                {
                    if (prop != null && value != null && (prop.value == null || !prop.value.Equals(value)))
                    {
                        prop.value = value;
                        // banterObject.scene.dirty = true;
                        string change = banterObject.scene.Serialise(prop, this);
                        if (change != null)
                        {
                            banterObject.scene.EnqueueChange(change);
                        }
                    }
                    else if (prop == null)
                    {
                        Debug.Log(this.type + ":" + name + " Is prop null?: " + (prop == null) + " " + (prop == null ? "" : prop.type));
                    }
                }
                else
                {
                    prop = new BanterComponentProperty()
                    {
                        banterComponent = this,
                        name = name,
                        type = type,
                        value = value
                    };
                    componentProperties.TryAdd(name, prop);
                    string change = banterObject.scene.Serialise(prop, this);
                    if (change != null)
                    {
                        banterObject.scene.EnqueueChange(change);
                    }
                }
                callback?.Invoke();
            }
            catch (Exception e)
            {
                Debug.Log("Error setting property: " + cid + " : " + banterObject.oid);
                Debug.LogError("Error setting property: " + e);
            }
        }

        public void UpdateProperty(PropertyName name, object value)
        {
            BanterComponentProperty prop;
            try
            {
                if (componentProperties.TryGetValue(name, out prop))
                {
                    if (prop != null && value != null)
                    {
                        prop.value = value;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.Log("Error setting property: " + cid + " : " + banterObject.oid);
                Debug.LogError("Error setting property: " + e);
            }
        }

        public async Task WatchProperties(PropertyName[] names)
        {
            await ObjectOnMainThread(component => component.WatchProperties(names));
        }

        public async Task GetProperties()
        {
            var done = false;
            _ = ObjectOnMainThread(component =>
            {
                component.SyncProperties(true, () => done = true);
            });
            await new WaitUntil(() => done);
        }
        public async Task CallMethod(string methodName, List<object> parameters, Action<object> callback)
        {
            await ObjectOnMainThread(component => callback(component.CallMethod(methodName, parameters)));
        }

        public Task ObjectOnMainThread(Action<BanterComponentBase> callback)
        {
            return UnityMainThreadTaskScheduler.Default.EnqueueAsync(() =>
            {
                var ObjectId = banterObject.unityAndBanterObject.id;
                if (ObjectId != null && ObjectId.mainThreadComponentMap.TryGetValue(cid, out var component))
                {
                    callback(component);
                }
            });
        }

        public void Dispose()
        {
            banterObject.RemoveComponent(cid);
            if (componentProperties != null)
            {
                componentProperties.Clear();
            }
            componentProperties = null;
        }
    }
}
