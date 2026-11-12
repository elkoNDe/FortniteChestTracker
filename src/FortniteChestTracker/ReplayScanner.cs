namespace FortniteChestTracker;

/// <summary>
/// Polls Fortnite's replay directory without touching the game process. Finished
/// replays are imported once; those still being recorded remain pending.
/// </summary>
public sealed class ReplayScanner
{
    // Fortnite finalizes and writes the actual encryption key after a match ends.
    // The old 30-second delay + two 12-second polls made normal updates slow.
    private static readonly TimeSpan FinalizedQuietPeriod = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LiveKeyQuietPeriod = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan UnknownHeaderQuietPeriod = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(5);

    private readonly TrackerStore _store;
    private readonly string _folder;
    private readonly string _errorLog;
    private readonly Func<string, MatchResult> _analyze;
    private readonly Dictionary<string, (long Size, DateTime Stamp)> _lastSeen =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _failed = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _lastMessage = new(StringComparer.OrdinalIgnoreCase);

    public int PendingCount { get; private set; }
    public int LastScanFailures { get; private set; }
    public bool FolderAvailable { get; private set; } = true;

    // Optional observer for the tray/status panel. No gameplay data is changed.
    public Action<string>? AnalysisStarted { get; set; }

    public ReplayScanner(TrackerStore store, string folder, string dataFolder)
        : this(store, folder, dataFolder, file => new ReplayAnalyzer().Analyze(file))
    {
    }

    // Test-only dependency injection allows a complete watch-cycle regression
    // test without putting private Fortnite replays in the repository.
    internal ReplayScanner(TrackerStore store, string folder, string dataFolder,
        Func<string, MatchResult> analyze)
    {
        _store = store;
        _folder = folder;
        _errorLog = Path.Combine(dataFolder, "errors.log");
        _analyze = analyze;
    }

    /// <summary>
    /// Null means ready. The result depends only on the current header, quiet
    /// time and whether the file has remained stable between consecutive polls.
    /// Do not decode an encrypted replay whose key is all zeros.
    /// </summary>
    internal static string? WaitingReason(
        (bool? IsLive, bool KeyMissing) header, TimeSpan quietFor, bool stable, bool once)
    {
        if (header.IsLive == true && header.KeyMissing)
            return "Fortnite sigue grabando la partida (clave pendiente)";
        if (header.IsLive == false && header.KeyMissing)
            return "Esperando la clave de la repetición";

        // An unrecognized header may be a newer Fortnite format. Give the file
        // enough time to finish, then attempt a read and report the real error,
        // instead of silently ignoring it forever.
        if (header.IsLive is null && !once && quietFor < UnknownHeaderQuietPeriod)
            return "Cabecera desconocida; esperando a que finalice la grabación";

        // A live flag sometimes persists after the encryption key is available.
        // Wait longer to avoid parsing/saving the middle of a match.
        var quietPeriod = header.IsLive == true ? LiveKeyQuietPeriod : FinalizedQuietPeriod;
        if (!once && quietFor < quietPeriod)
            return "Esperando a que Fortnite termine de escribir el archivo";
        if (header.IsLive == true && quietFor < LiveKeyQuietPeriod)
            return "Grabación todavía activa";

        if (!once && !stable)
            return "Comprobando que el archivo no cambia";
        return null;
    }

    public int Scan(bool once)
    {
        PendingCount = 0;
        LastScanFailures = 0;
        FolderAvailable = true;
        if (!Directory.Exists(_folder))
        {
            FolderAvailable = false;
            // Do not repeat the same warning every six seconds.
            Announce("missing-folder", "Repeticiones",
                $"No existe la carpeta de replays: {_folder}. Comprueba que Fortnite graba repeticiones.");
            return 0;
        }
        _lastMessage.Remove("missing-folder");

        string[] files;
        try
        {
            files = Directory.EnumerateFiles(_folder, "*.replay", SearchOption.TopDirectoryOnly)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            FolderAvailable = false;
            Announce("listing-error", "Repeticiones",
                $"No se pueden listar las repeticiones: {ex.Message}");
            return 0;
        }
        _lastMessage.Remove("listing-error");

        var newlyAdded = 0;
        foreach (var file in files)
        {
            FileInfo info;
            try { info = new FileInfo(file); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or ArgumentException or System.Security.SecurityException)
            {
                continue;
            }
            if (!info.Exists || info.Length == 0 || info.Length > 2L * 1024 * 1024 * 1024)
                continue;

            var fingerprint = $"{info.Name}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
            if (_store.HasProcessed(fingerprint))
                continue;

            var stable = _lastSeen.TryGetValue(file, out var previous)
                && previous.Size == info.Length && previous.Stamp == info.LastWriteTimeUtc;
            _lastSeen[file] = (info.Length, info.LastWriteTimeUtc);

            var header = ReplayProbe.Inspect(file);
            var quietFor = DateTime.UtcNow - info.LastWriteTimeUtc;
            var reason = WaitingReason(header, quietFor, stable, once);
            if (reason != null)
            {
                PendingCount++;
                Announce(fingerprint, info.Name, reason);
                continue;
            }

            if (!once && _failed.TryGetValue(fingerprint, out var failedAt)
                && DateTime.UtcNow - failedAt < RetryDelay)
            {
                PendingCount++;
                Announce(fingerprint, info.Name, "Análisis fallido; se volverá a intentar");
                continue;
            }

            _lastMessage.Remove(fingerprint);
            try
            {
                AnalysisStarted?.Invoke(info.Name);
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Partida finalizada: analizando {info.Name} " +
                    $"({info.Length / 1024.0 / 1024.0:N1} MB)...");
                var match = _analyze(file);
                if (!PlaylistRules.IsAllowed(match.Playlist))
                {
                    _store.Save(match, fingerprint); // remember and skip on future scans
                    Console.WriteLine($"  Modo excluido: {match.Playlist}. " +
                        "Solo se contabiliza Playlist_NoBuildBR_Solo/Duo/Trio/Squad.");
                }
                else if (_store.Save(match, fingerprint))
                {
                    newlyAdded++;
                    Console.WriteLine($"  {match.PlayerName}: {match.Chests} cofres " +
                        $"({match.Destroyed} destruidos)");
                }
                else
                {
                    Console.WriteLine("  Partida ya registrada; no se duplica.");
                }
                _failed.Remove(fingerprint);
                _lastSeen.Remove(file);
            }
            catch (Exception ex)
            {
                LastScanFailures++;
                LogError(info.Name, ex);
                _failed[fingerprint] = DateTime.UtcNow;
                PendingCount++;
                Console.Error.WriteLine($"  Error analizando {info.Name}: {ex.Message}");
                Console.Error.WriteLine($"  Más información en {_errorLog}");
            }
        }
        return newlyAdded;
    }

    /// <summary>
    /// Explicit, optional one-time enrichment for already imported games.
    /// This can be CPU-intensive, so it never runs during normal monitoring.
    /// It does not modify personal match counters or duplicate matches.
    /// </summary>
    public int BackfillSquads()
    {
        if (!Directory.Exists(_folder))
        {
            Console.WriteLine("No existe la carpeta de repeticiones.");
            return 0;
        }
        var completed = 0;
        var skipped = 0;
        var failed = 0;
        string[] files;
        try
        {
            files = Directory.EnumerateFiles(_folder, "*.replay", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine("No se pueden consultar las repeticiones: " + ex.Message);
            return 0;
        }

        foreach (var file in files)
        {
            try
            {
                var info = new FileInfo(file);
                if (!info.Exists || info.Length == 0 || info.Length > 2L * 1024 * 1024 * 1024)
                    continue;
                var fingerprint = $"{info.Name}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
                var sessionId = _store.SquadBackfillSession(fingerprint);
                if (sessionId == null) continue;

                var probe = ReplayProbe.Inspect(file);
                if (probe.KeyMissing || probe.IsLive == true ||
                    DateTime.UtcNow - info.LastWriteTimeUtc < FinalizedQuietPeriod)
                {
                    skipped++;
                    continue;
                }

                Console.WriteLine($"Recuperando escuadra de {info.Name}...");
                var analyzed = _analyze(file);
                if (analyzed.SessionId != sessionId)
                {
                    // Never attach another session's teammate data to the
                    // match just because filenames/fingerprints happened to match.
                    Console.Error.WriteLine($"  El identificador de partida no coincide: {info.Name}.");
                    failed++;
                    continue;
                }
                if (_store.SaveSquad(analyzed))
                    completed++;
            }
            catch (Exception ex)
            {
                failed++;
                LogError(Path.GetFileName(file), ex);
                Console.Error.WriteLine($"  No se pudo recuperar la escuadra de {Path.GetFileName(file)}: {ex.Message}");
            }
        }
        Console.WriteLine($"Desglose de escuadra recuperado en {completed} partidas. " +
            $"Pendientes/incompletas: {skipped}. Errores: {failed}.");
        Console.WriteLine("No se han alterado los cofres personales del histórico.");
        return completed;
    }

    private void Announce(string key, string filename, string status)
    {
        if (_lastMessage.TryGetValue(key, out var last) && last == status)
            return;
        _lastMessage[key] = status;
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {filename}: {status}");
    }

    private void LogError(string name, Exception exception)
    {
        try { File.AppendAllText(_errorLog, $"[{DateTime.UtcNow:O}] {name}: {exception}\n"); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
