using System.Runtime.CompilerServices;

// The bridge core is internal: it is not part of the creator SDK's public C# surface (Visual Scripting must not offer
// it as nodes, and the M0 surface snapshot must not freeze it). Friends: the SDK that plugs it in, and the tests.
[assembly: InternalsVisibleTo("BS.SDK")]
[assembly: InternalsVisibleTo("BS.SDKEditor")]
[assembly: InternalsVisibleTo("BS.SDKEditor.Tests")]
[assembly: InternalsVisibleTo("BS.Bridge.Core.Tests")]
