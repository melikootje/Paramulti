using System;
using System.Diagnostics;

namespace ParalivesMultiplayer.Performance
{
    public class BandwidthLimiter
    {
        readonly long _maxBytesPerSecond;
        readonly long _burstBytes;
        long _tokens;
        double _lastRefillTime;
        readonly object _lock = new object();
        readonly Stopwatch _sw = new Stopwatch();

        public long BytesSent { get; private set; }
        public long BytesDropped { get; private set; }
        public int DropCount { get; private set; }

        public BandwidthLimiter(long maxBytesPerSecond, long burstBytes = 0)
        {
            _maxBytesPerSecond = maxBytesPerSecond;
            _burstBytes = burstBytes > 0 ? burstBytes : maxBytesPerSecond;
            _tokens = _burstBytes;
            _sw.Start();
            _lastRefillTime = _sw.Elapsed.TotalSeconds;
        }

        public bool TryConsume(long byteCount)
        {
            lock (_lock)
            {
                Refill();
                if (_tokens >= byteCount)
                {
                    _tokens -= byteCount;
                    BytesSent += byteCount;
                    return true;
                }
                BytesDropped += byteCount;
                DropCount++;
                return false;
            }
        }

        public bool IsThrottled
        {
            get
            {
                lock (_lock)
                {
                    Refill();
                    return _tokens < 64;
                }
            }
        }

        public double UtilizationPercent
        {
            get
            {
                lock (_lock)
                {
                    if (_maxBytesPerSecond <= 0) return 0;
                    Refill();
                    double maxPossible = _burstBytes;
                    if (maxPossible <= 0) return 0;
                    return (1.0 - (double)_tokens / maxPossible) * 100.0;
                }
            }
        }

        void Refill()
        {
            double now = _sw.Elapsed.TotalSeconds;
            double elapsed = now - _lastRefillTime;
            if (elapsed <= 0) return;

            _lastRefillTime = now;
            long newTokens = (long)(elapsed * _maxBytesPerSecond);
            _tokens = Math.Min(_tokens + newTokens, _burstBytes);
        }

        public void Reset()
        {
            lock (_lock)
            {
                _tokens = _burstBytes;
                BytesSent = 0;
                BytesDropped = 0;
                DropCount = 0;
            }
        }
    }
}
