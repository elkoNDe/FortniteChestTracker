namespace FortniteChestTracker;

/// <summary>
/// Runs the existing replay scanner without blocking Windows Forms. Refreshes the
/// offline dashboard's heartbeat while preserving SQLite and replay semantics.
/// </summary>
internal sealed class MonitorRunner : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan DashboardInterval = TimeSpan.FromSeconds(15);

    private readonly TrackerStore _store;
    private readonly ReplayScanner _scanner;
    private readonly string _dataFolder;
    private readonly CancellationTokenSource _stop = new();
    private MonitorStatus _current = MonitorStatus.Starting();
    private Task? _task;
    private DateTimeOffset? _lastImportUtc;
    private DateTime _lastPublishedUtc = DateTime.MinValue;
    private int _previousPending = -1;
    private string _previousState = "";

    public MonitorStatus Current => Volatile.Read(ref _current);

    public MonitorRunner(TrackerStore store, ReplayScanner scanner, string dataFolder)
    {
        _store = store;
        _scanner = scanner;
        _dataFolder = dataFolder;
        _scanner.AnalysisStarted = _ =>
        {
            Update(new MonitorStatus("processing", DateTimeOffset.UtcNow,
                _scanner.PendingCount, _lastImportUtc, "Analizando repetición"));
            // Show long-running imports immediately in the dashboard and tray.
            // This callback runs on the scanner's background thread.
            TryPublish(exportCsv: false);
        };
    }

    public void Start()
    {
        if (_task is not null)
            throw new InvalidOperationException("El monitor ya se ha iniciado.");
        _task = Task.Run(RunAsync);
    }

    public async Task StopAsync()
    {
        _stop.Cancel();
        if (_task is not null)
            await _task;
    }

    private void Update(MonitorStatus snapshot) => Volatile.Write(ref _current, snapshot);

    private void TryPublish(bool exportCsv)
    {
        try
        {
            Dashboard.Write(_store.Snapshot(), _dataFolder, Current, exportCsv);
            _lastPublishedUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            RecordError(ex);
            // A write error is visible in the tray even if the HTML cannot be updated.
            Update(new MonitorStatus("error", DateTimeOffset.UtcNow,
                Current.PendingCount, _lastImportUtc, "Error al actualizar el panel"));
        }
    }

    private void RecordError(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(_dataFolder);
            File.AppendAllText(Path.Combine(_dataFolder, "errors.log"),
                $"[{DateTimeOffset.Now:O}] Error del monitor: {ex}{Environment.NewLine}");
        }
        catch (Exception) { /* Do not crash the tray because logging failed. */ }
    }

    private async Task RunAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var added = 0;
                try
                {
                    added = _scanner.Scan(once: false);
                    if (added > 0)
                        _lastImportUtc = DateTimeOffset.UtcNow;

                    var state = !_scanner.FolderAvailable || _scanner.LastScanFailures > 0
                        ? "error"
                        : _scanner.PendingCount > 0 ? "waiting" : "watching";
                    var detail = !_scanner.FolderAvailable ? "Carpeta de repeticiones no disponible"
                        : _scanner.LastScanFailures > 0 ? "Error de análisis; revisar errors.log"
                        : _scanner.PendingCount > 0 ? "Esperando repeticiones" : null;
                    Update(new MonitorStatus(state, DateTimeOffset.UtcNow,
                        _scanner.PendingCount, _lastImportUtc, detail));

                    var now = DateTime.UtcNow;
                    if (_lastPublishedUtc == DateTime.MinValue || added > 0 ||
                        _scanner.PendingCount != _previousPending ||
                        state != _previousState ||
                        now - _lastPublishedUtc >= DashboardInterval)
                        TryPublish(exportCsv: _lastPublishedUtc == DateTime.MinValue || added > 0);

                    _previousPending = _scanner.PendingCount;
                    _previousState = state;
                }
                catch (Exception ex)
                {
                    RecordError(ex);
                    Update(new MonitorStatus("error", DateTimeOffset.UtcNow,
                        _scanner.PendingCount, _lastImportUtc, "Error del monitor; revisar errors.log"));
                    TryPublish(exportCsv: false);
                }

                await Task.Delay(PollInterval, _stop.Token);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        finally
        {
            Update(new MonitorStatus("stopped", DateTimeOffset.UtcNow, 0,
                _lastImportUtc, "Monitor detenido"));
            TryPublish(exportCsv: false);
        }
    }

    public void Dispose() => _stop.Dispose();
}
