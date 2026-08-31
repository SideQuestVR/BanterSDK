using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.VisualScripting;

namespace BS
{
    /// <summary>
    /// Anchor component for runtime script-graph editing. Adopts the ScriptMachines on its
    /// object and mirrors a small summary to JS; the heavy graph traffic (view-models, op
    /// batches, envelopes) travels scene-level over the SCRIPT_GRAPH command, not through
    /// component properties.
    /// </summary>
    [DefaultExecutionOrder(-1)]
    [RequireComponent(typeof(BSObjectId))]
    [WatchComponent]
    public class BSScriptGraph : BSComponentBase
    {
        [Tooltip("Number of ScriptMachines on this object.")]
        [See(initial = "0")][SerializeField] internal int machineCount;

        [Tooltip("Comma-separated graph titles of the ScriptMachines on this object, by machine index.")]
        [See(initial = "")][SerializeField] internal string graphTitles = "";

        [Method]
        public void _CreateMachine()
        {
            var machine = gameObject.AddComponent<ScriptMachine>();
            machine.nest.SwitchToEmbed(FlowGraph.WithStartUpdate());
            RefreshMachinesInternal();
        }

        [Method]
        public void _RemoveMachine(int machineIndex)
        {
            var machines = GetComponents<ScriptMachine>();
            if (machineIndex >= 0 && machineIndex < machines.Length)
            {
                Destroy(machines[machineIndex]);
                // StartCoroutine throws on an inactive GameObject; the immediate refresh
                // then briefly counts the dying machine, which the deferred one corrects.
                if (isActiveAndEnabled)
                {
                    StartCoroutine(RefreshAfterDestroy());
                }
                else
                {
                    RefreshMachinesInternal();
                }
            }
        }

        [Method]
        public void _RefreshMachines()
        {
            RefreshMachinesInternal();
        }

        IEnumerator RefreshAfterDestroy()
        {
            // Destroy is deferred to end of frame; refresh once the component is really gone.
            yield return null;
            RefreshMachinesInternal();
        }

        void RefreshMachinesInternal()
        {
            var machines = GetComponents<ScriptMachine>();
            machineCount = machines.Length;
#if GREENFIELD_PROJECT
            graphTitles = string.Join(",", machines.Select(MachineDirectory.FriendlyTitle));
#endif
            SyncProperties(true);
        }

        internal override void StartStuff()
        {
            RefreshMachinesInternal();
        }

        internal override void DestroyStuff()
        {
#if GREENFIELD_PROJECT
            ScriptGraphSessionManager.Instance.CloseSessionsFor(GetComponent<BSObjectId>()?.Id);
#endif
        }

        internal override void UpdateStuff()
        {

        }
        internal void UpdateCallback(List<PropertyName> changedProperties)
        {

        }
        // BANTER COMPILED CODE 
        public System.Int32 MachineCount { get { return machineCount; } set { machineCount = value; UpdateCallback(new List<PropertyName> { PropertyName.machineCount }); } }
        public System.String GraphTitles { get { return graphTitles; } set { graphTitles = value; UpdateCallback(new List<PropertyName> { PropertyName.graphTitles }); } }

        BSScene _scene;
        public BSScene scene
        {
            get
            {
                if (_scene == null)
                {
                    _scene = BSScene.Instance();
                }
                return _scene;
            }
        }
        bool alreadyStarted = false;
        void Start()
        {
            Init();
            StartStuff();
        }

        internal override void ReSetup()
        {
            List<PropertyName> changedProperties = new List<PropertyName>() { PropertyName.machineCount, PropertyName.graphTitles, };
            UpdateCallback(changedProperties);
        }
        internal override string GetSignature()
        {
            return "ScriptGraph" +  PropertyName.machineCount + machineCount + PropertyName.graphTitles + graphTitles;
        }

        internal override void Init(List<object> constructorProperties = null)
        {
            if (alreadyStarted) { return; }
            alreadyStarted = true;
            scene.RegisterBanterMonoscript(gameObject.GetInstanceID(), GetInstanceID(), ComponentType.ScriptGraph);


            oid = gameObject.GetInstanceID();
            cid = GetInstanceID();

            if (constructorProperties != null)
            {
                Deserialise(constructorProperties);
            }

            SyncProperties(true);

        }

        void Awake()
        {
            BSScene.Instance().RegisterComponentOnMainThread(gameObject, this);
        }

        void OnDestroy()
        {
            scene.UnregisterComponentOnMainThread(gameObject, this);

            DestroyStuff();
        }

        void CreateMachine()
        {
            _CreateMachine();
        }
        void RemoveMachine(Int32 machineIndex)
        {
            _RemoveMachine(machineIndex);
        }
        void RefreshMachines()
        {
            _RefreshMachines();
        }
        internal override object CallMethod(string methodName, List<object> parameters)
        {

            if (methodName == "CreateMachine" && parameters.Count == 0)
            {
                CreateMachine();
                return null;
            }
            else if (methodName == "RemoveMachine" && parameters.Count == 1 && parameters[0] is Int32)
            {
                var machineIndex = (Int32)parameters[0];
                RemoveMachine(machineIndex);
                return null;
            }
            else if (methodName == "RefreshMachines" && parameters.Count == 0)
            {
                RefreshMachines();
                return null;
            }
            else
            {
                return null;
            }
        }

        internal override void Deserialise(List<object> values)
        {
            List<PropertyName> changedProperties = new List<PropertyName>();
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] is BSInt)
                {
                    var valmachineCount = (BSInt)values[i];
                    if (valmachineCount.n == PropertyName.machineCount)
                    {
                        machineCount = valmachineCount.x;
                        changedProperties.Add(PropertyName.machineCount);
                    }
                }
                if (values[i] is BSString)
                {
                    var valgraphTitles = (BSString)values[i];
                    if (valgraphTitles.n == PropertyName.graphTitles)
                    {
                        graphTitles = valgraphTitles.x;
                        changedProperties.Add(PropertyName.graphTitles);
                    }
                }
            }
            if (values.Count > 0) { UpdateCallback(changedProperties); }
        }

        internal override void SyncProperties(bool force = false, Action callback = null)
        {
            var updates = new List<BSComponentPropertyUpdate>();
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.machineCount,
                    type = PropertyType.Int,
                    value = machineCount,
                    componentType = ComponentType.ScriptGraph,
                    oid = oid,
                    cid = cid
                });
            }
            if (force)
            {
                updates.Add(new BSComponentPropertyUpdate()
                {
                    name = PropertyName.graphTitles,
                    type = PropertyType.String,
                    value = graphTitles,
                    componentType = ComponentType.ScriptGraph,
                    oid = oid,
                    cid = cid
                });
            }
            scene.SetFromUnityProperties(updates, callback);
        }

        internal override void WatchProperties(PropertyName[] properties)
        {
        }
        // END BANTER COMPILED CODE 
    }
}