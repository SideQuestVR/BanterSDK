using System;

namespace BS.AudioStream
{
    /// <summary>
    /// Plays either format, chosen frame by frame from the stream itself, because containers do not always say which they
    /// carry. AC-3 is decoded here; E-AC-3 goes to the operating system's decoder, opened at its first frame, and is refused
    /// by name where the system has none.
    /// </summary>
    internal sealed class Ac3AudioDecoder : IAudioDecoder, IAudioDecoderNotices, IAudioDecoderDrain, IDisposable
    {
        readonly Ac3Decoder _ac3 = new Ac3Decoder();
        PlatformEac3Decoder _enhanced;
        bool _playingEnhanced;

        public string CodecName => _playingEnhanced ? "E-AC-3" : "AC-3";
        public int SampleRate => _playingEnhanced ? _enhanced.SampleRate : _ac3.SampleRate;
        public int Channels => _playingEnhanced ? _enhanced.Channels : _ac3.Channels;
        public AudioChannelRole[] Layout => _playingEnhanced ? _enhanced.Layout : _ac3.Layout;

        // The system decoder can give out more than one frame at a time.
        public int MaxSamplesPerFrame => Math.Max(Ac3Tables.MaxBlocks * Ac3Tables.SamplesPerBlock, PlatformAudioDecoder.PlaneSamples);
        public string Refusal { get; private set; }

        public string TakeNotice() => _enhanced?.TakeNotice();

        /// <summary>Frames that arrived damaged and played as silence.</summary>
        public int DamagedFrames { get; private set; }

        public void Reset()
        {
            _ac3.Reset();
            _enhanced?.Reset();
        }

        public int Drain(float[][] planes)
        {
            if (!_playingEnhanced || Refusal != null)
                return 0;

            int samples = _enhanced.Drain(planes);
            Refusal = _enhanced.Refusal;
            return samples;
        }

        public void Dispose()
        {
            _enhanced?.Dispose();
        }

        public int Decode(byte[] data, int offset, int count, float[][] planes)
        {
            if (Refusal != null)
                return 0;

            try
            {
                return DecodeUnit(data, offset, count, planes);
            }
            catch (Exception e)
            {
                // Damaged frames are handled above; this is the last line behind the promise not to throw.
                DamagedFrames++;
                _ac3.Reset();
                _enhanced?.Reset();
                Refusal = "AC-3 decoder stopped on a bad frame: " + e.GetType().Name;
                return 0;
            }
        }

        int DecodeUnit(byte[] data, int offset, int count, float[][] planes)
        {
            var info = default(Ac3SyncInfo);
            bool readable = Ac3SyncInfo.TryRead(data, offset, count, ref info);

            if (readable && info.UnknownVersion)
            {
                Refusal = "AC-3 bitstream id " + info.BitStreamId + " is not supported";
                return 0;
            }

            if (readable && info.Enhanced)
            {
                if (_enhanced == null)
                {
                    _enhanced = PlatformEac3Decoder.Open(info, out var refusal);
                    if (_enhanced == null)
                    {
                        Refusal = refusal;
                        return 0;
                    }
                }

                _playingEnhanced = true;
                int samples = _enhanced.Decode(data, offset, count, planes);
                Refusal = _enhanced.Refusal;
                return samples;
            }

            _playingEnhanced = false;
            int written = _ac3.Decode(data, offset, count, planes);
            if (_ac3.LastError != null)
                DamagedFrames++;
            return written;
        }
    }
}
