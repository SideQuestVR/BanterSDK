using System;
using System.Collections.Generic;

namespace BS.AudioStream
{
    /// <summary>The roles a component's maximum layout offers, in role order.</summary>
    internal static class ChannelLayouts
    {
        static readonly AudioChannelRole[] Mono = { AudioChannelRole.Center };
        static readonly AudioChannelRole[] Stereo = { AudioChannelRole.FrontLeft, AudioChannelRole.FrontRight };
        static readonly AudioChannelRole[] Quad =
        {
            AudioChannelRole.FrontLeft, AudioChannelRole.FrontRight, AudioChannelRole.SurroundLeft, AudioChannelRole.SurroundRight,
        };
        static readonly AudioChannelRole[] Surround51 =
        {
            AudioChannelRole.FrontLeft, AudioChannelRole.FrontRight, AudioChannelRole.Center, AudioChannelRole.Lfe,
            AudioChannelRole.SurroundLeft, AudioChannelRole.SurroundRight,
        };
        static readonly AudioChannelRole[] Surround71 =
        {
            AudioChannelRole.FrontLeft, AudioChannelRole.FrontRight, AudioChannelRole.Center, AudioChannelRole.Lfe,
            AudioChannelRole.SurroundLeft, AudioChannelRole.SurroundRight, AudioChannelRole.BackLeft, AudioChannelRole.BackRight,
        };

        /// <summary>The layout of a maximum channel count: 1, 2, 4, 6 or 8.</summary>
        public static AudioChannelRole[] Of(int maxChannels)
        {
            switch (maxChannels)
            {
                case 1: return Mono;
                case 2: return Stereo;
                case 4: return Quad;
                case 6: return Surround51;
                default: return Surround71;
            }
        }
    }

    /// <summary>
    /// From a stream's channels to the channels that are wanted. A role the layout has passes straight through. A role it
    /// lacks folds into its neighbours by the ITU-R BS.775 rule of -3 dB per step: centre into left and right, a surround
    /// into its front, a back into its surround, front left and right into a mono centre (so surrounds reach mono at -6 dB,
    /// as BS.775 gives). The LFE is dropped from a layout with none, also as BS.775 gives. Nothing is normalised: float
    /// carries the headroom, and the mixer clips only what really sums past full scale.
    /// </summary>
    internal sealed class ChannelMapper
    {
        const float Step = 0.70710678f;

        readonly int[][] _sources;
        readonly float[][] _weights;
        readonly float[][] _mixed;

        /// <summary>The roles coming out, in layout order: those the stream reaches and that are wanted.</summary>
        public AudioChannelRole[] Outputs { get; }

        public ChannelMapper(AudioChannelRole[] stream, AudioChannelRole[] layout, Func<AudioChannelRole, bool> wanted, int maxFrames)
        {
            var rows = new List<AudioChannelRole>();
            var sources = new List<int[]>();
            var weights = new List<float[]>();
            foreach (var role in layout)
            {
                var from = new List<int>();
                var by = new List<float>();
                for (var i = 0; i < stream.Length; i++)
                {
                    var weight = Weight(stream[i], role, layout);
                    if (weight != 0f)
                    {
                        from.Add(i);
                        by.Add(weight);
                    }
                }
                if (from.Count > 0 && wanted(role))
                {
                    rows.Add(role);
                    sources.Add(from.ToArray());
                    weights.Add(by.ToArray());
                }
            }
            Outputs = rows.ToArray();
            _sources = sources.ToArray();
            _weights = weights.ToArray();
            _mixed = new float[rows.Count][];
            for (var i = 0; i < rows.Count; i++)
            {
                _mixed[i] = IsCopy(i) ? null : new float[maxFrames];
            }
        }

        bool IsCopy(int row) => _sources[row].Length == 1 && _weights[row][0] == 1f;

        /// <summary>
        /// The planes to write for <paramref name="count"/> frames. A role passing straight through is the input array
        /// itself, so a stream that needs no mixing costs nothing here.
        /// </summary>
        public float[][] Map(float[][] planes, int count, float[][] output)
        {
            for (var row = 0; row < _sources.Length; row++)
            {
                if (_mixed[row] == null)
                {
                    output[row] = planes[_sources[row][0]];
                    continue;
                }
                var mixed = _mixed[row];
                var sources = _sources[row];
                var weights = _weights[row];
                var first = planes[sources[0]];
                var weight = weights[0];
                for (var i = 0; i < count; i++)
                {
                    mixed[i] = first[i] * weight;
                }
                for (var s = 1; s < sources.Length; s++)
                {
                    var plane = planes[sources[s]];
                    weight = weights[s];
                    for (var i = 0; i < count; i++)
                    {
                        mixed[i] += plane[i] * weight;
                    }
                }
                output[row] = mixed;
            }
            return output;
        }

        // How much of one stream role reaches one layout role.
        internal static float Weight(AudioChannelRole from, AudioChannelRole to, AudioChannelRole[] layout)
        {
            if (from == to)
            {
                return 1f;
            }
            if (Array.IndexOf(layout, from) >= 0)
            {
                return 0f;
            }
            switch (from)
            {
                case AudioChannelRole.Center:
                    return Fold(AudioChannelRole.FrontLeft, to, layout) + Fold(AudioChannelRole.FrontRight, to, layout);
                case AudioChannelRole.FrontLeft:
                case AudioChannelRole.FrontRight:
                    return Fold(AudioChannelRole.Center, to, layout);
                case AudioChannelRole.SurroundLeft:
                    return Fold(AudioChannelRole.FrontLeft, to, layout);
                case AudioChannelRole.SurroundRight:
                    return Fold(AudioChannelRole.FrontRight, to, layout);
                case AudioChannelRole.BackLeft:
                    return Fold(AudioChannelRole.SurroundLeft, to, layout);
                case AudioChannelRole.BackRight:
                    return Fold(AudioChannelRole.SurroundRight, to, layout);
                case AudioChannelRole.BackCenter:
                    return Array.IndexOf(layout, AudioChannelRole.BackLeft) >= 0
                        ? Fold(AudioChannelRole.BackLeft, to, layout) + Fold(AudioChannelRole.BackRight, to, layout)
                        : Fold(AudioChannelRole.SurroundLeft, to, layout) + Fold(AudioChannelRole.SurroundRight, to, layout);
                default:
                    return 0f;
            }
        }

        static float Fold(AudioChannelRole next, AudioChannelRole to, AudioChannelRole[] layout) => Step * Weight(next, to, layout);
    }
}
