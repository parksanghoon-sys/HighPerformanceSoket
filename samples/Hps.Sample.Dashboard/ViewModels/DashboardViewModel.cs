using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Input;
using Hps.Sample.Dashboard.Commands;
using Hps.Sample.Dashboard.Models;

namespace Hps.Sample.Dashboard.ViewModels
{
    public sealed class DashboardViewModel : INotifyPropertyChanged
    {
        private readonly int _maxLogEntries;
        private DashboardStatus _serverStatus;
        private string _lastSmokeSummary;

        public DashboardViewModel()
            : this(200)
        {
        }

        public DashboardViewModel(int maxLogEntries)
        {
            if (maxLogEntries <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxLogEntries));

            _maxLogEntries = maxLogEntries;
            _serverStatus = DashboardStatus.Stopped;
            _lastSmokeSummary = string.Empty;
            LogEntries = new ObservableCollection<string>();
            Metrics = new ObservableCollection<TransportMetricRow>();

            StartServerCommand = new RelayCommand(StartServer, CanStartServer);
            StopServerCommand = new RelayCommand(StopServer, CanStopServer);
            RunTcpSmokeCommand = new AsyncRelayCommand(RunTcpSmokeAsync);
            RunUdpSmokeCommand = new AsyncRelayCommand(RunUdpSmokeAsync);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public DashboardStatus ServerStatus
        {
            get { return _serverStatus; }
            private set
            {
                if (_serverStatus == value)
                    return;

                _serverStatus = value;
                OnPropertyChanged(nameof(ServerStatus));
                OnPropertyChanged(nameof(ServerStatusText));
                RaiseServerCommandStateChanged();
            }
        }

        public string ServerStatusText
        {
            get
            {
                if (ServerStatus == DashboardStatus.Running)
                    return "실행 중";
                if (ServerStatus == DashboardStatus.Failed)
                    return "실패";

                return "중지됨";
            }
        }

        public string LastSmokeSummary
        {
            get { return _lastSmokeSummary; }
            private set
            {
                if (string.Equals(_lastSmokeSummary, value, StringComparison.Ordinal))
                    return;

                _lastSmokeSummary = value;
                OnPropertyChanged(nameof(LastSmokeSummary));
            }
        }

        public ObservableCollection<string> LogEntries { get; }

        public ObservableCollection<TransportMetricRow> Metrics { get; }

        public ICommand StartServerCommand { get; }

        public ICommand StopServerCommand { get; }

        public ICommand RunTcpSmokeCommand { get; }

        public ICommand RunUdpSmokeCommand { get; }

        public void AddLog(string message)
        {
            LogEntries.Add(message);

            while (LogEntries.Count > _maxLogEntries)
                LogEntries.RemoveAt(0);
        }

        public void ApplySmokeResult(SmokeRunResult result)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));

            LastSmokeSummary = string.Format(
                "{0}: sent={1}, received={2}, dropped={3}, payload-errors={4}, pool-rented={5}",
                result.Protocol,
                result.Sent,
                result.Received,
                result.Dropped,
                result.PayloadErrors,
                result.PoolRented);
        }

        private bool CanStartServer()
        {
            return ServerStatus == DashboardStatus.Stopped;
        }

        private bool CanStopServer()
        {
            return ServerStatus == DashboardStatus.Running;
        }

        private void StartServer()
        {
            ServerStatus = DashboardStatus.Running;
            AddLog("server 시작");
        }

        private void StopServer()
        {
            ServerStatus = DashboardStatus.Stopped;
            AddLog("server 중지");
        }

        private Task RunTcpSmokeAsync()
        {
            AddLog("TCP smoke 대기");
            return Task.CompletedTask;
        }

        private Task RunUdpSmokeAsync()
        {
            AddLog("UDP smoke 대기");
            return Task.CompletedTask;
        }

        private void RaiseServerCommandStateChanged()
        {
            RelayCommand? start = StartServerCommand as RelayCommand;
            if (start != null)
                start.RaiseCanExecuteChanged();

            RelayCommand? stop = StopServerCommand as RelayCommand;
            if (stop != null)
                stop.RaiseCanExecuteChanged();
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChangedEventHandler? handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
