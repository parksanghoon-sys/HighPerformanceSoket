using System;
using System.Runtime.InteropServices;

namespace Hps.Transport
{
    /// <summary>
    /// io_uring setup 이 반환한 file descriptor 를 정확히 한 번 닫는 owner 다.
    ///
    /// fd 는 transport 나 queue user 가 직접 닫지 않는다. queue/mmap cleanup 중 예외가 나도
    /// SafeHandle finalizer fallback 이 남도록 이 타입에 close 책임을 모은다.
    /// </summary>
    internal sealed class IoUringSafeHandle : SafeHandle
    {
        private IoUringSafeHandle()
            : base(new IntPtr(-1), true)
        {
        }

        internal IoUringSafeHandle(int fileDescriptor)
            : base(new IntPtr(-1), true)
        {
            SetHandle(new IntPtr(fileDescriptor));
        }

        public override bool IsInvalid
        {
            get { return handle.ToInt64() < 0; }
        }

        internal int FileDescriptor
        {
            get { return handle.ToInt32(); }
        }

        protected override bool ReleaseHandle()
        {
            return IoUringNative.CloseFileDescriptor(handle.ToInt32());
        }
    }
}
