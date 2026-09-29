using System;
using System.Collections.Generic;
using System.Timers;
using BS;

public class BatchUpdater
{
    BSPipe _pipe;
    List<string> _updates = new List<string>();
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
                _pipe.Send(MessageDelimiters.BATCH + string.Join(MessageDelimiters.BATCH, _updates));
                _updates.Clear();
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
