using System;
using System.Collections.Generic;

namespace Hps.Transport
{
    /// <summary>
    /// io_uring SQE `user_data` token과 managed operation context 사이의 유일한 mapping을 소유한다.
    ///
    /// CQE는 완료된 socket 작업의 managed 객체를 직접 알 수 없기 때문에 token resolve 경계가 틀리면
    /// 다른 connection의 receive/send waiter를 깨우는 치명적인 routing 오류가 된다. 단일 lock으로 발급,
    /// 조회, 제거를 직렬화해 stale token 재사용을 막는다.
    /// </summary>
    internal sealed class IoUringOperationRegistry
    {
        private readonly object _gate = new object();
        private readonly Dictionary<ulong, IoUringOperationContext> _contexts = new Dictionary<ulong, IoUringOperationContext>();
        private ulong _nextToken;

        internal IoUringOperationContext Register(IoUringOperationKind kind)
        {
            lock (_gate)
            {
                if (_nextToken == ulong.MaxValue)
                    throw new InvalidOperationException("io_uring operation token 공간을 모두 사용했습니다.");

                ulong token = _nextToken + 1;
                _nextToken = token;

                IoUringOperationContext context = new IoUringOperationContext();
                context.Reset(token, kind);
                _contexts.Add(token, context);
                return context;
            }
        }

        internal IoUringOperationContext Resolve(ulong token)
        {
            IoUringOperationContext? context;
            if (TryResolve(token, out context) && context != null)
                return context;

            throw new InvalidOperationException("등록되지 않은 io_uring operation token입니다.");
        }

        internal bool TryResolve(ulong token, out IoUringOperationContext? context)
        {
            lock (_gate)
            {
                return _contexts.TryGetValue(token, out context);
            }
        }

        internal bool Unregister(ulong token)
        {
            lock (_gate)
            {
                return _contexts.Remove(token);
            }
        }
    }
}
