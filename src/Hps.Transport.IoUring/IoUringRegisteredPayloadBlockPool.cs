using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Hps.Buffers;

namespace Hps.Transport
{
    /// <summary>
    /// io_uring queue lifetime 에 묶인 TCP publish payload fixed-buffer pool 이다.
    ///
    /// 이 pool 은 fallback 할당을 만들지 않는다. 등록된 slot 이 모두 사용 중이면 caller 가
    /// fallback source 를 명시적으로 선택할 수 있도록 TryRentCounted 가 false 를 반환한다.
    /// RefCountedBuffer 의 마지막 Release 는 Return(byte[]) 으로 돌아오며, 이때 같은 fixed table slot 을
    /// 다시 free 로 전환한다.
    /// </summary>
    internal sealed class IoUringRegisteredPayloadBlockPool : IRefCountedBufferOwner, IDisposable
    {
        private readonly object _gate;
        private readonly IIoUringFixedBufferRegistration _registration;
        private readonly Dictionary<byte[], Slot> _slotsByArray;
        private readonly Queue<int> _freeSlots;
        private readonly Slot[] _slots;
        private bool _disposed;

        private IoUringRegisteredPayloadBlockPool(
            int blockSize,
            IIoUringFixedBufferRegistration registration,
            Dictionary<byte[], Slot> slotsByArray,
            Slot[] slots)
        {
            BlockSize = blockSize;
            _gate = new object();
            _registration = registration;
            _slotsByArray = slotsByArray;
            _slots = slots;
            _freeSlots = new Queue<int>(slots.Length);

            for (int index = 0; index < slots.Length; index++)
                _freeSlots.Enqueue(index);
        }

        public int BlockSize { get; private set; }

        internal int SlotCount
        {
            get { return _slots.Length; }
        }

        internal static IoUringRegisteredPayloadBlockPool Create(
            IoUringQueue queue,
            int blockSize,
            int slotCount)
        {
            if (queue == null)
                throw new ArgumentNullException(nameof(queue));

            byte[][] blocks = AllocateBlocks(blockSize, slotCount);
            IoUringRegisteredBufferSet registration = IoUringRegisteredBufferSet.Register(queue, blocks);

            try
            {
                return CreateForRegisteredBlocks(blockSize, registration, blocks);
            }
            catch
            {
                registration.Dispose();
                throw;
            }
        }

        internal static IoUringRegisteredPayloadBlockPool CreateForRegisteredBuffers(
            int blockSize,
            int slotCount,
            IIoUringFixedBufferRegistration registration)
        {
            if (registration == null)
                throw new ArgumentNullException(nameof(registration));

            byte[][] blocks = AllocateBlocks(blockSize, slotCount);
            return CreateForRegisteredBlocks(blockSize, registration, blocks);
        }

        internal bool TryRentCounted(out RefCountedBuffer? buffer)
        {
            lock (_gate)
            {
                if (_disposed || _freeSlots.Count == 0)
                {
                    buffer = null;
                    return false;
                }

                int slotIndex = _freeSlots.Dequeue();
                Slot slot = _slots[slotIndex];
                if (slot.InUse)
                    throw new InvalidOperationException("registered payload slot 이 이미 사용 중입니다.");

                slot.InUse = true;
                _slots[slotIndex] = slot;
                _slotsByArray[slot.Block] = slot;
                buffer = new RefCountedBuffer(this, slot.Block);
                return true;
            }
        }

        public void Return(byte[] block)
        {
            if (block == null)
                throw new ArgumentNullException(nameof(block));

            lock (_gate)
            {
                Slot slot;
                if (!_slotsByArray.TryGetValue(block, out slot))
                    throw new InvalidOperationException("이 registered payload pool 이 소유하지 않는 block 입니다.");
                if (!slot.InUse)
                    throw new InvalidOperationException("registered payload slot 이 이미 반환되었습니다.");

                slot.InUse = false;
                _slots[slot.BufferIndex] = slot;
                _slotsByArray[block] = slot;

                if (!_disposed)
                    _freeSlots.Enqueue(slot.BufferIndex);
            }
        }

        internal bool TryGetBufferIndex(Memory<byte> memory, out int bufferIndex)
        {
            ArraySegment<byte> segment;
            if (!MemoryMarshal.TryGetArray(memory, out segment) || segment.Array == null)
            {
                bufferIndex = -1;
                return false;
            }

            lock (_gate)
            {
                Slot slot;
                if (_slotsByArray.TryGetValue(segment.Array, out slot))
                {
                    bufferIndex = slot.BufferIndex;
                    return true;
                }
            }

            bufferIndex = -1;
            return false;
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                    return;

                _disposed = true;
                _freeSlots.Clear();
            }

            _registration.Dispose();
        }

        private static byte[][] AllocateBlocks(int blockSize, int slotCount)
        {
            if (blockSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(blockSize), "registered payload block size 는 1 이상이어야 합니다.");
            if (slotCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(slotCount), "registered payload slot count 는 1 이상이어야 합니다.");

            byte[][] blocks = new byte[slotCount][];
            for (int index = 0; index < blocks.Length; index++)
                blocks[index] = GC.AllocateUninitializedArray<byte>(blockSize, pinned: true);

            return blocks;
        }

        private static IoUringRegisteredPayloadBlockPool CreateForRegisteredBlocks(
            int blockSize,
            IIoUringFixedBufferRegistration registration,
            byte[][] blocks)
        {
            if (registration.RegisteredBufferCount != blocks.Length)
                throw new ArgumentException("registration 의 fixed buffer count 와 payload slot count 가 일치해야 합니다.", nameof(registration));

            Dictionary<byte[], Slot> slotsByArray = new Dictionary<byte[], Slot>(ReferenceEqualityComparer<byte[]>.Instance);
            Slot[] slots = new Slot[blocks.Length];

            for (int index = 0; index < blocks.Length; index++)
            {
                byte[] block = blocks[index];
                Slot slot = new Slot(block, index, false);
                slots[index] = slot;
                slotsByArray.Add(block, slot);
            }

            return new IoUringRegisteredPayloadBlockPool(blockSize, registration, slotsByArray, slots);
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

        private struct Slot
        {
            internal Slot(byte[] block, int bufferIndex, bool inUse)
            {
                Block = block;
                BufferIndex = bufferIndex;
                InUse = inUse;
            }

            internal byte[] Block { get; private set; }

            internal int BufferIndex { get; private set; }

            internal bool InUse { get; set; }
        }
    }
}
