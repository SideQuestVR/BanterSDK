using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Stopwatch = System.Diagnostics.Stopwatch;
using Object = UnityEngine.Object;

namespace BS.VideoStream.Tests
{
    /// <summary>
    /// Picture against sound, with a clapper: from 2 s, every whole second has one white frame and a 20 ms click. For each
    /// flash, the time its frame was first drawn is compared with the time its click went through the Audio Source plus the
    /// output's buffer queue, which is when the engine takes it to leave the speakers. What this can't see: the display's
    /// own delay, and how far the real device's latency differs from the buffer queue; those need a camera and a microphone.
    /// </summary>
    public class AvSyncTests
    {
        static string Fixtures => Path.GetFullPath("Packages/com.sidequest.creator-sdk/Tests/PlayMode/Video/Fixtures~");

        // Proposed bar (plan, V4): within 45 ms either way, each flash.
        const double LimitMs = 45;

        sealed class ClickProbe : MonoBehaviour
        {
            readonly List<long> _onsets = new List<long>();
            int _rate;
            int _hold;

            void Awake() => _rate = AudioSettings.outputSampleRate;

            // Runs as the mixer takes this source's block. A sample's time is the call's time plus its place in the block.
            void OnAudioFilterRead(float[] data, int channels)
            {
                var now = Stopwatch.GetTimestamp();
                var frames = data.Length / channels;
                for (var i = 0; i < frames; i++)
                {
                    if (_hold > 0)
                    {
                        _hold--;
                        continue;
                    }
                    var peak = 0f;
                    for (var c = 0; c < channels; c++)
                    {
                        peak = Math.Max(peak, Math.Abs(data[i * channels + c]));
                    }
                    if (peak > 0.2f)
                    {
                        lock (_onsets)
                        {
                            _onsets.Add(now + (long)((double)i / _rate * Stopwatch.Frequency));
                        }
                        // One onset per click: the burst is 20 ms, the next click a second away.
                        _hold = _rate / 4;
                    }
                }
            }

            public long[] Onsets()
            {
                lock (_onsets)
                {
                    return _onsets.ToArray();
                }
            }
        }

        static readonly FieldInfo StatusField = typeof(VideoPlayback).GetField("_status", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo SessionField = typeof(VideoPlayback).GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic);

        LoopbackServer _server;
        GameObject _host;
        GameObject _listener;
        VideoPlayback _playback;

        [SetUp]
        public void SetUp()
        {
            _server = new LoopbackServer(Fixtures);
            _host = new GameObject("A/V sync test");
            if (Object.FindAnyObjectByType<AudioListener>() == null)
            {
                _listener = new GameObject("Listener", typeof(AudioListener));
            }
            _playback = new VideoPlayback(_host.transform);
        }

        [TearDown]
        public void TearDown()
        {
            _playback?.Dispose();
            Object.Destroy(_host);
            if (_listener != null)
            {
                // At once: a listener left to the end of the frame is still found by the next test's set-up, which then
                // makes none, and with no listener Unity mixes nothing.
                Object.DestroyImmediate(_listener);
            }
            _server?.Dispose();
        }

        static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

        static long OutputLatencyTicks(out string described)
        {
            AudioSettings.GetDSPBufferSize(out var length, out var count);
            var rate = AudioSettings.outputSampleRate;
            var seconds = rate > 0 && length > 0 ? Math.Min(0.15, (double)length * Math.Max(1, count) / rate) : 0;
            described = $"DSP buffer {length} x {count} at {rate} Hz = {seconds * 1000:F1} ms";
            return (long)(seconds * Stopwatch.Frequency);
        }

        // Records the loudest sample of each 10 ms of what the source plays, with when it was mixed. The ramp fixture's level
        // is 0.08 times its media time, so a window's level says which media time it was.
        sealed class LevelProbe : MonoBehaviour
        {
            readonly List<(long At, float Peak)> _windows = new List<(long, float)>();
            int _window;
            int _filled;
            float _peak;

            void Awake() => _window = AudioSettings.outputSampleRate / 100;

            void OnAudioFilterRead(float[] data, int channels)
            {
                var now = Stopwatch.GetTimestamp();
                var frames = data.Length / channels;
                for (var i = 0; i < frames; i++)
                {
                    _peak = Math.Max(_peak, Math.Abs(data[i * channels]));
                    if (++_filled >= _window)
                    {
                        lock (_windows)
                        {
                            _windows.Add((now, _peak));
                        }
                        _filled = 0;
                        _peak = 0;
                    }
                }
            }

            public (long At, float Peak)[] Windows()
            {
                lock (_windows)
                {
                    return _windows.ToArray();
                }
            }
        }

        const float RampPerSecond = 0.08f;

        // The media time of the first sound mixed after a moment, read off the ramp's level. The sound starts at the first
        // 10 ms window over the given level; its time comes from the windows 30 to 120 ms on (each window's loudest sample
        // is at its end), because an AAC decoder's first frame after a reset fades in, being half an overlap.
        static double FirstHeard((long At, float Peak)[] windows, long after, float over)
        {
            var first = Array.FindIndex(windows, w => w.At >= after && w.Peak > over);
            if (first < 0 || first + 12 >= windows.Length)
            {
                return double.NaN;
            }
            var estimates = new List<double>();
            for (var k = 3; k <= 12; k++)
            {
                estimates.Add(windows[first + k].Peak / RampPerSecond - 0.01 * (k + 1));
            }
            estimates.Sort();
            return (estimates[4] + estimates[5]) / 2;
        }

        IEnumerator PlayRampUntil(Func<bool> until, List<LevelProbe> probes, string what)
        {
            var start = Time.realtimeSinceStartupAsDouble;
            while (!until())
            {
                if (_playback.State == VideoPlaybackState.Failed)
                {
                    Assert.Fail("failed: " + _playback.Failure);
                }
                if (Time.realtimeSinceStartupAsDouble - start > 15)
                {
                    Assert.Fail($"timed out waiting for {what}: time {_playback.Time:F2}");
                }
                _playback.Tick();
                foreach (var source in _host.GetComponentsInChildren<AudioSource>())
                {
                    if (source.GetComponent<LevelProbe>() == null)
                    {
                        probes.Add(source.gameObject.AddComponent<LevelProbe>());
                    }
                }
                yield return null;
            }
        }

        // Where the sound starts against where it was asked to: nothing at the start of a video, or after a seek, may go
        // missing. At 0 the ramp is too quiet to hear for its first 50 ms, so the start counts from there.
        [UnityTest]
        public IEnumerator The_sound_starts_where_the_video_does([Values(0.0, 1.1, 1.4, 1.7, 3.0)] double from)
        {
            _playback.Open(_server.Url("ramp.mp4"), new VideoPlaybackSettings(), true, from);
            _playback.Volume = 1f;
            var probes = new List<LevelProbe>();
            yield return PlayRampUntil(() => _playback.Time >= from + 1.5, probes, "a second and a half");
            var windows = probes.Count > 0 ? probes[0].Windows() : Array.Empty<(long, float)>();
            var heard = FirstHeard(windows, 0, 0.004f);
            var expected = Math.Max(from, 0.05);
            Debug.Log($"[AvSyncTests] ramp from {from:F1} s: first heard {heard:F3} s, {(heard - expected) * 1000:F0} ms missing " +
                      $"({probes.Count} sources, {windows.Length} windows, loudest {(windows.Length > 0 ? windows.Max(w => w.Item2) : 0):F3}, time {_playback.Time:F2})");
            Assert.That(heard, Is.EqualTo(expected).Within(0.03), "the first sound is the start's");

            // And after a seek.
            var target = from + 4.0;
            var seekAt = Stopwatch.GetTimestamp();
            _playback.Seek(target);
            yield return PlayRampUntil(() => _playback.Time >= target + 1.0, probes, "a second after the seek");
            // Louder than anything before the seek could be.
            var after = FirstHeard(probes[0].Windows(), seekAt, RampPerSecond * (float)(target - 1.0));
            Debug.Log($"[AvSyncTests] ramp seek to {target:F1} s: first heard {after:F3} s, {(after - target) * 1000:F0} ms missing");
            // Wider than at the start: the level that tells new sound from old is high, so the decoder's faded-in first frame
            // can sit under it and the onset reads up to two windows late (measured 17-29 ms, against 6-10 ms at a start).
            Assert.That(after, Is.EqualTo(target).Within(0.045), "the first sound after the seek is the target's");
        }

        [UnityTest]
        public IEnumerator Each_flash_is_drawn_with_its_click([Values("clapper.mp4", "clapper.ts", "clapper-hls/index.m3u8")] string file)
        {
            _playback.Open(_server.Url(file), new VideoPlaybackSettings(), true);
            _playback.Volume = 1f;

            var probes = new List<ClickProbe>();
            var flashes = new List<(int Second, long At)>();
            var frameGaps = new List<double>();
            long firstShown = VideoDecoderNative.NoTime, lastShown = VideoDecoderNative.NoTime;
            long lastTick = 0;
            var seeked = false;
            long seekedAt = 0;
            var start = Time.realtimeSinceStartupAsDouble;
            while (flashes.Count == 0 || flashes[flashes.Count - 1].Second < 9)
            {
                if (_playback.State == VideoPlaybackState.Failed)
                {
                    Assert.Fail("failed: " + _playback.Failure);
                }
                if (Time.realtimeSinceStartupAsDouble - start > 25)
                {
                    Assert.Fail($"timed out: time {_playback.Time:F2}, flashes seen {string.Join(",", flashes.Select(f => f.Second))}");
                }
                _playback.Tick();
                var now = Stopwatch.GetTimestamp();
                foreach (var source in _host.GetComponentsInChildren<AudioSource>())
                {
                    if (source.GetComponent<ClickProbe>() == null)
                    {
                        probes.Add(source.gameObject.AddComponent<ClickProbe>());
                    }
                }
                var status = (VideoDecoderNative.Status)StatusField.GetValue(_playback);
                var shown = status.FramesShown > 0 ? status.ShownPtsUs : VideoDecoderNative.NoTime;
                if (shown != VideoDecoderNative.NoTime)
                {
                    if (firstShown == VideoDecoderNative.NoTime)
                    {
                        // Playing from the start, the first frame drawn is the stream's zero.
                        firstShown = shown;
                    }
                    else if (lastShown != VideoDecoderNative.NoTime && shown > lastShown)
                    {
                        for (var second = 2; second <= 9; second++)
                        {
                            var flashAt = firstShown + second * 1000000L - 1000;
                            // Reached by playing, not jumped to by the seek.
                            if (lastShown < flashAt && shown >= flashAt && shown - flashAt < 50000)
                            {
                                // Drawn on the render thread some time since the last look: the middle of that gap.
                                flashes.Add((second, (lastTick + now) / 2));
                                frameGaps.Add(Ms(now - lastTick));
                            }
                        }
                    }
                    lastShown = shown;
                }
                lastTick = now;
                if (!seeked && flashes.Any(f => f.Second == 4))
                {
                    // A seek part-way through a second: the flashes after it must line up as well.
                    seeked = true;
                    seekedAt = now;
                    _playback.Seek(5.5);
                    lastShown = VideoDecoderNative.NoTime;
                }
                yield return null;
            }

            var session = SessionField.GetValue(_playback) as VideoSession;
            Assert.That(session, Is.Not.Null);
            if (!session.Info.HasAudio || session.Info.AudioLost)
            {
                Assert.Inconclusive($"the sound wasn't the clock (has audio {session.Info.HasAudio}, lost {session.Info.AudioLost}): no audio device?");
            }
            Assert.That(probes, Is.Not.Empty, "the engine's Audio Sources");

            var latency = OutputLatencyTicks(out var latencyText);
            var onsets = probes[0].Onsets();
            var offsets = new List<double>();
            var lines = new List<string>();
            foreach (var flash in flashes)
            {
                // The click that leaves the speakers nearest the flash.
                var audible = onsets.Select(o => o + latency).OrderBy(a => Math.Abs(a - flash.At)).FirstOrDefault();
                if (audible == 0 || Math.Abs(Ms(flash.At - audible)) > 300)
                {
                    lines.Add($"{flash.Second} s: no click near it");
                    continue;
                }
                var offset = Ms(flash.At - audible);
                offsets.Add(offset);
                lines.Add($"{flash.Second} s: picture {(offset >= 0 ? "late" : "early")} by {Math.Abs(offset):F1} ms");
            }
            var spread = probes.Count > 1
                ? probes.Select(p => p.Onsets()).Where(o => o.Length > 0).Select(o => Ms(o[0])).ToList()
                : new List<double>();
            var heard = onsets.Length > 0
                ? string.Join(", ", onsets.Select(o => Ms(o - onsets[0]).ToString("F0"))) + $" ms after the first; the seek at {Ms(seekedAt - onsets[0]):F0} ms"
                : "none";
            Debug.Log($"[AvSyncTests] {file}: clicks mixed at {heard}");
            Debug.Log($"[AvSyncTests] {file}: {latencyText}, {probes.Count} sources, {onsets.Length} clicks heard, " +
                      $"frame gaps {frameGaps.DefaultIfEmpty(0).Average():F1} ms average, {frameGaps.DefaultIfEmpty(0).Max():F1} ms most; " +
                      (spread.Count > 1 ? $"first click spread over channels {spread.Max() - spread.Min():F2} ms; " : "") +
                      string.Join("; ", lines));
            Assert.That(offsets.Count, Is.GreaterThanOrEqualTo(6), "flashes paired with their clicks: " + string.Join("; ", lines));
            Debug.Log($"[AvSyncTests] {file}: picture minus sound, mean {offsets.Average():F1} ms, from {offsets.Min():F1} to {offsets.Max():F1} ms");
            foreach (var offset in offsets)
            {
                Assert.That(Math.Abs(offset), Is.LessThanOrEqualTo(LimitMs), string.Join("; ", lines));
            }
        }
    }
}
