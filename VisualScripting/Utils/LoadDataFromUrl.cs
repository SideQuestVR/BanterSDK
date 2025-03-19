#if BANTER_VISUAL_SCRIPTING
using UnityEngine;
using Unity.VisualScripting;
using Banter.SDK;
using UnityEngine.Networking;
using System.Collections;
using System.Runtime.Serialization;
using System;

namespace Banter.VisualScripting
{
    [UnitTitle("Load Texture from URL")]
    [UnitShortTitle("Load Texture")]
    [UnitCategory("Banter\\Networking")]
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
            url = ValueInput<string>("URL", string.Empty);
            generateMipmaps = ValueInput<bool>("Generate Mipmaps", true);
            texture = ValueOutput<Texture2D>("Texture");
        }

        private IEnumerator LoadTexture(Flow flow)
        {
            var url = flow.GetValue<string>(this.url);
            var genMipmaps = flow.GetValue<bool>(this.generateMipmaps);

            using (var request = UnityWebRequestTexture.GetTexture(url))
            {
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    yield return failure;
                }

                var texture = DownloadHandlerTexture.GetContent(request);

                if (!genMipmaps && texture.mipmapCount > 1)
                {
                    // Create a new texture without mipmaps
                    Texture2D noMipTexture = new Texture2D(texture.width, texture.height, texture.format, false);
                    noMipTexture.SetPixels(texture.GetPixels());
                    noMipTexture.Apply(false); // false = don't generate mipmaps
                    texture = noMipTexture;
                }
                flow.SetValue(this.texture, texture);
                yield return success;
            }
        }
    }

    [UnitTitle("Load Text from URL")]
    [UnitShortTitle("Load Text")]
    [UnitCategory("Banter\\Networking")]
    [TypeIcon(typeof(BanterObjectId))]
    public class LoadTextUrl : Unit
    {
        [DoNotSerialize]
        public ValueInput url;
        [DoNotSerialize]
        public ValueInput method;
        [DoNotSerialize]
        public ValueInput body;
        [DoNotSerialize]
        public ValueInput contentType;

        [DoNotSerialize]
        public ValueOutput text;

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
            input = ControlInputCoroutine("Load", (flow) => LoadText(flow));
            success = ControlOutput("Loaded");
            failure = ControlOutput("Failed");
            url = ValueInput<string>("URL", string.Empty);
            method = ValueInput<string>("Method", "GET");
            body = ValueInput<string>("Body", string.Empty);
            contentType = ValueInput<string>("ContentType", "application/json");
            text = ValueOutput<string>("Text");
        }

        private IEnumerator LoadText(Flow flow)
        {
            var url = flow.GetValue<string>(this.url);
            var method = flow.GetValue<string>(this.method);

            if(!url.StartsWith("http://") && !url.StartsWith("https://")) {
                yield return failure;
                yield break;
            }

            using (var request = method == "POST" ? UnityWebRequest.Put(url, flow.GetValue<string>(body)) : UnityWebRequest.Get(url))
            {
                if(method == "POST") {
                    request.SetRequestHeader("Content-Type", flow.GetValue<string>(this.contentType));
                }
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    yield return failure;
                }else{
                    flow.SetValue(this.text, request.downloadHandler.text);
                    yield return success;
                }
            }
        }
    }

    [UnitTitle("Load Audio from URL")]
    [UnitShortTitle("Load Audio")]
    [UnitCategory("Banter\\Networking")]
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

            url = ValueInput<string>("URL", string.Empty);
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
    [Obsolete("Use BanterGLTF Set Url")]
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
            bGLTF.Url = url;

            return output;
        }
    }
}
#endif
