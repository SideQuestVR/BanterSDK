
using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.VisualScripting;

namespace BS
{
    [RenamedFrom("Banter.SDK.BanterMonoBehaviourLifeCycle")]
    public enum BSMonoBehaviourLifeCycle
    {
        Start,
        Update,
        OnDestroy
    }

    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]
    public partial class BSMonoBehaviour : BSComponentBase
    {
        [See(initial = "20")][SerializeField] internal int fps = 20;

        [See(initial = "")][SerializeField] internal string startFunction = "";
        [See(initial = "")][SerializeField] internal string updateFunction = "";
        [See(initial = "")][SerializeField] internal string destroyFunction = "";

        internal override void StartStuff()
        {
            // Nothing to load. It never reported loaded, so every one held its space's load gate for good.
            SetLoadedIfNot();
        }

        internal override void DestroyStuff()
        {

        }

        internal override void UpdateStuff()
        {

        }
        internal void UpdateCallback(List<PropertyName> changedProperties)
        {

        }
    }
}