using System;
using System.Collections.Generic;
using System.Linq;

namespace BS.AudioStream.Tests
{
    internal static partial class Suite
    {
        static void Expect(string expected, Func<string> actual, string label)
        {
            string text = actual();
            Require(text == expected, label + " gave: " + (text ?? "no refusal"));
            Console.WriteLine($"  {label}: refused as \"{expected}\"");
        }

        static string RefusalOf(byte[] unit)
        {
            var decoder = new Ac3AudioDecoder();
            decoder.Decode(unit, 0, unit.Length, Ac3StreamEdit.NewPlanes());
            decoder.Dispose();
            return decoder.Refusal;
        }

        static void Refusals()
        {
            byte[] ac3 = Eac3StreamEdit.SplitFrames(EncodeRaw("ac3", "ac3", "stereo", 192, 48000, Signals.Rich(2, 48000, 0.2)))[1];
            foreach (int id in new[] { 9, 10, 17, 31 })
            {
                var frame = (byte[])ac3.Clone();
                frame[5] = (byte)((frame[5] & 7) | id << 3);
                Ac3StreamEdit.RestoreChecks(frame, frame.Length);
                Expect("AC-3 bitstream id " + id + " is not supported", () => RefusalOf(frame), "bitstream id " + id);
            }

            // E-AC-3 goes to the system decoder: it plays where there is one, and is refused by name where there isn't,
            // while AC-3 frames in the same decoder still play.
            byte[] eac3 = Eac3StreamEdit.SplitFrames(EncodeRaw("eac3", "eac3", "stereo", 192, 48000, Signals.Rich(2, 48000, 0.2)))[1];
            string refusal = RefusalOf(eac3);
            if (PlatformDecoderNative.IsAvailable(PlatformDecoderNative.CodecEac3))
            {
                Require(refusal == null, "E-AC-3 refused where the system decodes it: " + refusal);
                Console.WriteLine("  E-AC-3: the system decoder takes it");
            }
            else
            {
                Require(refusal != null && refusal.StartsWith("E-AC-3"), "E-AC-3 without a system decoder gave: " + (refusal ?? "no refusal"));
                Console.WriteLine($"  E-AC-3 without a system decoder: refused as \"{refusal}\"");
            }
            Require(RefusalOf(ac3) == null, "AC-3 refused");
        }
    }
}
