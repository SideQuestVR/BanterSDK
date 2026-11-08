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
    public partial class BSScriptGraph : BSComponentBase
    {
        [Tooltip("Number of ScriptMachines on this object.")]
        [See(initial = "0")][SerializeField] internal int machineCount = 0;

        [Tooltip("Comma-separated graph titles of the ScriptMachines on this object, by machine index.")]
        [See(initial = "")][SerializeField] internal string graphTitles = "";

        [Method]
        public void _CreateMachine()
        {
            var machine = gameObject.AddComponent<ScriptMachine>();
            machine.nest.SwitchToEmbed(FlowGraph.WithStartUpdate());
            GraphEditPermission.NotePageMade(machine);
            RefreshMachinesInternal();
        }

        [Method]
        public void _RemoveMachine(int machineIndex)
        {
            var machines = GetComponents<ScriptMachine>();
            if (machineIndex >= 0 && machineIndex < machines.Length)
            {
                // The world's own machines are its owner's to remove (GraphEditPermission); the page's own are its.
                if (!GraphEditPermission.MayChange(machines[machineIndex]))
                {
                    GraphEditPermission.WarnOnce("removing one of the world's own machines");
                    return;
                }
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
            // Nothing to load: reported once the machines are summarised, and even if that throws, so it can't hold the
            // space's load gate (it never reported, and held it for good).
            try
            {
                RefreshMachinesInternal();
            }
            finally
            {
                SetLoadedIfNot();
            }
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
    }
}