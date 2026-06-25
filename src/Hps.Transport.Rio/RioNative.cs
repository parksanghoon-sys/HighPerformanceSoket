using System;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Hps.Transport
{
    /// <summary>
    /// Winsock RIO extension function table을 보관하는 native 경계다.
    /// 포인터 값은 이 타입 밖으로 흘리지 않아 잘못된 delegate 변환과 수명 혼선을 막는다.
    /// </summary>
    internal sealed class RioNative
    {
        private const int SocketError = -1;
        private const int AddressFamilyInterNetwork = 2;
        private const int SocketTypeStream = 1;
        private const int ProtocolTypeTcp = 6;
        private const int GuidByteLength = 16;
        private const uint WsaFlagOverlapped = 0x1;
        private const uint WsaFlagRegisteredIo = 0x100;
        private const uint IocInOut = 0xC0000000;
        private const uint IocWs2 = 0x08000000;
        internal const int RioIocpCompletion = 2;
        // Windows SDK ws2def.h 기준 SIO_GET_MULTIPLE_EXTENSION_FUNCTION_POINTER 값이다.
        // RIO 함수들은 일반 DLL export 가 아니라 provider extension table 로 런타임 조회해야 한다.
        private const uint SioGetMultipleExtensionFunctionPointer = IocInOut | IocWs2 | 36;
        // Windows SDK MSWSock.h 의 WSAID_MULTIPLE_RIO GUID 와 맞춘다.
        // GUID 가 틀리면 WSAIoctl 은 성공하지 못하거나 다른 extension table 을 돌려줄 수 있다.
        private static readonly Guid WsaIdMultipleRio = new Guid("8509e081-96dd-4005-b165-9e2ee8c79e3f");
        private static readonly IntPtr InvalidSocket = new IntPtr(-1);
        private readonly RioExtensionFunctionTable _functionTable;
        private readonly RioCreateCompletionQueueDelegate _createCompletionQueue;
        private readonly RioCloseCompletionQueueDelegate _closeCompletionQueue;
        private readonly RioCreateRequestQueueDelegate _createRequestQueue;
        private readonly RioDequeueCompletionDelegate _dequeueCompletion;
        private readonly RioPostBufferDelegate _receive;
        private readonly RioPostBufferDelegate _send;
        private readonly RioPostBufferExDelegate? _receiveEx;
        private readonly RioPostBufferExDelegate? _sendEx;
        private readonly RioRegisterBufferDelegate _registerBuffer;
        private readonly RioDeregisterBufferDelegate _deregisterBuffer;
        private readonly RioNotifyDelegate _notify;
        private static long _bufferRegistrationCount;

        private RioNative(RioExtensionFunctionTable functionTable)
        {
            _functionTable = functionTable;
            _createCompletionQueue = Marshal.GetDelegateForFunctionPointer<RioCreateCompletionQueueDelegate>(functionTable.CreateCompletionQueue);
            _closeCompletionQueue = Marshal.GetDelegateForFunctionPointer<RioCloseCompletionQueueDelegate>(functionTable.CloseCompletionQueue);
            _createRequestQueue = Marshal.GetDelegateForFunctionPointer<RioCreateRequestQueueDelegate>(functionTable.CreateRequestQueue);
            _dequeueCompletion = Marshal.GetDelegateForFunctionPointer<RioDequeueCompletionDelegate>(functionTable.DequeueCompletion);
            _receive = Marshal.GetDelegateForFunctionPointer<RioPostBufferDelegate>(functionTable.Receive);
            _send = Marshal.GetDelegateForFunctionPointer<RioPostBufferDelegate>(functionTable.Send);
            if (functionTable.ReceiveEx != IntPtr.Zero)
                _receiveEx = Marshal.GetDelegateForFunctionPointer<RioPostBufferExDelegate>(functionTable.ReceiveEx);
            if (functionTable.SendEx != IntPtr.Zero)
                _sendEx = Marshal.GetDelegateForFunctionPointer<RioPostBufferExDelegate>(functionTable.SendEx);
            _registerBuffer = Marshal.GetDelegateForFunctionPointer<RioRegisterBufferDelegate>(functionTable.RegisterBuffer);
            _deregisterBuffer = Marshal.GetDelegateForFunctionPointer<RioDeregisterBufferDelegate>(functionTable.DeregisterBuffer);
            _notify = Marshal.GetDelegateForFunctionPointer<RioNotifyDelegate>(functionTable.Notify);
        }

        internal bool SupportsCompletionNotification
        {
            get { return _functionTable.Notify != IntPtr.Zero; }
        }

        internal bool SupportsDatagramOperations
        {
            get { return _receiveEx != null && _sendEx != null; }
        }

        internal static long BufferRegistrationCount
        {
            get { return System.Threading.Volatile.Read(ref _bufferRegistrationCount); }
        }

        internal static void ResetBufferRegistrationDiagnostics()
        {
            System.Threading.Volatile.Write(ref _bufferRegistrationCount, 0);
        }

        internal IntPtr CreateCompletionQueue(int queueSize)
        {
            return CreateCompletionQueue(queueSize, IntPtr.Zero);
        }

        internal IntPtr CreateCompletionQueue(int queueSize, IntPtr notificationCompletion)
        {
            if (queueSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(queueSize), "RIO completion queue 크기는 1 이상이어야 합니다.");

            // NotificationCompletion 을 null 로 두면 별도 event/IOCP notification 없이 poll/dequeue 방식으로 사용한다.
            // IOCP 전환 뒤에는 caller 가 수명 보장된 RIO_NOTIFICATION_COMPLETION pointer 를 넘긴다.
            return _createCompletionQueue((uint)queueSize, notificationCompletion);
        }

        internal void CloseCompletionQueue(IntPtr completionQueue)
        {
            if (completionQueue == IntPtr.Zero)
                throw new ArgumentException("RIO completion queue handle 은 null 일 수 없습니다.", nameof(completionQueue));

            _closeCompletionQueue(completionQueue);
        }

        internal IntPtr CreateRequestQueue(
            Socket socket,
            int maxOutstandingReceive,
            int maxReceiveDataBuffers,
            int maxOutstandingSend,
            int maxSendDataBuffers,
            IntPtr receiveCompletionQueue,
            IntPtr sendCompletionQueue)
        {
            if (socket == null)
                throw new ArgumentNullException(nameof(socket));
            if (maxOutstandingReceive <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxOutstandingReceive), "RIO receive outstanding 값은 1 이상이어야 합니다.");
            if (maxReceiveDataBuffers <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxReceiveDataBuffers), "RIO receive data buffer 수는 1 이상이어야 합니다.");
            if (maxOutstandingSend <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxOutstandingSend), "RIO send outstanding 값은 1 이상이어야 합니다.");
            if (maxSendDataBuffers <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxSendDataBuffers), "RIO send data buffer 수는 1 이상이어야 합니다.");
            if (receiveCompletionQueue == IntPtr.Zero)
                throw new ArgumentException("RIO receive completion queue handle 은 null 일 수 없습니다.", nameof(receiveCompletionQueue));
            if (sendCompletionQueue == IntPtr.Zero)
                throw new ArgumentException("RIO send completion queue handle 은 null 일 수 없습니다.", nameof(sendCompletionQueue));

            // RIO_RQ 는 socket 과 CQ pair 에 묶인다. 별도 close 함수가 없으므로 socket 수명이 RQ 수명 경계가 된다.
            return _createRequestQueue(
                socket.SafeHandle.DangerousGetHandle(),
                (uint)maxOutstandingReceive,
                (uint)maxReceiveDataBuffers,
                (uint)maxOutstandingSend,
                (uint)maxSendDataBuffers,
                receiveCompletionQueue,
                sendCompletionQueue,
                IntPtr.Zero);
        }

        internal uint DequeueCompletion(IntPtr completionQueue, RioResult[] results)
        {
            if (completionQueue == IntPtr.Zero)
                throw new ArgumentException("RIO completion queue handle 은 null 일 수 없습니다.", nameof(completionQueue));
            if (results == null)
                throw new ArgumentNullException(nameof(results));
            if (results.Length == 0)
                throw new ArgumentException("RIO completion 결과 배열은 비어 있을 수 없습니다.", nameof(results));

            GCHandle handle = GCHandle.Alloc(results, GCHandleType.Pinned);
            try
            {
                return _dequeueCompletion(completionQueue, handle.AddrOfPinnedObject(), (uint)results.Length);
            }
            finally
            {
                handle.Free();
            }
        }

        internal int Notify(IntPtr completionQueue)
        {
            if (completionQueue == IntPtr.Zero)
                throw new ArgumentException("RIO completion queue handle 은 null 일 수 없습니다.", nameof(completionQueue));

            return _notify(completionQueue);
        }

        internal static IntPtr CreateIoCompletionPortHandle(uint concurrentThreadCount)
        {
            IntPtr handle = CreateIoCompletionPort(new IntPtr(-1), IntPtr.Zero, UIntPtr.Zero, concurrentThreadCount);
            if (handle == IntPtr.Zero)
                throw new SocketException(Marshal.GetLastWin32Error());

            return handle;
        }

        internal static uint GetQueuedCompletionStatusEx(
            IntPtr completionPort,
            NativeOverlappedEntry[] entries,
            uint milliseconds)
        {
            if (completionPort == IntPtr.Zero)
                throw new ArgumentException("IOCP handle 은 null 일 수 없습니다.", nameof(completionPort));
            if (entries == null)
                throw new ArgumentNullException(nameof(entries));
            if (entries.Length == 0)
                throw new ArgumentException("IOCP completion entry 배열은 비어 있을 수 없습니다.", nameof(entries));

            uint removed;
            bool ok = GetQueuedCompletionStatusEx(
                completionPort,
                entries,
                (uint)entries.Length,
                out removed,
                milliseconds,
                false);

            if (!ok)
            {
                int error = Marshal.GetLastWin32Error();
                if (error == 258)
                    return 0;

                throw new SocketException(error);
            }

            return removed;
        }

        internal static void PostQueuedCompletionStatus(IntPtr completionPort, UIntPtr completionKey, IntPtr overlapped)
        {
            if (completionPort == IntPtr.Zero)
                throw new ArgumentException("IOCP handle 은 null 일 수 없습니다.", nameof(completionPort));

            if (!PostQueuedCompletionStatus(completionPort, 0, completionKey, overlapped))
                throw new SocketException(Marshal.GetLastWin32Error());
        }

        internal static void CloseNativeHandle(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
                return;

            if (!CloseHandle(handle))
                throw new SocketException(Marshal.GetLastWin32Error());
        }

        internal bool Receive(IntPtr requestQueue, RioBufferSegment[] buffers, IntPtr requestContext)
        {
            ValidatePostingArguments(requestQueue, buffers);
            return PostBuffers(_receive, requestQueue, buffers, requestContext);
        }

        internal bool Send(IntPtr requestQueue, RioBufferSegment[] buffers, IntPtr requestContext)
        {
            ValidatePostingArguments(requestQueue, buffers);
            return PostBuffers(_send, requestQueue, buffers, requestContext);
        }

        internal bool ReceiveEx(
            IntPtr requestQueue,
            RioBufferSegment? data,
            RioBufferSegment? localAddress,
            RioBufferSegment? remoteAddress,
            IntPtr requestContext)
        {
            ValidateExPostingArguments(requestQueue);
            if (_receiveEx == null)
                throw new NotSupportedException("현재 RIO provider 는 RIOReceiveEx 를 제공하지 않습니다.");

            return PostBuffersEx(_receiveEx, requestQueue, data, localAddress, remoteAddress, requestContext);
        }

        internal bool SendEx(
            IntPtr requestQueue,
            RioBufferSegment? data,
            RioBufferSegment? remoteAddress,
            IntPtr requestContext)
        {
            ValidateExPostingArguments(requestQueue);
            if (_sendEx == null)
                throw new NotSupportedException("현재 RIO provider 는 RIOSendEx 를 제공하지 않습니다.");

            return PostBuffersEx(_sendEx, requestQueue, data, null, remoteAddress, requestContext);
        }

        internal IntPtr RegisterBuffer(IntPtr dataBuffer, int dataLength)
        {
            if (dataBuffer == IntPtr.Zero)
                throw new ArgumentException("RIO register buffer pointer 는 null 일 수 없습니다.", nameof(dataBuffer));
            if (dataLength <= 0)
                throw new ArgumentOutOfRangeException(nameof(dataLength), "RIO register buffer 길이는 1 이상이어야 합니다.");

            IntPtr bufferId = _registerBuffer(dataBuffer, (uint)dataLength);
            if (bufferId != IntPtr.Zero)
                System.Threading.Interlocked.Increment(ref _bufferRegistrationCount);

            return bufferId;
        }

        internal void DeregisterBuffer(IntPtr bufferId)
        {
            if (bufferId == IntPtr.Zero)
                throw new ArgumentException("RIO buffer id 는 null 일 수 없습니다.", nameof(bufferId));

            _deregisterBuffer(bufferId);
        }

        private static void ValidatePostingArguments(IntPtr requestQueue, RioBufferSegment[] buffers)
        {
            if (requestQueue == IntPtr.Zero)
                throw new ArgumentException("RIO request queue handle 은 null 일 수 없습니다.", nameof(requestQueue));
            if (buffers == null)
                throw new ArgumentNullException(nameof(buffers));
            if (buffers.Length == 0)
                throw new ArgumentException("RIO buffer segment 배열은 비어 있을 수 없습니다.", nameof(buffers));
        }

        private static bool PostBuffers(RioPostBufferDelegate post, IntPtr requestQueue, RioBufferSegment[] buffers, IntPtr requestContext)
        {
            GCHandle handle = GCHandle.Alloc(buffers, GCHandleType.Pinned);
            try
            {
                return post(requestQueue, handle.AddrOfPinnedObject(), (uint)buffers.Length, 0, requestContext) != 0;
            }
            finally
            {
                handle.Free();
            }
        }

        private static void ValidateExPostingArguments(IntPtr requestQueue)
        {
            if (requestQueue == IntPtr.Zero)
                throw new ArgumentException("RIO request queue handle 은 null 일 수 없습니다.", nameof(requestQueue));
        }

        private static bool PostBuffersEx(
            RioPostBufferExDelegate post,
            IntPtr requestQueue,
            RioBufferSegment? data,
            RioBufferSegment? localAddress,
            RioBufferSegment? remoteAddress,
            IntPtr requestContext)
        {
            GCHandle dataHandle;
            GCHandle localAddressHandle;
            GCHandle remoteAddressHandle;
            uint dataBufferCount;

            IntPtr dataPointer = PinOptionalSegment(data, out dataHandle, out dataBufferCount);
            IntPtr localAddressPointer = PinOptionalSegment(localAddress, out localAddressHandle);
            IntPtr remoteAddressPointer = PinOptionalSegment(remoteAddress, out remoteAddressHandle);

            try
            {
                return post(
                    requestQueue,
                    dataPointer,
                    dataBufferCount,
                    localAddressPointer,
                    remoteAddressPointer,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    0,
                    requestContext) != 0;
            }
            finally
            {
                FreeIfAllocated(dataHandle);
                FreeIfAllocated(localAddressHandle);
                FreeIfAllocated(remoteAddressHandle);
            }
        }

        private static IntPtr PinOptionalSegment(RioBufferSegment? segment, out GCHandle handle)
        {
            uint ignored;
            return PinOptionalSegment(segment, out handle, out ignored);
        }

        private static IntPtr PinOptionalSegment(RioBufferSegment? segment, out GCHandle handle, out uint dataBufferCount)
        {
            handle = default(GCHandle);
            dataBufferCount = 0;

            if (!segment.HasValue)
                return IntPtr.Zero;

            RioBufferSegment[] buffer = new RioBufferSegment[] { segment.Value };
            handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            dataBufferCount = 1;
            return handle.AddrOfPinnedObject();
        }

        private static void FreeIfAllocated(GCHandle handle)
        {
            if (handle.IsAllocated)
                handle.Free();
        }

        internal static bool TryLoadFunctionTable(out RioNative? native)
        {
            native = null;

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return false;

            using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            {
                // 실제 WSAIoctl + WSAID_MULTIPLE_RIO load는 이 method 안에서만 수행한다.
                // 실패는 fallback 가능한 capability miss로 취급하고 예외를 밖으로 내보내지 않는다.
                return TryLoadFunctionTableCore(socket, out native);
            }
        }

        internal static Socket CreateTcpSocket()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                throw new PlatformNotSupportedException("RIO socket 은 Windows 에서만 생성할 수 있습니다.");

            // RIO request queue 는 WSA_FLAG_REGISTERED_IO 로 생성된 socket 에만 붙일 수 있다.
            // .NET Socket 생성자는 이 flag 를 노출하지 않으므로 WSASocketW 로 handle 을 만들고 Socket 이 소유하게 한다.
            IntPtr handle = WSASocketW(
                AddressFamilyInterNetwork,
                SocketTypeStream,
                ProtocolTypeTcp,
                IntPtr.Zero,
                0,
                WsaFlagOverlapped | WsaFlagRegisteredIo);

            if (handle == InvalidSocket)
                throw new SocketException(Marshal.GetLastWin32Error());

            return new Socket(new SafeSocketHandle(handle, ownsHandle: true));
        }

        private static bool TryLoadFunctionTableCore(Socket socket, out RioNative? native)
        {
            native = null;

            try
            {
                Guid extensionId = WsaIdMultipleRio;
                RioExtensionFunctionTable functionTable;
                uint bytesReturned;

                // Socket.SafeHandle 의 raw SOCKET 값만 native call 에 전달하고 소유권은 Socket 이 유지한다.
                // RIO table 은 socket provider 에 묶인 extension function table 이므로 probe socket 하나로 조회한다.
                int result = WSAIoctl(
                    socket.SafeHandle.DangerousGetHandle(),
                    SioGetMultipleExtensionFunctionPointer,
                    ref extensionId,
                    GuidByteLength,
                    out functionTable,
                    Marshal.SizeOf<RioExtensionFunctionTable>(),
                    out bytesReturned,
                    IntPtr.Zero,
                    IntPtr.Zero);

                if (result == SocketError || !functionTable.HasRequiredPointers())
                    return false;

                // function pointer table 은 이후 CQ/RQ/buffer registration owner 가 재사용할 native entry point 다.
                // 지금은 capability proof 로만 보관하고 실제 delegate marshalling 은 pump task 에서 필요한 함수부터 좁혀 붙인다.
                native = new RioNative(functionTable);
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }

        [DllImport("Ws2_32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern int WSAIoctl(
            IntPtr socket,
            uint ioControlCode,
            ref Guid inputBuffer,
            int inputBufferLength,
            out RioExtensionFunctionTable outputBuffer,
            int outputBufferLength,
            out uint bytesReturned,
            IntPtr overlapped,
            IntPtr completionRoutine);

        [DllImport("Ws2_32.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr WSASocketW(
            int addressFamily,
            int socketType,
            int protocolType,
            IntPtr protocolInfo,
            uint group,
            uint flags);

        [DllImport("Kernel32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr CreateIoCompletionPort(
            IntPtr fileHandle,
            IntPtr existingCompletionPort,
            UIntPtr completionKey,
            uint numberOfConcurrentThreads);

        [DllImport("Kernel32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern bool GetQueuedCompletionStatusEx(
            IntPtr completionPort,
            [Out] NativeOverlappedEntry[] completionPortEntries,
            uint count,
            out uint entriesRemoved,
            uint milliseconds,
            bool alertable);

        [DllImport("Kernel32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern bool PostQueuedCompletionStatus(
            IntPtr completionPort,
            uint numberOfBytesTransferred,
            UIntPtr completionKey,
            IntPtr overlapped);

        [DllImport("Kernel32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate IntPtr RioCreateCompletionQueueDelegate(uint queueSize, IntPtr notificationCompletion);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void RioCloseCompletionQueueDelegate(IntPtr completionQueue);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate IntPtr RioCreateRequestQueueDelegate(
            IntPtr socket,
            uint maxOutstandingReceive,
            uint maxReceiveDataBuffers,
            uint maxOutstandingSend,
            uint maxSendDataBuffers,
            IntPtr receiveCompletionQueue,
            IntPtr sendCompletionQueue,
            IntPtr socketContext);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint RioDequeueCompletionDelegate(IntPtr completionQueue, IntPtr results, uint resultCount);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int RioPostBufferDelegate(
            IntPtr requestQueue,
            IntPtr buffers,
            uint bufferCount,
            uint flags,
            IntPtr requestContext);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int RioPostBufferExDelegate(
            IntPtr requestQueue,
            IntPtr data,
            uint dataBufferCount,
            IntPtr localAddress,
            IntPtr remoteAddress,
            IntPtr controlContext,
            IntPtr flagsBuffer,
            uint flags,
            IntPtr requestContext);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate IntPtr RioRegisterBufferDelegate(IntPtr dataBuffer, uint dataLength);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void RioDeregisterBufferDelegate(IntPtr bufferId);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int RioNotifyDelegate(IntPtr completionQueue);

        [StructLayout(LayoutKind.Sequential)]
        private struct RioExtensionFunctionTable
        {
            internal uint Size;
            internal IntPtr Receive;
            internal IntPtr ReceiveEx;
            internal IntPtr Send;
            internal IntPtr SendEx;
            internal IntPtr CloseCompletionQueue;
            internal IntPtr CreateCompletionQueue;
            internal IntPtr CreateRequestQueue;
            internal IntPtr DequeueCompletion;
            internal IntPtr DeregisterBuffer;
            internal IntPtr Notify;
            internal IntPtr RegisterBuffer;
            internal IntPtr ResizeCompletionQueue;
            internal IntPtr ResizeRequestQueue;

            internal bool HasRequiredPointers()
            {
                // Task 6 전에 최소 TCP receive/send pump 에 필요한 함수들이 모두 있는지 확인한다.
                // Ex/resize 함수는 v1 pump 필수 경로가 아니므로 availability 판정에서 제외한다.
                return Size != 0 &&
                    Receive != IntPtr.Zero &&
                    Send != IntPtr.Zero &&
                    CloseCompletionQueue != IntPtr.Zero &&
                    CreateCompletionQueue != IntPtr.Zero &&
                    CreateRequestQueue != IntPtr.Zero &&
                    DequeueCompletion != IntPtr.Zero &&
                    DeregisterBuffer != IntPtr.Zero &&
                    Notify != IntPtr.Zero &&
                    RegisterBuffer != IntPtr.Zero;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RioResult
    {
        internal int Status;
        internal uint BytesTransferred;
        internal ulong SocketContext;
        internal ulong RequestContext;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RioBufferSegment
    {
        internal IntPtr BufferId;
        internal uint Offset;
        internal uint Length;

        internal RioBufferSegment(IntPtr bufferId, int offset, int length)
        {
            if (bufferId == IntPtr.Zero)
                throw new ArgumentException("RIO buffer id 는 null 일 수 없습니다.", nameof(bufferId));
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset), "RIO buffer offset 은 0 이상이어야 합니다.");
            if (length <= 0)
                throw new ArgumentOutOfRangeException(nameof(length), "RIO buffer length 는 1 이상이어야 합니다.");

            BufferId = bufferId;
            Offset = (uint)offset;
            Length = (uint)length;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RioNotificationCompletion
    {
        internal int Type;
        internal RioNotificationIocp Iocp;

        internal static RioNotificationCompletion ForIocp(IntPtr iocpHandle, UIntPtr completionKey, IntPtr overlapped)
        {
            if (iocpHandle == IntPtr.Zero)
                throw new ArgumentException("IOCP handle 은 null 일 수 없습니다.", nameof(iocpHandle));
            if (overlapped == IntPtr.Zero)
                throw new ArgumentException("RIO notification OVERLAPPED pointer 는 null 일 수 없습니다.", nameof(overlapped));

            RioNotificationCompletion completion = new RioNotificationCompletion();
            completion.Type = RioNative.RioIocpCompletion;
            completion.Iocp = new RioNotificationIocp(iocpHandle, completionKey, overlapped);
            return completion;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RioNotificationIocp
    {
        internal IntPtr IocpHandle;
        internal UIntPtr CompletionKey;
        internal IntPtr Overlapped;

        internal RioNotificationIocp(IntPtr iocpHandle, UIntPtr completionKey, IntPtr overlapped)
        {
            IocpHandle = iocpHandle;
            CompletionKey = completionKey;
            Overlapped = overlapped;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeOverlapped64
    {
        internal UIntPtr Internal;
        internal UIntPtr InternalHigh;
        internal uint Offset;
        internal uint OffsetHigh;
        internal IntPtr EventHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeOverlappedEntry
    {
        internal UIntPtr CompletionKey;
        internal IntPtr Overlapped;
        internal UIntPtr Internal;
        internal uint NumberOfBytesTransferred;
    }
}
