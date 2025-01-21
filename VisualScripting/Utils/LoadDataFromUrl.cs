#if BANTER_VISUAL_SCRIPTING
using UnityEngine;
using Unity.VisualScripting;
using Banter.SDK;
using UnityEngine.Networking;
using System.Collections;
using System.Runtime.Serialization;

namespace Banter.VisualScripting
{
    [UnitTitle("Load Texture from URL")]
    [UnitShortTitle("Load Texture")]
    [UnitCategory("Banter")]
    [TypeIcon(typeof(BanterObjectId))]
    public class LoadTextureUrl : Unit
    {
        [DoNotSerialize]
        public ValueInput url;

        [DoNotSerialize]
        public ValueOutput texture;

        [DoNotSerialize]
        [PortLabelHidden]
        public ControlInput input;

        [DoNotSerialize]
        [PortLabelHidden]
        public ControlOutput success;

        [DoNotSerialize]
        [PortLabelHidden]
        public ControlOutput failure;

        protected override void Definition()
        {
            input = ControlInputCoroutine("Load", (flow) => LoadTexture(flow));
            success = ControlOutput("Loaded");
            failure = ControlOutput("Failed");
            url = ValueInput<string>("URL");
            texture = ValueOutput<Texture2D>("Texture");
        }

        private IEnumerator LoadTexture(Flow flow)
        {
            var url = flow.GetValue<string>(this.url);

            using (var request = UnityWebRequestTexture.GetTexture(url))
            {
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    yield return failure;
                }

                var texture = DownloadHandlerTexture.GetContent(request);
                flow.SetValue(this.texture, texture);
                yield return success;
            }
        }
    }

    [UnitTitle("Load Audio from URL")]
    [UnitShortTitle("Load Audio")]
    [UnitCategory("Banter")]
    [TypeIcon(typeof(BanterObjectId))]
    public class LoadAudioUrl : Unit
    {
        [DoNotSerialize]
        public ValueInput url;

        [DoNotSerialize]
        public ValueInput audioType;

        [DoNotSerialize]
        public ValueOutput audio;

        [DoNotSerialize]
        [PortLabelHidden]
        public ControlInput input;

        [DoNotSerialize]
        [PortLabelHidden]
        public ControlOutput success;

        [DoNotSerialize]
        [PortLabelHidden]
        public ControlOutput failure;

        protected override void Definition()
        {
            input = ControlInputCoroutine("Load", (flow) => LoadAudioClip(flow));
            success = ControlOutput("Loaded");
            failure = ControlOutput("Failed");

            url = ValueInput<string>("URL");
            audioType = ValueInput("Audio Type", AudioType.UNKNOWN);
            audio = ValueOutput<AudioClip>("Audio Clip");
        }

        private IEnumerator LoadAudioClip(Flow flow)
        {
            var url = flow.GetValue<string>(this.url);
            var type = flow.GetValue<AudioType>(audioType);

            using (var request = UnityWebRequestMultimedia.GetAudioClip(url, type))
            {
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    yield return failure;
                }

                var clip = DownloadHandlerAudioClip.GetContent(request);
                flow.SetValue(audio, clip);
                yield return success;
            }
        }
    }

    [UnitTitle("Load glTF/glb from URL")]
    [UnitShortTitle("Load glTF")]
    [UnitCategory("Banter")]
    [TypeIcon(typeof(BanterObjectId))]
    public class LoadGltfUrl : Unit
    {
        [DoNotSerialize]
        public ValueInput url;

        [DoNotSerialize]
        public ValueInput gltfComponent;

        [DoNotSerialize]
        [PortLabelHidden]
        public ControlInput input;

        [DoNotSerialize]
        [PortLabelHidden]
        public ControlOutput output;

        protected override void Definition()
        {
            input = ControlInput("", (flow) => LoadGltf(flow));
            output = ControlOutput("");
            url = ValueInput<string>("URL", string.Empty);
            gltfComponent = ValueInput<BanterGLTF>("BanterGltf", null);
            gltfComponent.SetDefaultValue(null);
            gltfComponent.NullMeansSelf();
        }

        private ControlOutput LoadGltf(Flow flow)
        {
            var url = flow.GetValue<string>(this.url);
            var bGLTF = flow.GetValue<BanterGLTF>(gltfComponent);
            bGLTF._url = url;

            return output;
        }
    }
}
#endif
