using System;
using System.Collections.Generic;
using System.Timers;
using Banter.SDK;

public class BatchUpdater
{
    BanterPipe _pipe;
    List<string> _updates = new List<string>();
    Timer _timer;
    public BatchUpdater(BanterPipe pipe)
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

    public void Tick()
    {
        lock (_updates)
        {
            if (_updates.Count > 0)
            {
                _pipe.Send(MessageDelimiters.PRIMARY + MessageDelimiters.SECONDARY + MessageDelimiters.TERTIARY + string.Join(MessageDelimiters.PRIMARY + MessageDelimiters.SECONDARY + MessageDelimiters.TERTIARY, _updates));
                _updates.Clear();
            }
        }
    }
    public Timer SetInterval(Action action, int interval)
    {
        var timer = new Timer(interval);
        timer.Elapsed += (s, e) =>
        {
            timer.Enabled = false;
            action();
            timer.Enabled = true;
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