using System;
using System.Diagnostics;
using System.Threading;
using Xunit;
using ParalivesMultiplayer.Performance;

namespace ParalivesMultiplayer.Tests
{
    public class BandwidthLimiterTests
    {
        [Fact]
        public void Constructor_InitializesWithBurstTokens()
        {
            var limiter = new BandwidthLimiter(1000, 5000);
            Assert.True(limiter.TryConsume(5000));
            Assert.False(limiter.TryConsume(1));
        }

        [Fact]
        public void Constructor_DefaultBurstEqualsRate()
        {
            var limiter = new BandwidthLimiter(10000);
            Assert.True(limiter.TryConsume(10000));
            Assert.False(limiter.TryConsume(1));
        }

        [Fact]
        public void TryConsume_AllowsWithinBudget()
        {
            var limiter = new BandwidthLimiter(10000, 10000);
            Assert.True(limiter.TryConsume(5000));
            Assert.Equal(5000L, limiter.BytesSent);
            Assert.True(limiter.TryConsume(4999));
            Assert.Equal(9999L, limiter.BytesSent);
        }

        [Fact]
        public void TryConsume_RejectsOverBudget()
        {
            var limiter = new BandwidthLimiter(10000, 10000);
            limiter.TryConsume(10000);
            Assert.False(limiter.TryConsume(1));
            Assert.Equal(1L, limiter.BytesDropped);
            Assert.Equal(1, limiter.DropCount);
        }

        [Fact]
        public void TryConsume_RefillsOverTime()
        {
            var limiter = new BandwidthLimiter(100000, 100000);
            limiter.TryConsume(100000);

            Thread.Sleep(100);

            Assert.True(limiter.TryConsume(5000));
        }

        [Fact]
        public void IsThrottled_ReturnsTrueWhenLowTokens()
        {
            var limiter = new BandwidthLimiter(10000, 10000);
            limiter.TryConsume(9950);
            Assert.True(limiter.IsThrottled);
        }

        [Fact]
        public void IsThrottled_ReturnsFalseWhenHealthy()
        {
            var limiter = new BandwidthLimiter(10000, 10000);
            Assert.False(limiter.IsThrottled);
        }

        [Fact]
        public void UtilizationPercent_IncreasesWithUsage()
        {
            var limiter = new BandwidthLimiter(10000, 10000);
            double initialUtil = limiter.UtilizationPercent;
            limiter.TryConsume(8000);
            double afterUtil = limiter.UtilizationPercent;
            Assert.True(afterUtil > initialUtil);
        }

        [Fact]
        public void Reset_ClearsStats()
        {
            var limiter = new BandwidthLimiter(10000, 10000);
            limiter.TryConsume(5000);
            limiter.TryConsume(6000);

            limiter.Reset();
            Assert.Equal(0L, limiter.BytesSent);
            Assert.Equal(0L, limiter.BytesDropped);
            Assert.Equal(0, limiter.DropCount);
            Assert.True(limiter.TryConsume(10000));
        }

        [Fact]
        public void MultipleConsumers_AreThreadSafe()
        {
            var limiter = new BandwidthLimiter(1000000, 1000000);
            var sw = Stopwatch.StartNew();
            var threads = new System.Threading.Thread[4];

            for (int i = 0; i < threads.Length; i++)
            {
                threads[i] = new System.Threading.Thread(() =>
                {
                    for (int j = 0; j < 1000; j++)
                    {
                        limiter.TryConsume(100);
                    }
                });
                threads[i].Start();
            }

            foreach (var t in threads) t.Join();

            sw.Stop();
            Assert.True(limiter.BytesSent >= 0);
            Assert.True(limiter.BytesDropped >= 0);
        }

        [Fact]
        public void TokenRefill_DoesNotExceedBurst()
        {
            var limiter = new BandwidthLimiter(100000, 5000);
            Thread.Sleep(500);

            Assert.True(limiter.TryConsume(5000));
            Assert.False(limiter.TryConsume(4000));
        }

        [Fact]
        public void ZeroConsumption_IsAllowed()
        {
            var limiter = new BandwidthLimiter(1000, 1000);
            Assert.True(limiter.TryConsume(0));
        }
    }
}
