using System;
using System.Threading.Tasks;

namespace Hps.Transport
{
    /// <summary>
    /// 하나의 io_uring operation token과 managed waiter를 묶어 보존한다.
    ///
    /// 현재 단계에서는 명확한 소유권 검증을 우선해 TaskCompletionSource를 사용한다. 실제 pump가 안정화된 뒤
    /// allocation-free reusable source가 필요해지면 같은 API 뒤에서 교체한다.
    /// </summary>
    internal sealed class IoUringOperationContext
    {
        private readonly object _gate = new object();
        private TaskCompletionSource<IoUringCompletion>? _completionSource;
        private bool _completed;

        internal ulong Token { get; private set; }

        internal IoUringOperationKind Kind { get; private set; }

        internal void Reset(ulong token, IoUringOperationKind kind)
        {
            if (token == 0)
                throw new ArgumentOutOfRangeException(nameof(token), "io_uring user_data token은 0을 사용하지 않는다.");

            lock (_gate)
            {
                if (_completionSource != null && !_completed)
                    throw new InvalidOperationException("대기 중인 io_uring operation context는 reset할 수 없습니다.");

                Token = token;
                Kind = kind;
                _completionSource = null;
                _completed = false;
            }
        }

        internal ValueTask<IoUringCompletion> WaitAsync()
        {
            lock (_gate)
            {
                if (Token == 0)
                    throw new InvalidOperationException("io_uring operation context가 아직 token으로 초기화되지 않았습니다.");
                if (_completionSource != null)
                    throw new InvalidOperationException("io_uring operation context에 이미 waiter가 등록되어 있습니다.");

                _completionSource = new TaskCompletionSource<IoUringCompletion>(TaskCreationOptions.RunContinuationsAsynchronously);
                return new ValueTask<IoUringCompletion>(_completionSource.Task);
            }
        }

        internal void Complete(IoUringCompletion completion)
        {
            TaskCompletionSource<IoUringCompletion> completionSource;
            lock (_gate)
            {
                if (_completionSource == null)
                    throw new InvalidOperationException("waiter가 없는 io_uring operation context는 완료할 수 없습니다.");
                if (_completed)
                    throw new InvalidOperationException("io_uring operation context가 이미 완료되었습니다.");
                if (completion.Token != Token)
                    throw new InvalidOperationException("completion token이 operation context token과 일치하지 않습니다.");

                _completed = true;
                completionSource = _completionSource;
            }

            completionSource.SetResult(completion);
        }
    }
}
