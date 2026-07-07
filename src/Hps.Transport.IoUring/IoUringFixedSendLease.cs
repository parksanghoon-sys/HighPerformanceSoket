using System;
using System.Runtime.InteropServices;
using System.Threading;
using Hps.Buffers;

namespace Hps.Transport
{
    /// <summary>
    /// fixed buffer registration owner 를 lease 에 주입하기 위한 최소 내부 계약이다.
    /// production 구현은 <see cref="IoUringRegisteredBufferSet"/>이 맡고, 테스트는 dispose 횟수를 관측하는 fake owner 를 사용한다.
    /// </summary>
    internal interface IIoUringFixedBufferRegistration : IDisposable
    {
        /// <summary>
        /// kernel 에 등록된 fixed buffer 개수다. lease contract test 에서 registration owner 가 살아 있는지 관측하는 값이다.
        /// </summary>
        int RegisteredBufferCount { get; }
    }

    /// <summary>
    /// TCP fixed-write payload slice 와 fixed buffer registration lifetime 을 하나의 in-flight scope 로 묶는 내부 lease 다.
    /// 현재 단계에서는 production send pump 에 연결하지 않고, ownership contract 만 먼저 고정한다.
    /// </summary>
    internal sealed class IoUringFixedSendLease : IDisposable
    {
        private readonly TransportSendBuffer _sendBuffer;
        private readonly IIoUringFixedBufferRegistration _registration;
        private int _disposed;

        private IoUringFixedSendLease(
            TransportSendBuffer sendBuffer,
            IIoUringFixedBufferRegistration registration,
            byte[] registeredArray,
            int payloadOffset,
            int payloadLength)
        {
            _sendBuffer = sendBuffer;
            _registration = registration;
            RegisteredArray = registeredArray;
            BufferIndex = 0;
            PayloadOffset = payloadOffset;
            PayloadLength = payloadLength;
        }

        internal byte[] RegisteredArray { get; private set; }

        internal int BufferIndex { get; private set; }

        internal int PayloadOffset { get; private set; }

        internal int PayloadLength { get; private set; }

        internal static IoUringFixedSendLease CreateForRegisteredBuffer(
            TransportSendBuffer sendBuffer,
            IIoUringFixedBufferRegistration registration)
        {
            if (registration == null)
                throw new ArgumentNullException(nameof(registration));

            ArraySegment<byte> segment = GetPayloadSegment(sendBuffer);
            if (segment.Array == null)
                throw new InvalidOperationException("io_uring fixed send lease 는 pinned byte[] 기반 RefCountedBuffer 만 지원합니다.");

            return new IoUringFixedSendLease(sendBuffer, registration, segment.Array, segment.Offset, segment.Count);
        }

        internal static IoUringFixedSendLease Create(IoUringQueue queue, TransportSendBuffer sendBuffer)
        {
            if (queue == null)
                throw new ArgumentNullException(nameof(queue));

            ArraySegment<byte> segment = GetPayloadSegment(sendBuffer);
            if (segment.Array == null)
                throw new InvalidOperationException("io_uring fixed send lease 는 pinned byte[] 기반 RefCountedBuffer 만 지원합니다.");

            IoUringRegisteredBufferSet registration = IoUringRegisteredBufferSet.Register(
                queue,
                new byte[][] { segment.Array });

            return new IoUringFixedSendLease(sendBuffer, registration, segment.Array, segment.Offset, segment.Count);
        }

        internal static IoUringFixedSendLease CreateForSendPump(IoUringQueue queue, TransportSendBuffer sendBuffer)
        {
            if (queue == null)
                throw new ArgumentNullException(nameof(queue));

            return CreateForSendPump(
                sendBuffer,
                delegate(TransportSendBuffer buffer)
                {
                    ArraySegment<byte> segment = GetPayloadSegment(buffer);
                    if (segment.Array == null)
                        throw new InvalidOperationException("io_uring fixed send lease 는 pinned byte[] 기반 RefCountedBuffer 만 지원합니다.");

                    return IoUringRegisteredBufferSet.Register(
                        queue,
                        new byte[][] { segment.Array });
                });
        }

        internal static IoUringFixedSendLease CreateForSendPump(
            TransportSendBuffer sendBuffer,
            Func<TransportSendBuffer, IIoUringFixedBufferRegistration> register)
        {
            if (register == null)
                throw new ArgumentNullException(nameof(register));

            // production send pump 의 InFlightSend ref 는 completion/finally 경계가 소유한다.
            // fixed-write lease 는 registration lifetime 동안 payload 를 별도로 붙잡아야 하므로
            // 여기서 lease-owned ref 를 추가하고, 이후 Dispose 또는 실패 rollback 에서 이 ref 만 반환한다.
            sendBuffer.Buffer.AddRef();

            try
            {
                IIoUringFixedBufferRegistration registration = register(sendBuffer);
                return CreateForRegisteredBuffer(sendBuffer, registration);
            }
            catch
            {
                sendBuffer.Buffer.Release();
                throw;
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            try
            {
                _registration.Dispose();
            }
            finally
            {
                _sendBuffer.Buffer.Release();
            }
        }

        private static ArraySegment<byte> GetPayloadSegment(TransportSendBuffer sendBuffer)
        {
            RefCountedBuffer buffer = sendBuffer.Buffer;
            Memory<byte> memory = buffer.Memory.Slice(sendBuffer.Offset, sendBuffer.Length);
            ArraySegment<byte> segment;

            if (!MemoryMarshal.TryGetArray(memory, out segment))
                throw new InvalidOperationException("io_uring fixed send lease 는 pinned byte[] 기반 RefCountedBuffer 만 지원합니다.");

            return segment;
        }
    }
}
