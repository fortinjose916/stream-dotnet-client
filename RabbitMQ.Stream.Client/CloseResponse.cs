using System;
using System.Buffers;
using System.Collections.Generic;

namespace RabbitMQ.Stream.Client
{
    public readonly struct CloseResponse : ICommand
    {
        public const ushort Key = 22;
        private readonly uint correlationId;
        private readonly ResponseCode responseCode;
        
        public CloseResponse(uint correlationId, ResponseCode responseCode)
        {
            this.correlationId = correlationId;
            this.responseCode = responseCode;
        }

        public int SizeNeeded => throw new NotImplementedException();

        public uint CorrelationId => correlationId;

        public ResponseCode ResponseCode => responseCode;

        public int Write(Span<byte> span)
        {
            throw new NotImplementedException();
        }
        internal static int Read(ReadOnlySequence<byte> frame, out CloseResponse command)
        {
            var offset = WireFormatting.ReadUInt16(frame, out var tag);
            offset += WireFormatting.ReadUInt16(frame.Slice(offset), out var version);
            offset += WireFormatting.ReadUInt32(frame.Slice(offset), out var correlation);
            offset += WireFormatting.ReadUInt16(frame.Slice(offset), out var responseCode);
            command = new CloseResponse(correlation, (ResponseCode)responseCode);
            return offset;
        }
    }
}
