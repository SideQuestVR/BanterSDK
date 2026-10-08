using System;
using System.Collections.Generic;
using System.Timers;
using BS;

public class BatchUpdater
{
    BSPipe _pipe;
    List<string> _updates = new List<string>();
    const int MaxRetainedFrameBuffer = 256 * 1024;
    // WireFrame's scratch space, reused for every frame; only touched under the _updates lock.
    char[] _frameBuffer;
    Timer _timer;
    volatile bool _disposed;
    public BatchUpdater(BSPipe pipe)
    {
        _pipe = pipe;
        _timer = SetInterval(() => Tick(), 11);
    }

    public void Send(string msg)
    {
        lock (_updates)
        {
            _updates.Add(msg);
        }
    }

    /// <summary>Stop sending: the pipe is going away (play mode ended, the link was destroyed).</summary>
    public void Dispose()
    {
        _disposed = true;
        ClearInterval(_timer);
        _timer = null;
        lock (_updates)
        {
            _updates.Clear();
        }
    }

    public void Tick()
    {
        lock (_updates)
        {
            if (_disposed) return;
            if (_updates.Count > 0)
            {
                // Length-prefixed (WireFrame), so a ‽ inside a message can't split the batch.
                _pipe.Send(WireFrame.Pack(MessageDelimiters.BATCH, _updates, ref _frameBuffer));
                _updates.Clear();
                // Don't hold on to the buffer of one unusually large batch.
                if (_frameBuffer.Length > MaxRetainedFrameBuffer) _frameBuffer = null;
            }
        }
    }
    public Timer SetInterval(Action action, int interval)
    {
        var timer = new Timer(interval);
        timer.Elapsed += (s, e) =>
        {
            if (_disposed) return;
            timer.Enabled = false;
            action();
            // Re-arming a timer disposed while the tick ran would throw on the pool thread.
            if (!_disposed) timer.Enabled = true;
        };
        timer.Enabled = true;
        return timer;
    }
    public void ClearInterval(Timer timer)
    {
        if (timer == null) return;
        timer.Stop();
        timer.Dispose();
    }
}
