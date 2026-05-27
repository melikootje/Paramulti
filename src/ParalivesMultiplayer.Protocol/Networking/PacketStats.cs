using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace ParalivesMultiplayer.Networking
{
    public static class PacketStats
    {
        static long _bytesSent;
        static long _bytesReceived;
        static int _messagesSent;
        static int _messagesReceived;
        static int _errors;
        static readonly object _lock = new object();

        static readonly Queue<double> _pingSamples = new Queue<double>(64);
        static readonly object _pingLock = new object();

        public static void RecordSent(int bytes)
        {
            lock (_lock)
            {
                _bytesSent += bytes;
                _messagesSent++;
            }
        }

        public static void RecordReceived(int bytes)
        {
            lock (_lock)
            {
                _bytesReceived += bytes;
                _messagesReceived++;
            }
        }

        public static void RecordError()
        {
            lock (_lock) _errors++;
        }

        public static void RecordPing(double ms)
        {
            lock (_pingLock)
            {
                _pingSamples.Enqueue(ms);
                while (_pingSamples.Count > 64)
                    _pingSamples.Dequeue();
            }
        }

        public static double AveragePingMs
        {
            get
            {
                lock (_pingLock)
                {
                    if (_pingSamples.Count == 0) return 0;
                    double sum = 0;
                    foreach (var p in _pingSamples) sum += p;
                    return sum / _pingSamples.Count;
                }
            }
        }

        public static int PingSampleCount
        {
            get
            {
                lock (_pingLock) return _pingSamples.Count;
            }
        }

        public static long TotalBytesSent
        {
            get
            {
                lock (_lock) return _bytesSent;
            }
        }

        public static long TotalBytesReceived
        {
            get
            {
                lock (_lock) return _bytesReceived;
            }
        }

        public static int TotalMessagesSent
        {
            get
            {
                lock (_lock) return _messagesSent;
            }
        }

        public static int TotalMessagesReceived
        {
            get
            {
                lock (_lock) return _messagesReceived;
            }
        }

        public static int TotalErrors
        {
            get
            {
                lock (_lock) return _errors;
            }
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("F1") + " KB";
            return (bytes / (1024.0 * 1024.0)).ToString("F2") + " MB";
        }

        public static void Reset()
        {
            lock (_lock)
            {
                _bytesSent = 0;
                _bytesReceived = 0;
                _messagesSent = 0;
                _messagesReceived = 0;
                _errors = 0;
            }
            lock (_pingLock) _pingSamples.Clear();
        }
    }
}
