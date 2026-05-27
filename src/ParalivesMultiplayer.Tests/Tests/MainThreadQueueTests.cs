using System;
using Xunit;
using ParalivesMultiplayer.Networking;

namespace ParalivesMultiplayer.Tests
{
    public class MainThreadQueueTests
    {
        public MainThreadQueueTests()
        {
            MainThreadQueue.Drain();
        }

        [Fact]
        public void Enqueue_AddsActionToQueue()
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
        public void Drain_ExecutesActionsInOrder()
        {
            int order = 0;
            int expected = 0;

            MainThreadQueue.Enqueue(() => { Assert.Equal(0, order); expected++; });
            MainThreadQueue.Enqueue(() => { Assert.Equal(1, order); expected++; });
            MainThreadQueue.Enqueue(() => { Assert.Equal(2, order); expected++; });

            MainThreadQueue.Drain();
            Assert.Equal(3, expected);
        }

        [Fact]
        public void Drain_LimitsActionsPerFrame()
        {
            int executed = 0;
            for (int i = 0; i < 200; i++)
            {
                MainThreadQueue.Enqueue(() => { executed++; });
            }

            MainThreadQueue.Drain();
            Assert.Equal(128, executed);
            Assert.Equal(72, MainThreadQueue.PendingCount);
        }

        [Fact]
        public void Drain_EmptyQueue_DoesNothing()
        {
            MainThreadQueue.Drain();
            Assert.Equal(0, MainThreadQueue.PendingCount);
        }

        [Fact]
        public void Drain_CatchesExceptions()
        {
            bool exceptionCaught = false;
            MainThreadQueue.Enqueue(() => { throw new InvalidOperationException("test"); });
            MainThreadQueue.Enqueue(() => { exceptionCaught = true; });

            MainThreadQueue.Drain();
            Assert.True(exceptionCaught);
        }

        [Fact]
        public void MultipleDrains_ProcessAllActions()
        {
            int executed = 0;
            for (int i = 0; i < 200; i++)
            {
                MainThreadQueue.Enqueue(() => { executed++; });
            }

            MainThreadQueue.Drain();
            Assert.Equal(128, executed);

            MainThreadQueue.Drain();
            Assert.Equal(200, executed);
            Assert.Equal(0, MainThreadQueue.PendingCount);
        }

        [Fact]
        public void PendingCount_ReturnsCorrectValue()
        {
            Assert.Equal(0, MainThreadQueue.PendingCount);
            MainThreadQueue.Enqueue(() => { });
            Assert.Equal(1, MainThreadQueue.PendingCount);
            MainThreadQueue.Enqueue(() => { });
            Assert.Equal(2, MainThreadQueue.PendingCount);
            MainThreadQueue.Drain();
            Assert.Equal(0, MainThreadQueue.PendingCount);
        }

        [Fact]
        public void ThreadSafety_ConcurrentEnqueue()
        {
            var threads = new System.Threading.Thread[4];
            for (int i = 0; i < threads.Length; i++)
            {
                threads[i] = new System.Threading.Thread(() =>
                {
                    for (int j = 0; j < 100; j++)
                    {
                        MainThreadQueue.Enqueue(() => { });
                    }
                });
                threads[i].Start();
            }

            foreach (var t in threads) t.Join();
            Assert.Equal(400, MainThreadQueue.PendingCount);
            MainThreadQueue.Drain();
        }
    }
}
