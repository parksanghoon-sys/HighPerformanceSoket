using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Hps.Sample.Dashboard.Commands
{
    public sealed class AsyncRelayCommand : ICommand
    {
        private readonly Func<Task> _executeAsync;
        private readonly Func<bool>? _canExecute;
        private bool _isRunning;

        public AsyncRelayCommand(Func<Task> executeAsync)
            : this(executeAsync, null)
        {
        }

        public AsyncRelayCommand(Func<Task> executeAsync, Func<bool>? canExecute)
        {
            if (executeAsync == null)
                throw new ArgumentNullException(nameof(executeAsync));

            _executeAsync = executeAsync;
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            return !_isRunning && (_canExecute == null || _canExecute());
        }

        public async void Execute(object? parameter)
        {
            if (!CanExecute(parameter))
                return;

            await ExecuteAsync().ConfigureAwait(false);
        }

        public async Task ExecuteAsync()
        {
            if (!CanExecute(null))
                return;

            _isRunning = true;
            RaiseCanExecuteChanged();

            try
            {
                await _executeAsync().ConfigureAwait(false);
            }
            finally
            {
                _isRunning = false;
                RaiseCanExecuteChanged();
            }
        }

        public void RaiseCanExecuteChanged()
        {
            EventHandler? handler = CanExecuteChanged;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }
    }
}
