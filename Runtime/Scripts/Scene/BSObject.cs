using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.VisualScripting;

namespace BS
{
    [RenamedFrom("Banter.SDK.BanterObject")]
    public class BSObject
    {
        public BSObject()
        {
            scene = BSScene.Instance();
        }
        public int oid;
        public string name = "";
        public BSScene scene;
        public UnityAndBanterObject unityAndBanterObject;
        public ConcurrentDictionary<int, BSComponent> banterComponents = new ConcurrentDictionary<int, BSComponent>();
        public Transform previousParent;
        public BSComponent GetComponent(int id)
        {
            return scene?.GetBanterComponent(id);
        }
        public void AddComponent(int id, BSComponent component)
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
