using System;

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
