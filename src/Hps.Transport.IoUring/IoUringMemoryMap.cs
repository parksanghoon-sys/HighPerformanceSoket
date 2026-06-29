using System;

namespace Hps.Transport
{
    /// <summary>
    /// io_uring ring mmap pointer 와 length 를 소유하는 disposable owner 다.
    ///
    /// mmap 영역은 managed array 가 아니므로 GC가 수명을 알 수 없다. queue owner 가 Dispose 될 때
    /// 이 타입을 통해 munmap 을 정확히 한 번 호출한다.
    /// </summary>
    internal sealed class IoUringMemoryMap : IDisposable
    {
        private IntPtr _pointer;
        private UIntPtr _length;
        private bool _disposed;

        private IoUringMemoryMap(IntPtr pointer, UIntPtr length)
        {
            _pointer = pointer;
            _length = length;
        }

        internal IntPtr Pointer
        {
            get
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(IoUringMemoryMap));

                return _pointer;
            }
        }

        internal static IoUringMemoryMap Map(IoUringSafeHandle fileDescriptor, UIntPtr length, ulong offset)
        {
            if (fileDescriptor == null)
                throw new ArgumentNullException(nameof(fileDescriptor));
            if (fileDescriptor.IsInvalid)
                throw new ArgumentException("io_uring file descriptor 가 유효하지 않습니다.", nameof(fileDescriptor));

            IntPtr pointer = IoUringNative.Map(fileDescriptor.FileDescriptor, length, offset);
            return new IoUringMemoryMap(pointer, length);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            IntPtr pointer = _pointer;
            UIntPtr length = _length;
            _pointer = IntPtr.Zero;
            _length = UIntPtr.Zero;

            IoUringNative.Unmap(pointer, length);
        }
    }
}
