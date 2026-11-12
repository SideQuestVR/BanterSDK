using System;
using System.Net.WebSockets;
using NUnit.Framework;

namespace BS.LocalMultiplayer.Tests
{
    /// <summary>
    /// BSStarterUpper reports unobserved task exceptions as errors, except teardown noise. A connect abandoned after a
    /// cancel (Unity's AI Assistant times out its relay WebSocket connect that way, and every MPPM player retries it)
    /// arrives as a WebSocketException caused by the cancel, and must count as noise; real failures must not.
    /// </summary>
    public class UnobservedTaskNoiseTests
    {
        [Test]
        public void AConnectAbandonedAfterACancel_IsNoise()
        {
            // The chain from the console: WebSocketException -> OperationCanceledException -> ObjectDisposedException.
            var disposed = new ObjectDisposedException("System.Net.Sockets.Socket");
            var cancelled = new OperationCanceledException("The operation was canceled.", disposed);
            var connect = new WebSocketException("Unable to connect to the remote server", cancelled);
            Assert.IsTrue(BSStarterUpper.IsTeardownNoise(connect));
        }

        [Test]
        public void CancelledDisposedOrAbortedIo_IsNoise()
        {
            Assert.IsTrue(BSStarterUpper.IsTeardownNoise(new OperationCanceledException()));
            Assert.IsTrue(BSStarterUpper.IsTeardownNoise(new ObjectDisposedException("x")));
            Assert.IsTrue(BSStarterUpper.IsTeardownNoise(new System.IO.IOException("pipe closed")));
        }

        [Test]
        public void RealFailures_AreReported()
        {
            Assert.IsFalse(BSStarterUpper.IsTeardownNoise(new InvalidOperationException("bug")));
            Assert.IsFalse(BSStarterUpper.IsTeardownNoise(new WebSocketException("refused")));
            Assert.IsFalse(BSStarterUpper.IsTeardownNoise(new WebSocketException("bad", new InvalidOperationException("bug"))));
            // A non-transport exception is judged by itself, not by what it wraps.
            Assert.IsFalse(BSStarterUpper.IsTeardownNoise(new InvalidOperationException("bug", new OperationCanceledException())));
        }
    }
}
