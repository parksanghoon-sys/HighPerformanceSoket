using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Hps.Buffers;

namespace Hps.Transport
{
    /// <summary>
    /// connection-scoped fixed send registry가 반환하는 등록 buffer slot 정보다.
    /// 현재 Task에서는 lookup surface를 먼저 고정하고, 실제 lookup 동작은 후속 Red 테스트가 요구할 때 채운다.
    /// </summary>
    internal readonly struct IoUringFixedSendBufferSlot
    {
        internal IoUringFixedSendBufferSlot(byte[] registeredArray, int bufferIndex, int payloadOffset, int payloadLength)
        {
            RegisteredArray = registeredArray;
            BufferIndex = bufferIndex;
            PayloadOffset = payloadOffset;
            PayloadLength = payloadLength;
        }

        internal byte[] RegisteredArray
        {
            get;
        }

        internal int BufferIndex
        {
            get;
        }

        internal int PayloadOffset
        {
            get;
        }

        internal int PayloadLength
        {
            get;
        }
    }

    /// <summary>
    /// TCP send pump가 send마다 RegisterBuffers/UnregisterBuffers를 반복하지 않도록,
    /// connection/resource 수명에 묶인 fixed send buffer table 조회를 담당한다.
    /// </summary>
    internal sealed class IoUringFixedSendBufferRegistry : IDisposable
    {
        private readonly IIoUringFixedBufferRegistration _registration;
        private readonly Dictionary<byte[], Entry> _entriesByArray;
        private Entry[]? _entries;
        private int _disposed;

        private IoUringFixedSendBufferRegistry(
            IIoUringFixedBufferRegistration registration,
            Dictionary<byte[], Entry> entriesByArray,
            Entry[] entries)
        {
            _registration = registration;
            _entriesByArray = entriesByArray;
            _entries = entries;
        }

        internal int RegisteredBufferCount
        {
            get
            {
                Entry[]? entries = _entries;
                return entries == null ? 0 : entries.Length;
            }
        }

        internal static IoUringFixedSendBufferRegistry Create(
            IoUringQueue queue,
            TransportSendBuffer[] sendBuffers,
            int maxRegisteredBufferCount)
        {
            if (queue == null)
                throw new ArgumentNullException(nameof(queue));
            if (sendBuffers == null)
                throw new ArgumentNullException(nameof(sendBuffers));
            if (maxRegisteredBufferCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxRegisteredBufferCount), "fixed send registry capacity는 1 이상이어야 합니다.");

            byte[][] arrays = SelectUniqueArrays(sendBuffers, maxRegisteredBufferCount);
            IoUringRegisteredBufferSet registration = IoUringRegisteredBufferSet.Register(queue, arrays);

            return CreateForRegisteredBuffers(registration, sendBuffers, maxRegisteredBufferCount);
        }

        internal static IoUringFixedSendBufferRegistry CreateForRegisteredBuffers(
            IIoUringFixedBufferRegistration registration,
            TransportSendBuffer[] sendBuffers,
            int maxRegisteredBufferCount)
        {
            if (registration == null)
                throw new ArgumentNullException(nameof(registration));
            if (sendBuffers == null)
                throw new ArgumentNullException(nameof(sendBuffers));
            if (maxRegisteredBufferCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxRegisteredBufferCount), "fixed send registry capacity는 1 이상이어야 합니다.");

            int effectiveCapacity = Math.Min(maxRegisteredBufferCount, registration.RegisteredBufferCount);
            Dictionary<byte[], Entry> entriesByArray = new Dictionary<byte[], Entry>(ReferenceEqualityComparer<byte[]>.Instance);
            List<Entry> entries = new List<Entry>();

            try
            {
                for (int index = 0; index < sendBuffers.Length && entries.Count < effectiveCapacity; index++)
                {
                    TransportSendBuffer sendBuffer = sendBuffers[index];
                    ArraySegment<byte> segment = GetPayloadSegment(sendBuffer);
                    byte[] array = segment.Array!;

                    // 같은 RefCountedBuffer block에서 여러 slice가 들어올 수 있다.
                    // fixed table은 backing array 단위로 등록되므로 중복 slice는 새 slot을 만들지 않는다.
                    if (entriesByArray.ContainsKey(array))
                        continue;

                    sendBuffer.Buffer.AddRef();
                    Entry entry = new Entry(sendBuffer.Buffer, entries.Count);
                    entriesByArray.Add(array, entry);
                    entries.Add(entry);
                }

                return new IoUringFixedSendBufferRegistry(
                    registration,
                    entriesByArray,
                    entries.ToArray());
            }
            catch
            {
                for (int index = 0; index < entries.Count; index++)
                    entries[index].Buffer.Release();

                registration.Dispose();
                throw;
            }
        }

        internal bool TryGetSlot(TransportSendBuffer sendBuffer, out IoUringFixedSendBufferSlot slot)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                slot = default(IoUringFixedSendBufferSlot);
                return false;
            }

            ArraySegment<byte> segment = GetPayloadSegment(sendBuffer);
            byte[] array = segment.Array!;
            Entry entry;
            if (!_entriesByArray.TryGetValue(array, out entry))
            {
                slot = default(IoUringFixedSendBufferSlot);
                return false;
            }

            slot = new IoUringFixedSendBufferSlot(array, entry.BufferIndex, segment.Offset, segment.Count);
            return true;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            Entry[]? entries = _entries;
            _entries = null;

            try
            {
                _registration.Dispose();
            }
            finally
            {
                if (entries != null)
                {
                    for (int index = 0; index < entries.Length; index++)
                        entries[index].Buffer.Release();
                }
            }
        }

        private static ArraySegment<byte> GetPayloadSegment(TransportSendBuffer sendBuffer)
        {
            Memory<byte> memory = sendBuffer.Buffer.Memory.Slice(sendBuffer.Offset, sendBuffer.Length);
            ArraySegment<byte> segment;

            if (!MemoryMarshal.TryGetArray(memory, out segment) || segment.Array == null)
                throw new InvalidOperationException("io_uring fixed send registry는 pinned byte[] 기반 RefCountedBuffer만 지원합니다.");

            return segment;
        }

        private static byte[][] SelectUniqueArrays(TransportSendBuffer[] sendBuffers, int maxRegisteredBufferCount)
        {
            Dictionary<byte[], byte[]> selected = new Dictionary<byte[], byte[]>(ReferenceEqualityComparer<byte[]>.Instance);
            List<byte[]> arrays = new List<byte[]>();

            for (int index = 0; index < sendBuffers.Length && arrays.Count < maxRegisteredBufferCount; index++)
            {
                ArraySegment<byte> segment = GetPayloadSegment(sendBuffers[index]);
                byte[] array = segment.Array!;

                // native fixed table은 backing array 단위로 등록된다.
                // 같은 block의 여러 slice가 있어도 kernel table에는 같은 배열을 한 번만 올린다.
                if (selected.ContainsKey(array))
                    continue;

                selected.Add(array, array);
                arrays.Add(array);
            }

            if (arrays.Count == 0)
                throw new ArgumentException("fixed send registry에 등록할 payload block이 없습니다.", nameof(sendBuffers));

            return arrays.ToArray();
        }

        private sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T>
            where T : class
        {
            internal static readonly ReferenceEqualityComparer<T> Instance = new ReferenceEqualityComparer<T>();

            private ReferenceEqualityComparer()
            {
            }

            public bool Equals(T? x, T? y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(T obj)
            {
                return RuntimeHelpers.GetHashCode(obj);
            }
        }

        private readonly struct Entry
        {
            internal Entry(RefCountedBuffer buffer, int bufferIndex)
            {
                Buffer = buffer;
                BufferIndex = bufferIndex;
            }

            internal RefCountedBuffer Buffer { get; }

            internal int BufferIndex { get; }
        }
    }
}
