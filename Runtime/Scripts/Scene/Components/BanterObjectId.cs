using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Banter.SDK
{
    [ExecuteInEditMode]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-1)]
    public class BanterObjectId : MonoBehaviour
    {
        [Tooltip("A unique identifier for this object within the Banter system.")]
        public string Id;

        [HideInInspector]
        public Dictionary<int, BanterComponentBase> mainThreadComponentMap = new Dictionary<int, BanterComponentBase>();
        [HideInInspector] public UnityEvent loaded = new UnityEvent();
        void Start()
        {
            try
            {
                GenerateId();
                BanterScene.Instance().AddBanterObject(gameObject, this);
            }
            catch (Exception)
            {
                // Debug.LogError("BanterObjectId: " + e.Message);
            }
        }
        public void GenerateId(bool force = false)
        {
            if (string.IsNullOrEmpty(Id) || force)
            {
                Id = gameObject.GetInstanceID().ToString();
            }
        }
        public void ForceGenerateId()
        {
            Id = Convert.ToBase64String(Guid.NewGuid().ToByteArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        void OnDestroy()
        {
            mainThreadComponentMap.Clear();
            BanterScene.Instance().DestroyBanterObject(gameObject.GetInstanceID());
        }
    }
}
