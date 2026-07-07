using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Input;
using Hps.Sample.Dashboard.Commands;
using Hps.Sample.Dashboard.Models;
using Hps.Sample.Dashboard.Services;

namespace Hps.Sample.Dashboard.ViewModels
{
    public sealed class DashboardViewModel : INotifyPropertyChanged
    {
        private readonly int _maxLogEntries;
        private readonly DashboardBrokerService? _brokerService;
        private readonly DiagnosticsSnapshotService? _diagnosticsService;
        private readonly Func<Task<SmokeRunResult>> _tcpSmoke;
        private readonly Func<Task<SmokeRunResult>> _udpSmoke;
        private readonly RelayCommand? _startServerCommandState;
        private readonly RelayCommand? _stopServerCommandState;
        private DashboardStatus _serverStatus;
        private string _lastSmokeSummary;
        private string _tcpSmokeSummary;
        private string _udpSmokeSummary;
        private string _ioUringStatusText;

        public DashboardViewModel()
            : this(
                200,
                new DashboardBrokerService(),
                new DiagnosticsSnapshotService(),
                new IoUringEvidenceStatusService(),
                delegate { return new TcpSmokeTestService().RunAsync(); },
                delegate { return new UdpSmokeTestService().RunAsync(); })
        {
        }

        public DashboardViewModel(int maxLogEntries)
            : this(
                maxLogEntries,
                null,
                null,
                new IoUringEvidenceStatusService(),
                delegate { return Task.FromResult(new SmokeRunResult("TCP", true, 0, 0, 0, 0, 0, "not-run")); },
                delegate { return Task.FromResult(new SmokeRunResult("UDP", true, 0, 0, 0, 0, 0, "not-run")); })
        {
        }

        private DashboardViewModel(
            int maxLogEntries,
            DashboardBrokerService? brokerService,
            DiagnosticsSnapshotService? diagnosticsService,
            IoUringEvidenceStatusService ioUringEvidenceStatusService,
            Func<Task<SmokeRunResult>> tcpSmoke,
            Func<Task<SmokeRunResult>> udpSmoke)
        {
            if (maxLogEntries <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxLogEntries));
            if (ioUringEvidenceStatusService == null)
                throw new ArgumentNullException(nameof(ioUringEvidenceStatusService));
            if (tcpSmoke == null)
                throw new ArgumentNullException(nameof(tcpSmoke));
            if (udpSmoke == null)
                throw new ArgumentNullException(nameof(udpSmoke));

            _maxLogEntries = maxLogEntries;
            _brokerService = brokerService;
            _diagnosticsService = diagnosticsService;
            _tcpSmoke = tcpSmoke;
            _udpSmoke = udpSmoke;
            _serverStatus = DashboardStatus.Stopped;
            _lastSmokeSummary = string.Empty;
            _tcpSmokeSummary = string.Empty;
            _udpSmokeSummary = string.Empty;
            _ioUringStatusText = ioUringEvidenceStatusService.GetStatusText();
            LogEntries = new ObservableCollection<string>();
            Metrics = new ObservableCollection<TransportMetricRow>();

            _startServerCommandState = new RelayCommand(StartServer, CanStartServer);
            _stopServerCommandState = new RelayCommand(StopServer, CanStopServer);
            StartServerCommand = _startServerCommandState;
            StopServerCommand = _stopServerCommandState;
            RunTcpSmokeCommand = new AsyncRelayCommand(RunTcpSmokeAsync);
            RunUdpSmokeCommand = new AsyncRelayCommand(RunUdpSmokeAsync);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public static DashboardViewModel CreateForTests(Func<Task<object>> tcpSmoke, Func<Task<object>> udpSmoke)
        {
            if (tcpSmoke == null)
                throw new ArgumentNullException(nameof(tcpSmoke));
            if (udpSmoke == null)
                throw new ArgumentNullException(nameof(udpSmoke));

            return new DashboardViewModel(
                200,
                null,
                null,
                new IoUringEvidenceStatusService(),
                async delegate { return (SmokeRunResult)await tcpSmoke(); },
                async delegate { return (SmokeRunResult)await udpSmoke(); });
        }

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

        public string TcpSmokeSummary
        {
            get { return _tcpSmokeSummary; }
            private set
            {
                if (string.Equals(_tcpSmokeSummary, value, StringComparison.Ordinal))
                    return;

                _tcpSmokeSummary = value;
                OnPropertyChanged(nameof(TcpSmokeSummary));
            }
        }

        public string UdpSmokeSummary
        {
            get { return _udpSmokeSummary; }
            private set
            {
                if (string.Equals(_udpSmokeSummary, value, StringComparison.Ordinal))
                    return;

                _udpSmokeSummary = value;
                OnPropertyChanged(nameof(UdpSmokeSummary));
            }
        }

        public string IoUringStatusText
        {
            get { return _ioUringStatusText; }
            private set
            {
                if (string.Equals(_ioUringStatusText, value, StringComparison.Ordinal))
                    return;

                _ioUringStatusText = value;
                OnPropertyChanged(nameof(IoUringStatusText));
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

            string summary = string.Format(
                "{0}: sent={1}, received={2}, dropped={3}, payload-errors={4}, pool-rented={5}",
                result.Protocol,
                result.Sent,
                result.Received,
                result.Dropped,
                result.PayloadErrors,
                result.PoolRented);

            LastSmokeSummary = summary;

            if (string.Equals(result.Protocol, "TCP", StringComparison.OrdinalIgnoreCase))
            {
                TcpSmokeSummary = summary;
                return;
            }

            if (string.Equals(result.Protocol, "UDP", StringComparison.OrdinalIgnoreCase))
            {
                UdpSmokeSummary = summary;
            }
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
            if (_brokerService == null)
                return;

            try
            {
                _brokerService.StartAsync().AsTask().GetAwaiter().GetResult();
                ServerStatus = DashboardStatus.Running;
                AddLog("server 시작: TCP=" + FormatEndPoint(_brokerService.TcpLocalEndPoint) + ", UDP=" + FormatEndPoint(_brokerService.UdpLocalEndPoint));
                RefreshMetrics();
            }
            catch (Exception ex)
            {
                ServerStatus = DashboardStatus.Failed;
                AddLog("server 시작 실패: " + ex.Message);
            }
        }

        private void StopServer()
        {
            if (_brokerService == null)
                return;

            try
            {
                _brokerService.StopAsync().AsTask().GetAwaiter().GetResult();
                ServerStatus = DashboardStatus.Stopped;
                AddLog("server 중지");
                RefreshMetrics();
            }
            catch (Exception ex)
            {
                ServerStatus = DashboardStatus.Failed;
                AddLog("server 중지 실패: " + ex.Message);
            }
        }

        private async Task RunTcpSmokeAsync()
        {
            AddLog("TCP smoke 실행");
            SmokeRunResult result = await _tcpSmoke();
            ApplySmokeResult(result);
            AddLog(result.Succeeded ? "TCP smoke 성공" : "TCP smoke 실패: " + result.Message);
            RefreshMetrics();
        }

        private async Task RunUdpSmokeAsync()
        {
            AddLog("UDP smoke 실행");
            SmokeRunResult result = await _udpSmoke();
            ApplySmokeResult(result);
            AddLog(result.Succeeded ? "UDP smoke 성공" : "UDP smoke 실패: " + result.Message);
            RefreshMetrics();
        }

        private void RefreshMetrics()
        {
            if (_brokerService == null || _diagnosticsService == null)
                return;

            Metrics.Clear();
            TransportMetricRow[] rows = _diagnosticsService.CreateRows(_brokerService.DiagnosticsSource);
            for (int index = 0; index < rows.Length; index++)
                Metrics.Add(rows[index]);
        }

        private static string FormatEndPoint(EndPoint? endPoint)
        {
            return endPoint == null ? "-" : endPoint.ToString() ?? "-";
        }

        private void RaiseServerCommandStateChanged()
        {
            if (_startServerCommandState != null)
                _startServerCommandState.RaiseCanExecuteChanged();
            if (_stopServerCommandState != null)
                _stopServerCommandState.RaiseCanExecuteChanged();
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChangedEventHandler? handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
