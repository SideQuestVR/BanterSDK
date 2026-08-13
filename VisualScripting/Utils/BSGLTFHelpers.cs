#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using BS;
using System;

namespace BS.VisualScripting
{
    [UnitTitle("Banter glTF is Loaded")]
    [UnitShortTitle("is glTF Loaded")]
    [UnitCategory("Banter/Components/Banter glTF")]
    [Obsolete("Use BSGLTF IsLoaded instead")]
    [TypeIcon(typeof(BSGLTF))]
    [RenamedFrom("Banter.VisualScripting.BanterGLTFIsLoaded")]
    public class BSGLTFIsLoaded : Unit
    {
        [DoNotSerialize]
        [NullMeansSelf]
        public ValueInput gltfObject;

        [DoNotSerialize]
        public ValueOutput isLoaded;

        protected override void Definition()
        {
            isLoaded = ValueOutput<bool>("Is Loaded", (flow) => {
                var gltfComp = flow.GetValue<BSGLTF>(gltfObject);
                return gltfComp.IsLoaded;
            });

            gltfObject = ValueInput<BSGLTF>("BSGLTF", null);
            gltfObject.SetDefaultValue(null);
            gltfObject.NullMeansSelf();
        }
    }
}
#endif
