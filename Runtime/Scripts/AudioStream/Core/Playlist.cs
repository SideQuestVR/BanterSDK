using System;
using System.Collections.Generic;
using System.Globalization;

namespace BS.AudioStream
{
    /// <summary>The bounds put on anything a world's URL points at.</summary>
    internal static class StreamLimits
    {
        public const int MaxPlaylistBytes = 4 * 1024 * 1024;
        public const int MaxPlaylistEntries = 20000;
        public const int MaxUrlLength = 4096;
        public const int MaxPlaylistDepth = 4;
        public const int MaxRedirects = 5;
        public const long MaxSegmentBytes = 256L * 1024 * 1024;
    }

    /// <summary>URL handling shared by everything that fetches.</summary>
    internal static class StreamUrl
    {
        /// <summary>An absolute http or https address, or null. Anything else a playlist or redirect names is refused.</summary>
        public static Uri Parse(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length > StreamLimits.MaxUrlLength)
            {
                return null;
            }
            return Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) && IsAllowed(uri) ? uri : null;
        }

        /// <summary>Resolves a playlist entry or a redirect against the address it came from.</summary>
        public static Uri Resolve(Uri origin, string reference)
        {
            if (string.IsNullOrEmpty(reference) || reference.Length > StreamLimits.MaxUrlLength)
            {
                return null;
            }
            return Uri.TryCreate(origin, reference.Trim(), out var uri) && IsAllowed(uri) ? uri : null;
        }

        static bool IsAllowed(Uri uri) => uri.IsAbsoluteUri && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

        /// <summary>A URL as it may appear in a log: the host and nothing else. A query can hold tokens, and logs get uploaded.</summary>
        public static string Shown(Uri uri) => uri == null ? "(no address)" : uri.Host;

        public static string Shown(string text) => Shown(Uri.TryCreate(text ?? "", UriKind.Absolute, out var uri) ? uri : null);
    }

    internal sealed class PlaylistSegment
    {
        public Uri Uri;
        public double Duration;
        public long RangeStart = -1;
        public long RangeLength = -1;
        public bool Discontinuity;
        public long Sequence;
        public Uri MapUri;
        public long MapStart = -1;
        public long MapLength = -1;

        /// <summary>The EXT-X-KEY in force, or null. Audio refuses a key; it is kept for whoever can use one.</summary>
        public PlaylistKey Key;
    }

    internal sealed class PlaylistKey
    {
        public string Method;
        public Uri Uri;
        public string Iv;
        public string KeyFormat;
    }

    internal sealed class PlaylistVariant
    {
        public Uri Uri;
        public long Bandwidth;
        public string Codecs;
        public string AudioGroup;
        public string VideoGroup;
        public string SubtitleGroup;
        public string Resolution;
        public double FrameRate;

        /// <summary>Every attribute of the EXT-X-STREAM-INF, as written.</summary>
        public Dictionary<string, string> Attributes;
    }

    internal sealed class PlaylistRendition
    {
        public string Type;
        public Uri Uri;
        public string Group;
        public string Name;
        public string Language;
        public int Channels;
        public bool IsDefault;
        public bool AutoSelect;

        /// <summary>Every attribute of the EXT-X-MEDIA, as written.</summary>
        public Dictionary<string, string> Attributes;
    }

    internal enum PlaylistKind
    {
        Plain,
        Media,
        Master,
    }

    internal sealed class Playlist
    {
        public PlaylistKind Kind;
        public readonly List<PlaylistSegment> Segments = new List<PlaylistSegment>();
        public readonly List<PlaylistVariant> Variants = new List<PlaylistVariant>();
        public readonly List<PlaylistRendition> Renditions = new List<PlaylistRendition>();
        public double TargetDuration;
        public long MediaSequence;
        public bool Ended;

        /// <summary>Why it can't be played, such as encryption. Null while it can.</summary>
        public string Refusal;

        /// <summary>A playlist that will change: one with no end tag.</summary>
        public bool IsLive => Kind == PlaylistKind.Media && !Ended;

        public static Playlist Parse(string text, Uri origin)
        {
            var playlist = new Playlist();
            var isHls = false;
            var sawExtended = false;
            var sequence = 0L;
            var pendingDuration = 0.0;
            var pendingDiscontinuity = false;
            long pendingRangeStart = -1;
            long pendingRangeLength = -1;
            long lastRangeEnd = 0;
            Uri lastRangeUri = null;
            Uri mapUri = null;
            PlaylistKey key = null;
            long mapStart = -1;
            long mapLength = -1;
            PlaylistVariant pendingVariant = null;

            var at = 0;
            while (at < text.Length)
            {
                var end = text.IndexOfAny(new[] { '\n', '\r' }, at);
                if (end < 0)
                {
                    end = text.Length;
                }
                var line = text.Substring(at, end - at).Trim();
                at = end + 1;
                if (line.Length == 0)
                {
                    continue;
                }
                if (line[0] == '﻿')
                {
                    line = line.Substring(1);
                }
                if (line.StartsWith("#", StringComparison.Ordinal))
                {
                    if (line.StartsWith("#EXTM3U", StringComparison.Ordinal))
                    {
                        sawExtended = true;
                    }
                    else if (line.StartsWith("#EXT-X-TARGETDURATION:", StringComparison.Ordinal))
                    {
                        isHls = true;
                        playlist.TargetDuration = Number(line.Substring(22));
                    }
                    else if (line.StartsWith("#EXT-X-MEDIA-SEQUENCE:", StringComparison.Ordinal))
                    {
                        isHls = true;
                        playlist.MediaSequence = (long)Number(line.Substring(22));
                        sequence = playlist.MediaSequence;
                    }
                    else if (line.StartsWith("#EXT-X-ENDLIST", StringComparison.Ordinal))
                    {
                        playlist.Ended = true;
                    }
                    else if (line.StartsWith("#EXT-X-PLAYLIST-TYPE:VOD", StringComparison.Ordinal))
                    {
                        isHls = true;
                    }
                    else if (line.StartsWith("#EXT-X-DISCONTINUITY-SEQUENCE", StringComparison.Ordinal))
                    {
                        isHls = true;
                    }
                    else if (line.StartsWith("#EXT-X-DISCONTINUITY", StringComparison.Ordinal))
                    {
                        pendingDiscontinuity = true;
                    }
                    else if (line.StartsWith("#EXTINF:", StringComparison.Ordinal))
                    {
                        var comma = line.IndexOf(',');
                        pendingDuration = Number(comma > 0 ? line.Substring(8, comma - 8) : line.Substring(8));
                    }
                    else if (line.StartsWith("#EXT-X-BYTERANGE:", StringComparison.Ordinal))
                    {
                        ReadRange(line.Substring(17), out pendingRangeLength, out pendingRangeStart);
                    }
                    else if (line.StartsWith("#EXT-X-MAP:", StringComparison.Ordinal))
                    {
                        var attributes = Attributes(line.Substring(11));
                        mapUri = attributes.TryGetValue("URI", out var mapText) ? StreamUrl.Resolve(origin, mapText) : null;
                        mapStart = -1;
                        mapLength = -1;
                        if (attributes.TryGetValue("BYTERANGE", out var range))
                        {
                            ReadRange(range, out mapLength, out mapStart);
                        }
                        if (mapUri == null)
                        {
                            playlist.Refusal = "a playlist with an unusable EXT-X-MAP address";
                        }
                    }
                    else if (line.StartsWith("#EXT-X-KEY:", StringComparison.Ordinal))
                    {
                        var attributes = Attributes(line.Substring(11));
                        key = null;
                        if (attributes.TryGetValue("METHOD", out var method) && method != "NONE")
                        {
                            playlist.Refusal = "an encrypted stream (" + method + ")";
                            attributes.TryGetValue("URI", out var keyUri);
                            attributes.TryGetValue("IV", out var iv);
                            attributes.TryGetValue("KEYFORMAT", out var keyFormat);
                            key = new PlaylistKey { Method = method, Uri = StreamUrl.Resolve(origin, keyUri), Iv = iv, KeyFormat = keyFormat };
                        }
                    }
                    else if (line.StartsWith("#EXT-X-STREAM-INF:", StringComparison.Ordinal))
                    {
                        var attributes = Attributes(line.Substring(18));
                        pendingVariant = new PlaylistVariant { Attributes = attributes };
                        if (attributes.TryGetValue("BANDWIDTH", out var bandwidth))
                        {
                            pendingVariant.Bandwidth = (long)Number(bandwidth);
                        }
                        if (attributes.TryGetValue("FRAME-RATE", out var frameRate))
                        {
                            pendingVariant.FrameRate = Number(frameRate);
                        }
                        attributes.TryGetValue("CODECS", out pendingVariant.Codecs);
                        attributes.TryGetValue("AUDIO", out pendingVariant.AudioGroup);
                        attributes.TryGetValue("VIDEO", out pendingVariant.VideoGroup);
                        attributes.TryGetValue("SUBTITLES", out pendingVariant.SubtitleGroup);
                        attributes.TryGetValue("RESOLUTION", out pendingVariant.Resolution);
                    }
                    else if (line.StartsWith("#EXT-X-MEDIA:", StringComparison.Ordinal))
                    {
                        ReadRendition(playlist, Attributes(line.Substring(13)), origin);
                    }
                    continue;
                }

                if (playlist.Segments.Count + playlist.Variants.Count >= StreamLimits.MaxPlaylistEntries)
                {
                    break;
                }
                var uri = StreamUrl.Resolve(origin, line);
                if (pendingVariant != null)
                {
                    if (uri != null)
                    {
                        pendingVariant.Uri = uri;
                        playlist.Variants.Add(pendingVariant);
                    }
                    pendingVariant = null;
                    continue;
                }
                if (uri == null)
                {
                    pendingDuration = 0;
                    continue;
                }
                var segment = new PlaylistSegment
                {
                    Uri = uri,
                    Duration = pendingDuration,
                    Discontinuity = pendingDiscontinuity,
                    Sequence = sequence++,
                    MapUri = mapUri,
                    MapStart = mapStart,
                    MapLength = mapLength,
                    Key = key,
                };
                if (pendingRangeLength >= 0)
                {
                    segment.RangeLength = pendingRangeLength;
                    segment.RangeStart = pendingRangeStart >= 0 ? pendingRangeStart : lastRangeUri == uri ? lastRangeEnd : 0;
                    lastRangeEnd = segment.RangeStart + segment.RangeLength;
                    lastRangeUri = uri;
                }
                playlist.Segments.Add(segment);
                pendingDuration = 0;
                pendingDiscontinuity = false;
                pendingRangeLength = -1;
                pendingRangeStart = -1;
            }

            playlist.Kind = playlist.Variants.Count > 0 ? PlaylistKind.Master
                : isHls && sawExtended ? PlaylistKind.Media
                : PlaylistKind.Plain;
            return playlist;
        }

        static void ReadRendition(Playlist playlist, Dictionary<string, string> attributes, Uri origin)
        {
            if (playlist.Renditions.Count >= 256)
            {
                return;
            }
            var rendition = new PlaylistRendition { Attributes = attributes };
            attributes.TryGetValue("TYPE", out rendition.Type);
            attributes.TryGetValue("GROUP-ID", out rendition.Group);
            attributes.TryGetValue("NAME", out rendition.Name);
            attributes.TryGetValue("LANGUAGE", out rendition.Language);
            rendition.AutoSelect = attributes.TryGetValue("AUTOSELECT", out var autoSelect) && autoSelect == "YES";
            if (attributes.TryGetValue("URI", out var uri))
            {
                rendition.Uri = StreamUrl.Resolve(origin, uri);
            }
            if (attributes.TryGetValue("CHANNELS", out var channels))
            {
                var slash = channels.IndexOf('/');
                rendition.Channels = (int)Number(slash > 0 ? channels.Substring(0, slash) : channels);
            }
            rendition.IsDefault = attributes.TryGetValue("DEFAULT", out var isDefault) && isDefault == "YES";
            playlist.Renditions.Add(rendition);
        }

        static void ReadRange(string text, out long length, out long start)
        {
            var at = text.IndexOf('@');
            start = -1;
            if (at >= 0)
            {
                start = (long)Number(text.Substring(at + 1));
                text = text.Substring(0, at);
            }
            length = (long)Number(text);
        }

        static double Number(string text)
        {
            return double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value >= 0 && value < 9e15 ? value : 0;
        }

        /// <summary>An attribute list: NAME=value, NAME="quoted, with commas", and so on.</summary>
        internal static Dictionary<string, string> Attributes(string text)
        {
            var result = new Dictionary<string, string>();
            var at = 0;
            while (at < text.Length && result.Count < 64)
            {
                var equals = text.IndexOf('=', at);
                if (equals < 0)
                {
                    break;
                }
                var name = text.Substring(at, equals - at).Trim();
                at = equals + 1;
                string value;
                if (at < text.Length && text[at] == '"')
                {
                    var close = text.IndexOf('"', at + 1);
                    if (close < 0)
                    {
                        break;
                    }
                    value = text.Substring(at + 1, close - at - 1);
                    at = close + 1;
                    var comma = text.IndexOf(',', at);
                    at = comma < 0 ? text.Length : comma + 1;
                }
                else
                {
                    var comma = text.IndexOf(',', at);
                    var stop = comma < 0 ? text.Length : comma;
                    value = text.Substring(at, stop - at).Trim();
                    at = stop + 1;
                }
                result[name] = value;
            }
            return result;
        }
    }

    /// <summary>What to play from a master playlist.</summary>
    internal sealed class PlaylistChoice
    {
        public Uri Uri;
        public int Channels;
        public string Refusal;
    }

    internal static class PlaylistSelector
    {
        const int PlayableRank = 2;
        const int UnknownRank = 1;

        /// <summary>
        /// The rendition or variant with the most channels within <paramref name="maxChannels"/>, or the fewest above it if
        /// nothing fits. A codec this system plays beats one that isn't declared, and a declared codec it can't play is
        /// left out. Audio-only beats audio inside video, and a declared default breaks a tie.
        /// </summary>
        public static PlaylistChoice Choose(Playlist master, int maxChannels)
        {
            var groupCodecs = new Dictionary<string, string>();
            foreach (var variant in master.Variants)
            {
                if (variant.AudioGroup != null && !groupCodecs.ContainsKey(variant.AudioGroup))
                {
                    groupCodecs[variant.AudioGroup] = variant.Codecs;
                }
            }

            Uri best = null;
            var bestChannels = 0;
            var bestScore = long.MinValue;
            string refused = null;

            void Offer(Uri uri, string codecs, int channels, bool isDefault, bool hasVideo, long bandwidth)
            {
                var rank = CodecRank(codecs, out var codecName);
                if (rank == 0)
                {
                    refused = refused ?? codecName;
                    return;
                }
                // Within the maximum, more is better; above it, closer is better, and worse than anything that fits.
                var known = channels > 0 ? channels : 2;
                long fit = known <= maxChannels ? 100 + known : 100 - known;
                var score = fit * 1000000000L + rank * 100000000L + (hasVideo ? 0 : 10000000L) + (isDefault ? 5000000L : 0) + Math.Min(bandwidth / 1000, 4999999L);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = uri;
                    bestChannels = channels;
                }
            }

            foreach (var rendition in master.Renditions)
            {
                if (rendition.Uri != null && rendition.Type == "AUDIO")
                {
                    groupCodecs.TryGetValue(rendition.Group ?? "", out var codecs);
                    Offer(rendition.Uri, AudioCodecsOf(codecs), rendition.Channels, rendition.IsDefault, false, 0);
                }
            }
            foreach (var variant in master.Variants)
            {
                // A variant whose audio lives in a rendition with its own address is reached through that.
                if (variant.AudioGroup != null && master.Renditions.Exists(r => r.Type == "AUDIO" && r.Group == variant.AudioGroup && r.Uri != null))
                {
                    continue;
                }
                var channels = 0;
                if (variant.AudioGroup != null)
                {
                    var rendition = master.Renditions.Find(r => r.Type == "AUDIO" && r.Group == variant.AudioGroup);
                    channels = rendition?.Channels ?? 0;
                }
                Offer(variant.Uri, AudioCodecsOf(variant.Codecs), channels, false, HasVideo(variant.Codecs), variant.Bandwidth);
            }
            if (best != null)
            {
                return new PlaylistChoice { Uri = best, Channels = bestChannels };
            }
            return new PlaylistChoice { Refusal = refused ?? "a playlist with nothing to play" };
        }

        static string AudioCodecsOf(string codecs)
        {
            if (string.IsNullOrEmpty(codecs))
            {
                return null;
            }
            var audio = new List<string>();
            foreach (var part in codecs.Split(','))
            {
                var codec = part.Trim();
                if (!IsVideoCodec(codec))
                {
                    audio.Add(codec);
                }
            }
            return audio.Count == 0 ? "" : string.Join(",", audio);
        }

        static bool HasVideo(string codecs)
        {
            if (string.IsNullOrEmpty(codecs))
            {
                return false;
            }
            foreach (var part in codecs.Split(','))
            {
                if (IsVideoCodec(part.Trim()))
                {
                    return true;
                }
            }
            return false;
        }

        static bool IsVideoCodec(string codec)
        {
            return codec.StartsWith("avc", StringComparison.OrdinalIgnoreCase) || codec.StartsWith("hvc", StringComparison.OrdinalIgnoreCase)
                || codec.StartsWith("hev", StringComparison.OrdinalIgnoreCase) || codec.StartsWith("vp0", StringComparison.OrdinalIgnoreCase)
                || codec.StartsWith("vp9", StringComparison.OrdinalIgnoreCase) || codec.StartsWith("av01", StringComparison.OrdinalIgnoreCase)
                || codec.StartsWith("dvh", StringComparison.OrdinalIgnoreCase);
        }

        // 0 when nothing in the list can play, naming what can't.
        static int CodecRank(string codecs, out string name)
        {
            name = null;
            if (codecs == null)
            {
                return UnknownRank;
            }
            if (codecs.Length == 0)
            {
                name = "a video stream with no audio";
                return 0;
            }
            var best = 0;
            foreach (var part in codecs.Split(','))
            {
                var codec = part.Trim().ToLowerInvariant();
                var rank = 0;
                if (codec == "mp4a.40.34" || codec == "mp4a.69" || codec == "mp4a.6b" || codec == "mp3")
                {
                    rank = PlayableRank;
                }
                else if ((codec == "mp4a.40.2" || codec == "mp4a.40.5" || codec == "mp4a.40.29") && AudioCodecRegistry.CanPlay(AudioCodecId.Aac))
                {
                    rank = PlayableRank;
                }
                else if (codec.StartsWith("ec-3", StringComparison.Ordinal) && AudioCodecRegistry.CanPlay(AudioCodecId.Eac3))
                {
                    rank = PlayableRank;
                }
                else if (codec.StartsWith("ac-3", StringComparison.Ordinal) && AudioCodecRegistry.CanPlay(AudioCodecId.Ac3))
                {
                    rank = PlayableRank;
                }
                else if (codec == "opus" && AudioCodecRegistry.CanPlay(AudioCodecId.Opus))
                {
                    rank = PlayableRank;
                }
                else
                {
                    name = name ?? Named(codec);
                }
                best = Math.Max(best, rank);
            }
            return best;
        }

        static string Named(string codec)
        {
            if (codec == "mp4a.40.2" || codec == "mp4a.40.5" || codec == "mp4a.40.29")
            {
                return "AAC";
            }
            if (codec.StartsWith("ec-3", StringComparison.Ordinal))
            {
                return "E-AC-3";
            }
            if (codec.StartsWith("ac-3", StringComparison.Ordinal))
            {
                return "AC-3";
            }
            return codec == "opus" ? "Opus" : codec == "flac" ? "FLAC" : "the codec " + codec;
        }
    }
}
