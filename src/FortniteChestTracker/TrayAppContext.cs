using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace FortniteChestTracker;

/// <summary>
/// No visible window: just a Windows notification-area icon and its menu.
/// Both the dashboard and the statistics stay local.
/// </summary>
internal sealed class TrayAppContext : ApplicationContext
{
    private readonly MonitorRunner _runner;
    private readonly string _dataFolder;
    private readonly Stream _iconStream;
    private readonly Icon _icon;
    private readonly ContextMenuStrip _menu;
    private readonly NotifyIcon _notification;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly ToolStripMenuItem _stateItem;
    private readonly ToolStripMenuItem _pendingItem;
    private readonly ToolStripMenuItem _lastItem;
    private readonly ToolStripMenuItem _exitItem;
    private bool _exiting;

    public TrayAppContext(MonitorRunner runner, string dataFolder)
    {
        _runner = runner;
        _dataFolder = dataFolder;
        _iconStream = typeof(Dashboard).Assembly
            .GetManifestResourceStream("FortniteChestTracker.app-icon.ico")
            ?? throw new InvalidOperationException("No se encuentra el icono de la aplicación.");
        _icon = new Icon(_iconStream);
        _menu = new ContextMenuStrip();
        _stateItem = new ToolStripMenuItem("Iniciando monitor...") { Enabled = false };
        _pendingItem = new ToolStripMenuItem("Pendientes: 0") { Enabled = false };
        _lastItem = new ToolStripMenuItem("Última importación: —") { Enabled = false };
        _exitItem = new ToolStripMenuItem("Salir de FortniteChestTracker");
        _exitItem.Click += async (_, _) => await RequestExitAsync();

        var dashboard = new ToolStripMenuItem("Abrir dashboard");
        dashboard.Font = new Font(dashboard.Font, FontStyle.Bold);
        dashboard.Click += (_, _) => OpenDashboard();
        var folder = new ToolStripMenuItem("Abrir carpeta de datos");
        folder.Click += (_, _) => OpenPath(_dataFolder);
        var errors = new ToolStripMenuItem("Ver registro de errores");
        errors.Click += (_, _) => OpenErrors();
        _menu.Items.AddRange(new ToolStripItem[]
        {
            _stateItem, _pendingItem, _lastItem, new ToolStripSeparator(),
            dashboard, folder, errors, new ToolStripSeparator(), _exitItem
        });
        _menu.Opening += (_, _) => RefreshStatus();

        _notification = new NotifyIcon
        {
            Icon = _icon,
            Text = "Fortnite ChestTracker - Iniciando",
            ContextMenuStrip = _menu,
            Visible = true
        };
        _notification.DoubleClick += (_, _) => OpenDashboard();

        _timer = new System.Windows.Forms.Timer { Interval = 2000 };
        _timer.Tick += (_, _) => RefreshStatus();
        _timer.Start();
        RefreshStatus();
    }

    private static string StateLabel(string state) => state switch
    {
        "watching" => "Monitor activo",
        "waiting" => "Esperando repetición",
        "processing" => "Analizando repetición",
        "error" => "Atención: revisar monitor",
        "stopped" => "Monitor detenido",
        "offline" => "Monitor desconectado",
        _ => "Iniciando monitor"
    };

    private void RefreshStatus()
    {
        if (_exiting) return;
        var snapshot = _runner.Current;
        var label = StateLabel(snapshot.State);
        _stateItem.Text = label;
        _pendingItem.Text = $"Repeticiones pendientes: {snapshot.PendingCount}";
        _lastItem.Text = snapshot.LastImportedUtc.HasValue
            ? "Última importación: " + snapshot.LastImportedUtc.Value.ToLocalTime().ToString("dd/MM HH:mm")
            : "Sin nuevas importaciones en esta sesión";
        _notification.Text = "Fortnite ChestTracker - " + label;
    }

    private void OpenDashboard()
    {
        var file = Path.Combine(_dataFolder, "dashboard.html");
        if (File.Exists(file)) Dashboard.OpenBrowser(file);
    }

    private static void OpenPath(string file)
    {
        try { Process.Start(new ProcessStartInfo(file) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Surface actionable feedback, not a silent menu that appears broken.
            MessageBox.Show("No se ha podido abrir: " + ex.Message,
                "Fortnite ChestTracker", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OpenErrors()
    {
        var errorFile = Path.Combine(_dataFolder, "errors.log");
        var startupFile = Path.Combine(_dataFolder, "startup-errors.log");
        OpenPath(File.Exists(errorFile) ? errorFile
            : File.Exists(startupFile) ? startupFile : _dataFolder);
    }

    private async Task RequestExitAsync()
    {
        if (_exiting) return;
        _exiting = true;
        _exitItem.Enabled = false;
        _stateItem.Text = "Cerrando monitor...";
        _notification.Text = "Fortnite ChestTracker - Cerrando";
        await _runner.StopAsync();
        ExitThread();
    }

    protected override void ExitThreadCore()
    {
        _timer.Stop();
        _timer.Dispose();
        _notification.Visible = false;
        _notification.Dispose();
        _menu.Dispose();
        _icon.Dispose();
        _iconStream.Dispose();
        base.ExitThreadCore();
    }
}
