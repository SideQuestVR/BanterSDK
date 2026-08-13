#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using System;

namespace Banter.VisualScripting
{
    [UnitTitle("Banter Synced Object Take Ownership")]
    [UnitShortTitle("Take Ownership")]
    [UnitCategory("Banter/Components/Banter Synced Object")]
    [Obsolete("Use BSSyncedObject _TakeOwnership()")]
    [TypeIcon(typeof(BSSyncedObject))]
    [RenamedFrom("Banter.VisualScripting.BanterSyncedObjectTakeOwnership")]
    public class BSSyncedObjectTakeOwnership : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        [DoNotSerialize]
        [NullMeansSelf]
        public ValueInput syncedObject;

        protected override void Definition()
        {
            inputTrigger = ControlInput("", (flow) => {
                var syncObject = flow.GetValue<BSSyncedObject>(syncedObject);
                syncObject._TakeOwnership();

                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
            syncedObject = ValueInput<BSSyncedObject>("Sync Object", null);
            syncedObject.SetDefaultValue(null);
            syncedObject.NullMeansSelf();
        }
    }

    [UnitTitle("Banter Synced Object Is Owner")]
    [UnitShortTitle("Is Synced Object Owner")]
    [UnitCategory("Banter/Components/Banter Synced Object")]
    [Obsolete("Use BSSyncedObject _DoIOwn()")]
    [TypeIcon(typeof(BSSyncedObject))]
    [RenamedFrom("Banter.VisualScripting.BanterSyncedObjectDoIOwn")]
    public class BSSyncedObjectDoIOwn : Unit
    {
        [DoNotSerialize]
        [NullMeansSelf]
        public ValueInput syncedObject;

        [DoNotSerialize]
        public ValueOutput isOwner;

        protected override void Definition()
        {
            syncedObject = ValueInput<BSSyncedObject>("Sync Object", null);
            syncedObject.SetDefaultValue(null);
            syncedObject.NullMeansSelf();

            isOwner = ValueOutput<bool>("Is Owner", (flow) => {
                var syncObject = flow.GetValue<BSSyncedObject>(syncedObject);
                return syncObject._DoIOwn();
            });
        }
    }
}
#endif
