using Unity.VisualScripting;
using BS;

namespace BS.VisualScripting
{
    /// <summary>
    /// Fires when a <see cref="PersistFileSet"/> finishes, successfully or not.
    /// </summary>
    /// <remarks>
    /// Leave <c>Name</c> empty to catch every save; set it to the same bare name the write used to
    /// catch only that file. Matching is on the name the graph passed, not the stored
    /// <c>__persisted_*.json</c> one, so a graph never has to know about the prefix.
    /// </remarks>
    [UnitTitle("On Persist File Saved")]
    [UnitShortTitle("On Persist File Saved")]
    [UnitCategory("Events\\BS\\World")]
    [TypeIcon(typeof(BSObjectId))]
    public class OnPersistFileSaved : EventUnit<CustomEventArgs>
    {
        [DoNotSerialize]
        public ValueInput fileName;

        [DoNotSerialize]
        public ValueOutput savedName;

        [DoNotSerialize]
        public ValueOutput success;

        [DoNotSerialize]
        public ValueOutput error;

        protected override bool register => true;

        public override EventHook GetHook(GraphReference reference) => new EventHook("OnPersistFileSaved");

        protected override void Definition()
        {
            base.Definition();
            fileName = ValueInput("Name", string.Empty);
            savedName = ValueOutput<string>("Saved Name");
            success = ValueOutput<bool>("Success");
            error = ValueOutput<string>("Error");
        }

        protected override bool ShouldTrigger(Flow flow, CustomEventArgs data)
        {
            var wanted = flow.GetValue<string>(fileName)?.Trim();
            if (string.IsNullOrEmpty(wanted)) return true;
            return wanted == (data.arguments[0] as string);
        }

        protected override void AssignArguments(Flow flow, CustomEventArgs data)
        {
            flow.SetValue(savedName, data.arguments[0]);
            flow.SetValue(success, data.arguments[1]);
            flow.SetValue(error, data.arguments[3]);
        }
    }
}
