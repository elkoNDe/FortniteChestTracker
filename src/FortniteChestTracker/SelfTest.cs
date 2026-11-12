namespace FortniteChestTracker;

public static class SelfTest
{
    public static int Run()
    {
        var dir = Path.Combine(Path.GetTempPath(), "FortniteChestTracker-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Check(ChestTypes.IsChest(ChestTypes.Classify("B_TieredChest_Athena")), "cofre normal");
            Check(ChestTypes.IsChest(ChestTypes.Classify("B_RareChest")), "cofre raro");
            Check(!ChestTypes.IsChest(ChestTypes.Classify("B_AmmoBox")), "caja de municion");
            Check(ReplayProbe.Inspect(Path.Combine(dir, "no-existe.replay")).KeyMissing, "replay inexistente");
            var store = new TrackerStore(dir);
            var sample = new MatchResult("session-test", DateTime.UtcNow, "TestPlayer", null, "Playlist_NoBuildBR_Squad", 2, 1, 1, 0,
                new[] { new ChestEvent(30, "Normal", "B_TieredChest_Athena", false), new ChestEvent(60, "Raro", "B_RareChest", true) })
            { Placement = 3, DurationSeconds = 1092 };
            Check(store.Save(sample, "first.replay|100|1"), "primera insercion");
            Check(!store.Save(sample, "renamed.replay|100|1"), "partida renombrada deduplicada");
            Check(store.HasProcessed("renamed.replay|100|1"), "huella registrada");
            Check(new[] { "Solo", "Duo", "Trio", "Squad" }
                .All(mode => PlaylistRules.IsAllowed("Playlist_NoBuildBR_" + mode)) &&
                PlaylistRules.IsAllowed("playlist_nobuildbr_duo") &&
                !PlaylistRules.IsAllowed("Playlist_DefaultSolo") &&
                !PlaylistRules.IsAllowed("Playlist_NoBuildBR") &&
                !PlaylistRules.IsAllowed("Playlist_NoBuildBR_Squad_Extra"),
                "solo se aceptan cuatro modalidades oficiales de NoBuildBR");
            var excluded = sample with
            {
                SessionId = "ignored-default", Playlist = "Playlist_DefaultSolo",
                Chests = 999, Placement = 1
            };
            Check(!store.Save(excluded, "ignored.replay|999|1") &&
                store.HasProcessed("ignored.replay|999|1"),
                "modo excluido no se guarda y no se reanaliza constantemente");

            var snapshot = store.Snapshot();
            Check(snapshot.Matches[0].ChestEvents.Count == 2 &&
                snapshot.Matches[0].ChestEvents[1].Destroyed,
                "SQLite conserva segundos y cofres destruidos para probabilidades");

            // First confirmed Treasure Hour: 7 Nov 19:00-21:00 UTC, 1.5x.
            var boundary = new MatchSummary("ticket-boundary",
                new DateTime(2026, 11, 7, 18, 59, 0, DateTimeKind.Utc),
                "Jugador", "Playlist_NoBuildBR_Solo", 2, 1, 0, 0)
            {
                ChestEvents = new[]
                {
                    new ChestEvent(0, "Normal", "Normal", false),
                    new ChestEvent(120, "Normal", "Raro", true)
                },
                TeamMembers = new[]
                {
                    new TeamMemberResult("owner", "Jugador", true, 2, 1, 0),
                    new TeamMemberResult("other", "Compañero", false, 2, 0, 0)
                }
            };
            var odds = TicketOdds.Calculate(new TrackerSnapshot(new[] { boundary }),
                new DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero));
            var expectedChance = (1.0 -
                (1.0 - 1.0 / TicketOdds.BaseDenominator) *
                (1.0 - 1.5 / TicketOdds.BaseDenominator)) * 100.0;
            Check(odds.OwnChests == 2 && odds.PartyChests == 4 &&
                odds.BoostedOwnChests == 1 && odds.ProvisionalOwnChests == 0 &&
                odds.ApproximateOwnChests == 0 &&
                Math.Abs(odds.OwnPercent - expectedChance) < 1e-9 &&
                odds.PartyPercent > odds.OwnPercent,
                "cofre destruido incluido, frontera exacta x1.5 y party sin duplicar propietario");
            var secondRound = new MatchSummary("ticket-double",
                new DateTime(2026, 11, 8, 1, 59, 0, DateTimeKind.Utc),
                "Jugador", "Playlist_NoBuildBR_Solo", 1, 1, 0, 0)
            {
                ChestEvents = new[] { new ChestEvent(60, "Normal", "Cofre", true) }
            };
            var doubleOdds = TicketOdds.Calculate(new TrackerSnapshot(new[] { secondRound }),
                new DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero));
            Check(doubleOdds.OwnChests == 1 && doubleOdds.BoostedOwnChests == 1 &&
                Math.Abs(doubleOdds.OwnPercent - 2.0 / TicketOdds.BaseDenominator * 100.0) < 1e-9,
                "segunda sesion del 7/8 noviembre aplica x2 con el segundo exacto");
            var future = new MatchSummary("ticket-predicted",
                new DateTime(2026, 11, 14, 19, 0, 0, DateTimeKind.Utc),
                "Jugador", "Playlist_NoBuildBR_Solo", 1, 0, 0, 0)
            {
                ChestEvents = new[] { new ChestEvent(0, "Normal", "Cofre", false) }
            };
            var forecast = TicketOdds.Calculate(new TrackerSnapshot(new[] { future }),
                new DateTimeOffset(2026, 11, 14, 19, 10, 0, TimeSpan.Zero));
            Check(forecast.ProvisionalOwnChests == 1 && forecast.BoostedOwnChests == 1 &&
                forecast.CurrentMultiplier == 1.5 && forecast.CurrentMultiplierProvisional &&
                forecast.NextHourUtc == new DateTimeOffset(2026, 11, 15, 2, 0, 0, TimeSpan.Zero),
                "horas futuras se calculan como previsiones y se etiquetan, sin inventar confirmacion");
            var legacy = new MatchSummary("ticket-legacy",
                new DateTime(2026, 11, 7, 21, 0, 0, DateTimeKind.Utc),
                "Jugador", "Playlist_NoBuildBR_Solo", 3, 1, 0, 0);
            var legacyChance = TicketOdds.Calculate(new TrackerSnapshot(new[] { legacy }),
                new DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero));
            Check(legacyChance.OwnChests == 3 && legacyChance.ApproximateOwnChests == 3 &&
                legacyChance.BoostedOwnChests == 0,
                "historial sin eventos conserva total y marca la hora como aproximada");
            var obsolete = legacy with { Date = new DateTime(2026, 10, 31, 20, 0, 0, DateTimeKind.Utc) };
            Check(TicketOdds.Calculate(new TrackerSnapshot(new[] { obsolete })).OwnChests == 0,
                "no se mezclan cofres de temporadas previas");
            Check(snapshot.Matches.Count == 1 && snapshot.Chests == 2 && snapshot.Destroyed == 1 &&
                snapshot.SquadChests == 2 && snapshot.Matches[0].Placement == 3,
                "estadisticas solo Zero Build y posicion final guardada");
            Check(snapshot.Matches[0].DurationText == "18:12" &&
                snapshot.Matches[0].EndedUtc == sample.StartedUtc.AddSeconds(1092),
                "duración del replay persistida y fecha fin derivada sin alterar la fecha de inicio");
            var onlineStatus = new MonitorStatus("watching", DateTimeOffset.UtcNow, 2,
                DateTimeOffset.UtcNow.AddMinutes(-5));
            var html = Dashboard.Write(snapshot, dir, onlineStatus);
            Check(File.Exists(html) && File.Exists(Path.Combine(dir, "matches.csv")), "informes");
            var onlineHtml = File.ReadAllText(html);
            Check(onlineHtml.Contains("id=\"monitor-data\"", StringComparison.Ordinal) &&
                onlineHtml.Contains("\"state\":\"watching\"", StringComparison.Ordinal) &&
                onlineHtml.Contains("\"pendingCount\":2", StringComparison.Ordinal) &&
                onlineHtml.Contains("id=\"monitor-indicator\"", StringComparison.Ordinal),
                "estado del monitor integrado en HTML sin perder el resto de estadisticas");
            Check(File.ReadAllText(Path.Combine(dir, "matches.csv")).StartsWith(
                "Fecha inicio;Fecha fin;Duracion;Jugador;Posicion;", StringComparison.Ordinal),
                "CSV con fecha de fin y duración conservando las columnas anteriores");

            // Exercise the actual HTML payload with over 100 matches and hostile replay text.
            // The legacy dashboard silently truncated history after 60 matches.
            var hundreds = new TrackerSnapshot(Enumerable.Range(0, 135)
                .Select(i => new MatchSummary("many-" + i,
                    new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc).AddDays(-i),
                    i == 0 ? "</script><img src=x onerror=alert(1)>" : "Jugador " + i,
                    "Playlist_NoBuildBR_Squad",
                    i % 8, 0, 0, 0)
                {
                    TeamMembers = i == 0 ? new[]
                    {
                        new TeamMemberResult("owner", "</script><img src=x onerror=alert(1)>", true, 0, 0, 0),
                        new TeamMemberResult("mate", "Amiga de escuadra", false, 3, 1, 0),
                        new TeamMemberResult("unknown", "<script>cofre</script>", false, null, null, null)
                    } : Array.Empty<TeamMemberResult>(),
                    Placement = i == 0 ? 1 : null,
                    DurationSeconds = i == 0 ? 1092 : null
                }).ToArray());
            var extendedPath = Dashboard.Write(hundreds, dir,
                new MonitorStatus("watching", DateTimeOffset.UtcNow, 2));
            var extendedHtml = File.ReadAllText(extendedPath);
            Check(!extendedHtml.Contains("data-unit=\"week\"") &&
                  extendedHtml.Contains("data-unit=\"day\" aria-pressed=\"true\"") &&
                  extendedHtml.Contains("data-unit=\"month\"") &&
                  extendedHtml.Contains("id=\"week-chests\""),
                  "grafico diario/mensual y resumen semanal");
            // The favicon and the header logo must be the EXACT Windows EXE ICO.
            var icoPrefix = "data:image/x-icon;base64,";
            var iconLink = "<link rel=\"icon\" type=\"image/x-icon\" href=\"" + icoPrefix;
            var iconAt = extendedHtml.IndexOf(iconLink, StringComparison.Ordinal);
            Check(iconAt >= 0, "favicon integrado en el HTML sin acceso a Internet");
            var base64Start = iconAt + iconLink.Length;
            var base64End = extendedHtml.IndexOf('"', base64Start);
            var base64 = extendedHtml[base64Start..base64End];
            var iconBytes = Convert.FromBase64String(base64);
            Check(iconBytes.Length > 256 && iconBytes[0] == 0 && iconBytes[1] == 0 &&
                iconBytes[2] == 1 && iconBytes[3] == 0,
                "favicon es un archivo .ico Windows válido");
            Check(extendedHtml.Contains("<img src=\"" + icoPrefix + base64 + "\"",
                    StringComparison.Ordinal),
                "icono junto al título coincide exactamente con el icono de la pestaña");
            Check(!extendedHtml.Contains("▣", StringComparison.Ordinal) &&
                !extendedHtml.Contains("Solo Cero construcción BR", StringComparison.Ordinal) &&
                !extendedHtml.Contains("Cofres de la escuadra", StringComparison.Ordinal) &&
                !extendedHtml.Contains("id=\"squad-note\"", StringComparison.Ordinal),
                "cabecera renovada y aviso de escuadra eliminado");

            Check(extendedHtml.Contains("sha256-") && extendedHtml.Contains("history-body"), "dashboard con CSP y paginacion");
            // The browser normalizes all line endings in HTML script blocks to LF.
            // The CSP SHA-256 must match the exact normalized inline script text.
            var inlineOpen = extendedHtml.LastIndexOf("<script>", StringComparison.Ordinal);
            var inlineClose = extendedHtml.IndexOf("</script>", inlineOpen, StringComparison.Ordinal);
            Check(inlineOpen > 0 && inlineClose > inlineOpen, "script interactivo incluido");
            var inlineScript = extendedHtml[(inlineOpen + "<script>".Length)..inlineClose];
            var browserScript = inlineScript.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);
            var browserHash = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(browserScript)));
            Check(extendedHtml.Contains("script-src 'sha256-" + browserHash + "'", StringComparison.Ordinal),
                  "CSP permite ejecutar JavaScript tras normalizacion HTML en Windows");
            var startTag = "<script id=\"match-data\" type=\"application/json\">";
            var start = extendedHtml.IndexOf(startTag, StringComparison.Ordinal);
            Check(start > 0, "datos integrados en el informe");
            start += startTag.Length;
            var end = extendedHtml.IndexOf("</script>", start, StringComparison.Ordinal);
            using var json = System.Text.Json.JsonDocument.Parse(extendedHtml[start..end]);
            Check(json.RootElement.GetArrayLength() == 135 &&
                json.RootElement[0].GetProperty("placement").GetInt32() == 1 &&
                json.RootElement[0].GetProperty("duration").GetString() == "18:12" &&
                json.RootElement[0].GetProperty("party").GetInt32() == 3 &&
                json.RootElement[1].GetProperty("party").GetInt32() == 1 &&
                json.RootElement[0].GetProperty("end").ValueKind == System.Text.Json.JsonValueKind.String &&
                !json.RootElement[0].TryGetProperty("playlist", out _),
                "historial de 135 partidas con posicion y sin columna modo");
            Check(extendedHtml.Contains("Cofres de party") &&
                extendedHtml.Contains("class=\"squad-total\"") &&
                extendedHtml.Contains("<th>Posición</th>") &&
                extendedHtml.Contains("<th>Fecha</th>") &&
                !extendedHtml.Contains("<th>Fecha inicio</th>") &&
                !extendedHtml.Contains("<th>Fecha fin</th>") &&
                extendedHtml.Contains("<th>Duración</th>") &&
                extendedHtml.Contains("id=\"today-party\"") &&
                extendedHtml.Contains("id=\"week-party\"") &&
                extendedHtml.Contains("id=\"month-party\"") &&
                extendedHtml.Contains("id=\"period-party\"") &&
                extendedHtml.Contains("id=\"history-party\"") &&
                extendedHtml.Contains("data-series=\"owner\" aria-pressed=\"true\"") &&
                extendedHtml.Contains("data-series=\"party\" aria-pressed=\"true\""),
                "cabecera con total de escuadra y columna Posicion");
            Check(json.RootElement[0].GetProperty("team").GetArrayLength() == 3 &&
                json.RootElement[0].GetProperty("team")[2].GetProperty("chests").ValueKind ==
                    System.Text.Json.JsonValueKind.Null,
                "historial con tres jugadores, uno sin datos atribuibles");
            Check(json.RootElement[0].GetProperty("player").GetString() == "</script><img src=x onerror=alert(1)>" &&
                  !extendedHtml.Contains("<img src=x onerror=alert(1)>", StringComparison.Ordinal),
                  "nombres de jugadores escapados sin inyeccion HTML");
            Check(File.ReadAllLines(Path.Combine(dir, "matches.csv")).Length == 136, "CSV completo con 135 partidas");
            // A finished replay should be imported automatically after two stable
            // scans, rather than waiting for a restart or invoking --once.
            var replayFolder = Path.Combine(dir, "automatic-watch");
            Directory.CreateDirectory(replayFolder);
            var simulatedReplay = Path.Combine(replayFolder, "new-match.replay");
            void WriteFakeReplay(bool isLive, bool keyMissing)
            {
                using var stream = new FileStream(simulatedReplay, FileMode.Create,
                    FileAccess.Write, FileShare.ReadWrite);
                using var writer = new BinaryWriter(stream);
                writer.Write(0x1CA2E27Fu); // Fortnite replay header magic
                writer.Write(6u);          // version before replay version 7's custom versions
                writer.Write(900000u);     // 15-minute replay length in milliseconds
                writer.Write(0u);          // network version
                writer.Write(0u);          // changelist
                writer.Write(0);           // empty friendly name
                writer.Write(isLive ? 1u : 0u);
                writer.Write(0L);          // timestamp
                writer.Write(0u);          // compressed
                writer.Write(1u);          // encrypted
                writer.Write(4);           // key length
                writer.Write(keyMissing ? new byte[4] : new byte[] { 1, 2, 3, 4 });
            }

            var analyzeCalls = 0;
            var watch = new ReplayScanner(store, replayFolder, dir, file =>
            {
                analyzeCalls++;
                return sample with { SessionId = "auto-" + Path.GetFileNameWithoutExtension(file) };
            });
            WriteFakeReplay(isLive: true, keyMissing: true);
            File.SetLastWriteTimeUtc(simulatedReplay, DateTime.UtcNow.AddMinutes(-5));
            Check(ReplayProbe.Inspect(simulatedReplay) == (true, true),
                "cabecera de replay activo sin clave");
            Check(watch.Scan(once: false) == 0 && analyzeCalls == 0 && watch.PendingCount == 1,
                "no contabilizar partida mientras Fortnite sigue grabando");

            WriteFakeReplay(isLive: false, keyMissing: false);
            File.SetLastWriteTimeUtc(simulatedReplay, DateTime.UtcNow.AddSeconds(-10));
            Check(ReplayProbe.Inspect(simulatedReplay) == (false, false),
                "cabecera de replay finalizado con clave");
            Check(ReplayProbe.ReadDurationSeconds(simulatedReplay) == 900,
                "duración real del encabezado sin analizar cofres");
            Check(watch.Scan(once: false) == 0 && analyzeCalls == 0,
                "primer chequeo de estabilidad sin análisis prematuro");
            Check(watch.Scan(once: false) == 1 && analyzeCalls == 1,
                "importación automática al segundo chequeo");
            Check(watch.Scan(once: false) == 0 && analyzeCalls == 1,
                "una repetición finalizada no se importa dos veces");
            Check(store.Snapshot().Matches.Count == 2,
                "nueva partida persistida en SQLite sin perder el histórico");
            // A historic replay can restore its duration without re-decoding events.
            var olderReplay = Path.Combine(replayFolder, "historic-duration.replay");
            File.Copy(simulatedReplay, olderReplay);
            var olderInfo = new FileInfo(olderReplay);
            var olderFingerprint = $"{olderInfo.Name}|{olderInfo.Length}|{olderInfo.LastWriteTimeUtc.Ticks}";
            Check(store.Save(sample with { SessionId = "historic-duration", DurationSeconds = null },
                olderFingerprint), "partida antigua guardada sin duración");
            Check(store.BackfillDurations(replayFolder) == 1 &&
                store.Snapshot().Matches.Single(m => m.SessionId == "historic-duration").DurationText == "15:00" &&
                store.BackfillDurations(replayFolder) == 0,
                "migración de duración de partidas antiguas sin duplicados");

            // Some files retain the live bit even after the real decryption key is set.
            var liveWithKey = Path.Combine(replayFolder, "stale-live.replay");
            WriteFakeReplay(isLive: true, keyMissing: false);
            File.Move(simulatedReplay, liveWithKey);
            File.SetLastWriteTimeUtc(liveWithKey, DateTime.UtcNow.AddMinutes(-1));
            Check(watch.Scan(once: false) == 0, "replay con marca live necesita estabilidad");
            Check(watch.Scan(once: false) == 1 && analyzeCalls == 2,
                "replay con marca live y clave real no se ignora para siempre");

            Check(ReplayScanner.WaitingReason((true, true),
                      TimeSpan.FromHours(1), stable: true, once: false) != null,
                  "no leer partidas cifradas sin clave");
            Check(ReplayScanner.WaitingReason((false, false),
                      TimeSpan.FromSeconds(8), stable: true, once: false) == null,
                  "replay finalizado listo tras periodo breve");
            Check(ReplayScanner.WaitingReason((null, true),
                      TimeSpan.FromMinutes(3), stable: true, once: false) == null,
                  "cabezera desconocida se intenta leer tras estabilidad");

            // The replay parser returns Fortnite UTC ticks with Unspecified kind.
            // In the old version, ToUniversalTime() subtracted the local zone offset.
            var madrid = TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");
            var winterReplayClock = new DateTime(2026, 11, 10, 22, 45, 0, DateTimeKind.Unspecified);
            var winterUtc = MatchTimes.FromReplayUtc(winterReplayClock);
            Check(winterUtc.Kind == DateTimeKind.Utc &&
                TimeZoneInfo.ConvertTimeFromUtc(winterUtc, madrid).Hour == 23,
                "fecha de inicio de invierno: 22:45 UTC = 23:45 en España");
            var summerReplayClock = new DateTime(2026, 7, 10, 21, 45, 0, DateTimeKind.Unspecified);
            var summerUtc = MatchTimes.FromReplayUtc(summerReplayClock);
            Check(TimeZoneInfo.ConvertTimeFromUtc(summerUtc, madrid).Hour == 23,
                "fecha de inicio de verano: 21:45 UTC = 23:45 en España");

            // Simulate a DB created by 0.2.3, when existing games were
            // incorrectly transformed as if the replay's UTC ticks were local.
            var legacyFolder = Path.Combine(dir, "old-version");
            var oldStore = new TrackerStore(legacyFolder);
            var wrongWinterUtc = TimeZoneInfo.ConvertTimeToUtc(winterReplayClock, madrid);
            var wrongSummerUtc = TimeZoneInfo.ConvertTimeToUtc(summerReplayClock, madrid);
            Check(oldStore.Save(sample with {
                SessionId = "legacy-winter", StartedUtc = wrongWinterUtc
            }, "winter-fp"), "crear partida antigua de invierno");
            Check(oldStore.Save(sample with {
                SessionId = "legacy-summer", StartedUtc = wrongSummerUtc
            }, "summer-fp"), "crear partida antigua de verano");
            var dbPath = Path.Combine(legacyFolder, "tracker.db");
            using (var sql = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + dbPath))
            {
                sql.Open();
                using var reset = sql.CreateCommand();
                reset.CommandText = "DELETE FROM schema_migrations WHERE name='replay_utc_kind_v1'";
                reset.ExecuteNonQuery();
            }

            var updatedStore = new TrackerStore(legacyFolder, madrid);
            var upgraded = updatedStore.Snapshot().Matches;
            Check(upgraded.Count == 2 &&
                upgraded.Single(m => m.SessionId == "legacy-winter").Date == winterUtc &&
                upgraded.Single(m => m.SessionId == "legacy-summer").Date == summerUtc,
                "migración automática de horarios de invierno y verano");
            using (var sql = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + dbPath))
            {
                sql.Open();
                using var backup = sql.CreateCommand();
                backup.CommandText = "SELECT COUNT(*) FROM legacy_start_times_backup";
                Check(Convert.ToInt64(backup.ExecuteScalar()) == 2,
                    "fechas originales conservadas en tabla de copia de seguridad");
            }
            var afterRestart = new TrackerStore(legacyFolder, madrid).Snapshot().Matches;
            Check(afterRestart.Single(m => m.SessionId == "legacy-winter").Date == winterUtc,
                "la corrección no se aplica una segunda vez");
            Check(updatedStore.Save(sample with {
                SessionId = "correct-future", StartedUtc = winterUtc
            }, "future-fp") &&
                new TrackerStore(legacyFolder, madrid).Snapshot().Matches
                    .Single(m => m.SessionId == "correct-future").Date == winterUtc,
                "las nuevas partidas correctamente fechadas no se modifican");
            Check(extendedHtml.Contains("<th>Fecha</th>") &&
                !extendedHtml.Contains("<th>Fecha fin</th>") &&
                File.ReadLines(Path.Combine(dir, "matches.csv")).First().StartsWith("Fecha inicio;Fecha fin;Duracion;Jugador;Posicion;"),
                "una columna Fecha en HTML y exportacion CSV intacta");

            // The in-game TeamIndex, not the social lobby, identifies squads.
            // Enemy searches must never be included.
            var ownerCandidate = new SquadCandidate(10, 3, "player:10", "Yo");
            var roster = new[]
            {
                ownerCandidate,
                new SquadCandidate(11, 3, "player:11", "Amiga"),
                new SquadCandidate(12, 3, "player:12", "Compañero"),
                new SquadCandidate(14, 3, "player:14", "Sin relacion Pawn"),
                new SquadCandidate(13, 7, "player:13", "Enemigo")
            };
            var pawnToState = new Dictionary<uint, uint>
            {
                [101] = 1001, [102] = 1002, [103] = 1003, [105] = 1005
            };
            var stateToPlayer = new Dictionary<uint, int>
            {
                [1001] = 10, [1002] = 11, [1003] = 12, [1005] = 13
            };
            var observations = new[]
            {
                new SquadOpening(101, "Normal", false), // own chest
                new SquadOpening(102, "Normal", false),
                new SquadOpening(102, "Raro", true),
                new SquadOpening(102, "Municion", false),
                new SquadOpening(105, "Raro", false), // enemy, never a teammate
                new SquadOpening(null, "Normal", false)
            };
            var mates = SquadAttribution.DetectTeammates(
                ownerCandidate, roster, pawnToState, stateToPlayer, observations);
            Check(mates.Count == 3 && mates.All(m => m.PlayerName != "Enemigo"),
                "solo compañeros con TeamIndex del propietario");
            Check(mates.Single(m => m.PlayerName == "Amiga").Chests == 2 &&
                mates.Single(m => m.PlayerName == "Amiga").Destroyed == 1 &&
                mates.Single(m => m.PlayerName == "Amiga").AmmoBoxes == 1,
                "cofres, destruidos y munición atribuibles a compañera");
            Check(mates.Single(m => m.PlayerName == "Compañero").Chests == 0 &&
                mates.Single(m => m.PlayerName == "Sin relacion Pawn").Chests == null,
                "cero cofres observados distinto de datos desconocidos");
            Check(SquadAttribution.DetectTeammates(
                ownerCandidate with { TeamIndex = null }, roster,
                pawnToState, stateToPlayer, observations).Count == 0,
                "sin TeamIndex no adivinamos los compañeros");

            // Save squad observations in SQLite, then simulate a 0.2.5 match and
            // backfill only the team fields without modifying personal counters.
            var personalBefore = store.Snapshot().Chests;
            var squadMatch = sample with
            {
                SessionId = "squad-fixture",
                Placement = 1,
                TeamMembers = new[]
                {
                    new TeamMemberResult("owner", "Yo", true, 2, 1, 1),
                    new TeamMemberResult("player:11", "Amiga", false, 3, 0, 2),
                    new TeamMemberResult("player:14", "Sin datos", false, null, null, null)
                }
            };
            Check(store.Save(squadMatch, "squad-fixture.replay|123|1"), "guardar escuadra");
            var savedSquad = store.Snapshot().Matches.Single(m => m.SessionId == "squad-fixture");
            Check(savedSquad.TeamMembers.Count == 3 &&
                savedSquad.TeamMembers.Single(m => m.PlayerName == "Sin datos").Chests == null &&
                savedSquad.Placement == 1 && store.Snapshot().SquadChests == store.Snapshot().Chests + 3,
                "posicion y total de escuadra cuentan al propietario solo una vez");
            Check(store.SquadBackfillSession("squad-fixture.replay|123|1") == null,
                "partidas nuevas no se reprocesan");
            using (var db = new Microsoft.Data.Sqlite.SqliteConnection(
                "Data Source=" + Path.Combine(dir, "tracker.db")))
            {
                db.Open();
                using var clear = db.CreateCommand();
                clear.CommandText = """
                    DELETE FROM squad_scans WHERE session_id='squad-fixture';
                    DELETE FROM squad_players WHERE session_id='squad-fixture';
                    """;
                clear.ExecuteNonQuery();
            }
            Check(store.SquadBackfillSession("squad-fixture.replay|123|1") == "squad-fixture",
                "detectar partida histórica sin desglose");
            Check(store.SaveSquad(squadMatch), "recuperar jugadores antiguos");
            Check(!store.SaveSquad(squadMatch), "no duplicar desglose ya recuperado");
            var afterSquad = store.Snapshot();
            Check(afterSquad.Chests == personalBefore + squadMatch.Chests &&
                afterSquad.SquadChests == afterSquad.Chests + 3 &&
                afterSquad.Matches.Single(m => m.SessionId == "squad-fixture").TeamMembers.Count == 3,
                "recuperación mantiene intactos totales personales, escuadra y partidas");

            // Simulate a historic database containing an old unsupported mode.
            // Never delete those records; Snapshot and dashboard must exclude
            // them even if previous releases had already imported them.
            using (var oldDb = new Microsoft.Data.Sqlite.SqliteConnection(
                "Data Source=" + Path.Combine(dir, "tracker.db")))
            {
                oldDb.Open();
                using var old = oldDb.CreateCommand();
                old.CommandText = """
                    INSERT INTO matches(session_id,started_utc,player,playlist,chests,destroyed,ammo_boxes,other_containers,placement)
                    VALUES('old-mode', '2026-11-10T22:45:00.0000000Z', 'OtroModo', 'Playlist_DefaultDuo', 999, 0, 0, 0, 1)
                    """;
                old.ExecuteNonQuery();
            }
            var filtered = store.Snapshot();
            Check(filtered.Matches.All(m => PlaylistRules.IsAllowed(m.Playlist)) &&
                filtered.Matches.All(m => m.SessionId != "old-mode") &&
                filtered.SquadChests == filtered.Chests + 3,
                "modos de versiones antiguas excluidos de todos los totales e historial");

            // Dashboard and CSV must reject non-NoBuildBR even when passed an
            // unfiltered caller-provided snapshot.
            var filteredFolder = Path.Combine(dir, "filtered-report");
            var mixed = new TrackerSnapshot(new[]
            {
                new MatchSummary("allowed", sample.StartedUtc, "Yo", "Playlist_NoBuildBR_Squad", 4, 0, 0, 0)
                { Placement = 5 },
                new MatchSummary("excluded", sample.StartedUtc, "Otro", "Playlist_DefaultDuo", 500, 0, 0, 0)
                { Placement = 1 }
            });
            var htmlFiltered = File.ReadAllText(Dashboard.Write(mixed, filteredFolder));
            Check(!htmlFiltered.Contains("\"chests\":500", StringComparison.Ordinal) &&
                !htmlFiltered.Contains("\"player\":\"Otro\"", StringComparison.Ordinal) &&
                File.ReadAllLines(Path.Combine(filteredFolder, "matches.csv")).Length == 2 &&
                !File.ReadAllText(Path.Combine(filteredFolder, "matches.csv"))
                    .Contains("\"Otro\"", StringComparison.Ordinal),
                "HTML, CSV y estadisticas no contienen ninguna partida excluida");

            // Upgrade an existing 0.3.0 database without destroying history
            // when its matches table did not yet have the placement column.
            var prePositionDir = Path.Combine(dir, "prior-schema");
            Directory.CreateDirectory(prePositionDir);
            using (var beforeUpgrade = new Microsoft.Data.Sqlite.SqliteConnection(
                "Data Source=" + Path.Combine(prePositionDir, "tracker.db")))
            {
                beforeUpgrade.Open();
                using var create = beforeUpgrade.CreateCommand();
                create.CommandText = """
                    CREATE TABLE matches (
                       session_id TEXT PRIMARY KEY, started_utc TEXT NOT NULL,
                       player TEXT NOT NULL, epic_id TEXT, playlist TEXT NOT NULL,
                       chests INTEGER NOT NULL, destroyed INTEGER NOT NULL,
                       ammo_boxes INTEGER NOT NULL, other_containers INTEGER NOT NULL
                    )
                    """;
                create.ExecuteNonQuery();
            }
            var upgradedPositionStore = new TrackerStore(prePositionDir);
            Check(upgradedPositionStore.Save(sample with { SessionId = "position-upgrade" }, "pos-fp") &&
                upgradedPositionStore.Snapshot().Matches.Single().Placement == 3 &&
                upgradedPositionStore.Snapshot().Matches.Single().DurationText == "18:12",
                "migracion de SQLite añade posicion sin perder compatibilidad");

            // CI saves this self-contained report to test the actual HTML in Microsoft Edge.
            var browserFixture = Environment.GetEnvironmentVariable("FCT_DASHBOARD_TEST_OUTPUT");
            if (!string.IsNullOrWhiteSpace(browserFixture))
                File.Copy(extendedPath, browserFixture, overwrite: true);
            Console.WriteLine("Todas las pruebas internas han pasado.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("Falló una prueba: " + ex); return 1; }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        Console.WriteLine("OK " + name);
    }
}
