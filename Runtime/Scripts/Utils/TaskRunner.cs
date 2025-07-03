//#define TASK_DEBUG_LOGGING
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.VisualScripting;
using UnityEngine;

namespace Banter.SDK
{
    public static class TaskRunner
    {
#if TASK_DEBUG_LOGGING
        private static Dictionary<string, int> counters = new Dictionary<string, int>();
        private static Dictionary<string, int> totals = new Dictionary<string, int>();
        private static object counterLock = new object();
        private static System.Threading.Timer dbgDump;
        private static void CheckRunning()
        {
            //this is messy, in editor it'll keep going
            if (dbgDump == null)
            {
                dbgDump = new System.Threading.Timer((o) =>
                {
                    Dump();
                }, null, 5000, 5000);
            }
        }
        private static void Decrement(string tag)
        {
            lock (counterLock)
            {
                if (counters.TryGetValue(tag, out var ctr))
                {
                    counters[tag] = ctr - 1;
                }
            }
        }

        public static Task Track(this Task task, string tag)
        {
            CheckRunning();
            if (tag == null)
            {
                tag = "Unknown";
            }
            lock (counterLock)
            {
                if (!counters.TryAdd(tag, 1))
                {
                    counters[tag]++;
                }
                if (!totals.TryAdd(tag, 1))
                {
                    totals[tag]++;
                }
            }
            return task.ContinueWith(t =>
            {
                Decrement(tag);
                if (t.IsFaulted)
                {
                    Debug.LogError("TaskRunner Track exception running task tag " + tag + "!");
                    Debug.LogException(t.Exception);
                    throw t.Exception;
                }
                return t;
            }, TaskContinuationOptions.ExecuteSynchronously).Unwrap();
        }
        public static IEnumerator Track(this IEnumerator coroutine, string tag)
        {
            CheckRunning();
            if (tag == null)
            {
                tag = "Unknown";
            }

            tag = $"(IEnumerator) {tag}";
            // Optional: start tracking
            lock (counterLock)
            {
                if (!counters.TryAdd(tag, 1))
                {
                    counters[tag]++;
                }
                if (!totals.TryAdd(tag, 1))
                {
                    totals[tag]++;
                }
            }

            bool done = false;
            Exception caught = null;

            while (!done)
            {
                object current;
                try
                {
                    if (!coroutine.MoveNext())
                    {
                        done = true;
                        break;
                    }
                    current = coroutine.Current;
                }
                catch (Exception e)
                {
                    Debug.LogError("Coroutine exception in tag " + tag + "!");
                    Debug.LogException(e);
                    caught = e;
                    break;
                }

                yield return current;
            }

            lock (counterLock)
            {
                Decrement(tag);
            }

            if (caught != null)
            {
                throw caught; // Propagate outside if desired
            }
        }
        public static Action Track(this Action action, string tag)
        {
            CheckRunning();
            if (tag == null)
            {
                tag = "Unknown";
            }
            tag = $"(Action) {tag}";
            return () =>
            {
                // Insert pre-logic if needed (e.g., logging or counting)
                lock (counterLock)
                {
                    if (!counters.TryAdd(tag, 1))
                    {
                        counters[tag]++;
                    }
                    if (!totals.TryAdd(tag, 1))
                    {
                        totals[tag]++;
                    }
                }
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    Debug.LogError("TaskRunner exception running action with tag " + tag + "!");
                    Debug.LogException(e);
                    throw;
                }
                finally
                {
                    // Insert post-logic if needed
                    Decrement(tag);
                }
            };
        }

        public static Task<T> Track<T>(this Task<T> task, string tag)
        {
            CheckRunning();
            if (tag == null)
            {
                tag = "Unknown";
            }
            tag = $"(Task) {tag}";
            lock (counterLock)
            {
                if (!counters.TryAdd(tag, 1))
                {
                    counters[tag]++;
                }
                if (!totals.TryAdd(tag, 1))
                {
                    totals[tag]++;
                }
            }
            return task.ContinueWith(t =>
            {
                Decrement(tag);
                if (t.IsFaulted)
                {
                    Debug.LogError("TaskRunner Track exception running task tag " + tag + "!");
                    Debug.LogException(t.Exception);
                    throw t.Exception;
                }
                return t.Result;
            }, TaskContinuationOptions.ExecuteSynchronously);
        }

        public static Task Run(Action a, string tag = null, CancellationToken? cancelToken = null)
        {
            CheckRunning();
            if (tag == null)
            {
                tag = "Unknown";
            }
            tag = $"(Task) {tag}";
            lock (counterLock)
            {
                if (!counters.TryAdd(tag, 1))
                {
                    counters[tag]++;
                }
                if (!totals.TryAdd(tag, 1))
                {
                    totals[tag]++;
                }
            }
            try
            {
                return Task.Run(() =>
                {
                    try
                    {
                        a();
                    }
                    catch (Exception e)
                    {
                        Debug.LogError("TaskRunner exception running task tag " + tag + "!");
                        Debug.LogException(e);
                        throw;
                    }
                    finally
                    {
                        Decrement(tag);
                    }

                }, cancelToken.HasValue ? cancelToken.Value : CancellationToken.None);
            }
            catch (Exception e)
            {
                Debug.LogError("TaskRunner exception starting task!");
                Debug.LogException(e);
                Decrement(tag);
                throw;
            }

        }

        public static void Dump()
        {
            lock (counterLock)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("Active Task Counters:");
                foreach (KeyValuePair<string, int> kvp in counters.OrderByDescending(kv => kv.Value))
                {
                    sb.Append("\t");
                    sb.Append(kvp.Key);
                    sb.Append(": ");
                    sb.Append(kvp.Value);
                    sb.AppendLine();
                }
                sb.AppendLine("Total Task Counters:");
                foreach (KeyValuePair<string, int> kvp in totals.OrderByDescending(kv => kv.Value))
                {
                    sb.Append("\t");
                    sb.Append(kvp.Key);
                    sb.Append(": ");
                    sb.Append(kvp.Value);
                    sb.AppendLine();
                }
                Debug.Log(sb.ToString());
            }
        }
    
#else
        public static Task Track(this Task task, string tag)
        {
            return task;
        }

        public static IEnumerator Track(this IEnumerator coroutine, string tag)
        {
            return coroutine;
        }

        public static Action Track(this Action action, string tag)
        {
            return action;
        }

        public static Task<T> Track<T>(this Task<T> task, string tag)
        {
            return task;
        }

        public static Task Run(Action a, string tag = null, CancellationToken? cancelToken = null)
        {
            return Task.Run(a, cancelToken ?? CancellationToken.None);
        }

        public static void Dump()
        {
            // no-op
        }
#endif

        //non debug version straight passthrough
        /*
         *     public static Task Track(this Task task, string tag)
         {
             return task;
         }

         public static Task<T> Track<T>(this Task<T> task, string tag)
         {
             return task;
         }

         public static Task Run(Action a, string tag = null, CancellationToken? cancelToken = null)
         {
             return Task.Run(a, cancelToken ?? CancellationToken.None);
         }*/

    }
}