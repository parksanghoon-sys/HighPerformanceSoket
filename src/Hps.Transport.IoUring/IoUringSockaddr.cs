using System;
using System.Net;
using System.Net.Sockets;

namespace Hps.Transport
{
    /// <summary>
    /// io_uring UDP v1에서 사용하는 Linux IPv4 sockaddr_in encode/decode helper.
    ///
    /// v1은 IPv4 direct path만 다루므로 family/port/address layout을 이곳에 모아
    /// receive/send pump 가 byte offset 계산을 중복하지 않게 한다.
    /// </summary>
    internal static class IoUringSockaddr
    {
        internal const int Ipv4SockaddrLength = 16;

        internal static void EncodeIPv4(IPEndPoint endPoint, byte[] block)
        {
            if (endPoint == null)
                throw new ArgumentNullException(nameof(endPoint));
            if (block == null)
                throw new ArgumentNullException(nameof(block));
            if (endPoint.AddressFamily != AddressFamily.InterNetwork)
                throw new NotSupportedException("io_uring UDP v1은 IPv4 endpoint만 지원합니다.");
            if (block.Length < Ipv4SockaddrLength)
                throw new ArgumentException("IPv4 sockaddr block은 최소 16바이트여야 합니다.", nameof(block));

            Array.Clear(block, 0, Ipv4SockaddrLength);
            block[0] = 2;
            block[1] = 0;
            block[2] = (byte)((endPoint.Port >> 8) & 0xFF);
            block[3] = (byte)(endPoint.Port & 0xFF);

            byte[] addressBytes = endPoint.Address.GetAddressBytes();
            Buffer.BlockCopy(addressBytes, 0, block, 4, addressBytes.Length);
        }

        internal static IPEndPoint DecodeIPv4(byte[] block, int length)
        {
            if (block == null)
                throw new ArgumentNullException(nameof(block));
            if (length < Ipv4SockaddrLength || block.Length < Ipv4SockaddrLength)
                throw new SocketException((int)SocketError.InvalidArgument);
            if (block[0] != 2 || block[1] != 0)
                throw new NotSupportedException("io_uring UDP v1은 IPv4 remote endpoint만 decode합니다.");

            int port = (block[2] << 8) | block[3];
            byte[] address = new byte[4];
            Buffer.BlockCopy(block, 4, address, 0, address.Length);
            return new IPEndPoint(new IPAddress(address), port);
        }
    }
}
