using System.Runtime.CompilerServices;

// Everything here is internal: BSAudioStream is the public face, and this stays out of the creator SDK's C# surface.
[assembly: InternalsVisibleTo("BS.SDK")]
[assembly: InternalsVisibleTo("BS.SDKEditor")]
[assembly: InternalsVisibleTo("BS.SDKEditor.Tests")]
