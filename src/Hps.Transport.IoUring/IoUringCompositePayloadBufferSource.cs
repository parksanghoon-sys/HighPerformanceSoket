using System;
using Hps.Buffers;

namespace Hps.Transport
{
    /// <summary>
    /// TCP payload 조립에 사용할 source 를 registered pool hit 우선, fallback source 후순위로 합성한다.
    ///
    /// registered pool 은 고정 slot 이 모두 사용 중이면 hidden allocation 을 하지 않고 false 를 반환한다.
    /// 이 타입은 그 miss 를 명시적으로 fallback source 대여로 바꿔 protocol/server 상위 계층이 backend 세부 정책을
    /// 알 필요 없이 IRefCountedBufferSource 하나만 사용하게 만든다.
    /// </summary>
    internal sealed class IoUringCompositePayloadBufferSource : IRefCountedBufferSource
    {
        private readonly IoUringRegisteredPayloadBlockPool _registeredPool;
        private readonly IRefCountedBufferSource _fallbackSource;

        internal IoUringCompositePayloadBufferSource(
            IoUringRegisteredPayloadBlockPool registeredPool,
            IRefCountedBufferSource fallbackSource)
        {
            if (registeredPool == null)
                throw new ArgumentNullException(nameof(registeredPool));
            if (fallbackSource == null)
                throw new ArgumentNullException(nameof(fallbackSource));
            if (registeredPool.BlockSize != fallbackSource.BlockSize)
                throw new ArgumentException("registered pool 과 fallback source 의 BlockSize 가 같아야 합니다.", nameof(fallbackSource));

            _registeredPool = registeredPool;
            _fallbackSource = fallbackSource;
        }

        public int BlockSize
        {
            get { return _fallbackSource.BlockSize; }
        }

        public RefCountedBuffer RentCounted()
        {
            RefCountedBuffer? buffer;
            if (_registeredPool.TryRentCounted(out buffer))
                return buffer!;

            return _fallbackSource.RentCounted();
        }
    }
}
