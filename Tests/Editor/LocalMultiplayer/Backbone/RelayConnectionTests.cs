using NUnit.Framework;

namespace BS.LocalMultiplayer.Tests
{
    /// <summary>The relay socket's setup (RELAY.md 1).</summary>
    public class RelayConnectionTests
    {
        // As the hub probe's HttpClient: a system proxy must never take the localhost connection, or the probe
        // says Ready while every socket fails.
        [Test]
        public void TheRelaySocket_UsesNoProxy()
        {
            using (var socket = RelayConnection.CreateSocket())
            {
                Assert.IsNull(socket.Options.Proxy);
            }
        }
    }
}
