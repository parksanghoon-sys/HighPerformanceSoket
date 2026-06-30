using System;
using System.Runtime.InteropServices;

namespace Hps.Transport
{
    /// <summary>
    /// Linux io_uring syscall 과 mmap entry point 를 숨기는 internal native adapter 다.
    ///
    /// 이 타입은 pointer 수명을 소유하지 않는다. raw native 호출과 platform guard 만 담당하고,
    /// fd/mmap/registration 수명은 별도 owner 타입이 관리한다.
    /// </summary>
    internal static class IoUringNative
    {
        internal const ulong SqRingOffset = 0UL;
        internal const ulong CqRingOffset = 0x8000000UL;
        internal const ulong SqesOffset = 0x10000000UL;
        internal const int CompletionQueueEntrySize = 16;
        internal const int SubmissionQueueEntrySize = 64;
        internal const byte OperationSendMessage = 9;
        internal const byte OperationReceiveMessage = 10;
        internal const byte OperationSend = 26;
        internal const byte OperationReceive = 27;
        internal const uint EnterGetEvents = 0x1;

        private const long IoUringSetupSyscallNumber = 425;
        private const long IoUringEnterSyscallNumber = 426;
        private const long IoUringRegisterSyscallNumber = 427;
        private const uint RegisterBuffersOpcode = 0;
        private const uint UnregisterBuffersOpcode = 1;
        private const int ProtectionRead = 0x1;
        private const int ProtectionWrite = 0x2;
        private const int MapShared = 0x01;
        private const uint FeatureSingleMmap = 0x1;
        private static readonly IntPtr MapFailed = new IntPtr(-1);

        internal static IoUringCapabilityStatus GetPlatformStatus()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return IoUringCapabilityStatus.UnsupportedOperatingSystem;

            if (RuntimeInformation.ProcessArchitecture != Architecture.X64 &&
                RuntimeInformation.ProcessArchitecture != Architecture.Arm64)
            {
                return IoUringCapabilityStatus.Unavailable;
            }

            return IoUringCapabilityStatus.Available;
        }

        internal static void ThrowIfUnsupportedPlatform()
        {
            IoUringCapabilityStatus status = GetPlatformStatus();
            if (status == IoUringCapabilityStatus.UnsupportedOperatingSystem)
                throw new NotSupportedException("io_uring backend는 Linux에서만 사용할 수 있습니다.");

            if (status == IoUringCapabilityStatus.Unavailable)
                throw new NotSupportedException("현재 process architecture 에서는 io_uring syscall 번호가 정의되지 않았습니다.");
        }

        internal static IoUringSafeHandle Setup(uint entries, out IoUringParams parameters)
        {
            if (entries == 0)
                throw new ArgumentOutOfRangeException(nameof(entries), "io_uring entries 값은 1 이상이어야 합니다.");

            ThrowIfUnsupportedPlatform();

            parameters = new IoUringParams();
            long fileDescriptor = SyscallIoUringSetup(IoUringSetupSyscallNumber, entries, ref parameters);
            if (fileDescriptor < 0)
                throw CreateNativeException("io_uring_setup");

            return new IoUringSafeHandle((int)fileDescriptor);
        }

        internal static IntPtr Map(int fileDescriptor, UIntPtr length, ulong offset)
        {
            if (fileDescriptor < 0)
                throw new ArgumentOutOfRangeException(nameof(fileDescriptor), "io_uring file descriptor 가 유효하지 않습니다.");
            if (length == UIntPtr.Zero)
                throw new ArgumentOutOfRangeException(nameof(length), "mmap length 는 0일 수 없습니다.");

            ThrowIfUnsupportedPlatform();

            IntPtr pointer = Mmap(
                IntPtr.Zero,
                length,
                ProtectionRead | ProtectionWrite,
                MapShared,
                fileDescriptor,
                new IntPtr(unchecked((long)offset)));
            if (pointer == MapFailed)
                throw CreateNativeException("mmap");

            return pointer;
        }

        internal static void Unmap(IntPtr pointer, UIntPtr length)
        {
            if (pointer == IntPtr.Zero)
                return;
            if (length == UIntPtr.Zero)
                return;

            int result = Munmap(pointer, length);
            if (result != 0)
                throw CreateNativeException("munmap");
        }

        internal static bool CloseFileDescriptor(int fileDescriptor)
        {
            if (fileDescriptor < 0)
                return true;

            return Close(fileDescriptor) == 0;
        }

        internal static void RegisterBuffers(int fileDescriptor, IoUringIovec[] buffers)
        {
            if (fileDescriptor < 0)
                throw new ArgumentOutOfRangeException(nameof(fileDescriptor), "io_uring file descriptor 가 유효하지 않습니다.");
            if (buffers == null)
                throw new ArgumentNullException(nameof(buffers));
            if (buffers.Length == 0)
                throw new ArgumentException("등록할 fixed buffer 가 1개 이상 필요합니다.", nameof(buffers));

            ThrowIfUnsupportedPlatform();

            GCHandle handle = GCHandle.Alloc(buffers, GCHandleType.Pinned);
            try
            {
                long result = SyscallIoUringRegister(
                    IoUringRegisterSyscallNumber,
                    fileDescriptor,
                    RegisterBuffersOpcode,
                    handle.AddrOfPinnedObject(),
                    (uint)buffers.Length);
                if (result < 0)
                    throw CreateNativeException("io_uring_register_buffers");
            }
            finally
            {
                handle.Free();
            }
        }

        internal static void UnregisterBuffers(int fileDescriptor)
        {
            if (fileDescriptor < 0)
                return;

            ThrowIfUnsupportedPlatform();

            long result = SyscallIoUringRegister(
                IoUringRegisterSyscallNumber,
                fileDescriptor,
                UnregisterBuffersOpcode,
                IntPtr.Zero,
                0);
            if (result < 0)
                throw CreateNativeException("io_uring_unregister_buffers");
        }

        internal static int Enter(int fileDescriptor, uint toSubmit, uint minimumComplete, uint flags)
        {
            if (fileDescriptor < 0)
                throw new ArgumentOutOfRangeException(nameof(fileDescriptor), "io_uring file descriptor 가 유효하지 않습니다.");

            ThrowIfUnsupportedPlatform();

            long result = SyscallIoUringEnter(
                IoUringEnterSyscallNumber,
                fileDescriptor,
                toSubmit,
                minimumComplete,
                flags,
                IntPtr.Zero,
                UIntPtr.Zero);
            if (result < 0)
                throw CreateNativeException("io_uring_enter");

            return checked((int)result);
        }

        internal static bool HasSingleMmapFeature(IoUringParams parameters)
        {
            return (parameters.Features & FeatureSingleMmap) != 0;
        }

        internal static bool IsUnavailableError(int errorCode)
        {
            return errorCode == 1 ||
                errorCode == 12 ||
                errorCode == 13 ||
                errorCode == 22 ||
                errorCode == 38;
        }

        private static IoUringNativeException CreateNativeException(string operation)
        {
            int errorCode = Marshal.GetLastWin32Error();
            return new IoUringNativeException(operation, errorCode);
        }

        [DllImport("libc", EntryPoint = "syscall", SetLastError = true)]
        private static extern long SyscallIoUringSetup(long number, uint entries, ref IoUringParams parameters);

        [DllImport("libc", EntryPoint = "syscall", SetLastError = true)]
        private static extern long SyscallIoUringRegister(long number, int fileDescriptor, uint opcode, IntPtr argument, uint count);

        [DllImport("libc", EntryPoint = "syscall", SetLastError = true)]
        private static extern long SyscallIoUringEnter(
            long number,
            int fileDescriptor,
            uint toSubmit,
            uint minimumComplete,
            uint flags,
            IntPtr signalSet,
            UIntPtr signalSetSize);

        [DllImport("libc", EntryPoint = "mmap", SetLastError = true)]
        private static extern IntPtr Mmap(IntPtr address, UIntPtr length, int protection, int flags, int fileDescriptor, IntPtr offset);

        [DllImport("libc", EntryPoint = "munmap", SetLastError = true)]
        private static extern int Munmap(IntPtr address, UIntPtr length);

        [DllImport("libc", EntryPoint = "close", SetLastError = true)]
        private static extern int Close(int fileDescriptor);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct IoUringSubmissionQueueEntry
    {
        internal byte Opcode;
        internal byte Flags;
        internal ushort IoPriority;
        internal int FileDescriptor;
        internal ulong Offset;
        internal ulong Address;
        internal uint Length;
        internal uint OperationFlags;
        internal ulong UserData;
        internal ushort BufferIndex;
        internal ushort Personality;
        internal int SpliceFileDescriptorIn;
        internal ulong Address3;
        internal ulong Reserved2;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct IoUringCompletionQueueEntry
    {
        internal ulong UserData;
        internal int Result;
        internal uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct IoUringIovec
    {
        internal IntPtr BaseAddress;
        internal UIntPtr Length;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct IoUringMessageHeader
    {
        internal IntPtr Name;
        internal uint NameLength;
        internal uint Reserved0;
        internal IntPtr Iov;
        internal UIntPtr IovLength;
        internal IntPtr Control;
        internal UIntPtr ControlLength;
        internal int Flags;
        internal int Reserved1;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct IoUringParams
    {
        internal uint SqEntries;
        internal uint CqEntries;
        internal uint Flags;
        internal uint SqThreadCpu;
        internal uint SqThreadIdle;
        internal uint Features;
        internal uint WqFd;
        internal uint Reserved0;
        internal uint Reserved1;
        internal uint Reserved2;
        internal IoUringSqringOffsets SqOffsets;
        internal IoUringCqringOffsets CqOffsets;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct IoUringSqringOffsets
    {
        internal uint Head;
        internal uint Tail;
        internal uint RingMask;
        internal uint RingEntries;
        internal uint Flags;
        internal uint Dropped;
        internal uint Array;
        internal uint Reserved0;
        internal ulong UserAddress;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct IoUringCqringOffsets
    {
        internal uint Head;
        internal uint Tail;
        internal uint RingMask;
        internal uint RingEntries;
        internal uint Overflow;
        internal uint Cqes;
        internal uint Flags;
        internal uint Reserved0;
        internal ulong UserAddress;
    }

    internal sealed class IoUringNativeException : Exception
    {
        internal IoUringNativeException(string operation, int errorCode)
            : base(operation + " failed with errno " + errorCode + ".")
        {
            Operation = operation;
            ErrorCode = errorCode;
        }

        internal string Operation { get; }

        internal int ErrorCode { get; }
    }
}
