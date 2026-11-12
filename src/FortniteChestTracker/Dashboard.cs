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
            :root { color-scheme: dark; font-family: Inter, Segoe UI, Arial, sans-serif; background: var(--page, #090e1c); color: var(--text, #f5f6ff); }
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
            /* One placement/size system for all ten decorative motifs.
               The motif stays behind the numbers and never captures input. */
            .hero::after {
                content: "✦"; position: absolute;
                width: 140px; height: 140px; right: 6%; top: 50%;
                transform: translateY(-50%); display: flex;
                align-items: center; justify-content: center; text-align: center;
                font: 400 126px/1 "Segoe UI Symbol", "Segoe UI", sans-serif;
                color: var(--hero-ornament, #ffffff16);
                pointer-events: none; z-index: 0;
            }
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
                --hero-note: #b1c5e4; --hero-label: #d2d8fa; --hero-ornament: #ffffff16;
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
                --hero-note: #badad1; --hero-label: #b5eac7; --hero-ornament: #b8f47f18;
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
                --hero-note: #736b66; --hero-label: #81513c; --hero-ornament: #ca9a5a18;
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
                --hero-note: #d4c6ee; --hero-label: #e3d2ff; --hero-ornament: #e0c9ff18;
                --control-bg: #211d3e; --control-border: #5c527d; --control-text: #f0e7ff;
                --button-bg: #353057; --button-border: #65588b; --selected: #674ba1;
                --hover: #49416d; --chart-hover: #38315d; --owner-bottom: #7f56a9;
                --owner-top: #d4afff; --team-bottom: #347ca0; --team-top: #88dcf3;
                --row-alt: #272346; --table-rule: #383457; --radius: 20px; --hero-radius: 26px;
            }
            /* Four extra looks: industrial, arcade, frosted glass and sunset.
               All are offline CSS only and keep the same dashboard content. */
            html[data-theme="carbon"] {
                --page: #101112; --glow: #34383c44; --text: #f0f0ec; --muted: #a9acae;
                --surface: #1a1c1e; --line: #505358; --accent: #ff715c; --team: #d8dde0;
                --hero-a: #2b2c2e; --hero-b: #202123; --hero-c: #171819; --hero-line: #696a6a;
                --hero-note: #b7bdc0; --hero-label: #d8dddc; --hero-ornament: #ffffff16;
                --control-bg: #161819; --control-border: #676a70; --control-text: #f0f0ec;
                --button-bg: #25282a; --button-border: #62656a; --selected: #c34d3e;
                --hover: #373a3d; --chart-hover: #333537; --owner-bottom: #a63d30;
                --owner-top: #ff8b74; --team-bottom: #68747d; --team-top: #dce3e8;
                --row-alt: #242628; --table-rule: #36393c; --radius: 3px; --hero-radius: 3px;
            }
            html[data-theme="arcade"] {
                --page: #0b0920; --glow: #782baf66; --text: #fbf5ff; --muted: #c8aedb;
                --surface: #1b1335; --line: #614686; --accent: #ff79c7; --team: #72e5e3;
                --hero-a: #45216b; --hero-b: #271c53; --hero-c: #14284d; --hero-line: #bb66dc;
                --hero-note: #dcc6fa; --hero-label: #f2cbff; --hero-ornament: #e392ff18;
                --control-bg: #1b1438; --control-border: #78529b; --control-text: #faf3ff;
                --button-bg: #322051; --button-border: #8554aa; --selected: #934a9b;
                --hover: #4c3278; --chart-hover: #43275d; --owner-bottom: #ae398a;
                --owner-top: #ff92d5; --team-bottom: #277f9e; --team-top: #7ff7ef;
                --row-alt: #281d44; --table-rule: #48365e; --radius: 11px; --hero-radius: 16px;
            }
            html[data-theme="glacier"] {
                color-scheme: light;
                --page: #eaf4fa; --glow: #b9e0f9cc; --text: #22384d; --muted: #617e93;
                --surface: #ffffffcf; --line: #d2e4ef; --accent: #166fbd; --team: #009aaa;
                --hero-a: #d3edfb; --hero-b: #e6f4ff; --hero-c: #f9fdff; --hero-line: #b5d9ee;
                --hero-note: #56768e; --hero-label: #47759b; --hero-ornament: #3f8bd018;
                --control-bg: #f8fcff; --control-border: #a9ccdf; --control-text: #21445b;
                --button-bg: #ebf6fc; --button-border: #b5d7e8; --selected: #2478b9;
                --hover: #d9edf9; --chart-hover: #d6ecfa; --owner-bottom: #337ebd;
                --owner-top: #6bbbf0; --team-bottom: #078c9d; --team-top: #64d4dc;
                --row-alt: #eaf6fc; --table-rule: #dceaf3; --radius: 23px; --hero-radius: 29px;
            }
            html[data-theme="solar"] {
                --page: #1b1417; --glow: #a7513559; --text: #fff5ea; --muted: #cfaca4;
                --surface: #302026; --line: #64444b; --accent: #ffc26a; --team: #ff977c;
                --hero-a: #8d3e44; --hero-b: #653242; --hero-c: #342233; --hero-line: #b56a64;
                --hero-note: #f1c4b2; --hero-label: #ffe2bd; --hero-ornament: #ffd9a318;
                --control-bg: #281b21; --control-border: #8b5660; --control-text: #fff4e9;
                --button-bg: #4b3036; --button-border: #8f565d; --selected: #a75453;
                --hover: #634049; --chart-hover: #633944; --owner-bottom: #ca673f;
                --owner-top: #ffc46d; --team-bottom: #b64b63; --team-top: #ff997d;
                --row-alt: #3c2830; --table-rule: #59373f; --radius: 20px; --hero-radius: 26px;
            }
            /* Ember: volcanic charcoal, copper highlights and hard-edged instruments. */
            html[data-theme="ember"] {
                --page: #110e0d; --glow: #a541192f; --text: #fff2e7; --muted: #c7a394;
                --surface: #201814; --line: #5e3928; --accent: #ff8848; --team: #ffd3a5;
                --hero-a: #562619; --hero-b: #2f1b16; --hero-c: #181210; --hero-line: #b35a30;
                --hero-note: #e6b4a0; --hero-label: #ffbe8e; --hero-ornament: #ff743218;
                --control-bg: #241813; --control-border: #76412b; --control-text: #ffeede;
                --button-bg: #3b241c; --button-border: #854528; --selected: #ab4823;
                --hover: #583122; --chart-hover: #4b2a1e; --owner-bottom: #c0461f;
                --owner-top: #ffa351; --team-bottom: #8e5535; --team-top: #ffe0ad;
                --row-alt: #2d1e18; --table-rule: #4a3024; --radius: 6px; --hero-radius: 9px;
            }
            /* Blueprint: crisp drafting lines, technical grid and data-first panels. */
            html[data-theme="blueprint"] {
                --page: #092944; --glow: #217cb04d; --text: #e7f9ff; --muted: #a0c8db;
                --surface: #103853; --line: #47748c; --accent: #81e7fa; --team: #f9d880;
                --hero-a: #155074; --hero-b: #0e3b5c; --hero-c: #0a2e4c; --hero-line: #7bbbd4;
                --hero-note: #b6dbe7; --hero-label: #a9e9fd; --hero-ornament: #f1fcff18;
                --control-bg: #113852; --control-border: #79b1c9; --control-text: #e1f7ff;
                --button-bg: #164663; --button-border: #77aac1; --selected: #216f91;
                --hover: #205978; --chart-hover: #205473; --owner-bottom: #2aa6cc;
                --owner-top: #94edfa; --team-bottom: #a88c3a; --team-top: #ffe1a0;
                --row-alt: #15415e; --table-rule: #315e79; --radius: 2px; --hero-radius: 2px;
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
            html[data-theme="tactical"] .total, html[data-theme="tactical"] .metric .num {
                font-variant-numeric: tabular-nums; }
            html[data-theme="studio"] .hero, html[data-theme="studio"] .panel,
            html[data-theme="studio"] .metric, html[data-theme="studio"] .period-metric {
                box-shadow: 0 9px 28px #6b543412; }
            html[data-theme="studio"] .segment button.active { color: #fff; }
            html[data-theme="studio"] .chart-value { color: #9f472e; }
            html[data-theme="prism"] .hero { box-shadow: 0 18px 40px #6b3d9926; }
            /* Arena: esports scoreboard, angular panels and stadium highlights. */
            html[data-theme="arena"] body {
                background: radial-gradient(ellipse at 10% -10%, #3b6cb263, transparent 48%),
                    radial-gradient(ellipse at 95% 9%, #f7c66d16, transparent 35%), var(--page);
            }
            html[data-theme="arena"] .hero {
                border-radius: 10px; border-color: #5d73b6;
                background: radial-gradient(ellipse at 82% 0%, #d8e0ff24, transparent 44%),
                    linear-gradient(108deg, #2d4fa1, #192c64 57%, #101c38);
                box-shadow: inset 0 2px 0 #a8c5ff44, 0 10px 28px #0e173514;
            }
            /* Arena: two restrained stadium slashes, not a floating star. */
            html[data-theme="arena"] .hero::after {
                content: "";
                background: linear-gradient(112deg,
                    transparent 0 31%, var(--hero-ornament) 31% 40%,
                    transparent 40% 50%, var(--hero-ornament) 50% 59%,
                    transparent 59%);
                background-size: 140px 100px;
                background-position: center;
                background-repeat: no-repeat;
            }
            html[data-theme="arena"] .total { font-style: italic; letter-spacing: -4px; }
            html[data-theme="arena"] .metric, html[data-theme="arena"] .period-metric {
                border-radius: 5px; border-top: 2px solid #6984c5;
                background: linear-gradient(160deg, #1a2d4c, var(--surface) 66%);
            }
            html[data-theme="arena"] .panel { border-radius: 9px; }
            html[data-theme="arena"] .chart-fill, html[data-theme="arena"] .chart-bar {
                border-radius: 2px 2px 0 0;
            }
            html[data-theme="arena"] .segment button.active {
                box-shadow: inset 0 -3px 0 var(--accent);
            }
            html[data-theme="arena"] h2 { text-transform: uppercase; font-size: 16px; letter-spacing: 1.4px; }

            /* Tactical HUD: subtle radar grid, readout type, indexed controls;
               no vertical left accent on any panel or hero. */
            html[data-theme="tactical"] body {
                background: radial-gradient(circle at 88% 0%, #2d6d4829, transparent 38%),
                    repeating-linear-gradient(0deg, transparent 0 27px, #76b39909 27px 28px),
                    repeating-linear-gradient(90deg, transparent 0 27px, #76b39909 27px 28px), var(--page);
            }
            html[data-theme="tactical"] .hero {
                background: radial-gradient(circle at 88% 28%, #9ae3b00b, transparent 32%),
                    linear-gradient(110deg, #173d34, #102c29 65%, #0a2321);
                box-shadow: inset 0 1px 0 #b2f5be35, inset 0 -1px 0 #b2f5be20;
            }
            /* Tactical HUD: eight precision corner strokes, no central glyph. */
            html[data-theme="tactical"] .hero::after {
                content: "";
                background:
                    linear-gradient(var(--hero-ornament), var(--hero-ornament)) left 12px top 12px / 32px 2px no-repeat,
                    linear-gradient(var(--hero-ornament), var(--hero-ornament)) left 12px top 12px / 2px 32px no-repeat,
                    linear-gradient(var(--hero-ornament), var(--hero-ornament)) right 12px top 12px / 32px 2px no-repeat,
                    linear-gradient(var(--hero-ornament), var(--hero-ornament)) right 12px top 12px / 2px 32px no-repeat,
                    linear-gradient(var(--hero-ornament), var(--hero-ornament)) left 12px bottom 12px / 32px 2px no-repeat,
                    linear-gradient(var(--hero-ornament), var(--hero-ornament)) left 12px bottom 12px / 2px 32px no-repeat,
                    linear-gradient(var(--hero-ornament), var(--hero-ornament)) right 12px bottom 12px / 32px 2px no-repeat,
                    linear-gradient(var(--hero-ornament), var(--hero-ornament)) right 12px bottom 12px / 2px 32px no-repeat;
            }
            html[data-theme="tactical"] .metric, html[data-theme="tactical"] .period-metric,
            html[data-theme="tactical"] .panel {
                background: linear-gradient(180deg, #18312b 0, var(--surface) 68px);
                box-shadow: inset 0 1px 0 #97e2b51d;
            }
            html[data-theme="tactical"] .eyebrow, html[data-theme="tactical"] .metric .label,
            html[data-theme="tactical"] .period-metric .label,
            html[data-theme="tactical"] th {
                font-family: Consolas, "Courier New", monospace; letter-spacing: 1.1px;
            }
            html[data-theme="tactical"] .metric .num, html[data-theme="tactical"] .total,
            html[data-theme="tactical"] .period-metric .num,
            html[data-theme="tactical"] .squad-total {
                font-family: Consolas, "Courier New", monospace; font-variant-numeric: tabular-nums;
            }
            html[data-theme="tactical"] .chart-fill {
                border-radius: 1px 1px 0 0; outline: 1px solid #a7e6ae20; outline-offset: -2px;
            }
            html[data-theme="tactical"] .segment button.active {
                outline: 1px solid #b8f47f83; outline-offset: -1px;
            }
            html[data-theme="tactical"] .theme-select, html[data-theme="tactical"] .button,
            html[data-theme="tactical"] .control { border-radius: 4px; }

            /* Studio Light: airy editorial layout, restrained warm-paper texture. */
            html[data-theme="studio"] body {
                background: linear-gradient(125deg, #fffdf8 0%, #f7f2eb 48%, #f4eee7 100%);
            }
            html[data-theme="studio"] .shell { max-width: 1260px; }
            html[data-theme="studio"] .hero {
                background: linear-gradient(110deg, #fff7ed 0%, #fffdfa 72%, #f6ebdb);
                border: 1px solid #e9d8c4; padding-top: 38px; padding-bottom: 38px;
            }
            /* Studio Light: delicate concentric rules, like an editorial seal. */
            html[data-theme="studio"] .hero::after {
                content: "";
                border-radius: 50%;
                background: radial-gradient(circle at center,
                    transparent 0 44px, var(--hero-ornament) 45px 46px,
                    transparent 47px 55px, #ca9a5a10 56px 57px,
                    transparent 58px);
            }
            html[data-theme="studio"] h2 {
                font-family: Georgia, "Times New Roman", serif; font-size: 23px; font-weight: 500;
            }
            html[data-theme="studio"] .total, html[data-theme="studio"] .squad-total {
                font-family: Georgia, "Times New Roman", serif; font-weight: 600; letter-spacing: -2px;
            }
            html[data-theme="studio"] .metric, html[data-theme="studio"] .period-metric {
                border-color: #f1e8dd; box-shadow: 0 12px 36px #6b543410;
                padding: 25px 22px;
            }
            html[data-theme="studio"] .panel {
                border-color: #eee4d9; padding: 28px;
            }
            html[data-theme="studio"] .chart-fill {
                width: 32%; max-width: 19px; border-radius: 8px 8px 0 0;
            }
            html[data-theme="studio"] th {
                text-transform: none; font-size: 12px; letter-spacing: .15px; font-weight: 600;
            }
            html[data-theme="studio"] .metric .label, html[data-theme="studio"] .period-metric .label {
                text-transform: none; font-size: 13px; letter-spacing: 0;
            }

            /* Prism: layered glass with subtle refractions, not a flat purple coat. */
            html[data-theme="prism"] body {
                background: radial-gradient(ellipse at 0% -4%, #783db74a, transparent 52%),
                    radial-gradient(ellipse at 100% 18%, #3997ba3d, transparent 42%),
                    linear-gradient(145deg, #11102c, #191239 66%, #0c1935);
            }
            html[data-theme="prism"] .hero {
                background: linear-gradient(128deg, #684497b8, #2c2d65ad 52%, #193956a8);
                border-color: #9772bcaa; backdrop-filter: blur(14px);
                box-shadow: 0 18px 48px #04051545, inset 0 1px 0 #ffffff34;
            }
            html[data-theme="prism"] .hero::after {
                content: "◇"; font-size: 128px;
            }
            html[data-theme="prism"] .metric, html[data-theme="prism"] .period-metric,
            html[data-theme="prism"] .panel {
                background: linear-gradient(145deg, #332953b3, #192548bd);
                border-color: #8268af78; backdrop-filter: blur(16px);
                box-shadow: 0 12px 32px #060a2344, inset 0 1px 0 #ffffff1b;
            }
            html[data-theme="prism"] .metric .num {
                text-shadow: 0 0 22px #c1b0ee36;
            }
            html[data-theme="prism"] .chart-fill {
                background: linear-gradient(0deg, #835db4, #eac1ff);
                box-shadow: 0 0 11px #cd9aff55;
            }
            html[data-theme="prism"] .chart-fill-party {
                background: linear-gradient(0deg, #327c9e, #9deeff);
                box-shadow: 0 0 11px #83d9fc4a;
            }
            html[data-theme="prism"] .segment button.active { box-shadow: 0 0 14px #b09bff30; }

            /* Ember: compact heated metal, glowing top edge and crisp bars. */
            html[data-theme="ember"] body {
                background: radial-gradient(ellipse at 86% -4%, #c44c2325, transparent 42%),
                    radial-gradient(ellipse at 4% 90%, #ff73250e, transparent 48%), var(--page);
            }
            html[data-theme="ember"] .hero {
                background: radial-gradient(ellipse at 86% 10%, #ff903023, transparent 35%),
                    linear-gradient(115deg, #5c2516, #2d1c17 62%, #171210);
                box-shadow: 0 16px 36px #05040366, inset 0 2px 0 #fb954f45;
            }
            html[data-theme="ember"] .hero::after {
                content: "✶"; font-family: Georgia, serif; font-size: 122px;
            }
            html[data-theme="ember"] .metric, html[data-theme="ember"] .period-metric,
            html[data-theme="ember"] .panel {
                background: linear-gradient(170deg, #2b1b14, var(--surface) 65%);
                border-top: 2px solid #9e4c28;
            }
            html[data-theme="ember"] .metric .num { color: #ffd0a0; }
            html[data-theme="ember"] .chart-fill {
                border-radius: 2px 2px 0 0; box-shadow: 0 0 8px #e9542661;
            }
            html[data-theme="ember"] .chart-fill-party { box-shadow: 0 0 6px #ffdcac42; }
            html[data-theme="ember"] .segment button.active { box-shadow: inset 0 -2px 0 #ffd19d; }

            /* Blueprint: blueprint-paper grid, drafting borders and monospace readouts. */
            html[data-theme="blueprint"] body {
                background: linear-gradient(#b4e3ff09 1px, transparent 1px),
                    linear-gradient(90deg, #b4e3ff09 1px, transparent 1px),
                    linear-gradient(#b4e3ff13 1px, transparent 1px),
                    linear-gradient(90deg, #b4e3ff13 1px, transparent 1px), var(--page);
                background-size: 18px 18px, 18px 18px, 90px 90px, 90px 90px, auto;
            }
            html[data-theme="blueprint"] .hero, html[data-theme="blueprint"] .panel,
            html[data-theme="blueprint"] .metric, html[data-theme="blueprint"] .period-metric {
                border-style: dashed; box-shadow: inset 0 0 0 1px #a1e4ff14;
            }
            html[data-theme="blueprint"] .hero {
                background: linear-gradient(115deg, #165474f2, #0d3756ee 65%, #0a2e4cee);
            }
            html[data-theme="blueprint"] .hero::after {
                content: "◎"; font-size: 118px;
            }
            html[data-theme="blueprint"] h2, html[data-theme="blueprint"] th,
            html[data-theme="blueprint"] .metric .label,
            html[data-theme="blueprint"] .period-metric .label {
                font-family: Consolas, "Courier New", monospace; letter-spacing: .6px;
            }
            html[data-theme="blueprint"] .total, html[data-theme="blueprint"] .squad-total,
            html[data-theme="blueprint"] .metric .num {
                font-family: Consolas, "Courier New", monospace; font-variant-numeric: tabular-nums;
            }
            html[data-theme="blueprint"] .chart-fill, html[data-theme="blueprint"] .chart-bar {
                border-radius: 0;
            }
            html[data-theme="blueprint"] .button, html[data-theme="blueprint"] .control,
            html[data-theme="blueprint"] .theme-select, html[data-theme="blueprint"] .segment,
            html[data-theme="blueprint"] .segment button {
                border-radius: 2px;
            }
            html[data-theme="blueprint"] .segment button.active {
                box-shadow: inset 0 0 0 1px #c6f8ff7d;
            }
            /* Structural differences are deliberate; these are not just palette swaps. */
            html[data-theme="carbon"] body { background: var(--page); }
            html[data-theme="carbon"] .hero::after { content: none; }
            html[data-theme="carbon"] .hero-squad { position: relative; isolation: isolate; }
            html[data-theme="carbon"] .hero-squad::after {
                content: "////"; position: absolute; left: 50%; top: 50%;
                transform: translate(-50%, -50%);
                width: 140px; height: 140px; display: flex;
                align-items: center; justify-content: center; text-align: center;
                font: 900 72px/1 "Segoe UI", sans-serif; letter-spacing: -10px;
                color: var(--hero-ornament); pointer-events: none; z-index: -1;
            }
            html[data-theme="carbon"] h1 { letter-spacing: -1.3px; font-weight: 900; }
            html[data-theme="carbon"] .metric .num { font-variant-numeric: tabular-nums; }
            html[data-theme="carbon"] .chart-fill { border-radius: 0; }
            html[data-theme="carbon"] .chart-bar { border-radius: 0; }
            html[data-theme="carbon"] .button, html[data-theme="carbon"] .control,
            html[data-theme="carbon"] .segment, html[data-theme="carbon"] .segment button,
            html[data-theme="carbon"] .legend-toggle, html[data-theme="carbon"] .theme-select {
                border-radius: 3px; }
            html[data-theme="carbon"] th { letter-spacing: 1.4px; }
            html[data-theme="arcade"] body {
                background: radial-gradient(circle at 13% -9%, #9c35be40, transparent 40%),
                  linear-gradient(#8d52c707 1px, transparent 1px),
                  linear-gradient(90deg, #8d52c707 1px, transparent 1px), var(--page);
                background-size: auto, 42px 42px, 42px 42px, auto;
            }
            html[data-theme="arcade"] .hero { box-shadow: 0 0 32px #c34cc31c; }
            html[data-theme="arcade"] .hero::after { content: "◇"; font-size: 130px; }
            html[data-theme="arcade"] .panel, html[data-theme="arcade"] .metric,
            html[data-theme="arcade"] .period-metric { box-shadow: 0 6px 24px #0a041c62; }
            html[data-theme="arcade"] .chart-fill { box-shadow: 0 0 12px #ff79c74a; }
            html[data-theme="arcade"] .chart-fill-party { box-shadow: 0 0 12px #72e5e352; }
            html[data-theme="arcade"] .segment button.active { box-shadow: 0 0 12px #ff79c73b; }
            html[data-theme="glacier"] body {
                background: radial-gradient(ellipse at 0% 0%, #d3ecffc4, transparent 53%),
                  radial-gradient(ellipse at 90% 5%, #c8e9eacc, transparent 42%), var(--page);
            }
            html[data-theme="glacier"] .panel, html[data-theme="glacier"] .metric,
            html[data-theme="glacier"] .period-metric { backdrop-filter: blur(12px);
                box-shadow: 0 12px 35px #4181a214; }
            html[data-theme="glacier"] .hero { box-shadow: 0 14px 35px #3e88b120; }
            html[data-theme="glacier"] .hero::after { content: "✳"; font-size: 122px; }
            html[data-theme="glacier"] .chart-fill { border-radius: 12px 12px 0 0; }
            html[data-theme="glacier"] .button, html[data-theme="glacier"] .control,
            html[data-theme="glacier"] .theme-select { border-radius: 14px; }
            html[data-theme="solar"] body {
                background: radial-gradient(ellipse at 70% -10%, #c55c4159, transparent 54%),
                  radial-gradient(ellipse at 0% 25%, #8d435027, transparent 43%), var(--page);
            }
            html[data-theme="solar"] .hero {
                background: radial-gradient(circle at 86% 22%, #ffc26a20 0, transparent 31%),
                  linear-gradient(120deg, var(--hero-a), var(--hero-b) 55%, var(--hero-c)); }
            html[data-theme="solar"] .hero::after { content: "☼"; font-size: 96px; }
            html[data-theme="solar"] .metric, html[data-theme="solar"] .period-metric {
                background: linear-gradient(145deg, #3f2830, var(--surface) 80%); }
            html[data-theme="solar"] .total { text-shadow: 0 7px 24px #17101855; }
            html[data-theme="solar"] .chart-fill { border-radius: 8px 8px 0 0; }
            /* Light themes need explicitly dark inherited text despite dark default CSS. */
            html[data-theme="studio"], html[data-theme="glacier"] {
                color: var(--text);
            }
            html[data-theme="studio"] h1 { color: #243047; }
            html[data-theme="studio"] h1 span { color: #b85a36; }
            html[data-theme="studio"] .metric .num,
            html[data-theme="studio"] .period-metric .num,
            html[data-theme="studio"] .squad-total,
            html[data-theme="studio"] .chart-value { color: #243047; }
            html[data-theme="studio"] .metric .label,
            html[data-theme="studio"] .period-metric .label,
            html[data-theme="studio"] .subtitle,
            html[data-theme="studio"] .secondary,
            html[data-theme="studio"] .muted { color: #5c6d80; }
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
                    <option value="carbon">Carbon</option><option value="arcade">Neon Arcade</option>
                    <option value="glacier">Glacier</option><option value="solar">Solar Flare</option>
                    <option value="ember">Ember</option><option value="blueprint">Blueprint</option>
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
