using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BS.SDKEditor.Tests
{
    /// <summary>
    /// What only Unity's audio engine can show: whether streamed clips really start together and stay together. A local server
    /// plays an eight-channel file with a click on every channel at the same frames, and a filter on each Audio Source notes
    /// where each click lands on the dsp clock. A short clip length forces loop points during the run. Run by name, as it
    /// enters Play mode and needs a working audio device; with none, it says so and skips.
    /// </summary>
    [Explicit("Needs Unity's audio engine and enters Play mode: run by name.")]
    public class BSAudioStreamPlayModeTests
    {
        const int Rate = 48000;
        const int Channels = 6;
        const int Seconds = 20;
        const int ClickEvery = 12000;

        // A filter's own samples are counted on the shared dsp clock, so positions on different sources can be compared.
        sealed class ClickProbe : MonoBehaviour
        {
            readonly List<long> _clicks = new List<long>();
            int _outputRate;
            bool _inClick;

            void Awake() => _outputRate = AudioSettings.outputSampleRate;

            void OnAudioFilterRead(float[] data, int channels)
            {
                var start = (long)Math.Round(AudioSettings.dspTime * _outputRate);
                var frames = data.Length / channels;
                for (var i = 0; i < frames; i++)
                {
                    var peak = 0f;
                    for (var c = 0; c < channels; c++)
                    {
                        peak = Math.Max(peak, Math.Abs(data[i * channels + c]));
                    }
                    if (peak > 0.2f && !_inClick)
                    {
                        _inClick = true;
                        lock (_clicks)
                        {
                            _clicks.Add(start + i);
                        }
                    }
                    else if (peak < 0.05f)
                    {
                        _inClick = false;
                    }
                }
            }

            public long[] Clicks()
            {
                lock (_clicks)
                {
                    return _clicks.ToArray();
                }
            }
        }

        static byte[] ClickFile()
        {
            const int frames = Rate * Seconds;
            var data = new byte[frames * Channels * 2];
            for (var frame = 0; frame < frames; frame += ClickEvery)
            {
                for (var c = 0; c < Channels; c++)
                {
                    // Four frames wide, so that a resampling output still sees it.
                    for (var i = 0; i < 4; i++)
                    {
                        var at = ((frame + i) * Channels + c) * 2;
                        data[at] = 0xFF;
                        data[at + 1] = 0x6F;
                    }
                }
            }
            using (var stream = new MemoryStream())
            using (var w = new BinaryWriter(stream))
            {
                w.Write(Encoding.ASCII.GetBytes("RIFF"));
                w.Write(36 + data.Length);
                w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                w.Write(16);
                w.Write((short)1);
                w.Write((short)Channels);
                w.Write(Rate);
                w.Write(Rate * Channels * 2);
                w.Write((short)(Channels * 2));
                w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data"));
                w.Write(data.Length);
                w.Write(data);
                return stream.ToArray();
            }
        }

        static HttpListener Serve(byte[] body, out string url)
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://localhost:{port}/");
            listener.Start();
            url = $"http://localhost:{port}/clicks.wav";
            new Thread(() =>
            {
                try
                {
                    while (listener.IsListening)
                    {
                        var context = listener.GetContext();
                        context.Response.ContentType = "audio/wav";
                        context.Response.ContentLength64 = body.Length;
                        context.Response.OutputStream.Write(body, 0, body.Length);
                        context.Response.Close();
                    }
                }
                catch (Exception)
                {
                    // Stopping the listener ends the wait with an exception.
                }
            }) { IsBackground = true }.Start();
            return listener;
        }

        static void Set(object target, string field, object value)
        {
            target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        }

        // ClipFrames sets how soon the clip reaches its loop point: 1 << 16 is about 1.4 seconds, so a run passes several.
        [UnityTest]
        public IEnumerator EveryChannelStartsTogetherAndStaysTogetherThroughLoopPoints([Values(1 << 28, 1 << 16)] int clipFrames)
        {
            yield return new EnterPlayMode();
            var runInBackground = Application.runInBackground;
            Application.runInBackground = true;
            var root = new GameObject("Audio stream under test");
            var listenerObject = Object.FindAnyObjectByType<AudioListener>() == null ? new GameObject("Listener", typeof(AudioListener)) : null;
            HttpListener server = null;
            var previousClip = BSAudioStream.ClipFrames;
            try
            {
                var dspAtStart = AudioSettings.dspTime;
                var waited = 0f;
                while (waited < 1f)
                {
                    waited += Time.unscaledDeltaTime;
                    yield return null;
                }
                if (AudioSettings.dspTime == dspAtStart)
                {
                    Assert.Ignore("The audio clock isn't running here, so there is nothing to measure.");
                }

                server = Serve(ClickFile(), out var url);
                root.SetActive(false);
                var stream = root.AddComponent<BSAudioStream>();
                Set(stream, "url", url);
                Set(stream, "playOnEnable", false);
                Set(stream, "maxChannels", BSAudioStreamLayout.Surround51);
                Set(stream, "prebufferSeconds", 0.5f);
                BSAudioStream.ClipFrames = clipFrames;

                var rows = (BSAudioStreamChannel[])typeof(BSAudioStream).GetField("channels", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(stream);
                var probes = new List<ClickProbe>();
                var roles = BSAudioStream.RolesOf(BSAudioStreamLayout.Surround51);
                foreach (var row in rows.Where(r => System.Array.IndexOf(roles, r.role) >= 0))
                {
                    var child = new GameObject(row.role.ToString());
                    child.transform.SetParent(root.transform, false);
                    var source = child.AddComponent<AudioSource>();
                    source.playOnAwake = false;
                    source.spatialBlend = 0f;
                    source.dopplerLevel = 0f;
                    row.source = source;
                    probes.Add(child.AddComponent<ClickProbe>());
                }
                root.SetActive(true);
                stream.Play();

                var clock = 0f;
                while (clock < 8f)
                {
                    clock += Time.unscaledDeltaTime;
                    yield return null;
                }
                Debug.Log($"[BSAudioStream test] clip {clipFrames}: state {stream.State}, {stream.PlayingChannels} channels, buffered {stream.BufferedSeconds:F2} s, underruns {stream.Underruns}, failure '{stream.FailureReason}', output rate {AudioSettings.outputSampleRate}");
                Assert.AreEqual(BSAudioStreamState.Playing, stream.State, stream.FailureReason);
                Assert.AreEqual(Channels, stream.PlayingChannels);

                var clicks = probes.Select(p => p.Clicks()).ToArray();
                Debug.Log("[BSAudioStream test] clicks per channel: " + string.Join(", ", clicks.Select(c => c.Length)));
                var count = clicks.Min(c => c.Length);
                Assert.Greater(count, 10, "the clicks were heard");
                var worst = 0L;
                for (var k = 0; k < count; k++)
                {
                    var first = clicks.Min(c => c[k]);
                    var last = clicks.Max(c => c[k]);
                    worst = Math.Max(worst, last - first);
                }
                Debug.Log($"[BSAudioStream test] worst spread between channels over {count} clicks: {worst} samples");
                Assert.LessOrEqual(worst, 2, "every channel's click on the same sample");

                var spacing = clicks[0].Zip(clicks[0].Skip(1), (a, b) => b - a).ToArray();
                var expected = ClickEvery * (double)AudioSettings.outputSampleRate / Rate;
                Debug.Log($"[BSAudioStream test] click spacing {spacing.Min()}..{spacing.Max()}, expected {expected:F1}");
                Assert.AreEqual(0, stream.Underruns, "a local server shouldn't run dry");
                Assert.LessOrEqual(spacing.Max() - spacing.Min(), 4, "no jump at a loop point");
            }
            finally
            {
                BSAudioStream.ClipFrames = previousClip;
                server?.Stop();
                Object.Destroy(root);
                if (listenerObject != null)
                {
                    Object.Destroy(listenerObject);
                }
                Application.runInBackground = runInBackground;
            }
            yield return new ExitPlayMode();
        }
    }
}
