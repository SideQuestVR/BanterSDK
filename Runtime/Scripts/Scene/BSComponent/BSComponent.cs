using BS.Utilities.Async;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Unity.VisualScripting;

namespace BS
{
    [RenamedFrom("Banter.SDK.BanterComponent")]
    public class BSComponent
    {
        public int cid;
        public string jsId;
        public ComponentType type;
        public ConcurrentDictionary<PropertyName, BSComponentProperty> componentProperties = new ConcurrentDictionary<PropertyName, BSComponentProperty>();
        public BSObject banterObject;
        public float progress;
        public bool loaded;
        public void SetProperty(PropertyName name, PropertyType type, object value, Action callback = null)
        {
            BSComponentProperty prop;
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
                    prop = new BSComponentProperty()
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
            BSComponentProperty prop;
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
            Task pending = null;
            await ObjectOnMainThread(component =>
            {
                var result = component.CallMethod(methodName, parameters);
                if (result is Task task)
                {
                    pending = task;
                }
                else
                {
                    callback(result);
                }
            });
            if (pending == null)
            {
                return;
            }
            // An async [Method] (AudioSource.PlayOneShotFromUrl) hands back its Task, which reached the page as a
            // stringified Task object. Answer once it has finished instead, with its result if it has one; if it
            // throws, so does this, and the caller reports the error to the page.
            await pending;
            var value = TaskResult(pending);
            await UnityMainThreadTaskScheduler.Default.EnqueueAsync(TaskRunner.Track(() => callback(value), $"{nameof(BSComponent)}.{nameof(CallMethod)}"));
        }

        /// <summary>A finished Task's result: null for a plain Task, whose runtime type is Task&lt;VoidTaskResult&gt;.</summary>
        static object TaskResult(Task task)
        {
            for (var type = task.GetType(); type != null && type != typeof(Task); type = type.BaseType)
            {
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
                {
                    return type.GetGenericArguments()[0].Name == "VoidTaskResult" ? null : type.GetProperty("Result").GetValue(task);
                }
            }
            return null;
        }

        public BSComponentBase Object()
        {
            var ObjectId = banterObject.unityAndBanterObject.id;
            if (ObjectId != null && ObjectId.mainThreadComponentMap.TryGetValue(cid, out var component))
            {
                return component;
            }
            return null;
        }

        public Task ObjectOnMainThread(Action<BSComponentBase> callback)
        {
            return UnityMainThreadTaskScheduler.Default.EnqueueAsync(TaskRunner.Track(() =>
            {
                callback(Object());
            }, $"{nameof(BSComponent)}.{nameof(ObjectOnMainThread)}"));
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
