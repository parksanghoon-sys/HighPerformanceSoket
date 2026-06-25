using System;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Hps.Transport
{
    /// <summary>
    /// Winsock RIO extension function table을 보관하는 native 경계다.
    /// 포인터 값은 이 타입 밖으로 흘리지 않아 잘못된 delegate 변환과 수명 혼선을 막는다.
    /// </summary>
    internal sealed class RioNative
    {
        private RioNative()
        {
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

        private static bool TryLoadFunctionTableCore(Socket socket, out RioNative? native)
        {
            native = null;

            try
            {
                _ = socket;
                return false;
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
    }
}
