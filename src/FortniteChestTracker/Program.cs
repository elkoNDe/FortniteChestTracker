using System.Runtime.InteropServices;

namespace FortniteChestTracker;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Contains("--help") || args.Contains("-h"))
        {
            Console.WriteLine("Fortnite ChestTracker: --once | --watch | --backfill-squad | --self-test | --demos <ruta> | --data <ruta> | --no-browser");
            return 0;
        }
        if (args.Contains("--self-test")) return SelfTest.Run();

        static string? Option(string[] args, string name)
        {
            var index = Array.IndexOf(args, name);
            if (index < 0) return null;
            if (index + 1 >= args.Length) throw new ArgumentException($"Falta un valor para {name}");
            return args[index + 1];
        }
        string demos, data;
        try
        {
            demos = Option(args, "--demos") ?? Paths.DefaultDemos;
            data = Option(args, "--data") ?? Paths.DefaultData;
        }
        catch (ArgumentException e) { Console.Error.WriteLine(e.Message); return 2; }
        try
        {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("FORTNITE CHESTTRACKER — Solo archivos .replay, nunca el proceso del juego");
        Console.WriteLine($"Repeticiones: {demos}");
        Console.WriteLine($"Datos locales: {data}");
        Console.WriteLine("Activa las repeticiones en Fortnite: Ajustes > Juego > Repeticiones.\n");

            using var mutex = new Mutex(initiallyOwned: true, name: "FortniteChestTracker-Local-SingleInstance", out var created);
            if (!created)
            {
                Console.Error.WriteLine("El tracker ya está abierto en otra ventana.");
                var existingReport = Path.Combine(data, "dashboard.html");
                if (File.Exists(existingReport) && !args.Contains("--no-browser")) Dashboard.OpenBrowser(existingReport);
                return 0;
            }
            var store = new TrackerStore(data);
            var scanner = new ReplayScanner(store, demos, data);
            var report = Dashboard.Write(store.Snapshot(), data);
            if (!args.Contains("--no-browser")) Dashboard.OpenBrowser(report);
            if (args.Contains("--backfill-squad"))
            {
                // Explicit rather than automatic: re-reading up to ~100 historic
                // replays can consume CPU while Fortnite is running.
                scanner.Scan(once: true);
                scanner.BackfillSquads();
                Dashboard.Write(store.Snapshot(), data);
                Console.WriteLine("Histórico de escuadra actualizado: " + report);
                return 0;
            }
            if (args.Contains("--once"))
            {
                scanner.Scan(once: true);
                Dashboard.Write(store.Snapshot(), data);
                Console.WriteLine("Análisis terminado. Informe: " + report);
                return 0;
            }

            Console.WriteLine("Vigilando nuevas partidas cada 6 segundos. Los cofres se cuentan al terminar " +
                "y guardar la repetición; no mientras juegas. Ctrl+C para salir.");
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(6));
            scanner.Scan(once: false);
            Dashboard.Write(store.Snapshot(), data);
            var lastHeartbeat = DateTime.UtcNow;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Monitor activo. " +
                $"Repeticiones pendientes: {scanner.PendingCount}.");
            try
            {
                while (await timer.WaitForNextTickAsync(cts.Token))
                {
                    var added = scanner.Scan(once: false);
                    if (added > 0)
                    {
                        var snapshot = store.Snapshot();
                        Dashboard.Write(snapshot, data);
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Informe actualizado: " +
                            $"{snapshot.Chests} cofres en {snapshot.Matches.Count} partidas.");
                    }

                    if (DateTime.UtcNow - lastHeartbeat >= TimeSpan.FromMinutes(1))
                    {
                        lastHeartbeat = DateTime.UtcNow;
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Monitor activo. " +
                            $"Repeticiones pendientes: {scanner.PendingCount}. " +
                            "El navegador se actualiza cada 20 segundos.");
                    }
                }
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
            return 0;
        }
        catch (Exception e)
        {
            ReportStartupError(e, data);
            return 1;
        }
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
