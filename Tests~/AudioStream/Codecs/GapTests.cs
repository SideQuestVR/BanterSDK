using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BS.AudioStream.Tests
{
    internal static partial class Suite
    {
        /// <summary>The stream type and any audio descriptors a transport stream really carries, read from its PMT.</summary>
        static string TransportTypes(byte[] data)
        {
            int pmtPid = -1;
            for (int p = 0; p + 188 <= data.Length; p += 188)
            {
                int pid = (data[p + 1] & 0x1f) << 8 | data[p + 2];
                bool start = (data[p + 1] & 0x40) != 0;
                if (!start)
                    continue;

                int payload = p + 4 + ((data[p + 3] & 0x20) != 0 ? 1 + data[p + 4] : 0);
                int section = payload + 1 + data[payload];
                if (pid == 0 && pmtPid < 0)
                {
                    pmtPid = (data[section + 10] & 0x1f) << 8 | data[section + 11];
                }
                else if (pid == pmtPid)
                {
                    int length = (data[section + 1] & 0x0f) << 8 | data[section + 2];
                    int programInfo = (data[section + 10] & 0x0f) << 8 | data[section + 11];
                    int at = section + 12 + programInfo;
                    int end = section + 3 + length - 4;
                    var found = new List<string>();
                    while (at + 5 <= end)
                    {
                        int type = data[at];
                        int info = (data[at + 3] & 0x0f) << 8 | data[at + 4];
                        var descriptors = new List<string>();
                        for (int d = at + 5; d + 2 <= at + 5 + info; d += 2 + data[d + 1])
                            descriptors.Add("0x" + data[d].ToString("x2"));
                        found.Add($"stream type 0x{type:x2}, descriptors [{string.Join(" ", descriptors)}]");
                        at += 5 + info;
                    }
                    return string.Join("; ", found);
                }
            }
            return "no PMT found";
        }

        static void TransportCoverage()
        {
            const int rate = 48000;
            float[][] signal = RoleTones(Layouts["5.1(side)"], rate, 0.5);
            foreach (var (name, codec, extension) in new[] { ("AC-3", "ac3", ".ac3"), ("E-AC-3", "eac3", ".ec3") })
            {
                byte[] raw = EncodeRaw(codec, codec, "5.1(side)", 448, rate, signal);
                foreach (var (label, args) in new[]
                {
                    ("default", new[] { "-f", "mpegts" }),
                    ("system B", new[] { "-mpegts_flags", "system_b", "-f", "mpegts" }),
                })
                {
                    string path = Remux(raw, extension, args);
                    byte[] data = File.ReadAllBytes(path);
                    File.Delete(path);
                    Console.WriteLine($"  {name}, {label}: {TransportTypes(data)}");
                }
            }
        }

        /// <summary>A page that fails its check costs that page's packets and nothing more: the next page plays as if it never happened.</summary>
        static void OggDamage()
        {
            string path = EncodeOpus("stereo", 48000, 96, Signals.Rich(2, 48000, 2.0), "-page_duration 100000");
            byte[] file = File.ReadAllBytes(path);
            File.Delete(path);

            SessionSink whole = ParseOgg(file, 0, decode: false);
            int pages = 0;
            for (int offset = 0; offset + 27 <= file.Length; pages++)
                offset = OggPageAfter(file, pages + 1);

            for (int page = 2; page < pages; page++)
            {
                int at = OggPageAfter(file, page), next = OggPageAfter(file, page + 1);
                var damaged = (byte[])file.Clone();
                damaged[(at + next) / 2] ^= 0x40;

                int inPage = 0;
                int segments = file[at + 26];
                for (int s = 0; s < segments; s++)
                    if (file[at + 27 + s] < 255)
                        inPage++;

                SessionSink sink = ParseOgg(damaged, 0, decode: false);
                Require(sink.Refusals.Count == 0, "page " + page + " refused");
                Require(sink.Packets.Count == whole.Packets.Count - inPage, $"page {page}: lost {whole.Packets.Count - sink.Packets.Count}, the page held {inPage}");
            }
            Console.WriteLine($"  damaging any one of {pages - 2} audio pages costs exactly that page's packets ({whole.Packets.Count} in the stream)");

            // Without its header there is nothing to decode, so a damaged first page costs all of it, and says so if pages keep coming.
            var headless = (byte[])file.Clone();
            headless[30] ^= 0x40;
            SessionSink lost = ParseOgg(headless, 0, decode: false);
            Console.WriteLine($"  damaging the header page: {lost.Packets.Count} packets, refusals: {string.Join(",", lost.Refusals)}");
            Require(lost.Packets.Count == 0, "audio without a header");

            // The same stream with pages a second long, as ffmpeg writes by default: the unit of loss is what differs from AC-3.
            string longPages = EncodeOpus("stereo", 48000, 96, Signals.Rich(2, 48000, 2.0), "");
            byte[] longFile = File.ReadAllBytes(longPages);
            File.Delete(longPages);
            int total = ParseOgg(longFile, 0, decode: false).Packets.Count;
            int count = 0;
            for (int offset = 0; offset + 27 <= longFile.Length; count++)
                offset = OggPageAfter(longFile, count + 1);
            Console.WriteLine($"  default pages: {total} packets on {count - 2} audio pages, so one damaged page costs about {total / Math.Max(1, count - 2)} packets, where an AC-3 frame costs one");
        }

        /// <summary>The output gain a header asks for, which RFC 7845 requires a player to apply.</summary>
        static void OpusGainAndEndless()
        {
            string path = EncodeOpus("stereo", 48000, 128, Signals.Rich(2, 48000, 2.0), "-page_duration 100000");
            byte[] file = File.ReadAllBytes(path);
            File.Delete(path);

            float[] plain = ParseOgg(file, 0).Links[0][0];

            foreach (double decibels in new[] { 6.0, -12.0 })
            {
                var louder = (byte[])file.Clone();
                int firstPage = OggPageAfter(file, 1);
                var page = new byte[firstPage];
                Array.Copy(louder, page, firstPage);

                // OpusHead is the page's whole body; the gain is at byte 16 of it, as a signed Q7.8 number of decibels.
                int head = 27 + page[26];
                short q8 = (short)Math.Round(decibels * 256);
                page[head + 16] = (byte)q8;
                page[head + 17] = (byte)(q8 >> 8);
                RepairPage(page);
                Array.Copy(page, louder, firstPage);

                float[] gained = ParseOgg(louder, 0).Links[0][0];
                double plainPower = plain.Sum(x => (double)x * x), gainedPower = gained.Sum(x => (double)x * x);
                double measured = 10 * Math.Log10(gainedPower / plainPower);
                Console.WriteLine($"  output gain of {decibels:+0;-0} dB in the header: measured {measured:+0.00;-0.00} dB");

                // Loud passages clip at full scale once gained, which only ever lowers the measured figure.
                Require(Math.Abs(measured - decibels) < (decibels > 0 ? 1.0 : 0.05), "gain " + measured);
            }

            // An endless stream never sends its last page. With that flag gone the demuxer trims nothing and just keeps going.
            var endless = (byte[])file.Clone();
            int pages = 0, offset = 0, last = 0;
            while (offset + 27 <= endless.Length)
            {
                last = offset;
                int segments = endless[offset + 26];
                int size = 27 + segments;
                for (int s = 0; s < segments; s++)
                    size += endless[offset + 27 + s];
                offset += size;
                pages++;
            }
            var lastPage = new byte[offset - last];
            Array.Copy(endless, last, lastPage, 0, lastPage.Length);
            Require((lastPage[5] & 4) != 0, "the file's last page should end the stream");
            lastPage[5] &= 0xfb;
            RepairPage(lastPage);
            Array.Copy(lastPage, 0, endless, last, lastPage.Length);

            SessionSink running = ParseOgg(endless, 1500);
            SessionSink ended = ParseOgg(file, 1500);
            int played = running.Links[0][0].Length;
            Require(running.Refusals.Count == 0 && running.Packets.Count == ended.Packets.Count, "endless packets");
            Require(played >= ended.Links[0][0].Length, "endless samples");
            Console.WriteLine($"  a stream that never ends: {pages} pages, {running.Packets.Count} packets, {played} samples, none trimmed ({ended.Links[0][0].Length} when it does end)");
        }
    }
}
