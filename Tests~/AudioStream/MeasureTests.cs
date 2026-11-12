using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace BS.AudioStream.Tests
{
    /// <summary>Numbers for the report: what decoding costs here, and what a session holds. These print; they only fail if something is badly off.</summary>
    [TestFixture]
    public class MeasureTests
    {
        const int Rate = 48000;

        static byte[] Encode(string name, int channels, string layout, double seconds, params string[] options)
        {
            var raw = Reference.Path_(name + ".f32");
            var output = Reference.Path_(name);
            Signals.WriteRaw(raw, Signals.Rich(channels, Rate, seconds));
            var arguments = new List<string> { "-f", "f32le", "-ar", Rate.ToString(), "-ch_layout", layout, "-i", raw };
            arguments.AddRange(options);
            arguments.Add(output);
            Reference.Run(arguments.ToArray());
            File.Delete(raw);
            var bytes = File.ReadAllBytes(output);
            File.Delete(output);
            return bytes;
        }

        [TestCase("AAC-LC stereo 128k", "aac", 2, "stereo", "128k")]
        [TestCase("AAC-LC 5.1 384k", "aac", 6, "5.1", "384k")]
        [TestCase("AAC-LC 7.1 512k", "aac", 8, "7.1(wide)", "512k")]
        [TestCase("MP3 stereo 128k", "mp3", 2, "stereo", "128k")]
        [TestCase("MP3 stereo 320k", "mp3", 2, "stereo", "320k")]
        [TestCase("WAV 16-bit stereo", "wav", 2, "stereo", "")]
        public void DecodeCost(string name, string kind, int channels, string layout, string bitrate)
        {
            const double seconds = 20;
            byte[] bytes;
            MediaKind media;
            if (kind == "aac")
            {
                bytes = Encode("cost.aac", channels, layout, seconds, "-c:a", "aac", "-b:a", bitrate, "-f", "adts");
                media = MediaKind.Adts;
            }
            else if (kind == "mp3")
            {
                bytes = Encode("cost.mp3", channels, layout, seconds, "-c:a", "libmp3lame", "-b:a", bitrate, "-f", "mp3");
                media = MediaKind.Mp3;
            }
            else
            {
                bytes = Encode("cost.wav", channels, layout, seconds, "-c:a", "pcm_s16le", "-f", "wav");
                media = MediaKind.Wav;
            }

            // Warm the JIT, then keep the best of several: this is a cost, not a load test.
            void Once()
            {
                var sink = new Offline { KeepAudio = false };
                var parser = Offline.Parser(media, sink);
                for (var at = 0; at < bytes.Length; at += 16384)
                {
                    parser.Push(bytes, at, Math.Min(16384, bytes.Length - at));
                }
            }
            for (var i = 0; i < 3; i++)
            {
                Once();
            }
            var best = double.MaxValue;
            for (var i = 0; i < 6; i++)
            {
                var clock = Stopwatch.StartNew();
                Once();
                best = Math.Min(best, clock.Elapsed.TotalMilliseconds);
            }
            var perSecond = best / seconds;
            TestContext.Progress.WriteLine($"cost {name}: {perSecond:F2} ms per second of audio, {perSecond / channels:F2} ms per channel, {1000 / perSecond:F0}x real time");
            Assert.Less(perSecond, 500, "half of real time would be alarming even here");
        }

        [Test]
        public void WhatASevenPointOneSessionHolds()
        {
            // Opus, decoded here on every platform; the session's holdings don't depend on the codec.
            var opus = Encode("mem.opus", 8, "7.1", 6.0, "-c:a", "libopus", "-b:a", "384k", "-mapping_family", "1", "-f", "ogg");
            using var server = new TestServer();
            server.Add("m.opus", new Route { Body = opus, ChunkBytes = 8192, ChunkDelayMilliseconds = 5 });
            var unity = new FakeUnity(new FakeUnity.Options { ReadAheadBeforeStart = false });
            using var cancel = new CancellationTokenSource();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var before = GC.GetTotalMemory(true);
            var conductor = new StreamConductor(unity, 1.0, cancel.Token);
            var session = new StreamSession(new StreamSessionOptions { Url = server.Url("m.opus"), Layout = ChannelLayouts.Of(8) }, HttpStreamTransport.Shared, conductor, cancel.Token);
            var thread = new Thread(session.Run) { IsBackground = true };
            thread.Start();
            try
            {
                var clock = Stopwatch.StartNew();
                while (conductor.Phase != StreamPhase.Playing && clock.ElapsedMilliseconds < 10000)
                {
                    conductor.Tick(clock.Elapsed.TotalSeconds);
                    Thread.Sleep(1);
                }
                Assert.AreEqual(StreamPhase.Playing, conductor.Phase, conductor.Failure);
                var during = GC.GetTotalMemory(true);
                var ring = (conductor.BufferedSeconds, conductor.Format.Roles.Length);
                var capacitySeconds = 1.0 + 3.0;
                var ringBytes = (long)(capacitySeconds * Rate) * 4 * 8;
                TestContext.Progress.WriteLine($"memory 7.1 at 48 kHz: {(during - before) / 1024.0 / 1024.0:F1} MiB on the heap, of which the rings {ringBytes / 1024.0 / 1024.0:F1} MiB ({capacitySeconds} s x 8 channels)");
                Assert.Less(during - before, 16L * 1024 * 1024);
            }
            finally
            {
                cancel.Cancel();
                conductor.Stop();
                thread.Join(2000);
            }
        }

        // Icecast speaks HTTP, but old Shoutcast servers answer with an ICY status line. What happens depends on the HTTP stack:
        // .NET's refuses it, so this ends in a failure that says so. Unity's runtime has its own stack, not shown here.
        [Test]
        public void AnIcyStatusLineEndsCleanlyEitherWay()
        {
            var body = Encode("icy.mp3", 2, "stereo", 2.0, "-c:a", "libmp3lame", "-b:a", "128k");
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            string request = null;
            var serving = Task.Run(() =>
            {
                for (var i = 0; i < 8; i++)
                {
                    using var client = listener.AcceptTcpClient();
                    using var stream = client.GetStream();
                    var buffer = new byte[4096];
                    var read = stream.Read(buffer, 0, buffer.Length);
                    request = request ?? Encoding.ASCII.GetString(buffer, 0, read);
                    var head = Encoding.ASCII.GetBytes("ICY 200 OK\r\nicy-name:test\r\nContent-Type: audio/mpeg\r\n\r\n");
                    stream.Write(head, 0, head.Length);
                    stream.Write(body, 0, body.Length);
                }
            });
            var logged = new List<string>();
            var clock = Stopwatch.StartNew();
            var output = Play.Url($"http://127.0.0.1:{port}/stream", seconds: 30, configure: o => o.Log = logged.Add);
            listener.Stop();
            TestContext.Progress.WriteLine($"ICY on .NET: '{output.Failure}', {output.Frames} frames, after {clock.ElapsedMilliseconds} ms");
            Assert.IsTrue(output.Failure != null || output.Frames > 0, "it either plays or says why");
            StringAssert.DoesNotContain("Icy-MetaData", request ?? "", "the stream must not be asked to interleave metadata");
        }
    }
}
