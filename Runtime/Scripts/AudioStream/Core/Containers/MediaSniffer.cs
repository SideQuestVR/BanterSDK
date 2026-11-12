using System;

namespace BS.AudioStream
{
    internal enum MediaKind
    {
        /// <summary>Not enough bytes yet to say.</summary>
        NeedMore,
        Unknown,
        Playlist,
        Wav,
        Mp3,
        Adts,
        Ts,
        Mp4,
        Ogg,
        Ac3,
        Eac3,
        Flac,
        Html,
    }

    /// <summary>
    /// What a stream of bytes is. The bytes come first, then the Content-Type, then the name, because servers mislabel
    /// things. Reads only the bytes it is given.
    /// </summary>
    internal static class MediaSniffer
    {
        /// <summary>How much of a stream's start is enough to decide on bytes alone.</summary>
        public const int WindowBytes = 16 * 1024;

        public static MediaKind Detect(byte[] d, int count, bool endOfData, string contentType, string url)
        {
            var kind = FromBytes(d, count, endOfData);
            if (kind != MediaKind.Unknown)
            {
                return kind;
            }
            kind = FromContentType(contentType);
            return kind != MediaKind.Unknown ? kind : FromName(url);
        }

        static MediaKind FromBytes(byte[] d, int count, bool endOfData)
        {
            var at = 0;
            if (count >= 3 && d[0] == 0xEF && d[1] == 0xBB && d[2] == 0xBF)
            {
                at = 3;
            }
            if (count - at < 12 && !endOfData)
            {
                return MediaKind.NeedMore;
            }
            if (Starts(d, count, at, "#EXTM3U"))
            {
                return MediaKind.Playlist;
            }
            if ((Starts(d, count, 0, "RIFF") || Starts(d, count, 0, "RF64")) && Starts(d, count, 8, "WAVE"))
            {
                return MediaKind.Wav;
            }
            if (Starts(d, count, 0, "OggS"))
            {
                return MediaKind.Ogg;
            }
            if (Starts(d, count, 0, "fLaC"))
            {
                return MediaKind.Flac;
            }
            if (count >= 8 && IsMp4Box(d))
            {
                return MediaKind.Mp4;
            }
            if (Starts(d, count, 0, "ID3"))
            {
                return AfterId3(d, count, endOfData);
            }

            // A transport stream needs its sync byte at the packet stride: one match proves nothing.
            if (d[0] == 0x47)
            {
                if (count >= 3 * TsDemuxer.PacketBytes + 1)
                {
                    if (d[TsDemuxer.PacketBytes] == 0x47 && d[2 * TsDemuxer.PacketBytes] == 0x47 && d[3 * TsDemuxer.PacketBytes] == 0x47)
                    {
                        return MediaKind.Ts;
                    }
                }
                else if (!endOfData)
                {
                    return MediaKind.NeedMore;
                }
            }
            var audio = FindSync(d, 0, count, endOfData);
            if (audio != MediaKind.Unknown)
            {
                return audio;
            }
            return LooksLikeHtml(d, count) ? MediaKind.Html : count < WindowBytes && !endOfData ? MediaKind.NeedMore : LooksLikeList(d, count) ? MediaKind.Playlist : MediaKind.Unknown;
        }

        static bool IsMp4Box(byte[] d)
        {
            return Starts(d, 8, 4, "ftyp") || Starts(d, 8, 4, "styp") || Starts(d, 8, 4, "moof") || Starts(d, 8, 4, "moov") || Starts(d, 8, 4, "sidx") || Starts(d, 8, 4, "free");
        }

        // An ID3 tag leads both MP3 files and HLS packed audio. What follows it decides; a tag longer than the window is
        // an MP3's album art far more often than anything else.
        static MediaKind AfterId3(byte[] d, int count, bool endOfData)
        {
            if (count < 10)
            {
                return endOfData ? MediaKind.Mp3 : MediaKind.NeedMore;
            }
            var size = ((long)(d[6] & 0x7F) << 21) | ((long)(d[7] & 0x7F) << 14) | ((long)(d[8] & 0x7F) << 7) | (long)(d[9] & 0x7F);
            var end = 10 + size + ((d[5] & 0x10) != 0 ? 10 : 0);
            if (end >= WindowBytes)
            {
                return MediaKind.Mp3;
            }
            if (end + 8 > count)
            {
                return endOfData ? MediaKind.Mp3 : MediaKind.NeedMore;
            }
            var audio = FindSync(d, (int)end, count, endOfData);
            return audio == MediaKind.Unknown ? MediaKind.Mp3 : audio;
        }

        // Finds the first place two frames of the same kind follow one another.
        static MediaKind FindSync(byte[] d, int start, int count, bool endOfData)
        {
            for (var at = start; at + 4 <= count; at++)
            {
                if (d[at] == 0x0B && d[at + 1] == 0x77 && count - at >= 6)
                {
                    var bsid = d[at + 5] >> 3;
                    if (bsid <= 8)
                    {
                        return MediaKind.Ac3;
                    }
                    if (bsid >= 11 && bsid <= 16)
                    {
                        return MediaKind.Eac3;
                    }
                }
                if (d[at] != 0xFF)
                {
                    continue;
                }
                if (AdtsHeader.TryRead(d, at, count - at, out var adts))
                {
                    var next = at + adts.FrameLength;
                    if (next + AdtsHeader.MinimumBytes > count)
                    {
                        // The first frame is good and the second isn't here yet: only a short file ends like this.
                        if (endOfData)
                        {
                            return MediaKind.Adts;
                        }
                        continue;
                    }
                    if (AdtsHeader.TryRead(d, next, count - next, out var second) && second.RateIndex == adts.RateIndex)
                    {
                        return MediaKind.Adts;
                    }
                    continue;
                }
                if (Mp3Header.TryRead(d, at, count - at, out var mpeg))
                {
                    if (mpeg.FrameBytes == 0)
                    {
                        return MediaKind.Mp3;
                    }
                    var next = at + mpeg.FrameBytes;
                    if (next + Mp3Header.Bytes > count)
                    {
                        if (endOfData)
                        {
                            return MediaKind.Mp3;
                        }
                        continue;
                    }
                    if (Mp3Header.TryRead(d, next, count - next, out var second) && second.Matches(mpeg))
                    {
                        return MediaKind.Mp3;
                    }
                }
            }
            return MediaKind.Unknown;
        }

        static bool LooksLikeHtml(byte[] d, int count)
        {
            var at = 0;
            while (at < count && (d[at] == ' ' || d[at] == '\r' || d[at] == '\n' || d[at] == '\t'))
            {
                at++;
            }
            return at < count && d[at] == '<';
        }

        // A plain list: printable text, at least one line that reads as a URL or a file name.
        static bool LooksLikeList(byte[] d, int count)
        {
            if (count == 0)
            {
                return false;
            }
            var printable = true;
            var hasDot = false;
            for (var i = 0; i < count && i < 4096; i++)
            {
                var c = d[i];
                if (c < 9 || (c > 13 && c < 32) || c == 127)
                {
                    printable = false;
                    break;
                }
                hasDot |= c == '.' || c == '/';
            }
            return printable && hasDot;
        }

        static bool Starts(byte[] d, int count, int at, string text)
        {
            if (count - at < text.Length)
            {
                return false;
            }
            for (var i = 0; i < text.Length; i++)
            {
                if (d[at + i] != (byte)text[i])
                {
                    return false;
                }
            }
            return true;
        }

        static MediaKind FromContentType(string contentType)
        {
            if (string.IsNullOrEmpty(contentType))
            {
                return MediaKind.Unknown;
            }
            var type = contentType.ToLowerInvariant();
            if (type.Contains("mpegurl"))
            {
                return MediaKind.Playlist;
            }
            if (type.Contains("audio/mpeg") || type.Contains("audio/mp3"))
            {
                return MediaKind.Mp3;
            }
            if (type.Contains("audio/aac") || type.Contains("audio/aacp"))
            {
                return MediaKind.Adts;
            }
            if (type.Contains("wav"))
            {
                return MediaKind.Wav;
            }
            if (type.Contains("mp2t"))
            {
                return MediaKind.Ts;
            }
            if (type.Contains("audio/mp4") || type.Contains("video/mp4"))
            {
                return MediaKind.Mp4;
            }
            return type.Contains("ogg") || type.Contains("opus") ? MediaKind.Ogg : MediaKind.Unknown;
        }

        static MediaKind FromName(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return MediaKind.Unknown;
            }
            var end = url.IndexOfAny(new[] { '?', '#' });
            var path = (end >= 0 ? url.Substring(0, end) : url).ToLowerInvariant();
            if (path.EndsWith(".m3u8") || path.EndsWith(".m3u"))
            {
                return MediaKind.Playlist;
            }
            if (path.EndsWith(".mp3"))
            {
                return MediaKind.Mp3;
            }
            if (path.EndsWith(".aac"))
            {
                return MediaKind.Adts;
            }
            if (path.EndsWith(".wav"))
            {
                return MediaKind.Wav;
            }
            if (path.EndsWith(".ts"))
            {
                return MediaKind.Ts;
            }
            if (path.EndsWith(".m4s") || path.EndsWith(".mp4") || path.EndsWith(".m4a"))
            {
                return MediaKind.Mp4;
            }
            return MediaKind.Unknown;
        }
    }
}
