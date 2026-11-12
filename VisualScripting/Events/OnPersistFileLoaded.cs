using Unity.VisualScripting;
using BS;

namespace BS.VisualScripting
{
    /// <summary>
    /// Fires when a <see cref="PersistFileGet"/> finishes, carrying the file's contents.
    /// </summary>
    /// <remarks>
    /// A file that has never been written reports <c>Success</c> true with empty <c>Data</c>: not
    /// existing yet is the normal first-run state, not a failure. A real failure — no world, no
    /// network — reports <c>Success</c> false with the reason.
    ///
    /// Leave <c>Name</c> empty to catch every read; set it to filter to one file.
    /// </remarks>
    [UnitTitle("On Persist File Loaded")]
    [UnitShortTitle("On Persist File Loaded")]
    [UnitCategory("Events\\BS\\World")]
    [TypeIcon(typeof(BSObjectId))]
    public class OnPersistFileLoaded : EventUnit<CustomEventArgs>
    {
        [DoNotSerialize]
        public ValueInput fileName;

        [DoNotSerialize]
        public ValueOutput loadedName;

        [DoNotSerialize]
        public ValueOutput success;

        [DoNotSerialize]
        public ValueOutput data;

        [DoNotSerialize]
        public ValueOutput error;

        protected override bool register => true;

        public override EventHook GetHook(GraphReference reference) => new EventHook("OnPersistFileLoaded");

        protected override void Definition()
        {
            base.Definition();
            fileName = ValueInput("Name", string.Empty);
            loadedName = ValueOutput<string>("Loaded Name");
            success = ValueOutput<bool>("Success");
            data = ValueOutput<string>("Data");
            error = ValueOutput<string>("Error");
        }

        protected override bool ShouldTrigger(Flow flow, CustomEventArgs eventData)
        {
            var wanted = flow.GetValue<string>(fileName)?.Trim();
            if (string.IsNullOrEmpty(wanted)) return true;
            return wanted == (eventData.arguments[0] as string);
        }

        protected override void AssignArguments(Flow flow, CustomEventArgs eventData)
        {
            flow.SetValue(loadedName, eventData.arguments[0]);
            flow.SetValue(success, eventData.arguments[1]);
            flow.SetValue(data, eventData.arguments[2]);
            flow.SetValue(error, eventData.arguments[3]);
        }
    }
}
