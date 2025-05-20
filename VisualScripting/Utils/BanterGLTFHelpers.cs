#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using System;

namespace Banter.VisualScripting
{
    [UnitTitle("Banter glTF is Loaded")]
    [UnitShortTitle("is glTF Loaded")]
    [UnitCategory("Banter/Components/Banter glTF")]
    [Obsolete("Use BanterGLTF IsLoaded instead")]
    [TypeIcon(typeof(BanterGLTF))]
    public class BanterGLTFIsLoaded : Unit
    {
        [DoNotSerialize]
        [NullMeansSelf]
        public ValueInput gltfObject;

        [DoNotSerialize]
        public ValueOutput isLoaded;

        protected override void Definition()
        {
            isLoaded = ValueOutput<bool>("Is Loaded", (flow) => {
                var gltfComp = flow.GetValue<BanterGLTF>(gltfObject);
                return gltfComp.IsLoaded;
            });

            gltfObject = ValueInput<BanterGLTF>("BanterGltf", null);
            gltfObject.SetDefaultValue(null);
            gltfObject.NullMeansSelf();
        }
    }
}
#endif
