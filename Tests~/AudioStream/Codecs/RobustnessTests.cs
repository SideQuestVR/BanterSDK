using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace BS.AudioStream.Tests
{
    internal static partial class Suite
    {
        // ---- real containers, through the real demuxers

        static string Remux(byte[] stream, string extension, params string[] outputArgs)
        {
            string stem = Stem("remux");
            File.WriteAllBytes(stem + extension, stream);
            var args = new List<string> { "-i", stem + extension, "-map", "0:a:0", "-c", "copy" };
            args.AddRange(outputArgs);
            args.Add(stem + ".out");
            Reference.Run(args.ToArray());
            File.Delete(stem + extension);
            return stem + ".out";
        }

        static SessionSink Play(IMediaParser parser, SessionSink sink, byte[] data, int chunk)
        {
            for (int i = 0; i < data.Length; i += chunk)
                parser.Push(data, i, Math.Min(chunk, data.Length - i));
            sink.Finish();
            return sink;
        }

        static double SameAudio(SessionSink actual, SessionSink expected, out int samples)
        {
            float[][] a = actual.Links[0], b = expected.Links[0];
            Require(a.Length == b.Length, "channels " + a.Length + " vs " + b.Length);
            samples = Math.Min(a[0].Length, b[0].Length);
            double peak = 0;
            for (int c = 0; c < a.Length; c++)
                for (int i = 0; i < samples; i++)
                    peak = Math.Max(peak, Math.Abs(a[c][i] - b[c][i]));
            return peak;
        }

        static void ContainerPaths()
        {
            const int rate = 48000;
            float[][] signal = RoleTones(Layouts["5.1(side)"], rate, 1.5);

            foreach (var (name, codec, container, id, extension) in new[]
            {
                ("AC-3", "ac3", "ac3", AudioCodecId.Ac3, ".ac3"),
                ("E-AC-3", "eac3", "eac3", AudioCodecId.Eac3, ".ec3"),
            })
            {
                if (id == AudioCodecId.Eac3 && SkipWithoutSystemEac3("E-AC-3 container paths"))
                    continue;

                byte[] raw = EncodeRaw(codec, container, "5.1(side)", 448, rate, signal);
                var rawSink = new SessionSink();
                Play(new ElementaryParser(id, rawSink), rawSink, raw, 4096);
                Require(rawSink.Refusals.Count == 0, name + " raw: " + string.Join(",", rawSink.Refusals));

                foreach (var (label, parser, args) in new (string, Func<SessionSink, IMediaParser>, string[])[]
                {
                    ("MPEG-TS", s => new TsDemuxer(s), new[] { "-f", "mpegts" }),
                    ("MPEG-TS, system B descriptors", s => new TsDemuxer(s), new[] { "-mpegts_flags", "system_b", "-f", "mpegts" }),
                    ("fMP4", s => new Mp4Demuxer(s), new[] { "-movflags", "frag_keyframe+delay_moov+default_base_moof", "-f", "mp4" }),
                })
                {
                    string path = Remux(raw, extension, args);
                    byte[] data = File.ReadAllBytes(path);
                    File.Delete(path);

                    var sink = new SessionSink();
                    Play(parser(sink), sink, data, 1317);
                    Require(sink.Refusals.Count == 0, $"{name} in {label}: refused {string.Join(",", sink.Refusals)}");
                    Require(sink.Links.Count == 1, $"{name} in {label}: no audio");
                    Require(sink.Codecs[0].Codec == id, $"{name} in {label}: identified as {sink.Codecs[0].Codec}");

                    double peak = SameAudio(sink, rawSink, out int samples);
                    Console.WriteLine($"  {name} in {label}: identified, {sink.Packets.Count} units, {samples} samples, peak difference from the raw stream {peak:E2}");
                    Require(samples >= rawSink.Links[0][0].Length - 1536 && peak == 0, name + " in " + label + " differs from raw");
                }
            }

            // Opus in fMP4 goes through dOps, big-endian, where Ogg's head is little-endian.
            string ogg = EncodeOpus("5.1", rate, 256, RoleTones(OggRoles("5.1"), rate, 1.5), "-mapping_family 1");
            byte[] oggBytes = File.ReadAllBytes(ogg);
            SessionSink viaOgg = ParseOgg(oggBytes, 0);
            string mp4 = ogg + ".mp4";
            Reference.Run("-i", ogg, "-c", "copy", "-movflags", "frag_keyframe+empty_moov+default_base_moof", "-f", "mp4", mp4);
            byte[] mp4Bytes = File.ReadAllBytes(mp4);
            File.Delete(ogg);
            File.Delete(mp4);

            var viaMp4 = new SessionSink();
            Play(new Mp4Demuxer(viaMp4), viaMp4, mp4Bytes, 999);
            Require(viaMp4.Refusals.Count == 0, "Opus in fMP4: " + string.Join(",", viaMp4.Refusals));
            OpusSetup box = OpusSetup.Parse(viaMp4.Codecs[0].Setup), head = OpusSetup.Parse(viaOgg.Codecs[0].Setup);
            Require(box.PreSkip == head.PreSkip && box.Channels == head.Channels && box.Family == head.Family
                && box.Streams == head.Streams && box.CoupledStreams == head.CoupledStreams && box.Mapping.SequenceEqual(head.Mapping), "dOps and OpusHead disagree");
            double opusPeak = SameAudio(viaMp4, viaOgg, out int opusSamples);
            Console.WriteLine($"  Opus in fMP4: dOps matches OpusHead (pre-skip {box.PreSkip}, family {box.Family}, {box.Streams} streams, {box.CoupledStreams} coupled, map {string.Join(",", box.Mapping)}); {opusSamples} samples, peak difference from Ogg {opusPeak:E2}");
            Require(opusPeak == 0, "Opus in fMP4 differs from Ogg");
        }

        // ---- progressive delivery

        static float[][] FeedRaw(byte[] stream, Func<int, int> chunkAt, out int units, out AudioChannelRole[] layout)
        {
            var decoder = new Ac3AudioDecoder();
            var collector = new PlaneCollector(decoder);
            var framer = new Ac3Framer();
            int offset = 0, turn = 0;
            while (offset < stream.Length)
            {
                int take = Math.Min(chunkAt(turn++), stream.Length - offset);
                framer.Push(stream, offset, take, collector);
                offset += take;
            }
            units = collector.Units;
            layout = decoder.Layout;
            return collector.Result();
        }

        static void CheckChunks(string label, byte[] stream, int frameBytes)
        {
            float[][] whole = FeedRaw(stream, _ => int.MaxValue, out int units, out _);
            var random = new Random(5);
            var plans = new (string, Func<int, int>)[]
            {
                ("1", _ => 1), ("7", _ => 7), ("13", _ => 13), ("one frame", _ => frameBytes), ("one frame + 1", _ => frameBytes + 1),
                ("one frame - 1", _ => frameBytes - 1), ("half a frame", _ => frameBytes / 2 + 1), ("random 1-5000", _ => random.Next(1, 5000)),
            };

            foreach (var (name, plan) in plans)
            {
                float[][] pieces = FeedRaw(stream, plan, out int pieceUnits, out _);
                Require(pieceUnits == units, $"{label} in chunks of {name}: {pieceUnits} units, whole {units}");
                for (int c = 0; c < whole.Length; c++)
                    Require(pieces[c].SequenceEqual(whole[c]), $"{label} in chunks of {name}: channel {c} differs");
            }
            Console.WriteLine($"  {label}: {units} units; chunks of 1, 7, 13, a frame, a frame +/- 1, half a frame and random sizes all match feeding it whole");
        }

        static void Progressive()
        {
            const int rate = 48000;
            float[][] signal = Signals.Rich(6, rate, 1.0);
            byte[] ac3 = EncodeRaw("ac3", "ac3", "5.1(side)", 448, rate, signal);
            CheckChunks("AC-3 5.1", ac3, 1792);

            byte[] ac3Rate = EncodeRaw("ac3", "ac3", "stereo", 192, 44100, Signals.Rich(2, 44100, 1.0));
            CheckChunks("AC-3 44.1 kHz, frames of two sizes", ac3Rate, 836);

            if (!SkipWithoutSystemEac3("E-AC-3 chunking"))
            {
                byte[] eac3 = EncodeRaw("eac3", "eac3", "5.1(side)", 384, rate, signal);
                CheckChunks("E-AC-3 5.1", eac3, 1536);
            }
        }

        sealed class UnitCollector : IAudioFrameSink
        {
            public readonly List<byte[]> Units = new List<byte[]>();

            public void OnFrame(byte[] data, int offset, int count)
            {
                var unit = new byte[count];
                Buffer.BlockCopy(data, offset, unit, 0, count);
                Units.Add(unit);
            }
        }

        /// <summary>
        /// The system decoders take whole access units, so the framer must hand over an independent frame with its dependents
        /// as one, whatever the chunking: checked on the bytes alone, with no decoder.
        /// </summary>
        static void Eac3Framing()
        {
            const int rate = 48000;
            List<byte[]> beds = Eac3StreamEdit.SplitFrames(EncodeRaw("eac3", "eac3", "5.1(side)", 448, rate, Signals.Rich(6, rate, 1.0)));
            List<byte[]> pairs = Eac3StreamEdit.SplitFrames(EncodeRaw("eac3", "eac3", "stereo", 192, rate, Signals.Rich(2, rate, 1.0, 3)));
            int count = Math.Min(beds.Count, pairs.Count);
            var program = new List<byte>();
            var expected = new List<byte[]>();
            for (int i = 0; i < count; i++)
            {
                byte[] dependent = Eac3StreamEdit.AsDependent(pairs[i]);
                program.AddRange(beds[i]);
                program.AddRange(dependent);
                expected.Add(beds[i].Concat(dependent).ToArray());
            }
            byte[] stream = program.ToArray();

            var random = new Random(9);
            foreach (var (name, plan) in new (string, Func<int>)[] { ("whole", () => int.MaxValue), ("1", () => 1), ("7", () => 7), ("1000", () => 1000), ("random", () => random.Next(1, 5000)) })
            {
                var framer = new Ac3Framer();
                var sink = new UnitCollector();
                int offset = 0;
                while (offset < stream.Length)
                {
                    int take = Math.Min(plan(), stream.Length - offset);
                    framer.Push(stream, offset, take, sink);
                    offset += take;
                }
                // The last unit is only known to be complete when another starts.
                Require(sink.Units.Count == count - 1, $"chunks of {name}: {sink.Units.Count} units, expected {count - 1}");
                for (int i = 0; i < sink.Units.Count; i++)
                    Require(sink.Units[i].SequenceEqual(expected[i]), $"chunks of {name}: unit {i} is not its frame and dependent");
            }
            Console.WriteLine($"  E-AC-3 with a dependent substream: {count - 1} units, each the independent frame and its dependent, in every chunking");

            // Without dependents, frames go straight through, not a frame late.
            var direct = new UnitCollector();
            byte[] plain = beds.SelectMany(f => f).ToArray();
            new Ac3Framer().Push(plain, 0, plain.Length, direct);
            Require(direct.Units.Count >= beds.Count - 1, "plain E-AC-3: " + direct.Units.Count + " units of " + beds.Count);
            Console.WriteLine($"  E-AC-3 without dependents: {direct.Units.Count} units of {beds.Count} frames, one frame each");
        }

        // ---- damaged bytes

        static void Mutate(byte[] data, int start, int end, Random random)
        {
            if (end <= start)
                return;

            switch (random.Next(6))
            {
                case 0:
                    for (int i = random.Next(1, 9); i > 0; i--)
                        data[random.Next(start, end)] ^= (byte)(1 << random.Next(8));
                    break;
                case 1:
                    int at = random.Next(start, end);
                    int run = Math.Min(random.Next(1, 64), end - at);
                    for (int i = 0; i < run; i++)
                        data[at + i] = (byte)random.Next(256);
                    break;
                case 2:
                    at = random.Next(start, end);
                    run = Math.Min(random.Next(1, 256), end - at);
                    Array.Clear(data, at, run);
                    break;
                case 3:
                    at = random.Next(start, end);
                    run = Math.Min(random.Next(1, 256), end - at);
                    for (int i = 0; i < run; i++)
                        data[at + i] = 0xff;
                    break;
                case 4:
                    // Field-dense territory: the first bytes of a frame are all header.
                    data[start + random.Next(Math.Min(48, end - start))] = (byte)random.Next(256);
                    break;
                default:
                    int from = random.Next(start, end);
                    at = random.Next(start, end);
                    run = Math.Min(random.Next(1, 128), end - Math.Max(from, at));
                    if (run > 0)
                        Buffer.BlockCopy(data, from, data, at, run);
                    break;
            }
        }

        /// <summary>Decodes without the facade's safety net, so anything that would have been caught shows up here instead.</summary>
        sealed class BareDecoders : IAudioFrameSink
        {
            readonly Ac3Decoder _ac3 = new Ac3Decoder();
            readonly float[][] _planes = Ac3StreamEdit.NewPlanes();
            public int Units;
            public readonly Dictionary<string, int> Outcomes = new Dictionary<string, int>();

            public void OnFrame(byte[] data, int offset, int count)
            {
                Units++;
                var info = default(Ac3SyncInfo);
                bool enhanced = Ac3SyncInfo.TryRead(data, offset, count, ref info) && info.Enhanced;
                string outcome;
                if (enhanced)
                {
                    // Left to the system decoder; what is checked here is that the framer found it whole.
                    outcome = "E-AC-3 unit framed";
                }
                else
                {
                    _ac3.Decode(data, offset, count, _planes);
                    outcome = _ac3.LastError ?? "decoded";
                }
                Outcomes[outcome] = Outcomes.TryGetValue(outcome, out int n) ? n + 1 : 1;
            }
        }

        static byte[] Pieces(List<byte[]> frames, bool ac3, Random random, int mutateOneIn)
        {
            var output = new List<byte>();
            foreach (byte[] original in frames)
            {
                var frame = (byte[])original.Clone();
                if (random.Next(mutateOneIn) == 0)
                {
                    int times = random.Next(1, 4);
                    for (int i = 0; i < times; i++)
                        Mutate(frame, 2, frame.Length - 2, random);

                    // Repaired, so the damage reaches the parser behind the check. Most damage would otherwise stop there.
                    if (random.Next(8) != 0)
                    {
                        if (ac3)
                            Ac3StreamEdit.RestoreChecks(frame, frame.Length);
                        else
                            Eac3StreamEdit.RestoreCheck(frame, frame.Length);
                    }
                }

                switch (random.Next(40))
                {
                    case 0:
                        continue;
                    case 1:
                        output.AddRange(frame.Take(random.Next(frame.Length)));
                        continue;
                    case 2:
                        output.AddRange(Enumerable.Range(0, random.Next(300)).Select(_ => (byte)random.Next(256)));
                        break;
                }
                output.AddRange(frame);
            }
            return output.ToArray();
        }

        static byte[] OggPieces(byte[] file, Random random, int mutateOneIn)
        {
            var output = new List<byte>();
            int offset = 0;
            while (offset + 27 <= file.Length)
            {
                int segments = file[offset + 26];
                int size = 27 + segments;
                for (int s = 0; s < segments; s++)
                    size += file[offset + 27 + s];

                var page = new byte[size];
                Buffer.BlockCopy(file, offset, page, 0, size);
                offset += size;

                if (random.Next(mutateOneIn) == 0)
                {
                    int times = random.Next(1, 4);
                    for (int i = 0; i < times; i++)
                        Mutate(page, 4, page.Length, random);
                    if (random.Next(8) != 0)
                        RepairPage(page);
                }

                switch (random.Next(40))
                {
                    case 0:
                        continue;
                    case 1:
                        output.AddRange(page.Take(random.Next(page.Length)));
                        continue;
                }
                output.AddRange(page);
            }
            return output.ToArray();
        }

        static readonly uint[] OggCrc = Enumerable.Range(0, 256).Select(i =>
        {
            uint crc = (uint)i << 24;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc & 0x80000000u) != 0 ? (crc << 1) ^ 0x04c11db7u : crc << 1;
            return crc;
        }).ToArray();

        /// <summary>The page's own size can have been mutated, so the check covers what the mutated page claims, as far as it goes.</summary>
        static void RepairPage(byte[] page)
        {
            int segments = page[26];
            int size = 27 + segments;
            if (size > page.Length)
                return;
            for (int s = 0; s < segments; s++)
                size += page[27 + s];
            size = Math.Min(size, page.Length);

            Array.Clear(page, 22, 4);
            uint crc = 0;
            for (int i = 0; i < size; i++)
                crc = (crc << 8) ^ OggCrc[((crc >> 24) ^ page[i]) & 0xff];
            page[22] = (byte)crc;
            page[23] = (byte)(crc >> 8);
            page[24] = (byte)(crc >> 16);
            page[25] = (byte)(crc >> 24);
        }

        static void Fuzz()
        {
            int scale = int.Parse(Environment.GetEnvironmentVariable("CODEC_FUZZ") ?? "1");
            const int rate = 48000;
            float[][] signal = Signals.Rich(6, rate, 0.5);

            var counts = new FeatureCounts();
            var cases = new List<(string, List<byte[]>, bool)>
            {
                ("AC-3 5.1", Eac3StreamEdit.SplitFrames(EncodeRaw("ac3", "ac3", "5.1(side)", 448, rate, signal)), true),
                ("AC-3 stereo", Eac3StreamEdit.SplitFrames(EncodeRaw("ac3", "ac3", "stereo", 192, 44100, Signals.Rich(2, 44100, 0.5))), true),
                ("E-AC-3 5.1", Eac3StreamEdit.SplitFrames(EncodeRaw("eac3", "eac3", "5.1(side)", 384, rate, signal)), false),
            };

            Console.WriteLine("| target | iterations | exceptions | slowest iteration ms | most allocated in one iteration KB |");
            Console.WriteLine("|---|---|---|---|---|");
            foreach (var (name, frames, ac3) in cases)
            {
                int iterations = 3000 * scale;
                long slowest = 0, allocated = 0;
                var outcomes = new Dictionary<string, int>();
                for (int i = 0; i < iterations; i++)
                {
                    var random = new Random(i * 31 + name.Length);
                    byte[] data = Pieces(frames, ac3, random, 1 + random.Next(4));
                    var bare = new BareDecoders();
                    var framer = new Ac3Framer();
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    var clock = Stopwatch.StartNew();
                    try
                    {
                        int offset = 0;
                        while (offset < data.Length)
                        {
                            int take = Math.Min(random.Next(1, 9000), data.Length - offset);
                            framer.Push(data, offset, take, bare);
                            offset += take;
                        }
                    }
                    catch (Exception e)
                    {
                        throw new InvalidOperationException($"{name}, iteration {i}: {e}");
                    }
                    slowest = Math.Max(slowest, clock.ElapsedMilliseconds);
                    allocated = Math.Max(allocated, GC.GetAllocatedBytesForCurrentThread() - before);
                    foreach (var pair in bare.Outcomes)
                        outcomes[pair.Key] = outcomes.TryGetValue(pair.Key, out int n) ? n + pair.Value : pair.Value;
                }
                Console.WriteLine($"| {name} | {iterations} | 0 | {slowest} | {allocated / 1024} |");
                Console.WriteLine("    reached: " + string.Join("; ", outcomes.OrderByDescending(p => p.Value).Select(p => p.Key + " " + p.Value)));
            }

            foreach (var (name, layout, kbps, extra) in new[]
            {
                ("Ogg Opus stereo", "stereo", 96, "-page_duration 100000"),
                ("Ogg Opus 7.1", "7.1", 384, "-mapping_family 1 -page_duration 100000"),
                ("Ogg Opus voice (SILK and hybrid)", "mono", 16, "-application voip -page_duration 100000"),
            })
            {
                string path = EncodeOpus(layout, rate, kbps, Signals.Rich(OggRoles(layout).Length, rate, 0.5), extra);
                byte[] file = File.ReadAllBytes(path);
                File.Delete(path);

                int iterations = 1500 * scale;
                int damaged = 0;
                long slowest = 0, allocated = 0;
                for (int i = 0; i < iterations; i++)
                {
                    var random = new Random(i * 17 + layout.Length);
                    byte[] data = OggPieces(file, random, 1 + random.Next(3));
                    var sink = new FuzzSink();
                    var demuxer = new OggDemuxer(sink);
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    var clock = Stopwatch.StartNew();
                    try
                    {
                        int offset = 0;
                        while (offset < data.Length)
                        {
                            int take = Math.Min(random.Next(1, 20000), data.Length - offset);
                            demuxer.Push(data, offset, take);
                            offset += take;
                        }
                    }
                    catch (Exception e)
                    {
                        throw new InvalidOperationException($"{name}, iteration {i}: {e}");
                    }
                    slowest = Math.Max(slowest, clock.ElapsedMilliseconds);
                    allocated = Math.Max(allocated, GC.GetAllocatedBytesForCurrentThread() - before);
                    damaged += sink.Damaged;
                }
                Console.WriteLine($"| {name} | {iterations} | 0 | {slowest} | {allocated / 1024} | ({damaged} packets refused by the boundary)");
            }

            FuzzFloods();
            FuzzSetups();
            FuzzRawPackets();
        }

        /// <summary>Takes decoders from OnCodec like the session does, and counts what the Concentus boundary refused.</summary>
        sealed class FuzzSink : IMediaSink
        {
            OpusAudioDecoder _decoder;
            readonly float[][] _planes = NewOpusPlanes();
            public int Damaged => _decoder?.DamagedPackets ?? 0;

            public void OnCodec(AudioCodecConfig config) => _decoder = new OpusAudioDecoder(config);
            public void OnRefused(string what) { }
            public void OnTimestamp(long ptsUs) { }
            public void OnFrame(byte[] data, int offset, int count) => _decoder?.Decode(data, offset, count, _planes);
        }

        static float[][] NewOpusPlanes()
        {
            var planes = new float[8][];
            for (int i = 0; i < 8; i++)
                planes[i] = new float[OpusSetup.MaxPacketSamples];
            return planes;
        }

        /// <summary>Streams built to be as expensive as they can be: endless candidates, and claims of the largest legal sizes.</summary>
        static void FuzzFloods()
        {
            const int megabytes = 16;
            var results = new List<string>();

            byte[] syncFlood = new byte[megabytes << 20];
            for (int i = 0; i + 1 < syncFlood.Length; i += 2)
            {
                syncFlood[i] = 0x0b;
                syncFlood[i + 1] = 0x77;
            }

            byte[] aligned = new byte[megabytes << 20];
            for (int i = 0; i + 8 < aligned.Length; i += 1792)
            {
                aligned[i] = 0x0b;
                aligned[i + 1] = 0x77;
                aligned[i + 4] = 0x1c; // 48 kHz, 448 kbps
                aligned[i + 5] = 0x40; // bsid 8
            }

            byte[] oggFlood = new byte[megabytes << 20];
            for (int i = 0; i + 4 < oggFlood.Length; i += 4)
            {
                oggFlood[i] = (byte)'O';
                oggFlood[i + 1] = (byte)'g';
                oggFlood[i + 2] = (byte)'g';
                oggFlood[i + 3] = (byte)'S';
            }

            // Pages that chain into one packet that never ends, each claiming the most it can.
            byte[] endless = EndlessPacket(4096);

            // Headers that pass every cheap test and claim the largest page, with nothing where the next one should be, so each
            // costs a full check before it can be dismissed.
            byte[] claims = new byte[megabytes << 20];
            for (int i = 0; i + 282 < claims.Length; i += 512)
            {
                claims[i] = (byte)'O';
                claims[i + 1] = (byte)'g';
                claims[i + 2] = (byte)'g';
                claims[i + 3] = (byte)'S';
                claims[i + 26] = 255;
                for (int lace = 0; lace < 255; lace++)
                    claims[i + 27 + lace] = 255;
            }

            var noise = new byte[megabytes << 20];
            new Random(3).NextBytes(noise);

            foreach (var (name, data) in new[] { ("0B 77 repeated", syncFlood), ("frames with good sync and no check", aligned), ("random bytes", noise) })
            {
                var bare = new BareDecoders();
                var clock = Stopwatch.StartNew();
                new Ac3Framer().Push(data, 0, data.Length, bare);
                Console.WriteLine($"  Ac3Framer, {data.Length >> 20} MiB of {name}: {clock.ElapsedMilliseconds} ms, {bare.Units} units");
                Require(clock.ElapsedMilliseconds < 20000, name + " too slow");
            }

            foreach (var (name, data) in new[] { ("OggS repeated", oggFlood), ("random bytes", noise), ("one packet that never ends", endless), ("largest-page claims", claims) })
            {
                var sink = new SessionSink { KeepPackets = false };
                long before = GC.GetAllocatedBytesForCurrentThread();
                var clock = Stopwatch.StartNew();
                new OggDemuxer(sink).Push(data, 0, data.Length);
                long used = (GC.GetAllocatedBytesForCurrentThread() - before) / 1024;
                Console.WriteLine($"  OggDemuxer, {data.Length >> 20} MiB of {name}: {clock.ElapsedMilliseconds} ms, allocated {used} KB, refusals: {string.Join(",", sink.Refusals)}");
                Require(clock.ElapsedMilliseconds < 20000, name + " too slow");
                Require(used < 8 * 1024, name + " allocated too much");
            }
        }

        static byte[] EndlessPacket(int pages)
        {
            var output = new List<byte>();
            byte[] head = { (byte)'O', (byte)'p', (byte)'u', (byte)'s', (byte)'H', (byte)'e', (byte)'a', (byte)'d', 1, 2, 0x38, 1, 0x80, 0xbb, 0, 0, 0, 0, 0 };
            output.AddRange(OggPage(2, 0, 0, new byte[] { (byte)head.Length }, head));
            for (int page = 1; page <= pages; page++)
            {
                var lacing = Enumerable.Repeat((byte)255, 16).ToArray();
                output.AddRange(OggPage(page == 1 ? 0 : 1, -1, page, lacing, new byte[255 * 16]));
            }
            return output.ToArray();
        }

        static byte[] OggPage(int flags, long granule, int sequence, byte[] lacing, byte[] body)
        {
            var page = new byte[27 + lacing.Length + body.Length];
            page[0] = (byte)'O'; page[1] = (byte)'g'; page[2] = (byte)'g'; page[3] = (byte)'S';
            page[5] = (byte)flags;
            BitConverter.GetBytes(granule).CopyTo(page, 6);
            BitConverter.GetBytes(77).CopyTo(page, 14);
            BitConverter.GetBytes(sequence).CopyTo(page, 18);
            page[26] = (byte)lacing.Length;
            lacing.CopyTo(page, 27);
            body.CopyTo(page, 27 + lacing.Length);
            RepairPage(page);
            return page;
        }

        static void FuzzSetups()
        {
            var random = new Random(11);
            int refused = 0, accepted = 0;
            var planes = NewOpusPlanes();
            for (int i = 0; i < 40000; i++)
            {
                byte[] setup = new byte[random.Next(0, 64)];
                random.NextBytes(setup);
                if (setup.Length >= 8 && random.Next(2) == 0)
                    new byte[] { (byte)'O', (byte)'p', (byte)'u', (byte)'s', (byte)'H', (byte)'e', (byte)'a', (byte)'d' }.CopyTo(setup, 0);
                if (setup.Length >= 19 && random.Next(2) == 0)
                {
                    setup[8] = 1;
                    setup[9] = (byte)random.Next(1, 9);
                    setup[18] = (byte)random.Next(0, 3);
                }

                var decoder = new OpusAudioDecoder(new AudioCodecConfig { Codec = AudioCodecId.Opus, Setup = setup });
                if (decoder.Refusal == null)
                {
                    accepted++;
                    byte[] packet = new byte[random.Next(1, 200)];
                    random.NextBytes(packet);
                    decoder.Decode(packet, 0, packet.Length, planes);
                }
                else
                {
                    refused++;
                }
            }
            Console.WriteLine($"  OpusSetup and decoder construction: 40000 random setups, {refused} refused, {accepted} accepted and decoded a random packet, 0 exceptions");
        }

        /// <summary>Random packets straight at the Concentus boundary, for each mode's table of contents.</summary>
        static void FuzzRawPackets()
        {
            var random = new Random(23);
            var planes = NewOpusPlanes();
            int damaged = 0, played = 0;

            foreach (int channels in new[] { 1, 2 })
            {
                byte[] head = new byte[19];
                "OpusHead".Select(c => (byte)c).ToArray().CopyTo(head, 0);
                head[8] = 1;
                head[9] = (byte)channels;
                var decoder = new OpusAudioDecoder(new AudioCodecConfig { Codec = AudioCodecId.Opus, Setup = head });
                for (int i = 0; i < 150000; i++)
                {
                    byte[] packet = new byte[random.Next(1, 1400)];
                    random.NextBytes(packet);
                    if (decoder.Decode(packet, 0, packet.Length, planes) > 0)
                        played++;
                }
                damaged += decoder.DamagedPackets;
            }
            Console.WriteLine($"  Concentus boundary: 300000 random packets, {played} decoded to audio, {damaged} refused, 0 exceptions escaped");
        }

        // ---- cost

        static double Measure(Action decodeAll, double seconds, int channels)
        {
            decodeAll();
            var clock = Stopwatch.StartNew();
            int runs = 0;
            while (clock.Elapsed.TotalSeconds < 1.5)
            {
                decodeAll();
                runs++;
            }
            return clock.Elapsed.TotalMilliseconds / runs / seconds / channels;
        }

        static void Cost()
        {
            const int rate = 48000;
            const double seconds = 4.0;
            Console.WriteLine("| stream | channels | ms of CPU per second of audio per channel | share of one core, whole stream |");
            Console.WriteLine("|---|---|---|---|");

            foreach (var (name, codec, container, layout, channels, kbps) in new[]
            {
                ("AC-3 5.1 448k", "ac3", "ac3", "5.1(side)", 6, 448),
                ("AC-3 stereo 192k", "ac3", "ac3", "stereo", 2, 192),
                ("E-AC-3 5.1 384k", "eac3", "eac3", "5.1(side)", 6, 384),
                ("E-AC-3 stereo 128k", "eac3", "eac3", "stereo", 2, 128),
            })
            {
                if (codec == "eac3" && SkipWithoutSystemEac3(name))
                    continue;
                byte[] stream = EncodeRaw(codec, container, layout, kbps, rate, Signals.Rich(channels, rate, seconds));
                List<byte[]> frames = Eac3StreamEdit.SplitFrames(stream);
                var decoder = new Ac3AudioDecoder();
                float[][] planes = Ac3StreamEdit.NewPlanes();
                double cost = Measure(() =>
                {
                    foreach (byte[] frame in frames)
                        decoder.Decode(frame, 0, frame.Length, planes);
                }, seconds, channels);
                Console.WriteLine($"| {name} | {channels} | {cost:F3} | {cost * channels / 10:F2}% |");
            }

            foreach (var (name, layout, channels, kbps, extra) in new[]
            {
                ("Opus stereo 128k (CELT)", "stereo", 2, 128, ""),
                ("Opus stereo 32k (hybrid)", "stereo", 2, 32, "-application voip"),
                ("Opus mono 12k (SILK)", "mono", 1, 12, "-application voip"),
                ("Opus 5.1 256k", "5.1", 6, 256, "-mapping_family 1"),
                ("Opus 7.1 384k", "7.1", 8, 384, "-mapping_family 1"),
            })
            {
                string path = EncodeOpus(layout, rate, kbps, Signals.Rich(channels, rate, seconds), extra);
                SessionSink sink = ParseOgg(File.ReadAllBytes(path), 0, decode: false);
                var config = sink.Codecs[0];
                File.Delete(path);

                var decoder = new OpusAudioDecoder(config);
                float[][] planes = NewOpusPlanes();
                List<byte[]> packets = sink.Packets;
                double cost = Measure(() =>
                {
                    foreach (byte[] packet in packets)
                        decoder.Decode(packet, 0, packet.Length, planes);
                }, seconds, channels);
                Console.WriteLine($"| {name} | {channels} | {cost:F3} | {cost * channels / 10:F2}% |");
            }
        }
    }
}
