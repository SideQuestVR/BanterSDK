using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using NUnit.Framework;

namespace BS.AudioStream.Tests
{
    /// <summary>From one URL to PCM over real HTTP, through every layer that doesn't need Unity.</summary>
    [TestFixture]
    public class EndToEndTests
    {
        TestServer _server;

        [OneTimeSetUp]
        public void Start() => _server = new TestServer();

        [OneTimeTearDown]
        public void Finish() => _server.Dispose();

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

        static float[][] DecodeAdts(byte[] bytes)
        {
            var path = Reference.Path_("adts-" + Guid.NewGuid().ToString("N") + ".aac");
            File.WriteAllBytes(path, bytes);
            var planes = AacAccuracyTests.DecodeAdts(path, out _);
            File.Delete(path);
            return planes;
        }

        static string HlsFolder(string name, byte[] aac, string segmentType, double seconds = 1)
        {
            var folder = Reference.Path_("hls-" + name);
            Directory.CreateDirectory(folder);
            var input = Path.Combine(folder, "input.aac");
            File.WriteAllBytes(input, aac);
            var extra = segmentType == "fmp4" ? new[] { "-hls_fmp4_init_filename", "init.mp4", "-bsf:a", "aac_adtstoasc" } : new string[0];
            var arguments = new List<string> { "-i", input, "-c:a", "copy", "-f", "hls", "-hls_time", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture), "-hls_playlist_type", "vod", "-hls_segment_type", segmentType };
            arguments.AddRange(extra);
            arguments.AddRange(new[] { "-hls_segment_filename", Path.Combine(folder, segmentType == "fmp4" ? "seg%03d.m4s" : "seg%03d.ts"), Path.Combine(folder, "media.m3u8") });
            Reference.Run(arguments.ToArray());
            File.Delete(input);
            MoveInit(folder);
            return folder;
        }

        // ffmpeg writes the initialisation segment to its working folder, whatever the playlist's folder is.
        static void MoveInit(string folder)
        {
            var written = Reference.Path_("init.mp4");
            if (File.Exists(written))
            {
                File.Move(written, Path.Combine(folder, "init.mp4"), true);
            }
        }

        // Identical from the first frame both have: the only legal difference is frames left out at either end.
        static void AssertSame(float[] expected, float[] actual, string what)
        {
            var count = Math.Min(expected.Length, actual.Length);
            Assert.Greater(count, Rate, what + ": decoded at least a second");
            for (var i = 0; i < count; i++)
            {
                if (expected[i] != actual[i])
                {
                    Assert.Fail($"{what}: differs at frame {i} of {count}: expected {expected[i]}, got {actual[i]}");
                }
            }
        }

        [TestCase("mpegts")]
        [TestCase("fmp4")]
        public void AnHlsVodPlaysEverySegmentWithNoGapOrClick(string segmentType)
        {
            var aac = Encode("vod-" + segmentType + ".aac", 2, "stereo", 5.0, "-c:a", "aac", "-b:a", "128k", "-aac_pns", "0", "-f", "adts");
            var whole = DecodeAdts(aac);
            var folder = HlsFolder("vod-" + segmentType, aac, segmentType);
            _server.AddFolder(folder, "vod-" + segmentType);
            var output = Play.Url(_server.Url($"vod-{segmentType}/media.m3u8"));
            Assert.IsNull(output.Failure, output.Failure);
            Assert.IsTrue(output.Finished, "finished");
            Assert.AreEqual(new[] { AudioChannelRole.FrontLeft, AudioChannelRole.FrontRight }, output.Formats.Last().Roles);
            var left = output.Role(AudioChannelRole.FrontLeft);
            TestContext.Progress.WriteLine($"{segmentType}: {left.Length} frames through HLS, {whole[0].Length} whole");
            // Every frame of the stream arrives, with the decoder's state carried across each segment boundary: bit-identical
            // output is a stronger proof of no gap or click than measuring one.
            Assert.GreaterOrEqual(left.Length, whole[0].Length - 2048);
            AssertSame(whole[0], left, segmentType + " left");
            AssertSame(whole[1], output.Role(AudioChannelRole.FrontRight), segmentType + " right");
        }

        [Test]
        public void AMasterPlaylistFollowsToTheBestRenditionAndResolvesRelativeUrls()
        {
            var aac = Encode("master.aac", 2, "stereo", 3.0, "-c:a", "aac", "-b:a", "128k", "-aac_pns", "0", "-f", "adts");
            var folder = HlsFolder("master", aac, "mpegts");
            _server.AddFolder(folder, "m/audio/stereo");
            _server.AddText("m/master.m3u8", "#EXTM3U\n#EXT-X-VERSION:6\n" +
                "#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=\"a\",NAME=\"Stereo\",DEFAULT=YES,CHANNELS=\"2\",URI=\"audio/stereo/media.m3u8\"\n" +
                "#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=\"b\",NAME=\"Atmos\",CHANNELS=\"16/JOC\",URI=\"audio/atmos/media.m3u8\"\n" +
                "#EXT-X-STREAM-INF:BANDWIDTH=900000,CODECS=\"avc1.640028,mp4a.40.2\",AUDIO=\"a\"\nvideo/low.m3u8\n" +
                "#EXT-X-STREAM-INF:BANDWIDTH=900000,CODECS=\"avc1.640028,ec-3\",AUDIO=\"b\"\nvideo/high.m3u8\n");
            var output = Play.Url(_server.Url("m/master.m3u8"));
            Assert.IsNull(output.Failure, output.Failure);
            AssertSame(DecodeAdts(aac)[0], output.Role(AudioChannelRole.FrontLeft), "rendition");
        }

        [Test]
        public void ALivePlaylistReloadsAndPlaysWhatIsNew()
        {
            var aac = Encode("live.aac", 2, "stereo", 8.0, "-c:a", "aac", "-b:a", "128k", "-aac_pns", "0", "-f", "adts");
            var folder = HlsFolder("live", aac, "mpegts");
            _server.AddFolder(folder, "live");
            var names = Directory.GetFiles(folder, "seg*.ts").Select(Path.GetFileName).OrderBy(n => n).ToArray();
            Assert.GreaterOrEqual(names.Length, 6, "segments");

            var requests = 0;
            var route = new Route
            {
                ContentType = "application/vnd.apple.mpegurl",
                Text = () =>
                {
                    // A window of three segments moving on one segment per reload, ending after the last.
                    var first = Math.Min(Interlocked.Increment(ref requests) - 1, names.Length - 3);
                    var text = new StringBuilder("#EXTM3U\n#EXT-X-VERSION:3\n#EXT-X-TARGETDURATION:1\n#EXT-X-MEDIA-SEQUENCE:" + first + "\n");
                    for (var i = first; i < first + 3; i++)
                    {
                        text.Append("#EXTINF:1.0,\n").Append(names[i]).Append('\n');
                    }
                    if (first == names.Length - 3)
                    {
                        text.Append("#EXT-X-ENDLIST\n");
                    }
                    return text.ToString();
                },
            };
            _server.Add("live/index.m3u8", route);
            var output = Play.Url(_server.Url("live/index.m3u8"));
            Assert.IsNull(output.Failure, output.Failure);
            Assert.IsTrue(output.Finished);
            Assert.GreaterOrEqual(requests, names.Length - 3, "reloads");
            var left = output.Role(AudioChannelRole.FrontLeft);
            var seconds = left.Length / (double)Rate;
            TestContext.Progress.WriteLine($"live: {route.Requests} reloads, {seconds:F2} s played of {names.Length} segments");
            Assert.Greater(seconds, names.Length - 1.5, "joined at the start of the three-segment window and kept up");
        }

        [Test]
        public void ByteRangesPlayOneFileAsSegments()
        {
            var aac = Encode("ranges.aac", 2, "stereo", 5.0, "-c:a", "aac", "-b:a", "128k", "-aac_pns", "0", "-f", "adts");
            var folder = Reference.Path_("hls-ranges");
            Directory.CreateDirectory(folder);
            var input = Path.Combine(folder, "input.aac");
            File.WriteAllBytes(input, aac);
            Reference.Run("-i", input, "-c:a", "copy", "-f", "hls", "-hls_time", "1", "-hls_playlist_type", "vod", "-hls_flags", "single_file",
                "-hls_segment_filename", Path.Combine(folder, "all.ts"), Path.Combine(folder, "media.m3u8"));
            File.Delete(input);
            StringAssert.Contains("#EXT-X-BYTERANGE", File.ReadAllText(Path.Combine(folder, "media.m3u8")));
            _server.AddFolder(folder, "ranges");
            var output = Play.Url(_server.Url("ranges/media.m3u8"));
            Assert.IsNull(output.Failure, output.Failure);
            AssertSame(DecodeAdts(aac)[0], output.Role(AudioChannelRole.FrontLeft), "byte ranges");
            Assert.GreaterOrEqual(output.Frames, DecodeAdts(aac)[0].Length - 2048, "every range arrived");
        }

        [Test]
        public void AServerIgnoringByteRangesIsStillReadCorrectly()
        {
            var aac = Encode("noranges2.aac", 2, "stereo", 4.0, "-c:a", "aac", "-b:a", "128k", "-aac_pns", "0", "-f", "adts");
            var folder = Reference.Path_("hls-ranges2");
            Directory.CreateDirectory(folder);
            var input = Path.Combine(folder, "input.aac");
            File.WriteAllBytes(input, aac);
            Reference.Run("-i", input, "-c:a", "copy", "-f", "hls", "-hls_time", "1", "-hls_playlist_type", "vod", "-hls_flags", "single_file",
                "-hls_segment_filename", Path.Combine(folder, "all.ts"), Path.Combine(folder, "media.m3u8"));
            File.Delete(input);
            _server.AddText("ranges2/media.m3u8", File.ReadAllText(Path.Combine(folder, "media.m3u8")));
            _server.Add("ranges2/all.ts", new Route { Body = File.ReadAllBytes(Path.Combine(folder, "all.ts")), Ranges = false });
            var output = Play.Url(_server.Url("ranges2/media.m3u8"));
            Assert.IsNull(output.Failure, output.Failure);
            AssertSame(DecodeAdts(aac)[0], output.Role(AudioChannelRole.FrontLeft), "ranges cut from whole responses");
        }

        [Test]
        public void ADiscontinuityCanChangeRateAndChannels()
        {
            var first = Encode("disc1.ts", 2, "stereo", 2.0, "-c:a", "aac", "-b:a", "128k", "-f", "mpegts");
            var raw = Reference.Path_("disc2.f32");
            Signals.WriteRaw(raw, Signals.Rich(1, 44100, 2.0));
            var path = Reference.Path_("disc2.ts");
            Reference.Run("-f", "f32le", "-ar", "44100", "-ch_layout", "mono", "-i", raw, "-c:a", "aac", "-b:a", "96k", "-f", "mpegts", path);
            var second = File.ReadAllBytes(path);
            File.Delete(raw);
            File.Delete(path);
            _server.Add("disc/a.ts", first);
            _server.Add("disc/b.ts", second);
            _server.AddText("disc/m.m3u8", "#EXTM3U\n#EXT-X-TARGETDURATION:2\n#EXT-X-MEDIA-SEQUENCE:0\n#EXTINF:2,\na.ts\n#EXT-X-DISCONTINUITY\n#EXTINF:2,\nb.ts\n#EXT-X-ENDLIST\n");
            var output = Play.Url(_server.Url("disc/m.m3u8"));
            Assert.IsNull(output.Failure, output.Failure);
            Assert.AreEqual(2, output.Formats.Count, "both formats");
            Assert.AreEqual(48000, output.Formats[0].SampleRate);
            Assert.AreEqual(2, output.Formats[0].Roles.Length);
            Assert.AreEqual(44100, output.Formats[1].SampleRate);
            Assert.AreEqual(1, output.Formats[1].Roles.Length);
            Assert.Greater(output.Channels[0].Count, 44100, "the second part played");
        }

        [Test]
        public void ALiveSegmentThatCantBeFetchedIsSkipped()
        {
            var aac = Encode("skip.aac", 2, "stereo", 6.0, "-c:a", "aac", "-b:a", "128k", "-aac_pns", "0", "-f", "adts");
            var folder = HlsFolder("skip", aac, "mpegts");
            var names = Directory.GetFiles(folder, "seg*.ts").Select(Path.GetFileName).OrderBy(n => n).ToArray();
            _server.AddFolder(folder, "skip");
            _server.Add("skip/" + names[2], new Route { Status = 404 });
            var requests = 0;
            _server.Add("skip/index.m3u8", new Route
            {
                ContentType = "application/vnd.apple.mpegurl",
                Text = () =>
                {
                    var first = Math.Min(Interlocked.Increment(ref requests) - 1, names.Length - 3);
                    var text = new StringBuilder("#EXTM3U\n#EXT-X-TARGETDURATION:1\n#EXT-X-MEDIA-SEQUENCE:" + first + "\n");
                    for (var i = first; i < first + 3; i++)
                    {
                        text.Append("#EXTINF:1.0,\n").Append(names[i]).Append('\n');
                    }
                    if (first == names.Length - 3)
                    {
                        text.Append("#EXT-X-ENDLIST\n");
                    }
                    return text.ToString();
                },
            });
            var logged = new List<string>();
            var output = Play.Url(_server.Url("skip/index.m3u8"), configure: o => o.Log = logged.Add);
            Assert.IsNull(output.Failure, output.Failure);
            Assert.IsTrue(output.Finished);
            Assert.Greater(output.Frames, Rate * 3L, "what came after the gap played");
            Assert.IsTrue(logged.Any(l => l.Contains("Skipped")));
        }

        [Test]
        public void AVodSegmentThatCantBeFetchedStopsTheStream()
        {
            _server.AddText("vodgone/m.m3u8", "#EXTM3U\n#EXT-X-TARGETDURATION:2\n#EXTINF:2,\ngone.ts\n#EXT-X-ENDLIST\n");
            var output = Play.Url(_server.Url("vodgone/m.m3u8"));
            StringAssert.Contains("404", output.Failure ?? "");
        }

        [Test]
        public void APlainM3uListPlaysItsEntriesInOrder()
        {
            var one = Encode("plain1.aac", 1, "mono", 1.5, "-c:a", "aac", "-b:a", "96k", "-aac_pns", "0", "-f", "adts");
            var two = Encode("plain2.aac", 1, "mono", 1.5, "-c:a", "aac", "-b:a", "96k", "-aac_pns", "0", "-f", "adts");
            _server.Add("plain/a/one.aac", one);
            _server.Add("plain/b/two.aac", two);
            _server.AddText("plain/list.m3u", "# a comment\na/one.aac\n\n" + _server.Url("plain/b/two.aac") + "\n", "audio/x-mpegurl");
            var output = Play.Url(_server.Url("plain/list.m3u"));
            Assert.IsNull(output.Failure, output.Failure);
            var center = output.Role(AudioChannelRole.Center);
            Assert.Greater(center.Length, Rate * 2.8, "both entries played");
        }

        [TestCase("audio/mpeg", "song.bin")]
        [TestCase("text/plain", "song.wav")]
        [TestCase("application/octet-stream", "song")]
        public void FirstBytesDecideNotLabels(string contentType, string name)
        {
            var mp3 = Encode("label.mp3", 2, "stereo", 2.0, "-c:a", "libmp3lame", "-b:a", "128k");
            _server.Add("label/" + name, mp3, contentType);
            var output = Play.Url(_server.Url("label/" + name));
            Assert.IsNull(output.Failure, output.Failure);
            Assert.AreEqual("MP3", output.Formats.Last().Codec);
        }

        [Test]
        public void ADirectWavPlaysToItsEnd()
        {
            var wav = Encode("direct.wav", 2, "stereo", 2.0, "-c:a", "pcm_s16le");
            _server.Add("direct/a.wav", wav, "audio/wav");
            var output = Play.Url(_server.Url("direct/a.wav"));
            Assert.IsNull(output.Failure, output.Failure);
            Assert.IsTrue(output.Finished);
            Assert.AreEqual(Rate * 2, output.Frames);
        }

        [Test]
        public void ARedirectIsFollowed()
        {
            var mp3 = Encode("redirect.mp3", 1, "mono", 2.0, "-c:a", "libmp3lame", "-b:a", "96k");
            _server.Add("r/real.mp3", mp3);
            _server.Add("r/a", new Route { Status = 302, RedirectTo = _server.Url("r/b") });
            _server.Add("r/b", new Route { Status = 301, RedirectTo = "/r/real.mp3" });
            var output = Play.Url(_server.Url("r/a"));
            Assert.IsNull(output.Failure, output.Failure);
            Assert.Greater(output.Frames, Rate);
        }

        [TestCase("file:///C:/Windows/win.ini")]
        [TestCase("ftp://localhost/x.mp3")]
        [TestCase("data:audio/mp3;base64,AAAA")]
        public void ARedirectMayOnlyGoToHttpOrHttps(string location)
        {
            var name = "rs/" + Guid.NewGuid().ToString("N");
            _server.Add(name, new Route { Status = 302, RedirectTo = location });
            var output = Play.Url(_server.Url(name));
            StringAssert.Contains("isn't http or https", output.Failure);
        }

        [Test]
        public void ARedirectLoopEnds()
        {
            _server.Add("loop/a", new Route { Status = 302, RedirectTo = _server.Url("loop/b") });
            _server.Add("loop/b", new Route { Status = 302, RedirectTo = _server.Url("loop/a") });
            var output = Play.Url(_server.Url("loop/a"));
            StringAssert.Contains("too many redirects", output.Failure);
        }

        [TestCase("file:///C:/Windows/win.ini")]
        [TestCase("ftp://localhost/x.m3u8")]
        [TestCase("javascript:alert(1)")]
        [TestCase("not a url")]
        [TestCase("")]
        public void TheStartingUrlMustBeHttpOrHttps(string url)
        {
            var output = Play.Url(url);
            StringAssert.Contains("http or https", output.Failure);
        }

        [Test]
        public void AnEntryThatIsntHttpIsNeverFetched()
        {
            _server.AddText("bad/list.m3u", "#EXTM3U\n#EXTINF:1,\nfile:///C:/Windows/win.ini\n#EXTINF:1,\nftp://localhost/x.aac\n");
            using var transport = new HttpTransport();
            var output = Play.Url(_server.Url("bad/list.m3u"), transport: transport);
            Assert.IsNotNull(output.Failure);
            Assert.AreEqual(1, transport.Requests.Count, "only the playlist itself");
        }

        [Test]
        public void ADroppedDownloadResumesWithNothingLostOrRepeated()
        {
            var wav = Encode("resume.wav", 2, "stereo", 3.0, "-c:a", "pcm_s16le");
            _server.Add("resume/ok.wav", wav);
            _server.Add("resume/drop.wav", new Route { Body = wav, DropAfter = 200000, Drops = 2 });
            var clean = Play.Url(_server.Url("resume/ok.wav"));
            var dropped = Play.Url(_server.Url("resume/drop.wav"));
            Assert.IsNull(dropped.Failure, dropped.Failure);
            Assert.AreEqual(clean.Frames, dropped.Frames, "frames");
            AssertSame(clean.Role(AudioChannelRole.FrontLeft), dropped.Role(AudioChannelRole.FrontLeft), "after two drops");
        }

        [Test]
        public void AServerWithoutRangesIsReadThroughToWhereItDropped()
        {
            var wav = Encode("noranges.wav", 1, "mono", 3.0, "-c:a", "pcm_s16le");
            _server.Add("nr/ok.wav", wav);
            _server.Add("nr/drop.wav", new Route { Body = wav, DropAfter = 100000, Drops = 1, Ranges = false });
            var clean = Play.Url(_server.Url("nr/ok.wav"));
            var dropped = Play.Url(_server.Url("nr/drop.wav"));
            Assert.IsNull(dropped.Failure, dropped.Failure);
            AssertSame(clean.Role(AudioChannelRole.Center), dropped.Role(AudioChannelRole.Center), "read through");
            Assert.AreEqual(clean.Frames, dropped.Frames);
        }

        [Test]
        public void AQueueThatOverflowsDropsAndResumesInsteadOfGrowing()
        {
            var wav = Encode("overflow.wav", 2, "stereo", 4.0, "-c:a", "pcm_s16le");
            _server.Add("ovf/ok.wav", wav);
            using var small = new HttpTransport(256 * 1024);
            var output = new SlowOutput(0.002);
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            new StreamSession(new StreamSessionOptions { Url = _server.Url("ovf/ok.wav"), Layout = Play.All }, small, output, cancel.Token).Run();
            Assert.IsNull(output.Failure, output.Failure);
            Assert.AreEqual(Rate * 4L, output.Frames, "every frame arrived");
            TestContext.Progress.WriteLine($"overflow: peak queue {small.PeakPipeBytes} of {wav.Length} bytes, {small.Requests.Count} requests");
            Assert.LessOrEqual(small.PeakPipeBytes, 256 * 1024);
            Assert.Greater(small.Requests.Count, 1, "it had to resume");
        }

        sealed class SlowOutput : IStreamOutput
        {
            readonly double _secondsPerWrite;

            public SlowOutput(double secondsPerWrite)
            {
                _secondsPerWrite = secondsPerWrite;
            }

            public long Frames;
            public string Failure;

            public bool Configure(StreamFormat format) => true;

            public void MarkTimestamp(long ptsUs)
            {
            }

            public bool Write(float[][] planes, int count)
            {
                Frames += count;
                Thread.Sleep(TimeSpan.FromSeconds(_secondsPerWrite));
                return true;
            }

            public void Finish()
            {
            }

            public void Fail(string reason) => Failure = reason;
        }

        [TestCase("mp3", "libmp3lame", "128k", "MP3")]
        [TestCase("aac", "aac", "128k", "AAC-LC")]
        public void AnEndlessStreamRunsWithBoundedMemoryUntilStopped(string name, string encoder, string bitrate, string codec)
        {
            var extra = name == "aac" ? new[] { "-f", "adts" } : new string[0];
            var options = new List<string> { "-c:a", encoder, "-b:a", bitrate };
            options.AddRange(extra);
            var body = Encode("endless." + name, 2, "stereo", 6.0, options.ToArray());
            var route = _server.Add("endless/" + name, new Route
            {
                ContentType = name == "mp3" ? "audio/mpeg" : "audio/aac",
                ChunkDelayMilliseconds = 0,
                Endless = position =>
                {
                    var at = (int)(position % body.Length);
                    var count = Math.Min(4096, body.Length - at);
                    var piece = new byte[count];
                    Buffer.BlockCopy(body, at, piece, 0, count);
                    return piece;
                },
            });
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var transport = new HttpTransport(1024 * 1024);
            var output = new CollectingOutput();
            var before = GC.GetTotalMemory(true);
            var limit = Rate * 30L;
            output.OnWrite = frames =>
            {
                // Keep only the count: the collector isn't what is being measured.
                foreach (var channel in output.Channels)
                {
                    channel.Clear();
                }
                if (frames >= limit)
                {
                    cancel.Cancel();
                }
            };
            new StreamSession(new StreamSessionOptions { Url = _server.Url("endless/" + name), Layout = Play.All }, transport, output, cancel.Token).Run();
            var after = GC.GetTotalMemory(true);
            TestContext.Progress.WriteLine($"endless {name}: {output.Frames / (double)Rate:F1} s decoded, {route.BytesSent / 1024} KiB sent, peak queue {transport.PeakPipeBytes / 1024} KiB, heap change {(after - before) / 1024} KiB");
            Assert.GreaterOrEqual(output.Frames, limit, "ran past the length of the loop it was fed");
            Assert.AreEqual(codec, output.Formats.Last().Codec);
            Assert.IsNull(output.Failure, output.Failure);
            Assert.LessOrEqual(transport.PeakPipeBytes, 1024 * 1024);
            Assert.Less(after - before, 8 * 1024 * 1024, "heap");
        }

        [Test]
        public void AnM3uPointingAtAnEndlessStreamWorks()
        {
            var body = Encode("m3uendless.mp3", 2, "stereo", 4.0, "-c:a", "libmp3lame", "-b:a", "128k");
            _server.Add("radio/stream", new Route
            {
                ContentType = "audio/mpeg",
                Endless = position =>
                {
                    var at = (int)(position % body.Length);
                    var count = Math.Min(4096, body.Length - at);
                    var piece = new byte[count];
                    Buffer.BlockCopy(body, at, piece, 0, count);
                    return piece;
                },
            });
            _server.AddText("radio/listen.m3u", _server.Url("radio/stream") + "\n", "audio/x-mpegurl");
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var transport = new HttpTransport();
            var output = new CollectingOutput();
            output.OnWrite = frames =>
            {
                foreach (var channel in output.Channels)
                {
                    channel.Clear();
                }
                if (frames > Rate * 10L)
                {
                    cancel.Cancel();
                }
            };
            new StreamSession(new StreamSessionOptions { Url = _server.Url("radio/listen.m3u"), Layout = Play.All }, transport, output, cancel.Token).Run();
            Assert.IsNull(output.Failure, output.Failure);
            Assert.Greater(output.Frames, Rate * 10L);
        }

        [Test]
        public void LoopRefetchesAFiniteFile()
        {
            var mp3 = Encode("loop.mp3", 1, "mono", 1.0, "-c:a", "libmp3lame", "-b:a", "96k");
            var route = _server.Add("loopme/a.mp3", mp3);
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var transport = new HttpTransport();
            var output = new CollectingOutput();
            output.OnWrite = frames =>
            {
                if (frames > Rate * 3.5)
                {
                    cancel.Cancel();
                }
            };
            new StreamSession(new StreamSessionOptions { Url = _server.Url("loopme/a.mp3"), Layout = Play.All, Loop = true }, transport, output, cancel.Token).Run();
            Assert.GreaterOrEqual(route.Requests, 3, "fetched again each time round");
            Assert.AreEqual(1, output.Formats.Count, "one format: no restart between passes");
        }

        [TestCase("-c:a", "flac", "-f", "flac", "x.flac", "FLAC")]
        [TestCase("-c:a", "mp2", "-f", "mp2", "x.mp2", "Layer II")]
        [TestCase("-c:a", "adpcm_ms", "-f", "wav", "x.wav", "ADPCM")]
        [TestCase("-c:a", "pcm_mulaw", "-f", "wav", "y.wav", "mu-law")]
        public void WhatCantBePlayedIsRefusedByName(string flag, string encoder, string formatFlag, string format, string name, string expected)
        {
            var rate = encoder == "pcm_mulaw" || encoder == "adpcm_ms" ? 48000 : 48000;
            var body = Encode("refuse-" + name, 2, "stereo", 1.0, flag, encoder, formatFlag, format);
            _server.Add("refuse/" + name, body);
            var output = Play.Url(_server.Url("refuse/" + name));
            StringAssert.Contains(expected, output.Failure ?? "(no failure)");
            Assert.AreEqual(0, output.Frames);
            _ = rate;
        }

        [TestCase("x.opus", "Opus", "-c:a", "libopus", "-b:a", "96k", "-f", "ogg")]
        [TestCase("x.ac3", "AC-3", "-c:a", "ac3", "-b:a", "192k", "-f", "ac3")]
        [TestCase("x.eac3", "E-AC-3", "-c:a", "eac3", "-b:a", "192k", "-f", "eac3")]
        [TestCase("ac3.ts", "AC-3", "-c:a", "ac3", "-b:a", "192k", "-f", "mpegts")]
        public void TheOtherCodecsPlayThroughTheSameSession(string name, string codec, string a, string b, string c, string d, string e, string f)
        {
            if (codec == "E-AC-3")
            {
                SystemCodecs.RequireEac3();
            }
            var body = Encode("other-" + name, 2, "stereo", 2.0, a, b, c, d, e, f);
            _server.Add("other/" + name, body);
            var output = Play.Url(_server.Url("other/" + name));
            Assert.IsNull(output.Failure, output.Failure);
            Assert.AreEqual(2, output.Formats.Last().Roles.Length);
            Assert.Greater(output.Frames, Rate);
            StringAssert.Contains(codec.Split('-')[0], output.Formats.Last().Codec);
        }

        [TestCase("ac3", "AC-3")]
        [TestCase("eac3", "E-AC-3")]
        [TestCase("libopus", "Opus")]
        public void TheOtherCodecsPlayFromFragmentedMp4(string encoder, string codec)
        {
            if (codec == "E-AC-3")
            {
                SystemCodecs.RequireEac3();
            }
            var name = "frag-" + encoder + ".mp4";
            var body = Encode(name, 2, "stereo", 2.0, "-c:a", encoder, "-b:a", "192k", "-f", "mp4", "-movflags", encoder == "libopus" ? "frag_keyframe+empty_moov+default_base_moof" : "frag_keyframe+delay_moov+default_base_moof", "-frag_duration", "200000");
            _server.Add("frag/" + name, body);
            var output = Play.Url(_server.Url("frag/" + name));
            Assert.IsNull(output.Failure, output.Failure);
            Assert.Greater(output.Frames, Rate);
            StringAssert.Contains(codec.Split('-')[0], output.Formats.Last().Codec);
        }

        /// <summary>Where the system has no E-AC-3 decoder, the stream ends with its reason, and AC-3 beside it still plays.</summary>
        [Test]
        public void EAc3WithoutASystemDecoderIsRefusedByName()
        {
            if (SystemCodecs.Eac3)
            {
                Assert.Ignore("this machine decodes E-AC-3");
            }
            var body = Encode("noeac3.eac3", 2, "stereo", 1.0, "-c:a", "eac3", "-b:a", "192k", "-f", "eac3");
            _server.Add("noeac3/x.eac3", body);
            var output = Play.Url(_server.Url("noeac3/x.eac3"));
            StringAssert.StartsWith("this stream can't be played: E-AC-3", output.Failure ?? "(no failure)");
            Assert.AreEqual(0, output.Frames);
            TestContext.Progress.WriteLine("E-AC-3 here: " + output.Failure);
        }

        [Test]
        public void AnEncryptedPlaylistIsRefused()
        {
            _server.AddText("enc/media.m3u8", "#EXTM3U\n#EXT-X-TARGETDURATION:4\n#EXT-X-KEY:METHOD=AES-128,URI=\"key.bin\"\n#EXTINF:4,\nseg.ts\n#EXT-X-ENDLIST\n");
            var output = Play.Url(_server.Url("enc/media.m3u8"));
            StringAssert.Contains("encrypted", output.Failure ?? "");
        }

        [Test]
        public void AnHtmlErrorPageIsCalledOut()
        {
            _server.Add("html/x.mp3", Encoding.UTF8.GetBytes("<!doctype html><html><body>not found</body></html>"), "text/html");
            var output = Play.Url(_server.Url("html/x.mp3"));
            StringAssert.Contains("web page", output.Failure ?? "");
        }

        [Test]
        public void AFailureLogsOnlyTheHost()
        {
            var logged = new List<string>();
            var output = Play.Url(_server.Url("missing/file.mp3?token=SECRET123"), configure: o => o.Log = logged.Add);
            Assert.IsNotNull(output.Failure);
            CollectionAssert.IsNotEmpty(logged);
            foreach (var line in logged)
            {
                StringAssert.DoesNotContain("SECRET123", line);
                StringAssert.DoesNotContain("file.mp3", line);
            }
            StringAssert.DoesNotContain("SECRET123", output.Failure);
        }

        [Test]
        public void SevenPointOneInFmp4ArrivesAsConfiguration12WithEveryToneInPlace()
        {
            var order = new[] { "FL", "FR", "FC", "LFE", "BL", "BR", "SL", "SR" };
            var frequencies = new[] { 440.0, 554.0, 698.0, 62.0, 880.0, 1109.0, 1397.0, 1760.0 };
            var roles = new[]
            {
                AudioChannelRole.FrontLeft, AudioChannelRole.FrontRight, AudioChannelRole.Center, AudioChannelRole.Lfe,
                AudioChannelRole.BackLeft, AudioChannelRole.BackRight, AudioChannelRole.SurroundLeft, AudioChannelRole.SurroundRight,
            };
            var raw = Reference.Path_("c12.f32");
            Signals.WriteRaw(raw, Signals.Tones(frequencies, Rate, 3.0));
            var folder = Reference.Path_("hls-c12");
            Directory.CreateDirectory(folder);
            Reference.Run("-f", "f32le", "-ar", Rate.ToString(), "-ch_layout", "7.1", "-i", raw, "-c:a", "aac", "-b:a", "512k",
                "-f", "hls", "-hls_time", "1", "-hls_playlist_type", "vod", "-hls_segment_type", "fmp4", "-hls_fmp4_init_filename", "init.mp4",
                "-hls_segment_filename", Path.Combine(folder, "seg%03d.m4s"), Path.Combine(folder, "media.m3u8"));
            File.Delete(raw);
            MoveInit(folder);
            _server.AddFolder(folder, "c12");
            var output = Play.Url(_server.Url("c12/media.m3u8"));
            if (PlatformDecoderNative.Host == PlatformDecoderHost.Windows)
            {
                // Windows' AAC decoder stops at 5.1: the stream is refused, saying so.
                Assert.AreEqual("this stream can't be played: AAC with 8 channels (the Windows AAC decoder plays up to 5.1)", output.Failure);
                return;
            }
            AacAccuracyTests.RequireSystemAac();
            Assert.IsNull(output.Failure, output.Failure);
            Assert.AreEqual(8, output.Formats.Last().Roles.Length);
            for (var i = 0; i < 8; i++)
            {
                var loudest = Enumerable.Range(0, 8).OrderByDescending(c => AacSurroundTests.Goertzel(output.Channels[c].ToArray(), Rate, frequencies[i])).First();
                Assert.AreEqual(roles[i], output.Formats.Last().Roles[loudest], "tone of " + order[i]);
            }
        }

        [Test]
        public void Downmixing71ToStereoUsesTheStandardCoefficients()
        {
            // PCM, so the mix is all that is measured, on any platform.
            var wav = Encode("downmix.wav", 8, "7.1", 2.0, "-c:a", "pcm_f32le", "-f", "wav");
            _server.Add("downmix/a.wav", wav);
            var wide = Play.Url(_server.Url("downmix/a.wav"));
            var stereo = Play.Url(_server.Url("downmix/a.wav"), ChannelLayouts.Of(2));
            Assert.AreEqual(2, stereo.Formats.Last().Roles.Length);
            var l = stereo.Role(AudioChannelRole.FrontLeft);
            var fl = wide.Role(AudioChannelRole.FrontLeft);
            var c = wide.Role(AudioChannelRole.Center);
            var sl = wide.Role(AudioChannelRole.SurroundLeft);
            var bl = wide.Role(AudioChannelRole.BackLeft);
            for (var i = 1000; i < 40000; i += 97)
            {
                var expected = fl[i] + 0.70710678f * c[i] + 0.70710678f * sl[i] + 0.5f * bl[i];
                Assert.AreEqual(expected, l[i], 1e-5, "frame " + i);
            }
        }

        [Test]
        public void AnUntickedChannelIsDropped()
        {
            AacAccuracyTests.RequireSystemAac();
            var aac = Encode("drop.aac", 6, "5.1", 2.0, "-c:a", "aac", "-b:a", "384k", "-aac_pns", "0", "-f", "adts");
            _server.Add("dropch/a.aac", aac);
            var output = Play.Url(_server.Url("dropch/a.aac"), configure: o => o.Wanted = r => r != AudioChannelRole.Center && r != AudioChannelRole.Lfe);
            CollectionAssert.AreEqual(new[] { AudioChannelRole.FrontLeft, AudioChannelRole.FrontRight, AudioChannelRole.SurroundLeft, AudioChannelRole.SurroundRight }, output.Formats.Last().Roles);
        }
    }
}
