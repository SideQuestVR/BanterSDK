using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace BS.VideoStream.Tests
{
    /// <summary>
    /// The URL engine inside Unity: real fetcher, real native decoder and graphics device, real texture. The fixtures are a
    /// 320x240 clip whose top half is red and bottom half blue, with a tone, as MP4 and as MPEG-TS.
    /// </summary>
    public class VideoPlaybackTests
    {
        static string Fixtures => Path.GetFullPath("Packages/com.sidequest.creator-sdk/Tests/PlayMode/Video/Fixtures~");

        LoopbackServer _server;
        GameObject _host;
        VideoPlayback _playback;

        [SetUp]
        public void SetUp()
        {
            _server = new LoopbackServer(Fixtures);
            _host = new GameObject("Video test");
            _playback = new VideoPlayback(_host.transform);
        }

        [TearDown]
        public void TearDown()
        {
            _playback?.Dispose();
            UnityEngine.Object.Destroy(_host);
            _server?.Dispose();
        }

        IEnumerator Run(Func<bool> until, double seconds, string what)
        {
            var start = Time.realtimeSinceStartupAsDouble;
            while (!until())
            {
                if (_playback.State == VideoPlaybackState.Failed)
                {
                    Assert.Fail($"failed while waiting for {what}: {_playback.Failure}");
                }
                if (Time.realtimeSinceStartupAsDouble - start > seconds)
                {
                    Assert.Fail($"timed out waiting for {what}: state {_playback.State}, time {_playback.Time:F2}, frames {_playback.FramesShown}");
                }
                _playback.Tick();
                yield return null;
            }
        }

        IEnumerator Hold(double seconds)
        {
            var start = Time.realtimeSinceStartupAsDouble;
            while (Time.realtimeSinceStartupAsDouble - start < seconds)
            {
                _playback.Tick();
                yield return null;
            }
        }

        static void Describe(string label, VideoPlayback playback)
        {
            Debug.Log($"[VideoPlaybackTests] {label}: platform '{VideoDecoderNative.GetText(0, VideoDecoderNative.TextPlatform)}', path {VideoDecoderNative.Path}, " +
                      $"state {playback.State}, time {playback.Time:F2}/{playback.Duration:F2}, frames {playback.FramesShown}, " +
                      $"texture {(playback.Texture != null ? playback.Texture.width + "x" + playback.Texture.height : "none")}");
        }

        // Unity rows are bottom first: the picture's blue bottom half is in the first rows, its red top half in the last.
        IEnumerator CheckPicture()
        {
            var texture = _playback.Texture;
            Assert.That(texture, Is.Not.Null, "a texture");
            var request = AsyncGPUReadback.Request(texture, 0, TextureFormat.RGBA32);
            while (!request.done)
            {
                _playback.Tick();
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
            Debug.Log($"[VideoPlaybackTests] bottom-half pixel {bottom}, top-half pixel {top}");
            Assert.That(bottom.b, Is.GreaterThan(240), "blue at the bottom");
            Assert.That(bottom.r, Is.LessThan(16));
            Assert.That(top.r, Is.GreaterThan(240), "red at the top");
            Assert.That(top.b, Is.LessThan(16));
        }

        [UnityTest]
        public IEnumerator An_mp4_plays_upright_seeks_and_ends()
        {
            _playback.Open(_server.Url("redblue.mp4"), new VideoPlaybackSettings(), true);
            var ended = false;
            _playback.Ended += () => ended = true;
            yield return Run(() => _playback.IsPrepared, 15, "the first picture");
            Describe("prepared", _playback);
            Assert.That(_playback.Duration, Is.EqualTo(4.0).Within(0.1));
            yield return Run(() => _playback.Time >= 1.0, 8, "a second of playing");
            Describe("playing", _playback);
            yield return CheckPicture();

            _playback.Seek(3.0);
            yield return Run(() => _playback.Time >= 3.0, 8, "the seek");
            yield return Run(() => ended, 8, "the end");
            Describe("ended", _playback);
            Assert.That(_playback.IsPlaying, Is.False);
            Assert.That(_playback.Time, Is.EqualTo(4.0).Within(0.1));
        }

        [UnityTest]
        public IEnumerator A_pause_holds_the_picture_and_play_carries_on()
        {
            _playback.Open(_server.Url("redblue.mp4"), new VideoPlaybackSettings(), true);
            yield return Run(() => _playback.Time >= 0.5, 15, "playing");
            _playback.Pause();
            var at = _playback.Time;
            yield return Hold(1.0);
            Assert.That(_playback.Time, Is.EqualTo(at).Within(0.05), "the time holds while paused");
            Assert.That(_playback.IsPlaying, Is.False);
            _playback.Play();
            yield return Run(() => _playback.Time >= at + 0.5, 8, "playing again");
            Describe("resumed", _playback);
            yield return CheckPicture();
        }

        [UnityTest]
        public IEnumerator A_transport_stream_plays()
        {
            _playback.Open(_server.Url("redblue.ts"), new VideoPlaybackSettings(), true);
            yield return Run(() => _playback.IsPrepared, 15, "the first picture");
            yield return Run(() => _playback.Time >= 1.0, 8, "a second of playing");
            Describe("ts", _playback);
            yield return CheckPicture();
        }

        [UnityTest]
        public IEnumerator Prepared_without_playing_shows_the_first_picture()
        {
            _playback.Open(_server.Url("redblue.mp4"), new VideoPlaybackSettings(), false);
            yield return Run(() => _playback.IsPrepared, 15, "the first picture");
            yield return Hold(0.5);
            Assert.That(_playback.Time, Is.EqualTo(0).Within(0.05));
            Assert.That(_playback.IsPlaying, Is.False);
            yield return CheckPicture();
        }
    }
}
