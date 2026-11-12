using System;
using System.Collections.Generic;

namespace BS.AudioStream
{
    /// <summary>
    /// RIFF WAVE, read as it arrives. Sizes in the file only ever count bytes down: a fmt chunk is read into a fixed buffer
    /// and anything else is skipped. A data chunk sized 0 or 0xFFFFFFFF, as a streaming encoder writes, runs until the
    /// bytes stop. Chunks after data are skipped too.
    /// </summary>
    internal sealed class WavParser : IMediaParser
    {
        const int MaxFileChannels = 64;
        const int FormatBytes = 40;

        // The WAVE speaker bits this mapper knows, in file order. Others are left out.
        const int FrontLeft = 0x1;
        const int FrontRight = 0x2;
        const int FrontCenter = 0x4;
        const int LowFrequency = 0x8;
        const int BackLeft = 0x10;
        const int BackRight = 0x20;
        const int BackCenter = 0x100;
        const int SideLeft = 0x200;
        const int SideRight = 0x400;

        enum Stage
        {
            Riff,
            ChunkHeader,
            Format,
            Skip,
            Data,
            Done,
        }

        readonly IMediaSink _sink;
        readonly byte[] _small = new byte[FormatBytes];
        readonly byte[] _carry = new byte[MaxFileChannels * 8];
        Stage _stage = Stage.Riff;
        int _have;
        int _need = 12;
        long _left;
        bool _unknownLength;
        bool _pad;
        int _formatChunkBytes;
        int _carried;
        int _block;
        PcmFormat _format;

        public WavParser(IMediaSink sink)
        {
            _sink = sink;
        }

        public void Reset()
        {
            _stage = Stage.Riff;
            _have = 0;
            _need = 12;
            _carried = 0;
            _format = null;
        }

        public void Push(byte[] data, int offset, int count)
        {
            while (count > 0 && _stage != Stage.Done)
            {
                switch (_stage)
                {
                    case Stage.Riff:
                    case Stage.ChunkHeader:
                    case Stage.Format:
                    {
                        var take = Math.Min(count, _need - _have);
                        Buffer.BlockCopy(data, offset, _small, _have, take);
                        _have += take;
                        offset += take;
                        count -= take;
                        if (_have == _need)
                        {
                            Advance();
                        }
                        break;
                    }
                    case Stage.Skip:
                    {
                        var take = (int)Math.Min(count, _left);
                        offset += take;
                        count -= take;
                        _left -= take;
                        if (_left == 0)
                        {
                            NextChunk();
                        }
                        break;
                    }
                    case Stage.Data:
                    {
                        var take = _unknownLength ? count : (int)Math.Min(count, _left);
                        Emit(data, offset, take);
                        offset += take;
                        count -= take;
                        _left -= take;
                        if (!_unknownLength && _left == 0)
                        {
                            _carried = 0;
                            _left = _pad ? 1 : 0;
                            if (_left > 0)
                            {
                                _stage = Stage.Skip;
                            }
                            else
                            {
                                NextChunk();
                            }
                        }
                        break;
                    }
                }
            }
        }

        // Whole sample blocks go on as they are; a block split across two pushes waits in the carry.
        void Emit(byte[] data, int offset, int count)
        {
            if (_carried > 0)
            {
                var fill = Math.Min(count, _block - _carried);
                Buffer.BlockCopy(data, offset, _carry, _carried, fill);
                _carried += fill;
                offset += fill;
                count -= fill;
                if (_carried < _block)
                {
                    return;
                }
                _sink.OnFrame(_carry, 0, _block);
                _carried = 0;
            }
            var limit = _block * PcmDecoder.MaxBlocksPerCall;
            while (count >= _block)
            {
                var whole = Math.Min(count - count % _block, limit);
                _sink.OnFrame(data, offset, whole);
                offset += whole;
                count -= whole;
            }
            if (count > 0)
            {
                Buffer.BlockCopy(data, offset, _carry, 0, count);
                _carried = count;
            }
        }

        void Advance()
        {
            switch (_stage)
            {
                case Stage.Riff:
                    if (!Tag(0, "RIFF") && !Tag(0, "RF64") || !Tag(8, "WAVE"))
                    {
                        Refuse("a file that isn't RIFF WAVE");
                        return;
                    }
                    NextChunk();
                    break;
                case Stage.ChunkHeader:
                    ReadChunkHeader();
                    break;
                case Stage.Format:
                    ReadFormat();
                    break;
            }
        }

        void NextChunk()
        {
            _stage = Stage.ChunkHeader;
            _have = 0;
            _need = 8;
        }

        void ReadChunkHeader()
        {
            var size = (uint)(_small[4] | _small[5] << 8 | _small[6] << 16 | _small[7] << 24);
            var isFormat = Tag(0, "fmt ");
            var isData = Tag(0, "data");
            _pad = (size & 1) != 0;
            if (isFormat)
            {
                if (size < 16)
                {
                    Refuse("a WAV file with a broken fmt chunk");
                    return;
                }
                _formatChunkBytes = (int)Math.Min(size, FormatBytes);
                _left = size - _formatChunkBytes + (_pad ? 1 : 0);
                _stage = Stage.Format;
                _have = 0;
                _need = _formatChunkBytes;
                return;
            }
            if (isData)
            {
                if (_format == null)
                {
                    Refuse("a WAV file with no fmt chunk before its data");
                    return;
                }
                _unknownLength = size == 0 || size == 0xFFFFFFFF;
                _left = size;
                _stage = Stage.Data;
                return;
            }
            _left = (long)size + (_pad ? 1 : 0);
            if (_left == 0)
            {
                NextChunk();
                return;
            }
            _stage = Stage.Skip;
        }

        void ReadFormat()
        {
            var tag = U16(0);
            var channels = U16(2);
            var rate = (int)Math.Min(U32(4), int.MaxValue);
            var blockAlign = U16(12);
            var bits = U16(14);
            uint mask = 0;
            if (tag == 0xFFFE && _formatChunkBytes >= 40)
            {
                mask = U32(20);
                tag = U16(24);
            }
            else if (tag == 0xFFFE)
            {
                Refuse("a WAV file with a broken extensible fmt chunk");
                return;
            }

            if (tag != 1 && tag != 3)
            {
                Refuse(FormatName(tag));
                return;
            }
            if (channels == 0 || rate <= 0 || rate > AudioFormatLimits.MaxSampleRate)
            {
                Refuse("a WAV file with a broken fmt chunk");
                return;
            }
            if (channels > MaxFileChannels)
            {
                Refuse("a WAV file with " + channels + " channels");
                return;
            }
            var bytes = blockAlign / channels;
            if (blockAlign == 0 || blockAlign % channels != 0)
            {
                bytes = (bits + 7) / 8;
            }
            PcmEncoding encoding;
            if (tag == 3 && bytes == 4)
            {
                encoding = PcmEncoding.Float32;
            }
            else if (tag == 3 && bytes == 8)
            {
                encoding = PcmEncoding.Float64;
            }
            else if (tag == 1 && bytes >= 1 && bytes <= 4)
            {
                encoding = bytes == 1 ? PcmEncoding.Unsigned8 : bytes == 2 ? PcmEncoding.Signed16 : bytes == 3 ? PcmEncoding.Signed24 : PcmEncoding.Signed32;
            }
            else
            {
                Refuse("a WAV file with " + bits + "-bit samples");
                return;
            }
            if (!MapChannels(channels, mask, out var roles, out var sources))
            {
                Refuse("a " + channels + "-channel WAV file with no speaker mask to place them by");
                return;
            }
            _format = new PcmFormat
            {
                Encoding = encoding,
                SampleRate = rate,
                FileChannels = channels,
                Roles = roles,
                FileChannelOfRole = sources,
            };
            _block = _format.BlockBytes;
            _sink.OnCodec(new AudioCodecConfig { Codec = AudioCodecId.Pcm, Pcm = _format, SampleRate = rate, Channels = roles.Length });
            if (_left > 0)
            {
                _stage = Stage.Skip;
            }
            else
            {
                NextChunk();
            }
        }

        static string FormatName(int tag)
        {
            switch (tag)
            {
                case 2: return "Microsoft ADPCM in a WAV file";
                case 6: return "A-law in a WAV file";
                case 7: return "mu-law in a WAV file";
                case 0x11: return "IMA ADPCM in a WAV file";
                case 0x55: return "MP3 in a WAV file";
                default: return "WAV format 0x" + tag.ToString("X4");
            }
        }

        /// <summary>Roles of a file's channels, with the channels they come from. Speakers outside the eight roles are left out.</summary>
        internal static bool MapChannels(int channels, uint mask, out AudioChannelRole[] roles, out int[] sources)
        {
            var pairs = new List<KeyValuePair<AudioChannelRole, int>>();
            if (mask == 0)
            {
                var defaults = DefaultRoles(channels);
                if (defaults == null)
                {
                    roles = null;
                    sources = null;
                    return false;
                }
                for (var i = 0; i < channels; i++)
                {
                    pairs.Add(new KeyValuePair<AudioChannelRole, int>(defaults[i], i));
                }
            }
            else
            {
                var hasSide = (mask & (SideLeft | SideRight)) != 0;
                var channel = 0;
                for (var bit = 0; bit < 32 && channel < channels; bit++)
                {
                    var speaker = 1u << bit;
                    if ((mask & speaker) == 0)
                    {
                        continue;
                    }
                    if (TryRole(speaker, hasSide, out var role))
                    {
                        pairs.Add(new KeyValuePair<AudioChannelRole, int>(role, channel));
                    }
                    channel++;
                }
            }
            pairs.Sort((a, b) => a.Key.CompareTo(b.Key));
            roles = new AudioChannelRole[pairs.Count];
            sources = new int[pairs.Count];
            for (var i = 0; i < pairs.Count; i++)
            {
                roles[i] = pairs[i].Key;
                sources[i] = pairs[i].Value;
            }
            return pairs.Count > 0;
        }

        static bool TryRole(uint speaker, bool hasSide, out AudioChannelRole role)
        {
            switch (speaker)
            {
                case FrontLeft: role = AudioChannelRole.FrontLeft; return true;
                case FrontRight: role = AudioChannelRole.FrontRight; return true;
                case FrontCenter: role = AudioChannelRole.Center; return true;
                case LowFrequency: role = AudioChannelRole.Lfe; return true;
                case BackLeft: role = hasSide ? AudioChannelRole.BackLeft : AudioChannelRole.SurroundLeft; return true;
                case BackRight: role = hasSide ? AudioChannelRole.BackRight : AudioChannelRole.SurroundRight; return true;
                case BackCenter: role = AudioChannelRole.BackCenter; return true;
                case SideLeft: role = AudioChannelRole.SurroundLeft; return true;
                case SideRight: role = AudioChannelRole.SurroundRight; return true;
                default: role = default; return false;
            }
        }

        // The order of a file that gives no mask: WAVE's own channel order at the usual counts.
        static AudioChannelRole[] DefaultRoles(int channels)
        {
            const AudioChannelRole L = AudioChannelRole.FrontLeft;
            const AudioChannelRole R = AudioChannelRole.FrontRight;
            const AudioChannelRole C = AudioChannelRole.Center;
            const AudioChannelRole F = AudioChannelRole.Lfe;
            const AudioChannelRole Ls = AudioChannelRole.SurroundLeft;
            const AudioChannelRole Rs = AudioChannelRole.SurroundRight;
            const AudioChannelRole Bl = AudioChannelRole.BackLeft;
            const AudioChannelRole Br = AudioChannelRole.BackRight;
            const AudioChannelRole Bc = AudioChannelRole.BackCenter;
            switch (channels)
            {
                case 1: return new[] { C };
                case 2: return new[] { L, R };
                case 3: return new[] { L, R, C };
                case 4: return new[] { L, R, Ls, Rs };
                case 5: return new[] { L, R, C, Ls, Rs };
                case 6: return new[] { L, R, C, F, Ls, Rs };
                case 7: return new[] { L, R, C, F, Bc, Ls, Rs };
                case 8: return new[] { L, R, C, F, Bl, Br, Ls, Rs };
                default: return null;
            }
        }

        void Refuse(string what)
        {
            _stage = Stage.Done;
            _sink.OnRefused(what);
        }

        bool Tag(int at, string text)
        {
            for (var i = 0; i < 4; i++)
            {
                if (_small[at + i] != (byte)text[i])
                {
                    return false;
                }
            }
            return true;
        }

        int U16(int at) => _small[at] | _small[at + 1] << 8;

        uint U32(int at) => (uint)(_small[at] | _small[at + 1] << 8 | _small[at + 2] << 16 | _small[at + 3] << 24);
    }
}
