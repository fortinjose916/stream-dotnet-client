using System;
using System.Buffers;
using System.Collections.Generic;

namespace RabbitMQ.Stream.Client
{
    //Deliver => Key Version SubscriptionId OsirisChunk
    //   Key => uint16 // 8
    //   Version => uint32
    //   SubscriptionId => uint8
    public readonly struct Deliver : ICommand
    {
        private readonly byte subscriptionId;
        private readonly Chunk chunk;
        public const ushort Key = 8;
        public int SizeNeeded => throw new NotImplementedException();
        public Deliver(byte subscriptionId, Chunk chunk)
        {
            this.subscriptionId = subscriptionId;
            this.chunk = chunk;
        }

        public IEnumerable<MsgEntry> Messages
        {
            get
            {
                int offset = 0;
                var data = chunk.Data;
                for (ulong i = 0; i < chunk.NumEntries; i++)
                {
                    uint len;
                    offset += WireFormatting.ReadUInt32(data.Slice(offset), out len);
                    //TODO: assuming only simple entries for now
                    yield return new MsgEntry(chunk.ChunkId + i, chunk.Epoch, data.Slice(offset, len));
                }
            }
        }

        public Chunk Chunk => chunk;

        public int Write(Span<byte> span)
        {
            throw new NotImplementedException();
        }
        internal static int Read(ReadOnlySequence<byte> frame, out ICommand command)
        {
            ushort tag;
            ushort version;
            byte subscriptionId;
            var offset = WireFormatting.ReadUInt16(frame, out tag);
            offset += WireFormatting.ReadUInt16(frame.Slice(offset), out version);
            offset += WireFormatting.ReadByte(frame.Slice(offset), out subscriptionId);
            Chunk chunk;
            offset += Chunk.Read(frame.Slice(offset), out chunk);
            command = new Deliver(subscriptionId, chunk);
            return offset;
        }
    }

    public readonly struct MsgEntry
    {
        private readonly ulong offset;
        private readonly ulong epoch;
        private readonly ReadOnlySequence<byte> data;
        public MsgEntry(ulong offset, ulong epoch, ReadOnlySequence<byte> data)
        {
            this.offset = offset;
            this.epoch = epoch;
            this.data = data;
        }

        public ulong Offset => offset;

        public ulong Epoch => epoch;

        public ReadOnlySequence<byte> Data => data;
    }

    public readonly struct Chunk
    {
        public Chunk(byte magicVersion, ushort numEntries, uint numRecords, ulong epoch, ulong chunkId, int crc, ReadOnlySequence<byte> data)
        {
            MagicVersion = magicVersion;
            NumEntries = numEntries;
            NumRecords = numRecords;
            Epoch = epoch;
            ChunkId = chunkId;
            Crc = crc;
            Data = data;
        }
        public byte MagicVersion { get; }
        public ushort NumEntries { get; }
        public uint NumRecords { get; }
        public ulong Epoch { get; }
        public ulong ChunkId { get; }
        public int Crc { get; }
        public ReadOnlySequence<byte> Data { get; }

        //   OsirisChunk => MagicVersion NumEntries NumRecords Epoch ChunkFirstOffset ChunkCrc DataLength Messages
        //   MagicVersion => int8
        //   NumEntries => uint16
        //   NumRecords => uint32
        //   Epoch => uint64
        //   ChunkFirstOffset => uint64
        //   ChunkCrc => int32
        //   DataLength => uint32
        //   Messages => [Message] // no int32 for the size for this array
        //   Message => EntryTypeAndSize
        //   Data => bytes
        internal static int Read(ReadOnlySequence<byte> seq, out Chunk chunk)
        {
            byte magicVersion;
            byte chunkType;
            ushort numEntries;
            uint numRecords;
            long timestamp;
            ulong epoch;
            ulong chunkId;
            int crc;
            uint dataLen;
            uint trailerLen;
            var offset = WireFormatting.ReadByte(seq, out magicVersion);
            offset += WireFormatting.ReadByte(seq.Slice(offset), out chunkType);
            offset += WireFormatting.ReadUInt16(seq.Slice(offset), out numEntries);
            offset += WireFormatting.ReadUInt32(seq.Slice(offset), out numRecords);
            offset += WireFormatting.ReadInt64(seq.Slice(offset), out timestamp);
            offset += WireFormatting.ReadUInt64(seq.Slice(offset), out epoch);
            offset += WireFormatting.ReadUInt64(seq.Slice(offset), out chunkId);
            offset += WireFormatting.ReadInt32(seq.Slice(offset), out crc);
            offset += WireFormatting.ReadUInt32(seq.Slice(offset), out dataLen);
            offset += WireFormatting.ReadUInt32(seq.Slice(offset), out trailerLen);
            offset += 4; // reserved
            var data = seq.Slice(offset, dataLen).ToArray();
            offset += (int)dataLen;
            chunk = new Chunk(magicVersion, numEntries, numRecords, epoch, chunkId, crc, new ReadOnlySequence<byte>(data));
            return offset;
        }
    }
}
