#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;

namespace Banter.VisualScripting
{
    [UnitTitle("Banter glTF is Loaded")]
    [UnitShortTitle("is glTF Loaded")]
    [UnitCategory("Banter/Components/Banter glTF")]
    [TypeIcon(typeof(BanterGLTF))]
    public class BanterGLTFIsLoaded : Unit
    {
        [PortLabelHidden]
        [DoNotSerialize]
        [NullMeansSelf]
        public ValueInput gltfObject;

        [DoNotSerialize]
        public ValueOutput isLoaded;

        protected override void Definition()
        {
            isLoaded = ValueOutput<bool>("Is Loaded", (flow) => {
                var gltfComp = flow.GetValue<BanterGLTF>(gltfObject);
                return gltfComp.ModelLoaded;
            });

            gltfObject = ValueInput(typeof(BanterGLTF), nameof(gltfObject));
            gltfObject.SetDefaultValue(null);
            gltfObject.NullMeansSelf();
        }
    }
}
#endif
