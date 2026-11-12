using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace FortniteChestTracker;

public static class Program
{
    // The GUI subsystem prevents a console window from flashing on double-click.
    // CLI maintenance commands remain available through --once / --self-test.
    [STAThread]
    public static int Main(string[] args)
    {
        var help = args.Contains("--help") || args.Contains("-h");
        var cli = help || args.Contains("--self-test") ||
            args.Contains("--once") || args.Contains("--backfill-squad") ||
            args.Contains("--console") || args.Contains("--headless");
        if (cli) AttachParentConsole();

        if (help)
        {
            Console.WriteLine("Fortnite ChestTracker: --once | --watch | --backfill-squad | --self-test | --demos <ruta> | --data <ruta> | --no-browser | --console | --headless");
            return 0;
        }
        if (args.Contains("--self-test")) return SelfTest.Run();

        static string? Option(string[] arguments, string name)
        {
            var index = Array.IndexOf(arguments, name);
            if (index < 0) return null;
            if (index + 1 >= arguments.Length)
                throw new ArgumentException($"Falta un valor para {name}");
            return arguments[index + 1];
        }

        string demos, data;
        try
        {
            demos = Option(args, "--demos") ?? Paths.DefaultDemos;
            data = Option(args, "--data") ?? Paths.DefaultData;
        }
        catch (ArgumentException e)
        {
            Console.Error.WriteLine(e.Message);
            return 2;
        }

        try
        {
            if (cli)
            {
                try { Console.OutputEncoding = Encoding.UTF8; }
                catch (IOException) { /* WinExe started without an attached terminal. */ }
            }
            Console.WriteLine("FORTNITE CHESTTRACKER - Solo repeticiones .replay, sin tocar Fortnite");
            Console.WriteLine($"Repeticiones: {demos}");
            Console.WriteLine($"Datos locales: {data}");

            using var mutex = new Mutex(initiallyOwned: true,
                name: "FortniteChestTracker-Local-SingleInstance", out var created);
            if (!created)
            {
                // The existing tray owns the monitor. Launching a second EXE
                // simply opens its dashboard rather than creating another scanner.
                var existingReport = Path.Combine(data, "dashboard.html");
                if (File.Exists(existingReport) && !args.Contains("--no-browser"))
                    Dashboard.OpenBrowser(existingReport);
                return 0;
            }

            var store = new TrackerStore(data);
            var recovered = store.BackfillDurations(demos);
            if (recovered > 0)
                Console.WriteLine($"Duración recuperada de {recovered} partidas antiguas.");
            var scanner = new ReplayScanner(store, demos, data);
            var report = Dashboard.Write(store.Snapshot(), data, MonitorStatus.Starting());
            if (!args.Contains("--no-browser"))
                Dashboard.OpenBrowser(report);

            if (args.Contains("--backfill-squad"))
            {
                scanner.Scan(once: true);
                scanner.BackfillSquads();
                Dashboard.Write(store.Snapshot(), data,
                    new MonitorStatus("stopped", DateTimeOffset.UtcNow, 0));
                Console.WriteLine("Histórico de escuadra actualizado: " + report);
                return 0;
            }
            if (args.Contains("--once"))
            {
                scanner.Scan(once: true);
                Dashboard.Write(store.Snapshot(), data,
                    new MonitorStatus("stopped", DateTimeOffset.UtcNow, 0));
                Console.WriteLine("Análisis terminado. Informe: " + report);
                return 0;
            }

            using var runner = new MonitorRunner(store, scanner, data);
            runner.Start();

            // Headless is explicit for CI and terminal diagnostics; normal
            // launches run only the notification-area icon, never a console.
            if (args.Contains("--headless") || args.Contains("--console"))
            {
                using var stopping = new CancellationTokenSource();
                Console.CancelKeyPress += (_, eventArgs) =>
                {
                    eventArgs.Cancel = true;
                    stopping.Cancel();
                };
                try
                {
                    Task.Delay(Timeout.InfiniteTimeSpan, stopping.Token)
                        .GetAwaiter().GetResult();
                }
                catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
                runner.StopAsync().GetAwaiter().GetResult();
                return 0;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var tray = new TrayAppContext(runner, data))
                Application.Run(tray);

            // Also handle Windows logoff/shutdown, not just "Salir" from the menu.
            runner.StopAsync().GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception e)
        {
            ReportStartupError(e, data);
            return 1;
        }
    }

    private const uint AttachParentProcess = uint.MaxValue;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint processId);

    private static void AttachParentConsole()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            if (AttachConsole(AttachParentProcess))
            {
                Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false))
                    { AutoFlush = true });
                Console.SetError(new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false))
                    { AutoFlush = true });
            }
        }
        catch (Exception) { /* A redirected or unavailable console is optional. */ }
    }

    private static void ReportStartupError(Exception error, string dataFolder)
    {
        var log = Path.Combine(dataFolder, "startup-errors.log");
        try
        {
            Directory.CreateDirectory(dataFolder);
            File.AppendAllText(log, $"[{DateTimeOffset.Now:O}] {error}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception) { log = "(no se pudo escribir el archivo de registro)"; }

        Console.Error.WriteLine("No se ha podido iniciar Fortnite ChestTracker: " + error);
        Console.Error.WriteLine("Registro de errores: " + log);

        // A double click closes a failed console instantly. Show the actual error on Windows.
        // In CI / redirected shells avoid modal dialogs blocking automation.
        if (OperatingSystem.IsWindows() && Environment.UserInteractive && !Console.IsInputRedirected)
        {
            try
            {
                MessageBoxW(IntPtr.Zero,
                    $"No se ha podido iniciar Fortnite ChestTracker.\\n\\n{error.Message}\\n\\nDetalles: {log}",
                    "Fortnite ChestTracker - Error de inicio", 0x00000010);
            }
            catch (Exception) { /* The log and console still contain the diagnostic. */ }
        }
    }

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MessageBoxW(IntPtr owner, string text, string title, uint options);
}
