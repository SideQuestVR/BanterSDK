namespace BS
{
    /// <summary>How a sound fades with distance from the listener.</summary>
    public enum AudioRolloff
    {
        /// <summary>It doesn't: the same volume at any distance.</summary>
        Flat = 0,
        /// <summary>Like a real sound: full volume within the min distance, quieter further away, no quieter beyond the max distance.</summary>
        Logarithmic = 1,
        /// <summary>Evenly, from full volume at the min distance to silence at the max distance.</summary>
        Linear = 2,
    }
}
