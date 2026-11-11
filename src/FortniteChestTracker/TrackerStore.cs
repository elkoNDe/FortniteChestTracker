using Microsoft.Data.Sqlite;

namespace FortniteChestTracker;

/// <summary>Base de datos local. Las partidas se identifican por GameSessionId para evitar duplicados.</summary>
public sealed class TrackerStore
{
    private readonly string _connectionString;

    public TrackerStore(string folder) : this(folder, TimeZoneInfo.Local) { }

    internal TrackerStore(string folder, TimeZoneInfo localZone)
    {
        Directory.CreateDirectory(folder);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(folder, "tracker.db"),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString();
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS matches (
                session_id TEXT PRIMARY KEY,
                started_utc TEXT NOT NULL,
                player TEXT NOT NULL,
                epic_id TEXT,
                playlist TEXT NOT NULL,
                chests INTEGER NOT NULL CHECK (chests >= 0),
                destroyed INTEGER NOT NULL CHECK (destroyed >= 0),
                ammo_boxes INTEGER NOT NULL CHECK (ammo_boxes >= 0),
                other_containers INTEGER NOT NULL CHECK (other_containers >= 0),
                placement INTEGER CHECK (placement IS NULL OR placement BETWEEN 1 AND 100),
                duration_seconds REAL CHECK (duration_seconds IS NULL OR duration_seconds > 0)
            );
            CREATE TABLE IF NOT EXISTS chest_events (
                session_id TEXT NOT NULL REFERENCES matches(session_id),
                ordinal INTEGER NOT NULL,
                second REAL NOT NULL,
                kind TEXT NOT NULL,
                blueprint TEXT NOT NULL,
                destroyed INTEGER NOT NULL,
                PRIMARY KEY (session_id, ordinal)
            );
            CREATE TABLE IF NOT EXISTS processed_replays (
                fingerprint TEXT PRIMARY KEY,
                session_id TEXT NOT NULL,
                processed_utc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS squad_scans (
                session_id TEXT PRIMARY KEY REFERENCES matches(session_id),
                scanned_utc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS squad_players (
                session_id TEXT NOT NULL REFERENCES matches(session_id),
                player_key TEXT NOT NULL,
                player_name TEXT NOT NULL,
                is_owner INTEGER NOT NULL CHECK (is_owner IN (0,1)),
                chests INTEGER CHECK (chests IS NULL OR chests >= 0),
                destroyed INTEGER CHECK (destroyed IS NULL OR destroyed >= 0),
                ammo_boxes INTEGER CHECK (ammo_boxes IS NULL OR ammo_boxes >= 0),
                PRIMARY KEY (session_id, player_key)
            );
            CREATE INDEX IF NOT EXISTS squad_players_by_match
            ON squad_players (session_id, is_owner);
            """;
        command.ExecuteNonQuery();
        EnsurePlacementColumn(db);
        EnsureDurationColumn(db);
        RepairLegacyTimestamps(db, localZone);
    }

    private static void EnsurePlacementColumn(SqliteConnection db)
    {
        // Existing 0.3.x databases must remain compatible and retain all data.
        using var query = db.CreateCommand();
        query.CommandText = "PRAGMA table_info(matches)";
        using var reader = query.ExecuteReader();
        var hasPlacement = false;
        while (reader.Read())
            if (string.Equals(reader.GetString(1), "placement", StringComparison.OrdinalIgnoreCase))
                hasPlacement = true;
        reader.Close();
        if (hasPlacement) return;
        using var alter = db.CreateCommand();
        alter.CommandText = """
            ALTER TABLE matches ADD COLUMN placement INTEGER
            CHECK (placement IS NULL OR placement BETWEEN 1 AND 100)
            """;
        alter.ExecuteNonQuery();
    }

    private static void EnsureDurationColumn(SqliteConnection db)
    {
        using var query = db.CreateCommand();
        query.CommandText = "PRAGMA table_info(matches)";
        using var reader = query.ExecuteReader();
        var present = false;
        while (reader.Read())
            if (string.Equals(reader.GetString(1), "duration_seconds", StringComparison.OrdinalIgnoreCase))
                present = true;
        reader.Close();
        if (present) return;
        using var alter = db.CreateCommand();
        alter.CommandText = "ALTER TABLE matches ADD COLUMN duration_seconds REAL CHECK (duration_seconds IS NULL OR duration_seconds > 0)";
        alter.ExecuteNonQuery();
    }

    private static void RepairLegacyTimestamps(SqliteConnection db, TimeZoneInfo localZone)
    {
        // One-time, transactional migration. v0.2.3 and earlier interpreted
        // UtcTimeStartedMatch (UTC ticks without DateTimeKind) as local time.
        // Preserve EVERY original timestamp so the process is reversible.
        using var transaction = db.BeginTransaction();
        using var setup = db.CreateCommand();
        setup.Transaction = transaction;
        setup.CommandText = """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                name TEXT PRIMARY KEY,
                applied_at_utc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS legacy_start_times_backup (
                session_id TEXT PRIMARY KEY,
                started_utc_original TEXT NOT NULL
            );
            """;
        setup.ExecuteNonQuery();

        using (var exists = db.CreateCommand())
        {
            exists.Transaction = transaction;
            exists.CommandText = "SELECT 1 FROM schema_migrations WHERE name='replay_utc_kind_v1' LIMIT 1";
            if (exists.ExecuteScalar() != null)
            {
                transaction.Commit();
                return;
            }
        }

        var oldMatches = new List<(string Id, string Timestamp)>();
        using (var select = db.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = "SELECT session_id, started_utc FROM matches";
            using var reader = select.ExecuteReader();
            while (reader.Read()) oldMatches.Add((reader.GetString(0), reader.GetString(1)));
        }

        using var backup = db.CreateCommand();
        backup.Transaction = transaction;
        backup.CommandText = """
            INSERT OR IGNORE INTO legacy_start_times_backup(session_id, started_utc_original)
            VALUES($id,$original)
            """;
        var backupId = backup.Parameters.Add("$id", SqliteType.Text);
        var original = backup.Parameters.Add("$original", SqliteType.Text);

        using var update = db.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = "UPDATE matches SET started_utc=$new WHERE session_id=$id";
        var newTime = update.Parameters.Add("$new", SqliteType.Text);
        var matchId = update.Parameters.Add("$id", SqliteType.Text);

        foreach (var (id, timestamp) in oldMatches)
        {
            backupId.Value = id;
            original.Value = timestamp;
            backup.ExecuteNonQuery();
            // Old values were stored as ISO 8601 UTC. Invert the previous
            // mistaken conversion using the machine's zone, including DST.
            var incorrectlyStored = DateTime.Parse(timestamp,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind);
            var corrected = MatchTimes.RepairLegacyUtc(incorrectlyStored, localZone);
            newTime.Value = corrected.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
            matchId.Value = id;
            update.ExecuteNonQuery();
        }

        using var mark = db.CreateCommand();
        mark.Transaction = transaction;
        mark.CommandText = """
            INSERT INTO schema_migrations(name, applied_at_utc)
            VALUES('replay_utc_kind_v1', $time)
            """;
        mark.Parameters.AddWithValue("$time", DateTime.UtcNow.ToString("O"));
        mark.ExecuteNonQuery();
        transaction.Commit();

        if (oldMatches.Count > 0)
            Console.WriteLine($"Se han corregido las fechas de {oldMatches.Count} partidas " +
                "(copia del valor anterior en SQLite: legacy_start_times_backup).");
    }

    private SqliteConnection Open()
    {
        var db = new SqliteConnection(_connectionString);
        db.Open();
        return db;
    }

    /// <summary>
    /// Enrich old matches from available replay headers, once on startup.
    /// No parsing, deletion, re-importing or changes to chest counts are involved.
    /// Matches whose replay is no longer available keep an unknown duration.
    /// </summary>
    public int BackfillDurations(string replayFolder)
    {
        if (!Directory.Exists(replayFolder)) return 0;
        using var db = Open();
        var pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var select = db.CreateCommand())
        {
            select.CommandText = """
                SELECT p.fingerprint FROM processed_replays p
                JOIN matches m ON p.session_id = m.session_id
                WHERE m.duration_seconds IS NULL
                """;
            using var reader = select.ExecuteReader();
            while (reader.Read()) pending.Add(reader.GetString(0));
        }
        if (pending.Count == 0) return 0;

        using var update = db.CreateCommand();
        update.CommandText = """
            UPDATE matches SET duration_seconds = $duration
            WHERE duration_seconds IS NULL AND session_id IN (
                SELECT session_id FROM processed_replays WHERE fingerprint = $fingerprint)
            """;
        var duration = update.Parameters.Add("$duration", SqliteType.Real);
        var fingerprint = update.Parameters.Add("$fingerprint", SqliteType.Text);
        var updated = 0;
        try
        {
            foreach (var path in Directory.EnumerateFiles(replayFolder, "*.replay", SearchOption.TopDirectoryOnly))
            {
                FileInfo file;
                try { file = new FileInfo(path); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
                if (!file.Exists) continue;
                var key = $"{file.Name}|{file.Length}|{file.LastWriteTimeUtc.Ticks}";
                if (!pending.Contains(key)) continue;
                var seconds = ReplayProbe.ReadDurationSeconds(path);
                if (seconds is not > 0) continue;
                duration.Value = seconds.Value;
                fingerprint.Value = key;
                updated += update.ExecuteNonQuery();
            }
        }
        catch (IOException) { /* Old replays are optional. */ }
        catch (UnauthorizedAccessException) { }
        return updated;
    }

    public bool HasProcessed(string fingerprint)
    {
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = "SELECT 1 FROM processed_replays WHERE fingerprint = $fp LIMIT 1";
        command.Parameters.AddWithValue("$fp", fingerprint);
        return command.ExecuteScalar() != null;
    }

    /// <summary>Only re-read already imported replays whose squad rows are missing.</summary>
    public string? SquadBackfillSession(string fingerprint)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT p.session_id FROM processed_replays p
            JOIN matches m ON m.session_id = p.session_id
            WHERE p.fingerprint = $fp
              AND TRIM(m.playlist) COLLATE NOCASE IN (
                  'Playlist_NoBuildBR_Solo', 'Playlist_NoBuildBR_Duo',
                  'Playlist_NoBuildBR_Trio', 'Playlist_NoBuildBR_Squad')
              AND (m.placement IS NULL
                  OR NOT EXISTS (SELECT 1 FROM squad_scans s WHERE s.session_id = p.session_id))
            LIMIT 1
            """;
        cmd.Parameters.AddWithValue("$fp", fingerprint);
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>
    /// Fill squad rows for a historic match without altering its original
    /// personal counters, start time or event records.
    /// </summary>
    public bool SaveSquad(MatchResult match)
    {
        using var db = Open();
        using var tx = db.BeginTransaction();
        using var lookup = db.CreateCommand();
        lookup.Transaction = tx;
        lookup.CommandText = """
            SELECT 1 FROM matches m WHERE m.session_id=$id
            AND (m.placement IS NULL
                 OR NOT EXISTS (SELECT 1 FROM squad_scans s WHERE s.session_id=m.session_id))
            LIMIT 1
            """;
        lookup.Parameters.AddWithValue("$id", match.SessionId);
        if (lookup.ExecuteScalar() is null)
        {
            tx.Commit();
            return false;
        }

        if (match.Placement is int position && position is >= 1 and <= 100)
        {
            using var place = db.CreateCommand();
            place.Transaction = tx;
            place.CommandText = """
                UPDATE matches SET placement=COALESCE(placement,$position)
                WHERE session_id=$id
                """;
            place.Parameters.AddWithValue("$position", position);
            place.Parameters.AddWithValue("$id", match.SessionId);
            place.ExecuteNonQuery();
        }
        WriteSquadRows(db, tx, match);
        tx.Commit();
        return true;
    }

    private static void WriteSquadRows(SqliteConnection db, SqliteTransaction tx, MatchResult match)
    {
        // Save even the empty/owner-only result as a scan marker, so replays
        // without an attributable team are not reparsed forever.
        using var insert = db.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = """
            INSERT OR REPLACE INTO squad_players
            (session_id,player_key,player_name,is_owner,chests,destroyed,ammo_boxes)
            VALUES($session,$key,$name,$owner,$chests,$destroyed,$ammo)
            """;
        var session = insert.Parameters.Add("$session", SqliteType.Text);
        var key = insert.Parameters.Add("$key", SqliteType.Text);
        var name = insert.Parameters.Add("$name", SqliteType.Text);
        var owner = insert.Parameters.Add("$owner", SqliteType.Integer);
        var chests = insert.Parameters.Add("$chests", SqliteType.Integer);
        var destroyed = insert.Parameters.Add("$destroyed", SqliteType.Integer);
        var ammo = insert.Parameters.Add("$ammo", SqliteType.Integer);
        var members = match.TeamMembers.Count == 0
            ? new[] { new TeamMemberResult(
                match.EpicId ?? "owner", match.PlayerName, true,
                match.Chests, match.Destroyed, match.AmmoBoxes) }
            : match.TeamMembers;

        foreach (var member in members)
        {
            session.Value = match.SessionId;
            key.Value = member.PlayerKey;
            name.Value = member.PlayerName;
            owner.Value = member.IsOwner ? 1 : 0;
            chests.Value = (object?)member.Chests ?? DBNull.Value;
            destroyed.Value = (object?)member.Destroyed ?? DBNull.Value;
            ammo.Value = (object?)member.AmmoBoxes ?? DBNull.Value;
            insert.ExecuteNonQuery();
        }

        using var mark = db.CreateCommand();
        mark.Transaction = tx;
        mark.CommandText = """
            INSERT OR REPLACE INTO squad_scans(session_id,scanned_utc)
            VALUES($session,$time)
            """;
        mark.Parameters.AddWithValue("$session", match.SessionId);
        mark.Parameters.AddWithValue("$time", DateTime.UtcNow.ToString("O"));
        mark.ExecuteNonQuery();
    }

    public bool Save(MatchResult match, string fingerprint)
    {
        using var db = Open();
        using var transaction = db.BeginTransaction();
        if (!PlaylistRules.IsAllowed(match.Playlist))
        {
            // Remember excluded replays so the 6-second watcher does not decode
            // them repeatedly. Existing non-Zero Build rows stay intact in DB,
            // but are hidden by Snapshot and never contribute to statistics.
            using var ignored = db.CreateCommand();
            ignored.Transaction = transaction;
            ignored.CommandText = """
                INSERT OR IGNORE INTO processed_replays(fingerprint,session_id,processed_utc)
                VALUES($fingerprint,$session,CURRENT_TIMESTAMP)
                """;
            ignored.Parameters.AddWithValue("$fingerprint", fingerprint);
            ignored.Parameters.AddWithValue("$session", match.SessionId);
            ignored.ExecuteNonQuery();
            transaction.Commit();
            return false;
        }

        using var insert = db.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT OR IGNORE INTO matches(session_id, started_utc, player, epic_id, playlist, chests, destroyed, ammo_boxes, other_containers, placement, duration_seconds)
            VALUES($id,$time,$player,$epic,$playlist,$chests,$destroyed,$ammo,$others,$placement,$duration)
            """;
        insert.Parameters.AddWithValue("$id", match.SessionId);
        insert.Parameters.AddWithValue("$time", match.StartedUtc.ToString("O"));
        insert.Parameters.AddWithValue("$player", match.PlayerName);
        insert.Parameters.AddWithValue("$epic", (object?)match.EpicId ?? DBNull.Value);
        insert.Parameters.AddWithValue("$playlist", match.Playlist);
        insert.Parameters.AddWithValue("$chests", match.Chests);
        insert.Parameters.AddWithValue("$destroyed", match.Destroyed);
        insert.Parameters.AddWithValue("$ammo", match.AmmoBoxes);
        insert.Parameters.AddWithValue("$others", match.OtherContainers);
        insert.Parameters.AddWithValue("$placement", (object?)match.Placement ?? DBNull.Value);
        insert.Parameters.AddWithValue("$duration", (object?)match.DurationSeconds ?? DBNull.Value);
        var inserted = insert.ExecuteNonQuery() > 0;

        if (inserted)
        {
            using var eventInsert = db.CreateCommand();
            eventInsert.Transaction = transaction;
            eventInsert.CommandText = """
                INSERT INTO chest_events(session_id, ordinal, second, kind, blueprint, destroyed)
                VALUES($id,$num,$second,$kind,$blueprint,$destroyed)
                """;
            var id = eventInsert.Parameters.Add("$id", SqliteType.Text);
            var num = eventInsert.Parameters.Add("$num", SqliteType.Integer);
            var second = eventInsert.Parameters.Add("$second", SqliteType.Real);
            var kind = eventInsert.Parameters.Add("$kind", SqliteType.Text);
            var blueprint = eventInsert.Parameters.Add("$blueprint", SqliteType.Text);
            var destroyed = eventInsert.Parameters.Add("$destroyed", SqliteType.Integer);
            for (var i = 0; i < match.Events.Count; i++)
            {
                var item = match.Events[i];
                id.Value = match.SessionId;
                num.Value = i;
                second.Value = item.Second;
                kind.Value = item.Kind;
                blueprint.Value = item.Blueprint;
                destroyed.Value = item.Destroyed ? 1 : 0;
                eventInsert.ExecuteNonQuery();
            }
        }

        if (inserted) WriteSquadRows(db, transaction, match);

        using var processed = db.CreateCommand();
        processed.Transaction = transaction;
        processed.CommandText = """
            INSERT OR IGNORE INTO processed_replays(fingerprint, session_id, processed_utc)
            VALUES ($fingerprint,$session,CURRENT_TIMESTAMP)
            """;
        processed.Parameters.AddWithValue("$fingerprint", fingerprint);
        processed.Parameters.AddWithValue("$session", match.SessionId);
        processed.ExecuteNonQuery();
        transaction.Commit();
        return inserted;
    }

    public TrackerSnapshot Snapshot()
    {
        var matches = new List<MatchSummary>();
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = """
            SELECT session_id, started_utc, player, playlist, chests, destroyed, ammo_boxes, other_containers, placement, duration_seconds
            FROM matches
            WHERE TRIM(playlist) COLLATE NOCASE IN (
                'Playlist_NoBuildBR_Solo', 'Playlist_NoBuildBR_Duo',
                'Playlist_NoBuildBR_Trio', 'Playlist_NoBuildBR_Squad')
            ORDER BY started_utc DESC
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            matches.Add(new MatchSummary(reader.GetString(0), DateTime.Parse(reader.GetString(1), null, System.Globalization.DateTimeStyles.RoundtripKind),
                reader.GetString(2), reader.GetString(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7))
            { Placement = reader.IsDBNull(8) ? null : reader.GetInt32(8),
              DurationSeconds = reader.IsDBNull(9) ? null : reader.GetDouble(9) });
        }
        reader.Close();
        var groups = new Dictionary<string, List<TeamMemberResult>>(StringComparer.Ordinal);
        using (var squad = db.CreateCommand())
        {
            squad.CommandText = """
                SELECT session_id,player_key,player_name,is_owner,chests,destroyed,ammo_boxes
                FROM squad_players
                ORDER BY session_id, is_owner DESC, player_name COLLATE NOCASE
                """;
            using var squadReader = squad.ExecuteReader();
            while (squadReader.Read())
            {
                var id = squadReader.GetString(0);
                if (!groups.TryGetValue(id, out var list))
                {
                    list = new List<TeamMemberResult>();
                    groups.Add(id, list);
                }
                list.Add(new TeamMemberResult(
                    squadReader.GetString(1), squadReader.GetString(2), squadReader.GetInt32(3) != 0,
                    squadReader.IsDBNull(4) ? null : squadReader.GetInt32(4),
                    squadReader.IsDBNull(5) ? null : squadReader.GetInt32(5),
                    squadReader.IsDBNull(6) ? null : squadReader.GetInt32(6)));
            }
        }
        return new TrackerSnapshot(matches.Select(m => m with
        {
            TeamMembers = groups.TryGetValue(m.SessionId, out var members)
                ? members : Array.Empty<TeamMemberResult>()
        }).ToArray());
    }
}

public sealed record MatchSummary(string SessionId, DateTime Date, string Player, string Playlist,
    int Chests, int Destroyed, int AmmoBoxes, int OtherContainers)
{
    public IReadOnlyList<TeamMemberResult> TeamMembers { get; init; } = Array.Empty<TeamMemberResult>();
    public int? Placement { get; init; }
    public double? DurationSeconds { get; init; }
    public DateTime? EndedUtc => DurationSeconds is double seconds && seconds > 0
        ? Date.AddSeconds(seconds) : null;
    public string DurationText => FormatDuration(DurationSeconds);

    public static string FormatDuration(double? seconds)
    {
        if (seconds is not double value || value <= 0 || double.IsInfinity(value) || double.IsNaN(value))
            return "—";
        var time = TimeSpan.FromSeconds(Math.Round(value));
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{(int)time.TotalMinutes:00}:{time.Seconds:00}";
    }
}

public sealed record TrackerSnapshot(IReadOnlyList<MatchSummary> Matches)
{
    public int Chests => Matches.Sum(m => m.Chests);
    // This includes each owner's chests exactly once and only observed
    // teammate openings. Null/unavailable attribution contributes nothing.
    public int SquadChests => Matches.Sum(m => m.Chests +
        m.TeamMembers.Where(t => !t.IsOwner).Sum(t => t.Chests ?? 0));
    public int AmmoBoxes => Matches.Sum(m => m.AmmoBoxes);
    public int Destroyed => Matches.Sum(m => m.Destroyed);
    public int Record => Matches.Count == 0 ? 0 : Matches.Max(m => m.Chests);
    public double Average => Matches.Count == 0 ? 0 : (double)Chests / Matches.Count;
}
