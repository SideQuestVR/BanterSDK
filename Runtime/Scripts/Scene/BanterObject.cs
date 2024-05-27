using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Banter.SDK
{
    public class BanterObject
    {
        public BanterObject()
        {
            scene = BanterScene.Instance();
        }
        public int oid;
        public string name = "";
        public BanterScene scene;
        public UnityAndBanterObject unityAndBanterObject;
        public ConcurrentDictionary<int, BanterComponent> banterComponents = new ConcurrentDictionary<int, BanterComponent>();
        public Transform previousParent;
        public BanterComponent GetComponent(int id)
        {
            return scene?.GetBanterComponent(id);
        }
        public void AddComponent(int id, BanterComponent component)
        {
            banterComponents.TryAdd(id, component);
        }
        public void RemoveComponent(int id)
        {
            try
            {
                banterComponents.TryRemove(id, out _);
            }
            catch (Exception) { }
        }
        public void Destroy()
        {
            foreach (var comp in banterComponents.ToArray())
            {
                scene?.DestroyBanterComponent(comp.Value.cid);
            }
            banterComponents.Clear();
            banterComponents = null;
        }
    }
}
