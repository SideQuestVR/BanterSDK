using System.Runtime.CompilerServices;

// Anything in the SDK marked internal can't be seen by main project without this line
// Currently pointed at codegen variables to avoid requirement, but may need in future so keeping around.

// Main banter project is just using the generic assembly? TODO
// [assembly: InternalsVisibleTo("Assembly-CSharp")]

// Edit-mode tests set components' internal (serialized) fields directly.
[assembly: InternalsVisibleTo("BS.SDKEditor.Tests")]

// The in-editor local multiplayer host (Runtime/LocalMultiplayer, Editor/LocalMultiplayer) and its tests
// drive the same internals the Greenfield client's bridges use.
[assembly: InternalsVisibleTo("BS.SDK.LocalMultiplayer")]
[assembly: InternalsVisibleTo("BS.SDK.LocalMultiplayer.Editor")]
[assembly: InternalsVisibleTo("BS.SDK.LocalMultiplayer.Tests")]
