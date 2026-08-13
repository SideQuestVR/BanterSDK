namespace Banter.SDK
{
    /// <summary>
    /// The single shared secret every Greenfield-built Basis <c>.BEE</c> bundle — avatars and spaces
    /// alike — is encrypted with at build time and decrypted with at load time. Centralised here so
    /// the build side (<c>GreenfieldAvatarBuilder</c>, the space builder) and the runtime side
    /// (<c>AvatarService</c>, <c>BSAssetBundle</c>) can never drift apart: a key mismatch fails
    /// decryption silently, with no obvious cause.
    ///
    /// It is a fixed GUID rather than the memorable <c>"greenfield"</c> word purely so it isn't
    /// trivially recoverable by eyeballing a hosted bundle. This is obfuscation, not security — the
    /// key ships inside the client either way. Anything built with the old key must be rebuilt.
    /// TODO(greenfield): source per-bundle from the server once custom uploads land.
    /// </summary>
    public static class GreenfieldBundleCrypto
    {
        /// <summary>The shared bundle encryption password.</summary>
        public const string Password = "236e0ea4-0998-4e13-8fb1-a1381d1003f6";
    }
}
