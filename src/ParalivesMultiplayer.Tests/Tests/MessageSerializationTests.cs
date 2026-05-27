using System;
using System.IO;
using System.Text;
using Xunit;
using ParalivesMultiplayer.Networking;
using ParalivesMultiplayer.Networking.Messages;

namespace ParalivesMultiplayer.Tests
{
    public class MessageSerializationTests
    {
        byte[] EncodeMessage(MessageBase msg)
        {
            using var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms, Encoding.UTF8))
            {
                bw.Write(msg.MessageCode);
                msg.Encode(bw);
            }
            return ms.ToArray();
        }

        MessageBase DecodeMessage(byte[] data)
        {
            using var ms = new MemoryStream(data);
            using var br = new BinaryReader(ms, Encoding.UTF8);
            string code = br.ReadString();

            if (!MessageRegistry.TryGetHandler(code, out var prototype))
                throw new Exception($"Unknown message code: {code}");

            MessageBase result;
            if (!prototype.TryDecode(br, out result))
                throw new Exception($"Failed to decode message: {code}");

            return result;
        }

        void AssertRoundTrip<T>(T original) where T : MessageBase
        {
            var encoded = EncodeMessage(original);
            var decoded = DecodeMessage(encoded) as T;
            Assert.NotNull(decoded);
            Assert.Equal(original.MessageCode, decoded.MessageCode);
        }

        [Fact]
        public void MsgConnect_RoundTrip_PreservesClientName()
        {
            var orig = new MsgConnect { ClientName = "TestPlayer123" };
            var encoded = EncodeMessage(orig);
            var decoded = DecodeMessage(encoded) as MsgConnect;

            Assert.NotNull(decoded);
            Assert.Equal("Connect", decoded.MessageCode);
            Assert.Equal("TestPlayer123", decoded.ClientName);
        }

        [Fact]
        public void MsgDisconnect_RoundTrip_PreservesReason()
        {
            var orig = new MsgDisconnect { Reason = "Player left" };
            var encoded = EncodeMessage(orig);
            var decoded = DecodeMessage(encoded) as MsgDisconnect;

            Assert.NotNull(decoded);
            Assert.Equal("Disconnect", decoded.MessageCode);
            Assert.Equal("Player left", decoded.Reason);
        }

        [Fact]
        public void MsgPlayerJoin_RoundTrip_PreservesFields()
        {
            var orig = new MsgPlayerJoin { PlayerId = 42, PlayerName = "Alice" };
            var encoded = EncodeMessage(orig);
            var decoded = DecodeMessage(encoded) as MsgPlayerJoin;

            Assert.NotNull(decoded);
            Assert.Equal(42, decoded.PlayerId);
            Assert.Equal("Alice", decoded.PlayerName);
        }

        [Fact]
        public void MsgPlayerLeave_RoundTrip_PreservesPlayerId()
        {
            var orig = new MsgPlayerLeave { PlayerId = 99 };
            var encoded = EncodeMessage(orig);
            var decoded = DecodeMessage(encoded) as MsgPlayerLeave;

            Assert.NotNull(decoded);
            Assert.Equal(99, decoded.PlayerId);
        }

        [Fact]
        public void MsgChat_RoundTrip_PreservesContent()
        {
            var orig = new MsgChat { PlayerName = "Bob", Message = "Hello world!" };
            var encoded = EncodeMessage(orig);
            var decoded = DecodeMessage(encoded) as MsgChat;

            Assert.NotNull(decoded);
            Assert.Equal("Bob", decoded.PlayerName);
            Assert.Equal("Hello world!", decoded.Message);
        }

        [Fact]
        public void MsgCursorPing_RoundTrip_PreservesPosition()
        {
            var orig = new MsgCursorPing
            {
                PlayerId = 7,
                Position = new UnityEngine.Vector3(1.5f, 2.5f, 3.5f),
                Tick = 100
            };
            var encoded = EncodeMessage(orig);
            var decoded = DecodeMessage(encoded) as MsgCursorPing;

            Assert.NotNull(decoded);
            Assert.Equal(7, decoded.PlayerId);
            Assert.Equal(100u, decoded.Tick);
            Assert.Equal(1.5f, decoded.Position.x);
            Assert.Equal(2.5f, decoded.Position.y);
            Assert.Equal(3.5f, decoded.Position.z);
        }

        [Fact]
        public void MsgHeartbeat_RoundTrip_PreservesAllFields()
        {
            var orig = new MsgHeartbeat
            {
                PlayerId = 3,
                Tick = 500,
                SequenceNumber = 42,
                TimestampMs = 1234567890L
            };
            var encoded = EncodeMessage(orig);
            var decoded = DecodeMessage(encoded) as MsgHeartbeat;

            Assert.NotNull(decoded);
            Assert.Equal(3, decoded.PlayerId);
            Assert.Equal(500u, decoded.Tick);
            Assert.Equal(42u, decoded.SequenceNumber);
            Assert.Equal(1234567890L, decoded.TimestampMs);
        }

        [Fact]
        public void MsgPing_RoundTrip_PreservesFields()
        {
            var orig = new MsgPing { PlayerId = 1, TimestampMs = 9876543210L };
            var encoded = EncodeMessage(orig);
            var decoded = DecodeMessage(encoded) as MsgPing;

            Assert.NotNull(decoded);
            Assert.Equal(1, decoded.PlayerId);
            Assert.Equal(9876543210L, decoded.TimestampMs);
        }

        [Fact]
        public void MsgPong_RoundTrip_PreservesTimestamps()
        {
            var orig = new MsgPong
            {
                PlayerId = 2,
                OriginalTimestampMs = 1000L,
                ReplyTimestampMs = 1050L
            };
            var encoded = EncodeMessage(orig);
            var decoded = DecodeMessage(encoded) as MsgPong;

            Assert.NotNull(decoded);
            Assert.Equal(2, decoded.PlayerId);
            Assert.Equal(1000L, decoded.OriginalTimestampMs);
            Assert.Equal(1050L, decoded.ReplyTimestampMs);
        }

        [Fact]
        public void MsgReadyCheck_RoundTrip_PreservesFields()
        {
            var orig = new MsgReadyCheck { PlayerId = 5, IsReady = true };
            var encoded = EncodeMessage(orig);
            var decoded = DecodeMessage(encoded) as MsgReadyCheck;

            Assert.NotNull(decoded);
            Assert.Equal(5, decoded.PlayerId);
            Assert.True(decoded.IsReady);
        }

        [Fact]
        public void MsgRequestFullState_RoundTrip_PreservesPlayerId()
        {
            var orig = new MsgRequestFullState { PlayerId = 8 };
            var encoded = EncodeMessage(orig);
            var decoded = DecodeMessage(encoded) as MsgRequestFullState;

            Assert.NotNull(decoded);
            Assert.Equal(8, decoded.PlayerId);
        }

        [Fact]
        public void MsgEntitySpawn_RoundTrip_PreservesTransform()
        {
            var orig = new MsgEntitySpawn
            {
                PlayerId = 1,
                Tick = 100,
                EntityId = 42,
                EntityType = "Building_Wall",
                Position = new UnityEngine.Vector3(10f, 0f, -5f),
                Rotation = new UnityEngine.Quaternion(0f, 0.707f, 0f, 0.707f),
                Scale = new UnityEngine.Vector3(1f, 2f, 1f)
            };
            var encoded = EncodeMessage(orig);
            var decoded = DecodeMessage(encoded) as MsgEntitySpawn;

            Assert.NotNull(decoded);
            Assert.Equal(1, decoded.PlayerId);
            Assert.Equal(100u, decoded.Tick);
            Assert.Equal(42u, decoded.EntityId);
            Assert.Equal("Building_Wall", decoded.EntityType);
            Assert.Equal(10f, decoded.Position.x);
            Assert.Equal(0f, decoded.Position.y);
            Assert.Equal(-5f, decoded.Position.z);
            Assert.Equal(0f, decoded.Rotation.x);
            Assert.Equal(0.707f, decoded.Rotation.y);
            Assert.Equal(0f, decoded.Rotation.z);
            Assert.Equal(0.707f, decoded.Rotation.w);
            Assert.Equal(1f, decoded.Scale.x);
            Assert.Equal(2f, decoded.Scale.y);
            Assert.Equal(1f, decoded.Scale.z);
        }

        [Fact]
        public void MsgEntityDespawn_RoundTrip_PreservesFields()
        {
            var orig = new MsgEntityDespawn { PlayerId = 2, Tick = 200, EntityId = 42 };
            var encoded = EncodeMessage(orig);
            var decoded = DecodeMessage(encoded) as MsgEntityDespawn;

            Assert.NotNull(decoded);
            Assert.Equal(2, decoded.PlayerId);
            Assert.Equal(200u, decoded.Tick);
            Assert.Equal(42u, decoded.EntityId);
        }

        [Fact]
        public void MsgBuildModeEvent_RoundTrip_PreservesAllFields()
        {
            var orig = new MsgBuildModeEvent
            {
                PlayerId = 1,
                Tick = 300,
                EventType = BuildEventType.ObjectPlaced,
                EntityId = 55,
                ObjectTypeId = "Tree_Oak",
                Position = new UnityEngine.Vector3(20f, 1f, 30f),
                Rotation = new UnityEngine.Quaternion(0f, 0f, 0f, 1f),
                Scale = new UnityEngine.Vector3(1.5f, 1.5f, 1.5f),
                StyleName = "Default"
            };
            var encoded = EncodeMessage(orig);
            var decoded = DecodeMessage(encoded) as MsgBuildModeEvent;

            Assert.NotNull(decoded);
            Assert.Equal(1, decoded.PlayerId);
            Assert.Equal(300u, decoded.Tick);
            Assert.Equal(BuildEventType.ObjectPlaced, decoded.EventType);
            Assert.Equal(55u, decoded.EntityId);
            Assert.Equal("Tree_Oak", decoded.ObjectTypeId);
            Assert.Equal(20f, decoded.Position.x);
            Assert.Equal(1f, decoded.Position.y);
            Assert.Equal(30f, decoded.Position.z);
            Assert.Equal(1.5f, decoded.Scale.x);
            Assert.Equal("Default", decoded.StyleName);
        }

        [Fact]
        public void MsgBuildModeEvent_AllEventTypes_SerializeCorrectly()
        {
            foreach (BuildEventType type in (BuildEventType[])Enum.GetValues(typeof(BuildEventType)))
            {
                var orig = new MsgBuildModeEvent
                {
                    PlayerId = 1,
                    Tick = 1,
                    EventType = type,
                    EntityId = 1,
                    ObjectTypeId = "Test",
                    Position = UnityEngine.Vector3.zero,
                    Rotation = UnityEngine.Quaternion.identity,
                    Scale = UnityEngine.Vector3.one,
                    StyleName = ""
                };
                var encoded = EncodeMessage(orig);
                var decoded = DecodeMessage(encoded) as MsgBuildModeEvent;

                Assert.NotNull(decoded);
                Assert.Equal(type, decoded.EventType);
            }
        }

        [Fact]
        public void MsgSaveInitiate_RoundTrip_PreservesFields()
        {
            var orig = new MsgSaveInitiate
            {
                Tick = 1000,
                SceneName = "World_Main",
                TimeoutSeconds = 30f
            };
            var encoded = EncodeMessage(orig);
            var decoded = DecodeMessage(encoded) as MsgSaveInitiate;

            Assert.NotNull(decoded);
            Assert.Equal(1000u, decoded.Tick);
            Assert.Equal("World_Main", decoded.SceneName);
            Assert.Equal(30f, decoded.TimeoutSeconds);
        }

        [Fact]
        public void MsgSaveAck_RoundTrip_PreservesFields()
        {
            var orig = new MsgSaveAck
            {
                PlayerId = 3,
                Tick = 1000,
                Success = true,
                ErrorMessage = ""
            };
            var encoded = EncodeMessage(orig);
            var decoded = DecodeMessage(encoded) as MsgSaveAck;

            Assert.NotNull(decoded);
            Assert.Equal(3, decoded.PlayerId);
            Assert.True(decoded.Success);
            Assert.Equal("", decoded.ErrorMessage);
        }

        [Fact]
        public void MsgSaveComplete_RoundTrip_PreservesFields()
        {
            var orig = new MsgSaveComplete
            {
                Tick = 1001,
                AllAcksReceived = true,
                AcksReceived = 4,
                TotalPlayers = 4
            };
            var encoded = EncodeMessage(orig);
            var decoded = DecodeMessage(encoded) as MsgSaveComplete;

            Assert.NotNull(decoded);
            Assert.Equal(1001u, decoded.Tick);
            Assert.True(decoded.AllAcksReceived);
            Assert.Equal(4, decoded.AcksReceived);
            Assert.Equal(4, decoded.TotalPlayers);
        }

        [Fact]
        public void MsgLoadInitiate_RoundTrip_PreservesFields()
        {
            var orig = new MsgLoadInitiate
            {
                Tick = 2000,
                SceneName = "World_Main",
                TotalChunks = 5
            };
            var encoded = EncodeMessage(orig);
            var decoded = DecodeMessage(encoded) as MsgLoadInitiate;

            Assert.NotNull(decoded);
            Assert.Equal(2000u, decoded.Tick);
            Assert.Equal("World_Main", decoded.SceneName);
            Assert.Equal(5, decoded.TotalChunks);
        }

        [Fact]
        public void MsgLoadStateChunk_RoundTrip_PreservesEntities()
        {
            var orig = new MsgLoadStateChunk
            {
                ChunkIndex = 0,
                TotalChunks = 3
            };
            orig.Entities.Add(new EntitySnapshotEntry
            {
                EntityId = 1,
                EntityType = "Wall",
                Position = new UnityEngine.Vector3(0f, 0f, 0f),
                Rotation = UnityEngine.Quaternion.identity,
                Scale = UnityEngine.Vector3.one,
                OwnerPlayerId = 1
            });
            orig.Entities.Add(new EntitySnapshotEntry
            {
                EntityId = 2,
                EntityType = "Floor",
                Position = new UnityEngine.Vector3(1f, 0f, 1f),
                Rotation = UnityEngine.Quaternion.identity,
                Scale = UnityEngine.Vector3.one,
                OwnerPlayerId = 2
            });

            var encoded = EncodeMessage(orig);
            var decoded = DecodeMessage(encoded) as MsgLoadStateChunk;

            Assert.NotNull(decoded);
            Assert.Equal(0, decoded.ChunkIndex);
            Assert.Equal(3, decoded.TotalChunks);
            Assert.Equal(2, decoded.Entities.Count);
            Assert.Equal(1u, decoded.Entities[0].EntityId);
            Assert.Equal("Wall", decoded.Entities[0].EntityType);
            Assert.Equal(2u, decoded.Entities[1].EntityId);
            Assert.Equal("Floor", decoded.Entities[1].EntityType);
        }

        [Fact]
        public void MsgLoadComplete_RoundTrip_PreservesFields()
        {
            var orig = new MsgLoadComplete
            {
                PlayerId = 1,
                Tick = 2001,
                Success = true,
                ChunksReceived = 5,
                TotalChunks = 5
            };
            var encoded = EncodeMessage(orig);
            var decoded = DecodeMessage(encoded) as MsgLoadComplete;

            Assert.NotNull(decoded);
            Assert.Equal(1, decoded.PlayerId);
            Assert.True(decoded.Success);
            Assert.Equal(5, decoded.ChunksReceived);
            Assert.Equal(5, decoded.TotalChunks);
        }

        [Fact]
        public void MsgReconnectRequest_RoundTrip_PreservesFields()
        {
            var orig = new MsgReconnectRequest
            {
                PlayerId = 5,
                ClientName = "ReturningPlayer",
                LastKnownTick = 999,
                LastSequenceNumber = 42
            };
            var encoded = EncodeMessage(orig);
            var decoded = DecodeMessage(encoded) as MsgReconnectRequest;

            Assert.NotNull(decoded);
            Assert.Equal(5, decoded.PlayerId);
            Assert.Equal("ReturningPlayer", decoded.ClientName);
            Assert.Equal(999u, decoded.LastKnownTick);
            Assert.Equal(42u, decoded.LastSequenceNumber);
        }

        [Fact]
        public void MsgReconnectAck_RoundTrip_PreservesFields()
        {
            var orig = new MsgReconnectAck
            {
                PlayerId = 5,
                Allowed = true,
                SessionTick = 1000,
                ErrorMessage = ""
            };
            var encoded = EncodeMessage(orig);
            var decoded = DecodeMessage(encoded) as MsgReconnectAck;

            Assert.NotNull(decoded);
            Assert.Equal(5, decoded.PlayerId);
            Assert.True(decoded.Allowed);
            Assert.Equal(1000u, decoded.SessionTick);
        }

        [Fact]
        public void MsgHostMigration_RoundTrip_PreservesFields()
        {
            var orig = new MsgHostMigration
            {
                NewHostPlayerId = 2,
                NewHostAddress = "192.168.1.100",
                NewHostPort = 7890,
                SessionTick = 5000
            };
            var encoded = EncodeMessage(orig);
            var decoded = DecodeMessage(encoded) as MsgHostMigration;

            Assert.NotNull(decoded);
            Assert.Equal(2, decoded.NewHostPlayerId);
            Assert.Equal("192.168.1.100", decoded.NewHostAddress);
            Assert.Equal(7890, decoded.NewHostPort);
            Assert.Equal(5000u, decoded.SessionTick);
        }

        [Fact]
        public void MsgFullStateSnapshot_RoundTrip_PreservesEntities()
        {
            var orig = new MsgFullStateSnapshot
            {
                Tick = 3000,
                PlayerCount = 3
            };
            orig.Entities.Add(new EntitySnapshotEntry
            {
                EntityId = 100,
                EntityType = "Player",
                Position = new UnityEngine.Vector3(5f, 0f, 5f),
                Rotation = UnityEngine.Quaternion.identity,
                Scale = UnityEngine.Vector3.one,
                OwnerPlayerId = 1
            });

            var encoded = EncodeMessage(orig);
            var decoded = DecodeMessage(encoded) as MsgFullStateSnapshot;

            Assert.NotNull(decoded);
            Assert.Equal(3000u, decoded.Tick);
            Assert.Equal(3, decoded.PlayerCount);
            Assert.Equal(1, decoded.Entities.Count);
            Assert.Equal(100u, decoded.Entities[0].EntityId);
        }

        [Fact]
        public void MsgInputCommand_RoundTrip_PreservesFields()
        {
            var orig = new MsgInputCommand
            {
                PlayerId = 1,
                Tick = 50,
                Action = InputAction.MoveHorizontal,
                ValueX = 0.8f,
                ValueY = 0f,
                ValueZ = 0f,
                IsButton = false,
                ButtonName = ""
            };
            var encoded = EncodeMessage(orig);
            var decoded = DecodeMessage(encoded) as MsgInputCommand;

            Assert.NotNull(decoded);
            Assert.Equal(1, decoded.PlayerId);
            Assert.Equal(50u, decoded.Tick);
            Assert.Equal(InputAction.MoveHorizontal, decoded.Action);
            Assert.Equal(0.8f, decoded.ValueX);
            Assert.False(decoded.IsButton);
        }

        [Fact]
        public void MsgSyncState_RoundTrip_PreservesPositions()
        {
            var orig = new MsgSyncState
            {
                Tick = 100,
                PlayerCount = 2
            };
            orig.PlayerPositionsX = new float[] { 1f, 2f };
            orig.PlayerPositionsY = new float[] { 0f, 0f };
            orig.PlayerPositionsZ = new float[] { 3f, 4f };

            var encoded = EncodeMessage(orig);
            var decoded = DecodeMessage(encoded) as MsgSyncState;

            Assert.NotNull(decoded);
            Assert.Equal(100u, decoded.Tick);
            Assert.Equal(2, decoded.PlayerCount);
            Assert.Equal(2, decoded.PlayerPositionsX.Length);
            Assert.Equal(1f, decoded.PlayerPositionsX[0]);
            Assert.Equal(3f, decoded.PlayerPositionsZ[0]);
        }

        [Fact]
        public void MsgUpdateState_RoundTrip_PreservesTransform()
        {
            var orig = new MsgUpdateState
            {
                Tick = 200,
                PlayerId = 1,
                Position = new UnityEngine.Vector3(10f, 1f, 20f),
                Rotation = new UnityEngine.Quaternion(0f, 0.5f, 0f, 0.866f),
                InputHorizontal = 0.5f,
                InputVertical = -1f,
                JumpPressed = true,
                AttackPressed = false
            };
            var encoded = EncodeMessage(orig);
            var decoded = DecodeMessage(encoded) as MsgUpdateState;

            Assert.NotNull(decoded);
            Assert.Equal(200u, decoded.Tick);
            Assert.Equal(1, decoded.PlayerId);
            Assert.Equal(10f, decoded.Position.x);
            Assert.Equal(1f, decoded.Position.y);
            Assert.Equal(20f, decoded.Position.z);
            Assert.Equal(0.5f, decoded.InputHorizontal);
            Assert.True(decoded.JumpPressed);
            Assert.False(decoded.AttackPressed);
        }

        [Fact]
        public void MsgBuildObjectPlaced_RoundTrip_PreservesFields()
        {
            var orig = new MsgBuildObjectPlaced
            {
                PlayerId = 1,
                Tick = 75,
                SequenceNumber = 10,
                ObjectTypeId = "Chair_Modern",
                Position = new UnityEngine.Vector3(5f, 0f, 5f),
                Rotation = new UnityEngine.Quaternion(0f, 0.707f, 0f, 0.707f),
                StyleName = "Wooden"
            };
            var encoded = EncodeMessage(orig);
            var decoded = DecodeMessage(encoded) as MsgBuildObjectPlaced;

            Assert.NotNull(decoded);
            Assert.Equal(1, decoded.PlayerId);
            Assert.Equal(75u, decoded.Tick);
            Assert.Equal(10u, decoded.SequenceNumber);
            Assert.Equal("Chair_Modern", decoded.ObjectTypeId);
            Assert.Equal("Wooden", decoded.StyleName);
        }

        [Fact]
        public void EmptyStringFields_SerializeCorrectly()
        {
            var orig = new MsgChat { PlayerName = "", Message = "" };
            var encoded = EncodeMessage(orig);
            var decoded = DecodeMessage(encoded) as MsgChat;

            Assert.NotNull(decoded);
            Assert.Equal("", decoded.PlayerName);
            Assert.Equal("", decoded.Message);
        }

        [Fact]
        public void UnicodeStrings_SerializeCorrectly()
        {
            var orig = new MsgChat { PlayerName = "Player\u00e9", Message = "\u4f60\u597d\u4e16\u754c" };
            var encoded = EncodeMessage(orig);
            var decoded = DecodeMessage(encoded) as MsgChat;

            Assert.NotNull(decoded);
            Assert.Equal("Player\u00e9", decoded.PlayerName);
            Assert.Equal("\u4f60\u597d\u4e16\u754c", decoded.Message);
        }

        [Fact]
        public void LargeTickValues_SerializeCorrectly()
        {
            var orig = new MsgHeartbeat
            {
                PlayerId = 1,
                Tick = uint.MaxValue,
                SequenceNumber = uint.MaxValue,
                TimestampMs = long.MaxValue
            };
            var encoded = EncodeMessage(orig);
            var decoded = DecodeMessage(encoded) as MsgHeartbeat;

            Assert.NotNull(decoded);
            Assert.Equal(uint.MaxValue, decoded.Tick);
            Assert.Equal(uint.MaxValue, decoded.SequenceNumber);
            Assert.Equal(long.MaxValue, decoded.TimestampMs);
        }

        [Fact]
        public void NegativeCoordinates_SerializeCorrectly()
        {
            var orig = new MsgEntitySpawn
            {
                PlayerId = 1,
                Tick = 1,
                EntityId = 1,
                EntityType = "Test",
                Position = new UnityEngine.Vector3(-100f, -50f, -200f),
                Rotation = new UnityEngine.Quaternion(-0.5f, -0.5f, -0.5f, -0.5f),
                Scale = new UnityEngine.Vector3(1f, 1f, 1f)
            };
            var encoded = EncodeMessage(orig);
            var decoded = DecodeMessage(encoded) as MsgEntitySpawn;

            Assert.NotNull(decoded);
            Assert.Equal(-100f, decoded.Position.x);
            Assert.Equal(-50f, decoded.Position.y);
            Assert.Equal(-200f, decoded.Position.z);
        }

        [Fact]
        public void MultipleRoundTrips_MaintainConsistency()
        {
            var orig = new MsgBuildModeEvent
            {
                PlayerId = 42,
                Tick = 9999,
                EventType = BuildEventType.ObjectMoved,
                EntityId = 777,
                ObjectTypeId = "ComplexObject",
                Position = new UnityEngine.Vector3(12.345f, 67.890f, -42.123f),
                Rotation = new UnityEngine.Quaternion(0.1f, 0.2f, 0.3f, 0.928f),
                Scale = new UnityEngine.Vector3(2f, 0.5f, 1f),
                StyleName = "CustomStyle"
            };

            byte[] data = EncodeMessage(orig);
            for (int i = 0; i < 10; i++)
            {
                var decoded = DecodeMessage(data) as MsgBuildModeEvent;
                Assert.NotNull(decoded);
                Assert.Equal(orig.PlayerId, decoded.PlayerId);
                Assert.Equal(orig.Tick, decoded.Tick);
                Assert.Equal(orig.EventType, decoded.EventType);
                Assert.Equal(orig.EntityId, decoded.EntityId);
                Assert.Equal(orig.ObjectTypeId, decoded.ObjectTypeId);
                Assert.Equal(orig.Position.x, decoded.Position.x);
                Assert.Equal(orig.StyleName, decoded.StyleName);
            }
        }
    }
}
