using System;
using System.Threading;

namespace Hps.Buffers
{
    /// <summary>
    /// 팬아웃 메시지 payload 를 여러 송신 경로가 공유하기 위한 참조계수 버퍼이다.
    ///
    /// 생성 시 참조계수는 1이며, 이 최초 참조는 publish 작업이 보유하는 가드 ref 로 사용한다.
    /// 각 구독자 송신 큐에 넣기 전 <see cref="AddRef"/> 로 참조를 늘리고, 송신 완료나 enqueue 실패 시
    /// <see cref="Release"/> 로 줄인다. 마지막 Release 가 0에 도달하면 내부 블록은 원래
    /// <see cref="IRefCountedBufferOwner"/> 로 정확히 한 번 반환된다.
    ///
    /// 동시성: AddRef/Release 는 여러 송신 완료 경로에서 동시에 호출될 수 있으므로 Interlocked 기반이다.
    /// 0에 도달한 뒤 AddRef 하는 것은 반환된 블록을 되살리는 use-after-free 이므로 계약 위반으로 거부한다.
    /// </summary>
    public sealed class RefCountedBuffer
    {
        private readonly IRefCountedBufferOwner _owner;
        private byte[]? _block;
        private int _length;
        private int _refCount;
        private int _returned;

        /// <summary>
        /// owner 가 소유한 block 을 참조계수 버퍼로 감싼다.
        /// </summary>
        /// <param name="owner">마지막 Release 때 block 을 돌려받을 owner.</param>
        /// <param name="block">owner 의 BlockSize 와 같은 길이의 backing block.</param>
        public RefCountedBuffer(IRefCountedBufferOwner owner, byte[] block)
        {
            if (owner == null)
                throw new ArgumentNullException(nameof(owner));
            if (block == null)
                throw new ArgumentNullException(nameof(block));
            if (block.Length != owner.BlockSize)
                throw new ArgumentException("버퍼 블록 길이가 owner BlockSize 와 일치해야 한다.", nameof(block));

            _owner = owner;
            _block = block;
            _refCount = 1;
        }

        /// <summary>
        /// 현재 유효한 payload 길이. 전체 블록 용량과 다를 수 있으며, 송신 경로는 이 값을 기준으로
        /// 실제 전송 범위를 정해야 한다.
        /// </summary>
        public int Length => ReadPublishedLength();

        /// <summary>
        /// 내부 고정 블록 전체를 Memory 로 노출한다. UDP 직접 수신처럼 아직 실제 수신 길이를 모르는
        /// 경로도 전체 블록에 쓸 수 있어야 하므로 Length 로 잘라서 반환하지 않는다.
        /// </summary>
        public Memory<byte> Memory
        {
            get
            {
                byte[] block = GetLiveBlock();
                return new Memory<byte>(block);
            }
        }

        /// <summary>
        /// 내부 고정 블록 전체를 Span 으로 노출한다. TCP payload 복사 대상과 UDP recv 대상에 사용한다.
        /// 유효 payload 범위는 <see cref="Length"/> 로 별도 관리한다.
        /// </summary>
        public Span<byte> Span
        {
            get
            {
                byte[] block = GetLiveBlock();
                return new Span<byte>(block);
            }
        }

        /// <summary>
        /// 유효 payload 길이를 갱신한다. 길이는 0 이상, 내부 블록 크기 이하여야 한다.
        /// </summary>
        public void SetLength(int length)
        {
            byte[] block = GetLiveBlock();
            if (length < 0 || length > block.Length)
                throw new ArgumentOutOfRangeException(nameof(length), "Length 는 0 이상이고 블록 크기 이하여야 한다.");

            PublishLength(length);
        }

        /// <summary>
        /// 공유 참조를 하나 추가한다. 이미 반환된 버퍼를 다시 참조하는 것은 부활이므로 거부한다.
        /// </summary>
        public void AddRef()
        {
            while (true)
            {
                int current = ReadRefCountSnapshot();
                if (current <= 0 || IsReturned())
                    throw new InvalidOperationException("이미 반환된 RefCountedBuffer 는 AddRef 할 수 없다.");
                if (current == int.MaxValue)
                    throw new InvalidOperationException("RefCountedBuffer 참조계수가 너무 크다.");

                if (Interlocked.CompareExchange(ref _refCount, current + 1, current) == current)
                    return;
            }
        }

        /// <summary>
        /// 공유 참조를 하나 해제한다. 마지막 참조가 해제되면 내부 블록을 풀에 정확히 한 번 반환한다.
        /// </summary>
        public void Release()
        {
            while (true)
            {
                int current = ReadRefCountSnapshot();
                if (current <= 0)
                    throw new InvalidOperationException("RefCountedBuffer 가 이미 반환됐다.");

                int next = current - 1;
                if (Interlocked.CompareExchange(ref _refCount, next, current) != current)
                    continue;

                if (next == 0)
                    ReturnToPoolOnce();

                return;
            }
        }

        private byte[] GetLiveBlock()
        {
            byte[]? block = ReadBlockSnapshot();
            if (block == null || IsReturned())
                throw new ObjectDisposedException(nameof(RefCountedBuffer), "이미 풀로 반환된 버퍼에는 접근할 수 없다.");

            return block;
        }

        // Length 는 payload 유효 범위를 다른 경로가 읽는 관측값이다.
        // 직접 Volatile.Read 를 노출하지 않고 "publish 된 길이"라는 의미를 호출부에 남긴다.
        private int ReadPublishedLength()
        {
            return Volatile.Read(ref _length);
        }

        // 수신/조립 경로가 payload 길이를 확정해 이후 송신 경로가 관측할 수 있게 공개한다.
        // 버퍼 용량(Memory/Span 길이)과 실제 payload 길이를 분리하기 위한 publish 지점이다.
        private void PublishLength(int length)
        {
            Volatile.Write(ref _length, length);
        }

        // 참조계수 변경은 CompareExchange 가 최종 권위지만, 루프의 현재 판단에는 최신 스냅샷이 필요하다.
        // AddRef/Release 호출부가 저수준 volatile primitive 를 직접 해석하지 않도록 감싼다.
        private int ReadRefCountSnapshot()
        {
            return Volatile.Read(ref _refCount);
        }

        // _block 은 마지막 Release 에서 null 로 교체된다. 접근 경로는 이 스냅샷과 반환 상태를 함께 확인해
        // 풀로 돌아간 배열을 다시 사용하는 use-after-free 를 막는다.
        private byte[]? ReadBlockSnapshot()
        {
            return Volatile.Read(ref _block);
        }

        // _returned 는 0 도달 반환이 이미 수행됐는지 나타내는 수명 경계 플래그다.
        // AddRef 부활 방지와 반환 후 Memory/Span 접근 차단에서 같은 의미로 사용한다.
        private bool IsReturned()
        {
            return Volatile.Read(ref _returned) != 0;
        }

        private void ReturnToPoolOnce()
        {
            // 여러 송신 완료 경로가 동시에 마지막 Release 주변을 지나더라도 실제 풀 반환은 한 번만 허용한다.
            if (Interlocked.Exchange(ref _returned, 1) != 0)
                throw new InvalidOperationException("RefCountedBuffer 가 풀에 두 번 반환되려고 했다.");

            byte[]? block = Interlocked.Exchange(ref _block, null);
            if (block == null)
                throw new InvalidOperationException("반환할 버퍼 블록이 없다.");

            PublishLength(0);
            _owner.Return(block);
        }
    }
}
