#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;

namespace Banter.VisualScripting
{
    [UnitTitle("Banter Synced Object Take Ownership")]
    [UnitShortTitle("Take Ownership")]
    [UnitCategory("Banter/Components/Banter Synced Object")]
    [TypeIcon(typeof(BanterSyncedObject))]
    public class BanterSyncedObjectTakeOwnership : Unit
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
                var syncObject = flow.GetValue<BanterSyncedObject>(syncedObject);
                syncObject._TakeOwnership();

                return outputTrigger;
            });
            outputTrigger = ControlOutput("");
            syncedObject = ValueInput<BanterSyncedObject>("Sync Object", null);
            syncedObject.SetDefaultValue(null);
            syncedObject.NullMeansSelf();
        }
    }

    [UnitTitle("Banter Synced Object Is Owner")]
    [UnitShortTitle("Is Synced Object Owner")]
    [UnitCategory("Banter/Components/Banter Synced Object")]
    [TypeIcon(typeof(BanterSyncedObject))]
    public class BanterSyncedObjectDoIOwn : Unit
    {
        [DoNotSerialize]
        [NullMeansSelf]
        public ValueInput syncedObject;

        [DoNotSerialize]
        public ValueOutput isOwner;

        protected override void Definition()
        {
            syncedObject = ValueInput<BanterSyncedObject>("Sync Object", null);
            syncedObject.SetDefaultValue(null);
            syncedObject.NullMeansSelf();

            isOwner = ValueOutput<bool>("Is Owner", (flow) => {
                var syncObject = flow.GetValue<BanterSyncedObject>(syncedObject);
                syncObject._DoIOwn();
                return syncObject.doIOwn;
            });
        }
    }
}
#endif
