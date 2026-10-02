using System.Runtime.CompilerServices;

// The local multiplayer window and the edit-mode tests read the host's internals.
[assembly: InternalsVisibleTo("BS.SDK.LocalMultiplayer.Editor")]
[assembly: InternalsVisibleTo("BS.SDK.LocalMultiplayer.Tests")]
