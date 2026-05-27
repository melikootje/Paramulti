using System;
using System.Collections.Generic;

namespace ParalivesMultiplayer.Networking
{
    public static class MainThreadQueue
    {
        static readonly Queue<Action> _queue = new Queue<Action>();
        static readonly object _lock = new object();

        const int MaxPerFrame = 128;

        public static void Enqueue(Action action)
        {
            if (action == null) return;
            lock (_lock)
                _queue.Enqueue(action);
        }

        public static void Drain()
        {
            int count = 0;
            while (count < MaxPerFrame)
            {
                Action action;
                lock (_lock)
                {
                    if (_queue.Count == 0) break;
                    action = _queue.Dequeue();
                }
                count++;
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"[MainThreadQueue] Exception in dispatched action: {ex}");
                }
            }
        }

        public static int PendingCount
        {
            get
            {
                lock (_lock) return _queue.Count;
            }
        }
    }
}
