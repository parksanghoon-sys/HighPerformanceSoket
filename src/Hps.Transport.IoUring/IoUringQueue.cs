using System;
using System.Threading;

namespace Hps.Transport
{
    /// <summary>
    /// io_uring setup fd 와 SQ/CQ/SQE mmap 수명을 함께 소유하는 queue root 다.
    ///
    /// 이 타입은 아직 submit/complete pump 를 제공하지 않는다. 첫 native boundary 에서는 setup 과 cleanup 이
    /// 안전한지 확인하고, 후속 TCP/UDP pump 가 필요한 pointer 계산은 이 owner 안쪽으로만 확장한다.
    /// </summary>
    internal sealed class IoUringQueue : IDisposable
    {
        private readonly IoUringParams _parameters;
        private IoUringSafeHandle? _handle;
        private IoUringMemoryMap? _sqRing;
        private IoUringMemoryMap? _cqRing;
        private IoUringMemoryMap? _sqes;
        private readonly object _submissionGate;
        private bool _disposed;

        private IoUringQueue(
            IoUringSafeHandle handle,
            IoUringMemoryMap sqRing,
            IoUringMemoryMap? cqRing,
            IoUringMemoryMap sqes,
            IoUringParams parameters)
        {
            _handle = handle;
            _sqRing = sqRing;
            _cqRing = cqRing;
            _sqes = sqes;
            _parameters = parameters;
            _submissionGate = new object();
        }

        internal uint SubmissionQueueEntries
        {
            get { return _parameters.SqEntries; }
        }

        internal uint CompletionQueueEntries
        {
            get { return _parameters.CqEntries; }
        }

        internal int FileDescriptor
        {
            get
            {
                if (_disposed || _handle == null)
                    throw new ObjectDisposedException(nameof(IoUringQueue));

                return _handle.FileDescriptor;
            }
        }

        internal static IoUringQueue CreateForProbe(uint entries)
        {
            if (entries == 0)
                throw new ArgumentOutOfRangeException(nameof(entries), "io_uring entries 값은 1 이상이어야 합니다.");

            IoUringNative.ThrowIfUnsupportedPlatform();

            IoUringParams parameters;
            IoUringSafeHandle handle = IoUringNative.Setup(entries, out parameters);
            IoUringMemoryMap? sqRing = null;
            IoUringMemoryMap? cqRing = null;
            IoUringMemoryMap? sqes = null;

            try
            {
                ulong sqRingSize = CalculateSubmissionQueueRingSize(parameters);
                ulong cqRingSize = CalculateCompletionQueueRingSize(parameters);
                if (IoUringNative.HasSingleMmapFeature(parameters))
                {
                    sqRing = IoUringMemoryMap.Map(handle, ToUIntPtr(Math.Max(sqRingSize, cqRingSize)), IoUringNative.SqRingOffset);
                }
                else
                {
                    sqRing = IoUringMemoryMap.Map(handle, ToUIntPtr(sqRingSize), IoUringNative.SqRingOffset);
                    cqRing = IoUringMemoryMap.Map(handle, ToUIntPtr(cqRingSize), IoUringNative.CqRingOffset);
                }

                sqes = IoUringMemoryMap.Map(handle, ToUIntPtr(CalculateSubmissionQueueEntrySize(parameters)), IoUringNative.SqesOffset);

                IoUringQueue queue = new IoUringQueue(handle, sqRing, cqRing, sqes, parameters);
                handle = null!;
                sqRing = null;
                cqRing = null;
                sqes = null;
                return queue;
            }
            finally
            {
                sqes?.Dispose();
                cqRing?.Dispose();
                sqRing?.Dispose();
                handle?.Dispose();
            }
        }

        internal static IoUringQueueProbeResult TryCreateForProbe(uint entries)
        {
            IoUringCapabilityStatus platformStatus = IoUringNative.GetPlatformStatus();
            if (platformStatus != IoUringCapabilityStatus.Available)
                return new IoUringQueueProbeResult(platformStatus, 0);

            try
            {
                using (IoUringQueue queue = CreateForProbe(entries))
                {
                    return new IoUringQueueProbeResult(IoUringCapabilityStatus.Available, 0);
                }
            }
            catch (IoUringNativeException exception)
            {
                if (IoUringNative.IsUnavailableError(exception.ErrorCode))
                    return new IoUringQueueProbeResult(IoUringCapabilityStatus.Unavailable, exception.ErrorCode);

                return new IoUringQueueProbeResult(IoUringCapabilityStatus.Unavailable, exception.ErrorCode);
            }
            catch (NotSupportedException)
            {
                return new IoUringQueueProbeResult(IoUringCapabilityStatus.UnsupportedOperatingSystem, 0);
            }
        }

        internal unsafe bool TrySubmitReceive(int fileDescriptor, byte[] buffer, int length, ulong token)
        {
            if (fileDescriptor < 0)
                throw new ArgumentOutOfRangeException(nameof(fileDescriptor), "socket file descriptor가 유효하지 않습니다.");
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (length <= 0 || length > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(length), "receive length는 buffer 범위 안의 양수여야 합니다.");
            if (token == 0)
                throw new ArgumentOutOfRangeException(nameof(token), "io_uring user_data token은 0을 사용하지 않습니다.");

            ThrowIfDisposed();

            fixed (byte* receivePointer = buffer)
            {
                lock (_submissionGate)
                {
                    IoUringSubmissionQueueEntry* submission = TryAcquireSubmissionEntry();
                    if (submission == null)
                        return false;

                    *submission = default(IoUringSubmissionQueueEntry);
                    submission->Opcode = IoUringNative.OperationReceive;
                    submission->FileDescriptor = fileDescriptor;
                    submission->Address = (ulong)receivePointer;
                    submission->Length = (uint)length;
                    submission->UserData = token;
                    PublishSubmissionEntry(submission);
                }
            }

            IoUringNative.Enter(FileDescriptor, 1, 0, 0);
            return true;
        }

        internal unsafe bool TrySubmitSend(int fileDescriptor, byte[] buffer, int offset, int length, ulong token)
        {
            if (fileDescriptor < 0)
                throw new ArgumentOutOfRangeException(nameof(fileDescriptor), "socket file descriptor가 유효하지 않습니다.");
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || offset > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(offset), "send offset은 buffer 범위 안에 있어야 합니다.");
            if (length <= 0 || length > buffer.Length - offset)
                throw new ArgumentOutOfRangeException(nameof(length), "send length는 buffer 범위 안의 양수여야 합니다.");
            if (token == 0)
                throw new ArgumentOutOfRangeException(nameof(token), "io_uring user_data token은 0을 사용하지 않습니다.");

            ThrowIfDisposed();

            fixed (byte* bufferPointer = buffer)
            {
                lock (_submissionGate)
                {
                    IoUringSubmissionQueueEntry* submission = TryAcquireSubmissionEntry();
                    if (submission == null)
                        return false;

                    *submission = default(IoUringSubmissionQueueEntry);
                    submission->Opcode = IoUringNative.OperationSend;
                    submission->FileDescriptor = fileDescriptor;
                    submission->Address = (ulong)(bufferPointer + offset);
                    submission->Length = (uint)length;
                    submission->UserData = token;
                    PublishSubmissionEntry(submission);
                }
            }

            IoUringNative.Enter(FileDescriptor, 1, 0, 0);
            return true;
        }

        internal unsafe bool TryDequeueCompletion(out IoUringCompletion completion)
        {
            ThrowIfDisposed();

            IntPtr completionRing = GetCompletionRingPointer();
            byte* completionRingBase = (byte*)completionRing;
            uint* headPointer = (uint*)(completionRingBase + _parameters.CqOffsets.Head);
            uint* tailPointer = (uint*)(completionRingBase + _parameters.CqOffsets.Tail);
            uint* maskPointer = (uint*)(completionRingBase + _parameters.CqOffsets.RingMask);

            uint head = Volatile.Read(ref *headPointer);
            uint tail = Volatile.Read(ref *tailPointer);
            if (head == tail)
            {
                completion = default(IoUringCompletion);
                return false;
            }

            uint index = head & Volatile.Read(ref *maskPointer);
            IoUringCompletionQueueEntry* entries = (IoUringCompletionQueueEntry*)(completionRingBase + _parameters.CqOffsets.Cqes);
            IoUringCompletionQueueEntry* entry = entries + index;

            completion = new IoUringCompletion(entry->UserData, entry->Result, entry->Flags);
            Volatile.Write(ref *headPointer, head + 1);
            return true;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _sqes?.Dispose();
            _cqRing?.Dispose();
            _sqRing?.Dispose();
            _handle?.Dispose();
            _sqes = null;
            _cqRing = null;
            _sqRing = null;
            _handle = null;
        }

        private unsafe IoUringSubmissionQueueEntry* TryAcquireSubmissionEntry()
        {
            IntPtr submissionRing = GetSubmissionRingPointer();
            byte* submissionRingBase = (byte*)submissionRing;
            uint* headPointer = (uint*)(submissionRingBase + _parameters.SqOffsets.Head);
            uint* tailPointer = (uint*)(submissionRingBase + _parameters.SqOffsets.Tail);
            uint* entriesPointer = (uint*)(submissionRingBase + _parameters.SqOffsets.RingEntries);

            uint head = Volatile.Read(ref *headPointer);
            uint tail = Volatile.Read(ref *tailPointer);
            uint entries = Volatile.Read(ref *entriesPointer);
            if (tail - head >= entries)
                return null;

            uint* maskPointer = (uint*)(submissionRingBase + _parameters.SqOffsets.RingMask);
            uint index = tail & Volatile.Read(ref *maskPointer);
            byte* submissionEntries = (byte*)GetSubmissionEntriesPointer();
            return (IoUringSubmissionQueueEntry*)(submissionEntries + (index * IoUringNative.SubmissionQueueEntrySize));
        }

        private unsafe void PublishSubmissionEntry(IoUringSubmissionQueueEntry* submissionEntry)
        {
            IntPtr submissionRing = GetSubmissionRingPointer();
            byte* submissionRingBase = (byte*)submissionRing;
            uint* tailPointer = (uint*)(submissionRingBase + _parameters.SqOffsets.Tail);
            uint* maskPointer = (uint*)(submissionRingBase + _parameters.SqOffsets.RingMask);
            uint* arrayPointer = (uint*)(submissionRingBase + _parameters.SqOffsets.Array);

            uint tail = Volatile.Read(ref *tailPointer);
            uint index = tail & Volatile.Read(ref *maskPointer);
            uint submissionIndex = checked((uint)(((byte*)submissionEntry - (byte*)GetSubmissionEntriesPointer()) / IoUringNative.SubmissionQueueEntrySize));

            Volatile.Write(ref arrayPointer[index], submissionIndex);
            Volatile.Write(ref *tailPointer, tail + 1);
        }

        private IntPtr GetSubmissionRingPointer()
        {
            IoUringMemoryMap? sqRing = _sqRing;
            if (_disposed || sqRing == null)
                throw new ObjectDisposedException(nameof(IoUringQueue));

            return sqRing.Pointer;
        }

        private IntPtr GetCompletionRingPointer()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(IoUringQueue));

            IoUringMemoryMap? cqRing = _cqRing;
            if (cqRing != null)
                return cqRing.Pointer;

            IoUringMemoryMap? sqRing = _sqRing;
            if (sqRing == null)
                throw new ObjectDisposedException(nameof(IoUringQueue));

            return sqRing.Pointer;
        }

        private IntPtr GetSubmissionEntriesPointer()
        {
            IoUringMemoryMap? sqes = _sqes;
            if (_disposed || sqes == null)
                throw new ObjectDisposedException(nameof(IoUringQueue));

            return sqes.Pointer;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(IoUringQueue));
        }

        private static ulong CalculateSubmissionQueueRingSize(IoUringParams parameters)
        {
            return parameters.SqOffsets.Array + ((ulong)parameters.SqEntries * sizeof(uint));
        }

        private static ulong CalculateCompletionQueueRingSize(IoUringParams parameters)
        {
            return parameters.CqOffsets.Cqes + ((ulong)parameters.CqEntries * IoUringNative.CompletionQueueEntrySize);
        }

        private static ulong CalculateSubmissionQueueEntrySize(IoUringParams parameters)
        {
            return (ulong)parameters.SqEntries * IoUringNative.SubmissionQueueEntrySize;
        }

        private static UIntPtr ToUIntPtr(ulong value)
        {
            if (value == 0)
                throw new ArgumentOutOfRangeException(nameof(value), "mmap length 는 0일 수 없습니다.");

            return new UIntPtr(value);
        }
    }

    internal sealed class IoUringQueueProbeResult
    {
        internal IoUringQueueProbeResult(IoUringCapabilityStatus status, int errorCode)
        {
            Status = status;
            ErrorCode = errorCode;
        }

        internal IoUringCapabilityStatus Status { get; }

        internal int ErrorCode { get; }
    }
}
