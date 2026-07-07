using System;
using System.Runtime.InteropServices;

namespace Hps.Transport
{
    /// <summary>
    /// io_uring fixed buffer registration 과 managed buffer pinning 수명을 함께 소유한다.
    ///
    /// kernel 에 등록된 iovec 는 등록 해제 전까지 같은 주소를 유지해야 하므로, owner 는 buffer 를 먼저 pin 하고
    /// register 에 성공한 뒤 Dispose 에서 unregister 후 pin 을 해제한다. 실제 send/recv pump 연결은 후속 task 범위다.
    /// </summary>
    internal sealed class IoUringRegisteredBufferSet : IIoUringFixedBufferRegistration
    {
        private readonly IoUringQueue _queue;
        private readonly int _fileDescriptor;
        private readonly int _registeredBufferCount;
        private GCHandle[]? _bufferHandles;
        private bool _disposed;

        private IoUringRegisteredBufferSet(IoUringQueue queue, int fileDescriptor, GCHandle[] bufferHandles)
        {
            _queue = queue;
            _fileDescriptor = fileDescriptor;
            _registeredBufferCount = bufferHandles.Length;
            _bufferHandles = bufferHandles;
        }

        internal int RegisteredBufferCount
        {
            get { return _registeredBufferCount; }
        }

        int IIoUringFixedBufferRegistration.RegisteredBufferCount
        {
            get { return _registeredBufferCount; }
        }

        internal static IoUringRegisteredBufferSet Register(IoUringQueue queue, byte[][] buffers)
        {
            IoUringNative.ThrowIfUnsupportedPlatform();

            if (queue == null)
                throw new ArgumentNullException(nameof(queue));
            if (buffers == null)
                throw new ArgumentNullException(nameof(buffers));
            if (buffers.Length == 0)
                throw new ArgumentException("등록할 fixed buffer 가 1개 이상 필요합니다.", nameof(buffers));

            GCHandle[] handles = new GCHandle[buffers.Length];
            IoUringIovec[] vectors = new IoUringIovec[buffers.Length];

            try
            {
                for (int i = 0; i < buffers.Length; i++)
                {
                    byte[] buffer = buffers[i];
                    if (buffer == null)
                        throw new ArgumentException("fixed buffer 항목은 null 일 수 없습니다.", nameof(buffers));
                    if (buffer.Length == 0)
                        throw new ArgumentException("fixed buffer 길이는 1 이상이어야 합니다.", nameof(buffers));

                    handles[i] = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                    vectors[i] = new IoUringIovec
                    {
                        BaseAddress = handles[i].AddrOfPinnedObject(),
                        Length = new UIntPtr((uint)buffer.Length)
                    };
                }

                int fileDescriptor = queue.FileDescriptor;
                IoUringNative.RegisterBuffers(fileDescriptor, vectors);
                return new IoUringRegisteredBufferSet(queue, fileDescriptor, handles);
            }
            catch
            {
                ReleaseHandles(handles);
                throw;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            GCHandle[]? handles = _bufferHandles;
            _bufferHandles = null;

            try
            {
                IoUringNative.UnregisterBuffers(_fileDescriptor);
                GC.KeepAlive(_queue);
            }
            finally
            {
                if (handles != null)
                    ReleaseHandles(handles);
            }
        }

        private static void ReleaseHandles(GCHandle[] handles)
        {
            for (int i = 0; i < handles.Length; i++)
            {
                if (handles[i].IsAllocated)
                    handles[i].Free();
            }
        }
    }
}
