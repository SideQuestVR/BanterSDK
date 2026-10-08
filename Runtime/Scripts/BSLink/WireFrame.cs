using System.Collections.Generic;

namespace BS
{
    /// <summary>
    /// The batch frame both directions of the bus use: a marker, then each message as its length,
    /// a colon and the message itself (<c>‽12:!e!bm!¶3§hi…</c>). The receiver jumps from length to
    /// length and never looks inside a message, so a delimiter in one message can't cut the batch
    /// or turn its tail into a message of its own. The marker only tells a batch from a message
    /// sent on its own (responses, reload).
    ///
    /// Lengths count UTF-16 code units, which C# <c>Length</c> and JS <c>length</c> agree on.
    /// Mirror of Injection~/src/game/wire-frame.ts; keep the two in step.
    /// </summary>
    public static class WireFrame
    {
        /// <summary>A length longer than this many digits is a corrupt frame, not a message.</summary>
        const int MaxLengthDigits = 9;

        /// <summary>
        /// The marker followed by every message. Like <c>string.Join</c>, it measures the frame
        /// first and copies each message once; <paramref name="buffer"/> is scratch space the caller
        /// keeps between frames (grown here when too small), so the frame string is the only
        /// allocation.
        /// </summary>
        public static string Pack(string marker, IReadOnlyList<string> messages, ref char[] buffer)
        {
            int total = marker.Length;
            for (int i = 0; i < messages.Count; i++)
            {
                int length = messages[i]?.Length ?? 0;
                total += DigitCount(length) + 1 + length;
            }
            if (buffer == null || buffer.Length < total)
            {
                buffer = new char[System.Math.Max(total, buffer == null ? 0 : buffer.Length * 2)];
            }

            marker.CopyTo(0, buffer, 0, marker.Length);
            int position = marker.Length;
            for (int i = 0; i < messages.Count; i++)
            {
                var message = messages[i] ?? string.Empty;
                int length = message.Length;
                int digits = DigitCount(length);
                for (int d = position + digits - 1; d >= position; d--)
                {
                    buffer[d] = (char)('0' + length % 10);
                    length /= 10;
                }
                position += digits;
                buffer[position++] = ':';
                message.CopyTo(0, buffer, position, message.Length);
                position += message.Length;
            }
            return new string(buffer, 0, total);
        }

        /// <summary>
        /// Reads the messages of a frame starting at <paramref name="start"/> (just after the
        /// marker) into <paramref name="into"/>. Returns false when a length is malformed or runs
        /// past the end; the messages read before that point are left in <paramref name="into"/>.
        /// </summary>
        public static bool Unpack(string frame, int start, List<string> into)
        {
            int position = start;
            int end = frame.Length;
            while (position < end)
            {
                int length = 0;
                int digits = 0;
                while (position < end && frame[position] >= '0' && frame[position] <= '9')
                {
                    if (++digits > MaxLengthDigits) return false;
                    length = length * 10 + (frame[position] - '0');
                    position++;
                }
                if (digits == 0 || position >= end || frame[position] != ':') return false;
                position++;
                if (length > end - position) return false;
                into.Add(frame.Substring(position, length));
                position += length;
            }
            return true;
        }

        static int DigitCount(int value)
        {
            int digits = 1;
            while (value >= 10)
            {
                value /= 10;
                digits++;
            }
            return digits;
        }
    }
}
