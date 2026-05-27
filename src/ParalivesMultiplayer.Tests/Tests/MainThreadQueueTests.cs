using System;
using System.Threading;
using Xunit;
using ParalivesMultiplayer.Networking;

namespace ParalivesMultiplayer.Tests
{
    public class MainThreadQueueTests
    {
        [Fact]
        public void Enqueue_IncrementsPendingCount()
        {
            int before = MainThreadQueue.PendingCount;
            MainThreadQueue.Enqueue(() => { });
            Assert.Equal(before + 1, MainThreadQueue.PendingCount);
        }

        [Fact]
        public void Enqueue_NullAction_IsIgnored()
        {
            int before = MainThreadQueue.PendingCount;
            MainThreadQueue.Enqueue(null);
            Assert.Equal(before, MainThreadQueue.PendingCount);
        }

        [Fact]
        public void Drain_ReducesPendingCount()
        {
            int before = MainThreadQueue.PendingCount;
            for (int i = 0; i < 10; i++)
                MainThreadQueue.Enqueue(() => { });
            int afterEnqueue = MainThreadQueue.PendingCount;
            Assert.Equal(before + 10, afterEnqueue);

            MainThreadQueue.Drain();
            Assert.True(MainThreadQueue.PendingCount < afterEnqueue);
        }

        [Fact]
        public void Drain_LimitsTo128ActionsPerCall()
        {
            int before = MainThreadQueue.PendingCount;
            for (int i = 0; i < 200; i++)
                MainThreadQueue.Enqueue(() => { });
            int afterEnqueue = MainThreadQueue.PendingCount;
            Assert.Equal(before + 200, afterEnqueue);

            MainThreadQueue.Drain();
            int remaining = MainThreadQueue.PendingCount;
            int drained = afterEnqueue - remaining;
            Assert.Equal(128, drained);
        }

        [Fact]
        public void Drain_EmptyQueue_IsSafe()
        {
            MainThreadQueue.Drain();
            Assert.True(MainThreadQueue.PendingCount >= 0);
        }

        [Fact]
        public void Drain_DoesNotThrowOnBadAction()
        {
            int before = MainThreadQueue.PendingCount;
            MainThreadQueue.Enqueue(() => { throw new InvalidOperationException("test"); });
            MainThreadQueue.Enqueue(() => { });

            int afterEnqueue = MainThreadQueue.PendingCount;
            Assert.Equal(before + 2, afterEnqueue);

            var ex = Record.Exception(() => MainThreadQueue.Drain());
            Assert.Null(ex);
        }

        [Fact]
        public void SecondDrain_CompletesRemaining()
        {
            int before = MainThreadQueue.PendingCount;
            for (int i = 0; i < 200; i++)
                MainThreadQueue.Enqueue(() => { });
            int afterEnqueue = MainThreadQueue.PendingCount;
            Assert.Equal(before + 200, afterEnqueue);

            MainThreadQueue.Drain();
            int afterFirst = MainThreadQueue.PendingCount;
            Assert.Equal(afterEnqueue - 128, afterFirst);

            MainThreadQueue.Drain();
            int afterSecond = MainThreadQueue.PendingCount;
            Assert.True(afterSecond < afterFirst);
        }

        [Fact]
        public void ThreadSafety_ConcurrentEnqueue()
        {
            int before = MainThreadQueue.PendingCount;
            var threads = new Thread[4];
            for (int i = 0; i < threads.Length; i++)
            {
                threads[i] = new Thread(() =>
                {
                    for (int j = 0; j < 100; j++)
                        MainThreadQueue.Enqueue(() => { });
                });
                threads[i].Start();
            }
            foreach (var t in threads) t.Join();

            Assert.Equal(before + 400, MainThreadQueue.PendingCount);
        }
    }
}
