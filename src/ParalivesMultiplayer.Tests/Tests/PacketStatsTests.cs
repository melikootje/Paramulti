using System;
using Xunit;
using ParalivesMultiplayer.Networking;

namespace ParalivesMultiplayer.Tests
{
    public class PacketStatsTests
    {
        public PacketStatsTests()
        {
            PacketStats.Reset();
        }

        [Fact]
        public void RecordSent_IncreasesBytesAndMessages()
        {
            PacketStats.RecordSent(100);
            Assert.Equal(100L, PacketStats.TotalBytesSent);
            Assert.Equal(1, PacketStats.TotalMessagesSent);
        }

        [Fact]
        public void RecordReceived_IncreasesBytesAndMessages()
        {
            PacketStats.RecordReceived(200);
            Assert.Equal(200L, PacketStats.TotalBytesReceived);
            Assert.Equal(1, PacketStats.TotalMessagesReceived);
        }

        [Fact]
        public void RecordError_IncreasesErrorCount()
        {
            PacketStats.RecordError();
            PacketStats.RecordError();
            Assert.Equal(2, PacketStats.TotalErrors);
        }

        [Fact]
        public void RecordPing_CalculatesAverage()
        {
            PacketStats.RecordPing(10.0);
            PacketStats.RecordPing(20.0);
            PacketStats.RecordPing(30.0);
            Assert.Equal(20.0, PacketStats.AveragePingMs, 0.001);
            Assert.Equal(3, PacketStats.PingSampleCount);
        }

        [Fact]
        public void RecordPing_NoSamples_ReturnsZero()
        {
            Assert.Equal(0.0, PacketStats.AveragePingMs);
            Assert.Equal(0, PacketStats.PingSampleCount);
        }

        [Fact]
        public void PingQueue_CapsAt64Samples()
        {
            for (int i = 0; i < 100; i++)
            {
                PacketStats.RecordPing(i);
            }
            Assert.Equal(64, PacketStats.PingSampleCount);
        }

        [Fact]
        public void Reset_ClearsAllStats()
        {
            PacketStats.RecordSent(100);
            PacketStats.RecordReceived(200);
            PacketStats.RecordError();
            PacketStats.RecordPing(10.0);

            PacketStats.Reset();

            Assert.Equal(0L, PacketStats.TotalBytesSent);
            Assert.Equal(0L, PacketStats.TotalBytesReceived);
            Assert.Equal(0, PacketStats.TotalMessagesSent);
            Assert.Equal(0, PacketStats.TotalMessagesReceived);
            Assert.Equal(0, PacketStats.TotalErrors);
            Assert.Equal(0.0, PacketStats.AveragePingMs);
        }

        [Fact]
        public void FormatBytes_ReturnsCorrectUnits()
        {
            Assert.Equal("500 B", PacketStats.FormatBytes(500));
            Assert.Equal("1.0 KB", PacketStats.FormatBytes(1024));
            Assert.Equal("1.5 KB", PacketStats.FormatBytes(1536));
            Assert.Equal("1.00 MB", PacketStats.FormatBytes(1048576));
            Assert.Equal("2.50 MB", PacketStats.FormatBytes(2621440));
        }

        [Fact]
        public void MultipleRecordSent_Accumulate()
        {
            PacketStats.RecordSent(100);
            PacketStats.RecordSent(200);
            PacketStats.RecordSent(300);
            Assert.Equal(600L, PacketStats.TotalBytesSent);
            Assert.Equal(3, PacketStats.TotalMessagesSent);
        }

        [Fact]
        public void ThreadSafety_ConcurrentRecords()
        {
            var threads = new System.Threading.Thread[4];
            for (int i = 0; i < threads.Length; i++)
            {
                threads[i] = new System.Threading.Thread(() =>
                {
                    for (int j = 0; j < 1000; j++)
                    {
                        PacketStats.RecordSent(10);
                    }
                });
                threads[i].Start();
            }

            foreach (var t in threads) t.Join();
            Assert.Equal(40000L, PacketStats.TotalBytesSent);
            Assert.Equal(4000, PacketStats.TotalMessagesSent);
        }
    }
}
