using System;
using System.Threading;

namespace Hps.Buffers
{
    /// <summary>
    /// Bipartite 원형 버퍼. 일반 링버퍼와 달리 경계에서 데이터가 두 조각으로 갈라지지 않고
    /// 쓰기/읽기 모두 항상 <b>연속된 단일 <see cref="Span{T}"/></b> 영역을 돌려준다.
    /// 덕분에 소켓 send/recv를 복사 없이 1회 호출로 처리할 수 있다.
    ///
    /// 동시성: 단일 생산자(소켓 recv → <see cref="GetWriteSpan"/>/<see cref="Commit"/>) +
    /// 단일 소비자(파서 → <see cref="GetReadSpan"/>/<see cref="Consume"/>) (SPSC) 를 가정한다.
    /// 생산자는 <c>_write</c>/<c>_watermark</c> 만, 소비자는 <c>_read</c> 만 전진시키며,
    /// 가시성은 Volatile 읽기/쓰기와 Interlocked 카운트로 보장한다.
    ///
    /// 사용 가능 용량은 (capacity - 1) 이다. 마지막 1바이트는 full/empty 를 모호함 없이
    /// 구분하기 위한 갭으로 항상 비워 둔다.
    /// </summary>
    public sealed class BipBuffer
    {
        private readonly byte[] _buffer;
        private readonly int _capacity;

        // 소비자 소유 커서. [0, capacity)
        private int _read;
        // 생산자 소유 커서. [0, capacity)
        private int _write;
        // 생산자 소유. 랩(wrap)된 상태에서 상단 데이터 영역 [_read, _watermark) 의 끝.
        // 랩되지 않은 상태에서는 capacity 와 같다(무의미).
        private int _watermark;
        // 저장된 읽기 가능 바이트 수. Interlocked 로 갱신하여 양쪽에서 안전하게 본다.
        private int _count;

        public BipBuffer(int capacity)
        {
            if (capacity < 2)
                throw new ArgumentOutOfRangeException(nameof(capacity), "capacity 는 최소 2 이상이어야 한다.");
            _buffer = new byte[capacity];
            _capacity = capacity;
            _watermark = capacity;
        }

        /// <summary>전체 물리 용량(바이트). 실사용 가능 용량은 Capacity - 1.</summary>
        public int Capacity => _capacity;

        /// <summary>현재 읽기 가능한 바이트 수.</summary>
        public int Count => Volatile.Read(ref _count);

        /// <summary>비어 있는가.</summary>
        public bool IsEmpty => Volatile.Read(ref _count) == 0;

        /// <summary>
        /// 생산자: 쓰기 가능한 <b>연속</b> 영역을 돌려준다. 길이는 가용 상황에 따라
        /// <paramref name="minimumSize"/> 보다 작을 수 있다(0 가능). 데이터를 채운 뒤
        /// 실제로 쓴 바이트 수만큼 <see cref="Commit"/> 를 호출해야 반영된다.
        /// </summary>
        public Span<byte> GetWriteSpan(int minimumSize = 1)
        {
            int read = Volatile.Read(ref _read);
            int write = _write; // 생산자 소유

            if (write < read)
            {
                // 랩된 상태: 가용 연속 영역은 [write, read), 단 1바이트 갭 유지.
                int free = read - write - 1;
                if (free < 0) free = 0;
                return _buffer.AsSpan(write, free);
            }

            // 랩되지 않은 상태: 데이터 [read, write). 꼬리 [write, cap), 앞쪽 [0, read).
            if (read == 0)
            {
                // 앞쪽을 쓸 수 없다(갭이 read=0 직전, 즉 cap-1 위치). 꼬리에서 1바이트 갭 유지.
                int t = _capacity - write - 1;
                if (t < 0) t = 0;
                return _buffer.AsSpan(write, t);
            }

            int tail = _capacity - write;
            int frontFree = read - 1;
            if (frontFree < 0) frontFree = 0;

            if (Volatile.Read(ref _count) == 0)
            {
                // 버퍼가 비어 있지만 read/write 가 0이 아닌 위치에서 만난 상태다.
                // 여기서 앞쪽으로 랩하면 상단 데이터 구간의 길이가 0인 watermark 를 만들고,
                // 소비자는 아직 read 를 0으로 되돌릴 기회가 없어 front 데이터를 관측하지 못한다.
                // minimumSize 보다 작더라도 tail 을 먼저 채우면 기존 Consume 경로가 자연스럽게 read 를 0으로 되돌린다.
                return _buffer.AsSpan(write, tail);
            }

            if (tail >= minimumSize || tail >= frontFree)
            {
                // 꼬리에 충분하거나, 앞쪽으로 랩해도 이득이 없으면 꼬리를 그대로 쓴다.
                return _buffer.AsSpan(write, tail);
            }

            // 앞쪽이 더 크고 꼬리가 부족하다 → 앞쪽으로 랩한다.
            // 데이터 [read, write) 는 그대로 두고, 그 끝을 watermark 로 기록한 뒤 write 를 0 으로.
            _watermark = write;               // 생산자 소유 (Volatile.Write(_write) 가 release 역할)
            Volatile.Write(ref _write, 0);
            // [0, read-1), read 직전 1바이트 갭. 위 비교에서 frontFree 가 tail 보다 큰 경우만 여기로 온다.
            return _buffer.AsSpan(0, frontFree);
        }

        /// <summary>생산자: 직전에 받은 쓰기 영역에 실제로 채운 바이트 수를 확정한다.</summary>
        public void Commit(int bytes)
        {
            if (bytes < 0) throw new ArgumentOutOfRangeException(nameof(bytes));
            if (bytes == 0) return;

            int write = _write + bytes;
            if (write > _capacity)
                throw new InvalidOperationException("Commit 이 직전 GetWriteSpan 영역을 초과했다.");

            Interlocked.Add(ref _count, bytes);

            if (write == _capacity)
            {
                // 꼬리를 물리 끝까지 채운 뒤 capacity 값을 그대로 저장하면, 소비자가 전량 처리해
                // read=0 으로 돌아왔을 때 빈 버퍼인데도 꼬리 여유 계산이 음수가 되어 재진입할 수 없다.
                // 이 시점에는 read>0 인 꼬리 쓰기였으므로 상단 데이터 끝을 watermark 로 남기고 즉시 랩한다.
                _watermark = _capacity;
                write = 0;
            }

            Volatile.Write(ref _write, write);
        }

        /// <summary>
        /// 소비자: 읽을 수 있는 <b>연속</b> 영역을 돌려준다(없으면 빈 Span). 처리한 뒤
        /// 소비한 바이트 수만큼 <see cref="Consume"/> 를 호출해야 공간이 회수된다.
        /// SPSC 계약상 소비자는 이 메서드로 받은 데이터를 실제로 모두 처리한 뒤에만
        /// <see cref="Consume"/> 를 호출해야 하며, 처리 전에 커서를 먼저 전진시키면 생산자가
        /// 아직 읽는 중인 영역을 덮어쓸 수 있다.
        /// </summary>
        public ReadOnlySpan<byte> GetReadSpan()
        {
            int available = Volatile.Read(ref _count);
            if (available == 0)
                return ReadOnlySpan<byte>.Empty;

            int read = _read; // 소비자 소유
            int write = Volatile.Read(ref _write);
            int contiguous;

            if (write > read)
            {
                contiguous = write - read; // 랩되지 않음
            }
            else
            {
                // 랩됨(write < read): 상단 영역 [read, watermark) 를 먼저 읽는다.
                // write==read 이면서 available>0 인 순간은 생산자가 count 를 먼저 발행하고
                // write 를 곧 발행하는 짧은 전이일 수 있으므로, 아래 count clamp 가 권위값이다.
                int wm = Volatile.Read(ref _watermark);
                contiguous = wm - read;
            }

            // 커서 기하가 순간적으로 count 보다 앞서 보일 수 있으므로, 소비자에게 노출하는 길이는
            // 항상 커밋 완료 바이트 수(_count) 이하로 제한한다. _count 값을 보정하지는 않는다.
            int length = contiguous < available ? contiguous : available;
            if (length <= 0)
                return ReadOnlySpan<byte>.Empty;

            return _buffer.AsSpan(read, length);
        }

        /// <summary>소비자: 읽기 영역에서 실제로 처리한 바이트 수만큼 전진시킨다.</summary>
        public void Consume(int bytes)
        {
            if (bytes < 0) throw new ArgumentOutOfRangeException(nameof(bytes));
            if (bytes == 0) return;
            if (bytes > Volatile.Read(ref _count))
                throw new InvalidOperationException("Consume 이 읽기 가능한 바이트 수를 초과했다.");

            int read = _read;
            int write = Volatile.Read(ref _write);
            int newRead = read + bytes;

            if (write > read)
            {
                // 랩되지 않음: 꼬리 끝(capacity)에 도달하면 앞으로 되돌린다.
                if (newRead == _capacity) newRead = 0;
            }
            else
            {
                // 랩됨: 상단 영역 끝(watermark)에 도달하면 앞쪽 영역으로 되돌린다.
                int wm = Volatile.Read(ref _watermark);
                if (newRead == wm) newRead = 0;
            }

            Volatile.Write(ref _read, newRead);
            Interlocked.Add(ref _count, -bytes);
        }
    }
}
