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
        private readonly RioRegisterBufferDelegate _registerBuffer;
        private readonly RioDeregisterBufferDelegate _deregisterBuffer;

        private RioNative(RioExtensionFunctionTable functionTable)
        {
            _functionTable = functionTable;
            _createCompletionQueue = Marshal.GetDelegateForFunctionPointer<RioCreateCompletionQueueDelegate>(functionTable.CreateCompletionQueue);
            _closeCompletionQueue = Marshal.GetDelegateForFunctionPointer<RioCloseCompletionQueueDelegate>(functionTable.CloseCompletionQueue);
            _createRequestQueue = Marshal.GetDelegateForFunctionPointer<RioCreateRequestQueueDelegate>(functionTable.CreateRequestQueue);
            _dequeueCompletion = Marshal.GetDelegateForFunctionPointer<RioDequeueCompletionDelegate>(functionTable.DequeueCompletion);
            _receive = Marshal.GetDelegateForFunctionPointer<RioPostBufferDelegate>(functionTable.Receive);
            _send = Marshal.GetDelegateForFunctionPointer<RioPostBufferDelegate>(functionTable.Send);
            _registerBuffer = Marshal.GetDelegateForFunctionPointer<RioRegisterBufferDelegate>(functionTable.RegisterBuffer);
            _deregisterBuffer = Marshal.GetDelegateForFunctionPointer<RioDeregisterBufferDelegate>(functionTable.DeregisterBuffer);
        }

        internal IntPtr CreateCompletionQueue(int queueSize)
        {
            if (queueSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(queueSize), "RIO completion queue 크기는 1 이상이어야 합니다.");

            // NotificationCompletion 을 null 로 두면 별도 event/IOCP notification 없이 poll/dequeue 방식으로 사용한다.
            // 초기 pump 는 명시 notification 을 붙이기 전 단일 worker polling 모델로 검증한다.
            return _createCompletionQueue((uint)queueSize, IntPtr.Zero);
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

        internal IntPtr RegisterBuffer(IntPtr dataBuffer, int dataLength)
        {
            if (dataBuffer == IntPtr.Zero)
                throw new ArgumentException("RIO register buffer pointer 는 null 일 수 없습니다.", nameof(dataBuffer));
            if (dataLength <= 0)
                throw new ArgumentOutOfRangeException(nameof(dataLength), "RIO register buffer 길이는 1 이상이어야 합니다.");

            return _registerBuffer(dataBuffer, (uint)dataLength);
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
        private delegate IntPtr RioRegisterBufferDelegate(IntPtr dataBuffer, uint dataLength);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void RioDeregisterBufferDelegate(IntPtr bufferId);

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
}
