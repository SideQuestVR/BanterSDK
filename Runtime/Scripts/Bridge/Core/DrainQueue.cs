using System;
using System.Collections.Generic;

namespace BS.Bridge
{
    /// <summary>
    /// Hands batches added on any thread to one worker at a time, in order. "Anything left?" and the worker stopping
    /// are one step under the lock, so an Add can't land between them and sit there until some later Add wakes it.
    /// </summary>
    internal sealed class DrainQueue<T>
    {
        readonly object gate = new object();
        readonly Action<List<T>> process;
        readonly Action<Action> startWorker;
        readonly Action<Exception> onError;
        List<T> pending;
        bool running;

        /// <param name="process">Runs on the worker, one batch at a time.</param>
        /// <param name="startWorker">Runs the given drain on another thread.</param>
        /// <param name="onError">Told about a batch that threw. The rest of that batch is lost; the queue carries on.</param>
        public DrainQueue(Action<List<T>> process, Action<Action> startWorker, Action<Exception> onError)
        {
            this.process = process;
            this.startWorker = startWorker;
            this.onError = onError;
        }

        public void Add(List<T> items)
        {
            lock (gate)
            {
                if (pending == null)
                {
                    pending = new List<T>();
                }
                pending.AddRange(items);
                if (running)
                {
                    return;
                }
                running = true;
            }
            startWorker(Drain);
        }

        void Drain()
        {
            while (true)
            {
                List<T> batch;
                lock (gate)
                {
                    batch = pending;
                    pending = null;
                    if (batch == null)
                    {
                        running = false;
                        return;
                    }
                }
                try
                {
                    process(batch);
                }
                catch (Exception e)
                {
                    onError(e);
                }
            }
        }
    }
}
