using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using UnityEngine.Video;
using BS.Utilities.Async;
using Object = UnityEngine.Object;

namespace BS.VideoStream.Tests
{
    /// <summary>
    /// BSVideoPlayer on the URL engine, as a world uses it: a scene with a link but no page, the component on an object,
    /// and the same red-over-blue fixtures as <see cref="VideoPlaybackTests"/> from a loopback server. Embedded clips need
    /// a VideoClip asset and aren't covered here; the switch between a creator's Video Player and the engine is.
    /// </summary>
    public class BSVideoPlayerTests
    {
        static string Fixtures => Path.GetFullPath("Packages/com.sidequest.creator-sdk/Tests/PlayMode/Video/Fixtures~");
        static readonly FieldInfo EngineField = typeof(BSVideoPlayer).GetField("_engine", BindingFlags.Instance | BindingFlags.NonPublic);

        LoopbackServer _server;
        BSScene _scene;
        BSLink _previousLink;
        GameObject _linkObject;
        GameObject _host;
        RenderTexture _renderTexture;
        Material _material;
        Texture2D _authored;

        [SetUp]
        public void SetUp()
        {
            _server = new LoopbackServer(Fixtures);
            _scene = BSScene.Instance();
            _previousLink = _scene.link;
            // Messages for a page queue on a link that has none.
            _linkObject = new GameObject("Video test link");
            _scene.link = _linkObject.AddComponent<BSLink>();
            _scene.link.scene = _scene;
            // The sound plays to something, and Unity doesn't complain every frame that nothing hears it.
            if (Object.FindAnyObjectByType<AudioListener>() == null)
            {
                _linkObject.AddComponent<AudioListener>();
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null)
            {
                Object.Destroy(_host);
            }
            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Object.Destroy(_renderTexture);
            }
            if (_material != null)
            {
                Object.Destroy(_material);
            }
            if (_authored != null)
            {
                Object.Destroy(_authored);
            }
            _scene.link = _previousLink;
            Object.Destroy(_linkObject);
            _server?.Dispose();
        }

        static VideoPlayback Engine(BSVideoPlayer player) => EngineField.GetValue(player) as VideoPlayback;

        // Frames go by with the component's own Update ticking the engine; BSScene's legacy calls wait on the main-thread
        // queue, which nothing else runs in a test scene.
        static IEnumerator Until(BSVideoPlayer player, Func<bool> until, double seconds, string what)
        {
            var start = Time.realtimeSinceStartupAsDouble;
            while (!until())
            {
                var engine = player != null ? Engine(player) : null;
                if (engine != null && engine.State == VideoPlaybackState.Failed)
                {
                    Assert.Fail($"failed while waiting for {what}: {engine.Failure}");
                }
                if (Time.realtimeSinceStartupAsDouble - start > seconds)
                {
                    Assert.Fail($"timed out waiting for {what}: " + (engine != null
                        ? $"state {engine.State}, time {engine.Time:F2}, frames {engine.FramesShown}"
                        : "no engine"));
                }
                if (UnityMainThreadTaskScheduler.Default.QueuedCount > 0)
                {
                    UnityMainThreadTaskScheduler.Default.RunPass();
                }
                yield return null;
            }
        }

        static IEnumerator Frames(double seconds)
        {
            var start = Time.realtimeSinceStartupAsDouble;
            while (Time.realtimeSinceStartupAsDouble - start < seconds)
            {
                yield return null;
            }
        }

        // Rows are bottom first: the picture's blue bottom half is in the first rows, its red top half in the last.
        static IEnumerator CheckPicture(Texture texture, string where)
        {
            Assert.That(texture, Is.Not.Null, $"a picture in {where}");
            var request = AsyncGPUReadback.Request(texture, 0, TextureFormat.RGBA32);
            while (!request.done)
            {
                yield return null;
            }
            Assert.That(request.hasError, Is.False, "readback");
            var data = request.GetData<byte>();
            Color32 At(int x, int y)
            {
                var i = (y * texture.width + x) * 4;
                return new Color32(data[i], data[i + 1], data[i + 2], data[i + 3]);
            }
            var bottom = At(texture.width / 2, texture.height / 4);
            var top = At(texture.width / 2, texture.height * 3 / 4);
            Debug.Log($"[BSVideoPlayerTests] {where}: bottom-half pixel {bottom}, top-half pixel {top}");
            Assert.That(bottom.b, Is.GreaterThan(240), $"blue at the bottom of {where}");
            Assert.That(bottom.r, Is.LessThan(16));
            Assert.That(top.r, Is.GreaterThan(240), $"red at the top of {where}");
            Assert.That(top.b, Is.LessThan(16));
        }

        // A quad with URP's Lit, which shows _BaseMap and keeps an unused _MainTex, and an authored texture on it.
        MeshRenderer Screen()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Assert.Ignore("URP's Lit shader isn't in this project.");
            }
            _host = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _host.name = "Video test screen";
            _host.SetActive(false);
            _authored = new Texture2D(2, 2) { name = "authored" };
            _material = new Material(shader);
            _material.SetTexture("_BaseMap", _authored);
            var renderer = _host.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            return renderer;
        }

        static Texture Shown(Renderer renderer, string property)
        {
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            return block.GetTexture(property);
        }

        [UnityTest]
        public IEnumerator A_url_plays_on_the_engine_onto_the_renderer_and_clearing_it_restores_the_material()
        {
            var renderer = Screen();
            var player = _host.AddComponent<BSVideoPlayer>();
            // As placed in the Inspector: Start applies the fields.
            player.url = _server.Url("redblue.mp4");
            _host.SetActive(true);

            yield return Until(player, () => player.isPrepared && player.time >= 1.0f, 15, "a second of playing");
            Assert.That(player.isPlaying, Is.True);
            Assert.That(player.duration, Is.EqualTo(4.0f).Within(0.1f));
            var picture = player.Texture;
            Assert.That(picture, Is.Not.Null, "the engine's texture");
            Assert.That(Shown(renderer, "_BaseMap"), Is.SameAs(picture), "the picture on Lit's main texture");
            Assert.That(Shown(renderer, "_MainTex"), Is.SameAs(picture), "and on the property a Video Player would set");
            Assert.That(BSAssetRegistry.Instance.GetAsset<Texture2D>(player.TextureAssetId), Is.SameAs(picture), "published as " + player.TextureAssetId);
            yield return CheckPicture(picture, "the engine's texture");

            player._PlayToggle();
            yield return Frames(0.3);
            Assert.That(player.isPlaying, Is.False, "_PlayToggle pauses");
            player._PlayToggle();
            yield return Until(player, () => player.isPlaying, 5, "_PlayToggle to play again");

            player.Url = "";
            yield return null;
            Assert.That(player.isPlaying, Is.False, "clearing the url stops it");
            Assert.That(player.isPrepared, Is.False);
            Assert.That(player.time, Is.EqualTo(0f));
            yield return Frames(0.3);
            Assert.That(player.isPlaying, Is.False, "and it stays reported stopped");
            Assert.That(Shown(renderer, "_BaseMap"), Is.SameAs(_authored), "the authored texture back");
            Assert.That(BSAssetRegistry.Instance.GetAsset<Texture2D>(player.TextureAssetId), Is.Null, "unpublished");
        }

        [UnityTest]
        public IEnumerator The_legacy_url_call_plays_and_reports_prepared()
        {
            Screen();
            var player = _host.AddComponent<BSVideoPlayer>();
            player.playOnAwake = false;
            _host.SetActive(true);
            yield return null;
            Assert.That(Engine(player), Is.Null, "no url, no engine");

            string prepared = null;
            void OnPrepared(string id) => prepared = id;
            _scene.events.OnVideoPrepareCompleted.AddListener(OnPrepared);
            try
            {
                _scene.LegacySetVideoUrl(_host, _server.Url("redblue.ts"), "screen-1");
                yield return Until(player, () => prepared != null, 15, "OnVideoPrepareCompleted");
                Assert.That(prepared, Is.EqualTo("screen-1"));
                yield return Until(player, () => player.time >= 1.0f, 8, "a second of playing");
                Assert.That(player.isPlaying, Is.True, "the legacy call plays, whatever playOnAwake says");
                yield return CheckPicture(player.Texture, "the engine's texture");
            }
            finally
            {
                _scene.events.OnVideoPrepareCompleted.RemoveListener(OnPrepared);
            }
        }

        [UnityTest]
        public IEnumerator A_creators_video_player_with_a_url_gives_its_render_texture_and_stays_off()
        {
            _host = new GameObject("Video test with a Video Player");
            _host.SetActive(false);
            _renderTexture = new RenderTexture(320, 240, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            _renderTexture.Create();
            var clip = _host.AddComponent<VideoPlayer>();
            clip.source = VideoSource.Url;
            clip.url = _server.Url("redblue.mp4");
            clip.renderMode = VideoRenderMode.RenderTexture;
            clip.targetTexture = _renderTexture;
            clip.playOnAwake = true;
            clip.isLooping = true;
            var player = _host.AddComponent<BSVideoPlayer>();
            _host.SetActive(true);

            yield return Until(player, () => player.isPrepared && player.time >= 1.0f, 15, "a second of playing");
            Assert.That(clip.enabled, Is.False, "the creator's Video Player is off");
            Assert.That(clip.isPlaying, Is.False, "and isn't playing the url too");
            Assert.That(player.url, Is.EqualTo(clip.url), "the url read from it");
            Assert.That(player.loop, Is.True, "its loop setting read back");
            Assert.That(Engine(player).Loop, Is.True, "and given to the engine");
            yield return CheckPicture(_renderTexture, "the creator's Render Texture");
        }

        [UnityTest]
        public IEnumerator Without_a_url_the_creators_video_player_stays_in_charge_and_gets_it_back_when_the_url_goes()
        {
            var renderer = Screen();
            var clip = _host.AddComponent<VideoPlayer>();
            clip.source = VideoSource.VideoClip;
            clip.playOnAwake = false;
            clip.renderMode = VideoRenderMode.MaterialOverride;
            clip.targetMaterialRenderer = renderer;
            clip.targetMaterialProperty = "_BaseMap";
            var player = _host.AddComponent<BSVideoPlayer>();
            _host.SetActive(true);
            yield return Frames(0.2);
            Assert.That(clip.enabled, Is.True, "a clip player with no url is left alone");
            Assert.That(Engine(player), Is.Null, "no engine");

            player.Url = _server.Url("redblue.mp4");
            yield return Until(player, () => player.isPrepared, 15, "the first picture");
            Assert.That(clip.enabled, Is.False, "off while the url plays");
            Assert.That(Shown(renderer, "_BaseMap"), Is.SameAs(player.Texture), "the picture on the property the Video Player named");

            player.Url = "";
            yield return null;
            Assert.That(clip.enabled, Is.True, "the clip player back");
            Assert.That(clip.isPlaying, Is.False, "stopped, as clearing the url always left it");
            Assert.That(player.Texture, Is.Null);
            Assert.That(Shown(renderer, "_BaseMap"), Is.SameAs(_authored), "the authored texture back");
        }
    }
}
