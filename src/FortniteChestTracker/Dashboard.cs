using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;

namespace FortniteChestTracker;

public static class Dashboard
{
    public static string Write(TrackerSnapshot snapshot, string dataFolder)
    {
        Directory.CreateDirectory(dataFolder);
        var destination = Path.Combine(dataFolder, "dashboard.html");
        // Defence in depth: never include other playlists even if callers
        // supply an unfiltered snapshot (old/imported/test records).
        var filtered = new TrackerSnapshot(snapshot.Matches
            .Where(m => PlaylistRules.IsAllowed(m.Playlist)).ToArray());
        var html = Render(filtered);
        var temp = destination + ".tmp";
        File.WriteAllText(temp, html, new UTF8Encoding(false));
        File.Move(temp, destination, overwrite: true);
        ExportCsv(filtered, Path.Combine(dataFolder, "matches.csv"));
        return destination;
    }

    public static void OpenBrowser(string file)
    {
        try { Process.Start(new ProcessStartInfo(file) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        { Console.Error.WriteLine($"Abre manualmente el informe: {file} ({ex.Message})"); }
    }

    private static void ExportCsv(TrackerSnapshot snapshot, string path)
    {
        static string Cell(string s)
        {
            // Evitar inyeccion de formulas al abrir el CSV con Excel.
            if (s.Length > 0 && "=+-@\t\r".Contains(s[0])) s = "'" + s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
        var sb = new StringBuilder("Fecha inicio;Fecha fin;Duracion;Jugador;Posicion;Cofres;Destruidos;Municion;Otros;IdPartida\r\n");
        foreach (var m in snapshot.Matches)
            sb.AppendLine(string.Join(';', Cell(m.Date.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                Cell(m.EndedUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? ""),
                Cell(m.DurationSeconds is > 0 ? m.DurationText : ""),
                Cell(m.Player), m.Placement?.ToString(CultureInfo.InvariantCulture) ?? "", m.Chests.ToString(CultureInfo.InvariantCulture), m.Destroyed.ToString(CultureInfo.InvariantCulture), m.AmmoBoxes.ToString(CultureInfo.InvariantCulture), m.OtherContainers.ToString(CultureInfo.InvariantCulture), Cell(m.SessionId)));
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }

    private static string Render(TrackerSnapshot data)
    {
        // All statistics and the history are local. The JS source is embedded in the EXE,
        // hashed into a restrictive CSP, and receives untrusted replay fields only as JSON.
        // HTML parsing normalizes CRLF/CR to LF. Without canonicalizing the
        // embedded JS before hashing, Windows builds produce a CSP hash that
        // does not match the script text actually seen by Chromium.
        var script = ReadScript().Replace("\r\n", "\n", StringComparison.Ordinal)
                                 .Replace("\r", "\n", StringComparison.Ordinal);
        var hash = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(script)));
        // This is the SAME ICO resource used as the Windows executable icon.
        // A data URL keeps both favicon and title logo working in a standalone,
        // offline file:// dashboard (without external assets or network calls).
        var iconDataUrl = ReadIconDataUrl();
        var payload = System.Text.Json.JsonSerializer.Serialize(
            data.Matches.Select(m => new
            {
                date = m.Date.ToLocalTime().ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
                end = m.EndedUtc?.ToLocalTime().ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
                duration = m.DurationText,
                player = m.Player,
                placement = m.Placement,
                chests = m.Chests,
                party = m.Chests + m.TeamMembers.Where(t => !t.IsOwner).Sum(t => t.Chests ?? 0),
                destroyed = m.Destroyed,
                ammo = m.AmmoBoxes,
                team = m.TeamMembers.Select(p => new
                {
                    name = p.PlayerName,
                    owner = p.IsOwner,
                    chests = p.Chests,
                    destroyed = p.Destroyed,
                    ammo = p.AmmoBoxes
                }).ToArray()
            }),
            new System.Text.Json.JsonSerializerOptions
            {
                // Escapes <, >, &, quotes and other HTML-sensitive characters.
                // A replay player name can never close the application/json script tag.
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Default
            });

        var sb = new StringBuilder();
        sb.Append($$"""
            <!doctype html>
            <html lang="es"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta http-equiv="refresh" content="20">
            <meta http-equiv="Content-Security-Policy"
                  content="default-src 'none'; style-src 'unsafe-inline'; img-src data:; script-src 'sha256-{{hash}}'">
            <title>Fortnite ChestTracker · Estadísticas</title>
            <link rel="icon" type="image/x-icon" href="{{iconDataUrl}}">
            <style>
            :root { color-scheme: dark; font-family: Inter, Segoe UI, Arial, sans-serif; background: #090e1c; color: #f5f6ff; }
            * { box-sizing: border-box; }
            body { margin: 0; background: radial-gradient(ellipse at 12% -8%, #26346970 0%, transparent 45%), #090e1c; min-height: 100vh; }
            .shell { max-width: 1200px; margin: 0 auto; padding: 32px 24px 80px; }
            .top, .section-head, .toolbar, .pagination, .period-summary { display: flex; justify-content: space-between; align-items: center; gap: 16px; flex-wrap: wrap; }
            .top { margin-bottom: 25px; }
            .brand { display: flex; align-items: center; gap: 14px; }
            .logo { display: grid; place-items: center; width: 53px; height: 53px; flex: 0 0 53px; }
            .logo img { display: block; width: 100%; height: 100%; object-fit: contain; filter: drop-shadow(0 6px 14px #0008); }
            h1 { font-size: 24px; letter-spacing: -.6px; margin: 0; }
            .subtitle, .secondary { color: #99a8c3; font-size: 13px; margin: 6px 0 0; }
            .pill { border: 1px solid #314569; background: #13213b; color: #a0f3d2; padding: 9px 13px; border-radius: 99px; font-size: 12px; }
            .hero { background: linear-gradient(110deg, #283b81, #18254a 55%, #141f39); border: 1px solid #435a9b; border-radius: 23px; padding: 30px; margin-bottom: 18px; position: relative; overflow: hidden; }
            .hero-grid { display: flex; align-items: center; justify-content: space-between; gap: 28px; flex-wrap: wrap; position: relative; z-index: 1; }
            .hero-primary { flex: 1 1 280px; }
            .hero-squad { flex: 0 1 290px; border-left: 1px solid #6074ab77; padding: 15px 0 15px 27px; }
            .squad-total { font-size: 44px; font-weight: 750; letter-spacing: -1px; line-height: 1.12; color: #cde0ff; }
            .squad-note { color: #b1c5e4; font-size: 12px; margin: 9px 0 0; max-width: 280px; line-height: 1.5; }
            .hero:after { content: '✦'; position: absolute; right: 6%; top: -45px; font-size: 220px; color: #ffffff0a; pointer-events: none; }
            .eyebrow { font-size: 12px; letter-spacing: 2px; text-transform: uppercase; color: #d2d8fa; margin: 0 0 8px; }
            .total { font-size: 72px; letter-spacing: -3px; line-height: 1.05; font-weight: 800; color: #ffda7b; position: relative; z-index: 1; }
            .total span { font-size: 20px; color: #d8e1ff; letter-spacing: 0; font-weight: 500; }
            .hint { font-size: 13px; color: #bec9e8; margin-top: 10px; }
            .metrics, .period-metrics { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 13px; margin-bottom: 20px; }
            .period-metrics { grid-template-columns: repeat(3, minmax(0, 1fr)); }
            .metric, .panel, .period-metric { border: 1px solid #283854; background: #111b30; border-radius: 18px; padding: 20px; }
            .metric .label, .period-metric .label { font-size: 12px; color: #92a4c0; text-transform: uppercase; letter-spacing: .8px; }
            .metric .num, .period-metric .num { font-size: 31px; font-weight: 750; margin-top: 10px; }
            .metric .small, .period-metric .small { font-size: 12px; color: #8498b7; margin-top: 6px; }
            .period-metric .num { color: #f9d47b; }
            .period-metric .party-count { display: flex; align-items: baseline; gap: 8px; margin-top: 12px; padding-top: 10px; border-top: 1px solid #30415f; color: #9fc8ff; font-size: 13px; flex-wrap: wrap; }
            .period-metric .party-count strong { color: #bbd9ff; font-size: 18px; }
            .panel { margin-top: 20px; }
            h2 { font-size: 19px; letter-spacing: -.3px; margin: 0; }
            .toolbar { justify-content: flex-start; }
            .control { background: #101a2e; color: #dce6f9; border: 1px solid #3c4d68; border-radius: 10px; padding: 10px 12px; font: inherit; font-size: 13px; }
            .control:focus-visible, button:focus-visible { outline: 2px solid #f7ca56; outline-offset: 2px; }
            .segment { display: flex; border: 1px solid #3c4d68; padding: 3px; border-radius: 12px; background: #0a1427; gap: 2px; }
            .segment button, .button { cursor: pointer; font: inherit; font-size: 13px; color: #bdcce6; background: transparent; border: 0; border-radius: 9px; padding: 9px 15px; }
            .segment button.active { background: #355298; color: #fff; }
            .button { background: #1d3053; border: 1px solid #364e79; }
            .button:hover:not(:disabled), .segment button:hover:not(.active) { background: #29426d; color: #fff; }
            button:disabled { opacity: .38; cursor: not-allowed; }
            .period-summary { margin-top: 17px; justify-content: flex-start; color: #94abc9; font-size: 12px; gap: 16px; }
            .period-summary strong { color: #ffda7b; font-size: 16px; }
            .chart-scroll { overflow-x: auto; margin-top: 22px; padding-bottom: 4px; }
            .chart { display: flex; height: 212px; gap: 8px; min-width: 560px; border-bottom: 1px solid #354463; }
            .chart-bar { flex: 1 1 0; min-width: 0; display: flex; flex-direction: column; align-items: center; gap: 7px; justify-content: flex-end; border: 1px solid transparent; border-radius: 9px 9px 0 0; color: #9aacc8; padding: 8px 2px 0; background: transparent; cursor: pointer; font: inherit; }
            .chart-bar:hover, .chart-bar.selected { background: #223a62; border-color: #4a6699; }
            .chart-value { font-size: 12px; color: #f6d58a; font-weight: 650; }
            .chart-rail { width: 100%; height: 145px; display: flex; align-items: flex-end; justify-content: center; gap: 4px; }
            .chart-fill { display: block; width: 44%; max-width: 26px; min-height: 0; border-radius: 6px 6px 0 0; background: linear-gradient(0deg, #a87924, #f6d889); }
            .chart-fill-party { background: linear-gradient(0deg, #3463a5, #b0d8ff); }
            .chart-legend { display: flex; align-items: center; flex-wrap: wrap; gap: 7px; }
            .legend-toggle { display: inline-flex; align-items: center; gap: 7px; padding: 6px 10px; border: 1px solid #415573; border-radius: 10px; background: #172744; color: #c8d6ed; cursor: pointer; font: inherit; font-size: 12px; }
            .legend-toggle.owner span { color: #f6d889; }
            .legend-toggle.party span { color: #b0d8ff; }
            .legend-toggle[aria-pressed="false"] { opacity: .48; text-decoration: line-through; }
            .legend-toggle:hover { background: #263e65; }
            .chart-rail .chart-fill:only-child { width: 70%; max-width: 42px; }
            .chart-label { font-size: 11px; white-space: nowrap; padding-bottom: 6px; }
            .tablewrap { overflow-x: auto; margin-top: 17px; }
            table { width: 100%; border-collapse: collapse; white-space: nowrap; text-align: left; font-size: 13px; }
            th { color: #8496b5; font-size: 11px; text-transform: uppercase; letter-spacing: .9px; padding: 11px 9px; border-bottom: 1px solid #31415e; }
            td { padding: 13px 9px; border-bottom: 1px solid #22304a; color: #cfd9ef; }
            .emphasis { font-weight: 750; color: #ffd46b; }
            .muted { color: #8fa0bc; }
            .squad-mate td { background: #142039; }
            .squad-mate td.player-cell { padding-left: 22px; }
            .squad-owner td.player-cell { color: #f4d47c; font-weight: 700; }
            .squad-owner td.player-cell::after { content: " · Tú"; color: #8fb2e6; font-weight: 500; font-size: 11px; }
            .group-start td { border-top: 1px solid #385073; }
            td[rowspan] { vertical-align: top; padding-top: 14px; }
            .date-cell .date-start { color: #dce6f9; }
            .date-cell .date-end { color: #91a6c9; font-size: 12px; margin-top: 5px; }
            .unobserved { color: #758ba8; }

            .empty { padding: 25px 0; color: #9cacca; }
            .pagination { justify-content: flex-end; margin-top: 17px; }
            .pagination .secondary { margin: 0; }
            .footer { color: #7789a5; font-size: 12px; text-align: center; margin-top: 30px; }

            /* Themes share the same layout and data. Only presentation changes. */
            :root {
                --page: #090e1c; --glow: #26346970; --text: #f5f6ff; --muted: #99a8c3;
                --surface: #111b30; --line: #283854; --accent: #ffda7b; --team: #b0d8ff;
                --hero-a: #283b81; --hero-b: #18254a; --hero-c: #141f39; --hero-line: #435a9b;
                --hero-note: #b1c5e4; --hero-label: #d2d8fa; --hero-ornament: #ffffff0a;
                --control-bg: #101a2e; --control-border: #3c4d68; --control-text: #dce6f9;
                --button-bg: #1d3053; --button-border: #364e79; --selected: #355298;
                --hover: #29426d; --chart-hover: #223a62; --owner-bottom: #a87924;
                --owner-top: #f6d889; --team-bottom: #3463a5; --team-top: #b0d8ff;
                --row-alt: #142039; --table-rule: #22304a; --radius: 18px; --hero-radius: 23px;
            }
            html[data-theme="tactical"] {
                --page: #091313; --glow: #1a4b3b66; --text: #effff6; --muted: #92b5ad;
                --surface: #112320; --line: #2b5148; --accent: #b8f47f; --team: #7be4da;
                --hero-a: #20483b; --hero-b: #17352e; --hero-c: #102821; --hero-line: #487e64;
                --hero-note: #badad1; --hero-label: #b5eac7; --hero-ornament: #b8f47f12;
                --control-bg: #0c211d; --control-border: #37685b; --control-text: #e7fff3;
                --button-bg: #1b3931; --button-border: #427a66; --selected: #346e50;
                --hover: #285846; --chart-hover: #1e463b; --owner-bottom: #4b8b38;
                --owner-top: #b8f47f; --team-bottom: #2c8384; --team-top: #7be4da;
                --row-alt: #18332b; --table-rule: #28443e; --radius: 9px; --hero-radius: 12px;
            }
            html[data-theme="studio"] {
                color-scheme: light;
                --page: #f6f3ed; --glow: #eccdac88; --text: #243047; --muted: #67758a;
                --surface: #fffcf8; --line: #e3dcd3; --accent: #c75c36; --team: #477fc7;
                --hero-a: #fff0da; --hero-b: #fff7ec; --hero-c: #fffcf6; --hero-line: #eed0ac;
                --hero-note: #736b66; --hero-label: #81513c; --hero-ornament: #ca9a5a16;
                --control-bg: #fffefa; --control-border: #d6cfc7; --control-text: #243047;
                --button-bg: #f9eee2; --button-border: #dbcbb8; --selected: #d76c45;
                --hover: #f1e3d5; --chart-hover: #f7e9d7; --owner-bottom: #b9492e;
                --owner-top: #e8a078; --team-bottom: #507ab4; --team-top: #93b9e5;
                --row-alt: #f8f1e8; --table-rule: #eae2d8; --radius: 16px; --hero-radius: 20px;
            }
            html[data-theme="prism"] {
                --page: #101027; --glow: #58368f66; --text: #f8f3ff; --muted: #b0a6ca;
                --surface: #1d1b3b; --line: #423b66; --accent: #d4afff; --team: #88dcf3;
                --hero-a: #50377c; --hero-b: #302552; --hero-c: #1c204b; --hero-line: #775ca6;
                --hero-note: #d4c6ee; --hero-label: #e3d2ff; --hero-ornament: #e0c9ff12;
                --control-bg: #211d3e; --control-border: #5c527d; --control-text: #f0e7ff;
                --button-bg: #353057; --button-border: #65588b; --selected: #674ba1;
                --hover: #49416d; --chart-hover: #38315d; --owner-bottom: #7f56a9;
                --owner-top: #d4afff; --team-bottom: #347ca0; --team-top: #88dcf3;
                --row-alt: #272346; --table-rule: #383457; --radius: 20px; --hero-radius: 26px;
            }
            html { background: var(--page); color: var(--text); }
            body { background: radial-gradient(ellipse at 12% -8%, var(--glow) 0%, transparent 47%), var(--page); }
            .subtitle, .secondary, .period-summary, .metric .label, .period-metric .label,
            .metric .small, .period-metric .small, th, .muted, .unobserved, .empty,
            .footer, .chart-label, .chart-bar, .date-cell .date-end { color: var(--muted); }
            h1 span, .total, .period-metric .num, .period-summary strong, .chart-value,
            .emphasis, .squad-owner td.player-cell, .legend-toggle.owner span { color: var(--accent); }
            .hero { background: linear-gradient(110deg, var(--hero-a), var(--hero-b) 55%, var(--hero-c));
                border-color: var(--hero-line); border-radius: var(--hero-radius); }
            .hero:after { color: var(--hero-ornament); }
            .hero-squad { border-color: var(--hero-line); }
            .eyebrow { color: var(--hero-label); }
            .squad-total, .period-metric .party-count strong { color: var(--team); }
            .squad-note, .hint, .total span { color: var(--hero-note); }
            .metric, .panel, .period-metric { background: var(--surface);
                border-color: var(--line); border-radius: var(--radius); }
            .period-metric .party-count { border-color: var(--line); color: var(--muted); }
            .control, .theme-select { background: var(--control-bg); color: var(--control-text);
                border-color: var(--control-border); }
            .control:focus-visible, button:focus-visible, .theme-select:focus-visible {
                outline-color: var(--accent); }
            .segment { background: var(--control-bg); border-color: var(--control-border); }
            .segment button, .button { color: var(--text); }
            .segment button.active { background: var(--selected); color: #fff; }
            .button { background: var(--button-bg); border-color: var(--button-border); }
            .button:hover:not(:disabled), .segment button:hover:not(.active) {
                background: var(--hover); color: var(--text); }
            .chart { border-color: var(--line); }
            .chart-bar:hover, .chart-bar.selected { background: var(--chart-hover); border-color: var(--control-border); }
            .chart-fill { background: linear-gradient(0deg, var(--owner-bottom), var(--owner-top)); }
            .chart-fill-party { background: linear-gradient(0deg, var(--team-bottom), var(--team-top)); }
            .legend-toggle { background: var(--button-bg); color: var(--text); border-color: var(--button-border); }
            .legend-toggle.owner span { color: var(--accent); }
            .legend-toggle.party span { color: var(--team); }
            .legend-toggle:hover { background: var(--hover); }
            th { border-color: var(--line); }
            td { color: var(--text); border-color: var(--table-rule); }
            .squad-mate td { background: var(--row-alt); }
            .squad-owner td.player-cell::after { color: var(--team); }
            .group-start td { border-color: var(--line); }
            .date-cell .date-start { color: var(--text); }
            .theme-picker { display: flex; align-items: center; gap: 10px; }
            .theme-picker label { color: var(--muted); font-size: 13px; }
            .theme-select { border: 1px solid var(--control-border); border-radius: 10px;
                padding: 10px 34px 10px 12px; font: inherit; font-size: 13px; cursor: pointer; }
            html[data-theme="tactical"] .metric, html[data-theme="tactical"] .period-metric,
            html[data-theme="tactical"] .panel { box-shadow: inset 3px 0 0 #b8f47f18; }
            html[data-theme="tactical"] .hero { border-left: 4px solid var(--accent); }
            html[data-theme="tactical"] .total, html[data-theme="tactical"] .metric .num {
                font-variant-numeric: tabular-nums; }
            html[data-theme="studio"] .hero, html[data-theme="studio"] .panel,
            html[data-theme="studio"] .metric, html[data-theme="studio"] .period-metric {
                box-shadow: 0 9px 28px #6b543412; }
            html[data-theme="studio"] .segment button.active { color: #fff; }
            html[data-theme="studio"] .chart-value { color: #9f472e; }
            html[data-theme="prism"] .hero { box-shadow: 0 18px 40px #6b3d9926; }
            [hidden] { display: none !important; }
            @media (max-width: 760px) {
                .shell { padding: 20px 14px 50px; }
                .hero { padding: 24px; } .total { font-size: 55px; }
                .hero-squad { border-left: 0; border-top: 1px solid #6074ab77; padding: 17px 0 0; flex-basis: 100%; }
                .squad-total { font-size: 34px; }
                .metrics, .period-metrics { grid-template-columns: repeat(2, minmax(0,1fr)); }
                .metric, .period-metric, .panel { padding: 16px; }
                .chart { min-width: 480px; }
            }
            </style></head><body><main class="shell">
            <header class="top">
                <div class="brand"><div class="logo"><img src="{{iconDataUrl}}" width="53" height="53" alt="" aria-hidden="true"></div><div>
                    <h1>FORTNITE <span>CHESTTRACKER</span></h1>
                    <p class="subtitle">Estadísticas privadas a partir de tus repeticiones</p>
                </div></div>
                <div class="theme-picker"><label for="theme-select">Estilo</label>
                  <select class="theme-select" id="theme-select" aria-label="Estilo visual">
                    <option value="arena">Arena</option><option value="tactical">Tactical HUD</option>
                    <option value="studio">Studio Light</option><option value="prism">Prism</option>
                  </select></div>
            </header>
            <section class="hero">
              <div class="hero-grid">
                <div class="hero-primary">
                  <p class="eyebrow">Tus cofres</p>
                  <div class="total">
            """);
        sb.Append(data.Chests.ToString("N0", CultureInfo.GetCultureInfo("es-ES")));
        sb.Append("""
                  <span> cofres</span></div>
                </div>
                <div class="hero-squad">
                  <p class="eyebrow">Cofres de party</p>
                  <div class="squad-total">
            """);
        sb.Append(data.SquadChests.ToString("N0", CultureInfo.GetCultureInfo("es-ES")));
        sb.Append("""
                  </div>
                  <p class="squad-note">Total observado. Puede faltar actividad de compañeros no identificados o de partidas antiguas sin desglose.</p>
                </div>
              </div>
              <div class="hint">Tu histórico se conserva en SQLite.</div>
            </section>
            """);
        void Metric(string label, string value, string note)
        {
            sb.Append("<article class=\"metric\"><div class=\"label\">").Append(System.Net.WebUtility.HtmlEncode(label))
              .Append("</div><div class=\"num\">").Append(System.Net.WebUtility.HtmlEncode(value))
              .Append("</div><div class=\"small\">").Append(System.Net.WebUtility.HtmlEncode(note))
              .Append("</div></article>");
        }
        sb.Append("<section class=\"metrics\" aria-label=\"Estadísticas totales\">");
        Metric("Partidas", data.Matches.Count.ToString("N0", CultureInfo.GetCultureInfo("es-ES")), "Total de partidas importadas");
        Metric("Media por partida", data.Average.ToString("0.0", CultureInfo.GetCultureInfo("es-ES")), "Cofres por partida");
        Metric("Récord", data.Record.ToString("N0", CultureInfo.GetCultureInfo("es-ES")), "Máximo en una partida");
        Metric("Destruidos", data.Destroyed.ToString("N0", CultureInfo.GetCultureInfo("es-ES")), "Incluidos en los cofres");
        sb.Append("""
            </section>
            <section class="period-metrics" aria-label="Estadísticas recientes">
              <article class="period-metric"><div class="label">Hoy</div><div class="num" id="today-chests">—</div><div class="small">Tus cofres</div><div class="party-count">Party <strong id="today-party">—</strong><span>cofres observados</span></div><div class="small" id="today-games"></div></article>
              <article class="period-metric"><div class="label">Esta semana · lunes a domingo</div><div class="num" id="week-chests">—</div><div class="small">Tus cofres</div><div class="party-count">Party <strong id="week-party">—</strong><span>cofres observados</span></div><div class="small" id="week-games"></div></article>
              <article class="period-metric"><div class="label">Este mes</div><div class="num" id="month-chests">—</div><div class="small">Tus cofres</div><div class="party-count">Party <strong id="month-party">—</strong><span>cofres observados</span></div><div class="small" id="month-games"></div></article>
            </section>
            <section class="panel" aria-label="Cofres por periodo">
              <div class="section-head">
                <div><h2>Evolución de cofres</h2><p class="secondary" id="period-subtitle">Estadísticas por periodo</p></div>
                <div class="segment" role="group" aria-label="Agrupación de estadísticas">
                  <button type="button" data-unit="day" aria-pressed="true">Diario</button>
                  <button type="button" data-unit="month" aria-pressed="false">Mensual</button>
                </div>
              </div>
              <div class="period-summary"><strong id="period-total">—</strong><strong id="period-party">—</strong><span id="period-games">—</span><div class="chart-legend" role="group" aria-label="Mostrar u ocultar barras">
                <button class="legend-toggle owner" type="button" data-series="owner" aria-pressed="true"><span aria-hidden="true">■</span> Tú</button>
                <button class="legend-toggle party" type="button" data-series="party" aria-pressed="true"><span aria-hidden="true">■</span> Party (incluye tus cofres)</button>
              </div></div>
              <div class="chart-scroll"><div class="chart" id="period-chart" aria-label="Gráfica interactiva de cofres"></div></div>
            </section>
            <section class="panel" id="history-panel">
              <div class="section-head">
                <div><h2 id="history-title">Historial de partidas</h2><p class="secondary" id="history-subtitle"></p></div>
                <div class="toolbar">
                  <label for="history-filter" class="secondary">Periodo</label>
                  <select class="control" id="history-filter">
                    <option value="all">Todo el histórico</option>
                    <option value="today">Hoy</option>
                    <option value="week">Esta semana</option>
                    <option value="month">Este mes</option>
                    <option value="7days">Últimos 7 días</option>
                    <option value="30days">Últimos 30 días</option>
                    <option value="bucket">Periodo de la gráfica</option>
                  </select>
                  <button type="button" class="button" id="reset-filter">Limpiar</button>
                  <label for="page-size" class="secondary">Filas</label>
                  <select class="control" id="page-size">
                    <option value="10">10</option><option value="20">20</option><option value="30">30</option>
                    <option value="40">40</option><option value="50">50</option><option value="60">60</option>
                    <option value="70">70</option><option value="80">80</option><option value="90">90</option>
                    <option value="100">100</option>
                  </select>
                </div>
              </div>
              <div class="period-summary"><strong id="history-total">—</strong><strong id="history-party">—</strong><span id="history-count"></span></div>
              <div class="tablewrap" id="history-tablewrap">
                <table><thead><tr><th>Fecha</th><th>Duración</th><th>Jugador / escuadra</th><th>Posición</th><th>Cofres</th><th>Destruidos</th><th>Munición</th></tr></thead>
                <tbody id="history-body"></tbody></table>
              </div>
              <div class="empty" id="history-empty" hidden>Sin partidas en este periodo. Prueba a cambiar el filtro.</div>
              <nav class="pagination" aria-label="Paginación de partidas">
                <button class="button" type="button" id="page-prev">Anterior</button>
                <span class="secondary" id="history-page">Página 1 de 1</span>
                <button class="button" type="button" id="page-next">Siguiente</button>
              </nav>
            </section>
            <div class="footer">Fortnite ChestTracker · Sin conexión al juego · Sin telemetría · No afiliado a Epic Games</div>
            </main>
            <script id="match-data" type="application/json">
            """);
        sb.Append(payload);
        sb.Append("</script><script>").Append(script).Append("</script></body></html>");
        return sb.ToString();
    }

    private static string ReadIconDataUrl()
    {
        using var stream = typeof(Dashboard).Assembly
            .GetManifestResourceStream("FortniteChestTracker.app-icon.ico")
            ?? throw new InvalidOperationException("No se encuentra el icono integrado del ejecutable.");
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return "data:image/x-icon;base64," + Convert.ToBase64String(bytes.ToArray());
    }

    private static string ReadScript()
    {
        using var resource = typeof(Dashboard).Assembly
            .GetManifestResourceStream("FortniteChestTracker.dashboard.js")
            ?? throw new InvalidOperationException("No se encuentra el script integrado del panel.");
        using var reader = new StreamReader(resource, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
