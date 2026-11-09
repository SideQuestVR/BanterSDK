using System;
using System.IO;
using System.Linq;
using System.Threading;
using BS.AudioStream;
using BS.VideoStream;

namespace VideoStreamTests
{
    // Not a test: counts what reading a real file costs through the range source, with no network involved.
    static class Probe
    {
        public static int Requests(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var t = new FakeTransport { Bytes = bytes };
            var source = new HttpRangeSource(new StreamFetcher(t, CancellationToken.None, 2000), new Uri("https://files.example.test/a.mp4"));
            var sink = new Collect { Hash = false };
            var reader = new PlainMp4Reader(source, sink);
            if (!reader.Open()) { Console.WriteLine("refused: " + reader.Refusal); return 1; }
            var opened = t.Requests;
            var samples = 0;
            while (reader.ReadNext()) samples++;
            Console.WriteLine($"{Path.GetFileName(path)}: {bytes.Length / 1e6:F1} MB, {samples} samples, open {opened} requests, read {t.Requests - opened} requests, served {t.Served / 1e6:F1} MB");
            return 0;
        }
    }
}
