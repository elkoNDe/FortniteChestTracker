"use strict";

const assert = require("node:assert/strict");
const { execFileSync } = require("node:child_process");
const fs = require("node:fs");
const path = require("node:path");
const os = require("node:os");
const { pathToFileURL } = require("node:url");

const reportPath = process.argv[2];
assert.ok(reportPath && fs.existsSync(reportPath), "HTML de prueba inexistente");

const candidates = [
  process.env.FCT_BROWSER_PATH,
  path.join(process.env["PROGRAMFILES(X86)"] || "", "Microsoft", "Edge", "Application", "msedge.exe"),
  path.join(process.env.ProgramFiles || "", "Microsoft", "Edge", "Application", "msedge.exe"),
  path.join(process.env.ProgramFiles || "", "Google", "Chrome", "Application", "chrome.exe"),
].filter(Boolean);
const browser = candidates.find((candidate) => fs.existsSync(candidate));
assert.ok(browser, "No se ha encontrado Edge/Chrome en el runner de Windows.");

const profile = fs.mkdtempSync(path.join(os.tmpdir(), "fct-browser-"));
try {
  const html = execFileSync(
    browser,
    [
      "--headless=new",
      "--disable-gpu",
      "--no-first-run",
      "--no-default-browser-check",
      "--disable-extensions",
      "--disable-background-networking",
      "--virtual-time-budget=3000",
      "--user-data-dir=" + profile,
      "--dump-dom",
      pathToFileURL(path.resolve(reportPath)).href,
    ],
    {
      encoding: "utf8",
      timeout: 45000,
      maxBuffer: 16 * 1024 * 1024,
      windowsHide: true,
    }
  );

  // This catches the regression where only static overall statistics render:
  // a blocked inline script leaves today/weekly/monthly at the em dash, 0 chart bars,
  // and no match rows.
  assert.match(html, /id="monitor-indicator"[^>]*data-state="watching"/,
    "El estado del monitor no se inicializa con el estado real.");
  assert.match(html, /id="monitor-title">Monitor activo</,
    "El dashboard no informa que el monitor está funcionando.");
  assert.match(html, /id="monitor-details">Última revisión[^<]*2 pendientes</,
    "Falta la hora de revisión o el número de repeticiones pendientes.");
  assert.match(html, /id="monitor-imported">Sin nuevas partidas en esta sesión</,
    "El monitor no indica si ha importado partidas nuevas.");
  assert.match(html, /id="today-chests">[0-9]/, "El script no actualiza los cofres de hoy.");
  assert.match(html, /id="week-chests">[0-9]/, "El script no actualiza los cofres semanales.");
  assert.match(html, /id="month-chests">[0-9]/, "El script no actualiza los cofres mensuales.");
  for (const period of ["today", "week", "month"]) {
    assert.match(html, new RegExp('id="' + period + '-party">[0-9]'),
      "No se muestra el total observado de party para " + period);
  }
  assert.match(html, /id="period-party">Party: [0-9]/, "El gráfico no totaliza la party.");
  assert.match(html, /id="history-party">Party: [0-9]/, "El filtro no totaliza la party.");
  assert.match(html, /id="history-page">Página 1 de 14</, "La paginación no se ha inicializado.");

  const history = html.match(/<tbody id="history-body">([\s\S]*?)<\/tbody>/);
  assert.ok(history, "No existe el historial en el DOM.");
  // Pagination is per match, not per player: 10 matches plus two squad
  // teammates under the first match = 12 table rows.
  assert.equal((history[1].match(/<tr[^>]*>/g) || []).length, 12,
    "No aparecen las 10 partidas con los jugadores de la primera escuadra.");
  assert.match(history[1], /rowspan="3"/,
    "La fecha y la posición de una escuadra deben aparecer una sola vez.");
  assert.match(html, /<th>Posición<\/th>/, "Falta la columna Posición.");
  assert.match(html, /<th>Fecha<\/th>/, "Falta la columna Fecha unificada.");
  assert.doesNotMatch(html, /<th>Fecha fin<\/th>/, "Fecha fin no debe ocupar otra columna.");
  assert.doesNotMatch(html, /<th>Fecha inicio<\/th>/, "Fecha inicio no debe ocupar otra columna.");
  assert.match(history[1], /class="date-start">Inicio: /, "Falta inicio en Fecha.");
  assert.match(history[1], /class="date-end">Fin: /, "Falta fin en Fecha.");
  assert.match(html, /<th>Duración<\/th>/, "Falta la duración.");
  assert.match(history[1], /18:12/, "Falta la duración de la primera partida.");
  assert.match(html, /<option value="90">90<\/option>/, "Paginación no configurable de 10 en 10.");
  assert.doesNotMatch(html, /<th>Modo<\/th>/, "Modo debe desaparecer.");
  assert.match(history[1], /title="Victoria">#1<\/td>/,
    "La primera partida debe mostrar una posición de victoria.");
  // Real Edge DOM check: both locations use one offline embedded ICO.
  const favicon = html.match(/<link rel="icon"[^>]+href="([^"]+)"/);
  assert.ok(favicon && favicon[1].startsWith("data:image/x-icon;base64,"),
    "No se ha configurado el icono de la pestaña.");
  assert.ok(html.includes('<img src="' + favicon[1] + '"'),
    "El logo del título no utiliza la misma imagen que el icono del EXE.");
  assert.doesNotMatch(html, /id="squad-note"/, "Se debe quitar el aviso de desglose antiguo.");
  assert.doesNotMatch(html, /Todas las partidas guardadas \(sin límite de 60\)/,
    "El subtítulo antiguo debe desaparecer.");
  assert.doesNotMatch(html, /Solo Cero construcción BR/,
    "El hero no debe incluir la coletilla del modo.");
  assert.match(html, /Cofres de party/, "Falta el total de cofres de la party.");
  assert.match(html, /class="squad-total">\s*[\d.,]+\s*<\/div>/,
    "No se representa el total de cofres observados de la escuadra.");
  assert.match(history[1], /Amiga de escuadra/,
    "Falta el nombre de la compañera.");
  assert.ok(history[1].includes('class="unobserved">—</td>'),
    "No se distingue un jugador sin datos atribuibles de uno con cero cofres.");
  const chart = html.match(/<div class="chart" id="period-chart"[^>]*>([\s\S]*?)<\/div>/);
  assert.ok(chart, "No existe el contenedor de la gráfica.");
  assert.ok((html.match(/class="chart-bar"/g) || []).length >= 12, "No se han generado las barras semanales.");
  assert.ok((html.match(/class="chart-fill chart-fill-party"/g) || []).length >= 12,
    "La gráfica no muestra las barras de la party.");
  assert.match(html, /data-series="owner" aria-pressed="true"/,
    "Falta el control accesible para alternar tus cofres.");
  assert.match(html, /data-series="party" aria-pressed="true"/,
    "Falta el control accesible para alternar los cofres de party.");
  assert.match(html, /Party \(incluye tus cofres\)/,
    "La leyenda de la party no está identificada.");

  assert.match(html, /data-unit="day"[^>]*aria-pressed="true"/, "El gráfico no inicia en Diario.");
  assert.match(html, /data-unit="month"/, "Falta la opción Mensual.");
  assert.doesNotMatch(html, /data-unit="week"/, "Semanal no debe aparecer en el selector del gráfico.");
  for (const theme of ["arena", "tactical", "studio", "prism", "carbon", "arcade", "glacier", "solar", "ember", "blueprint"]) {
    assert.ok(html.includes('<option value="' + theme + '">'), "El selector no incluye " + theme);
  }
  assert.match(html, /<html[^>]*data-theme="arena"/, "No se aplica el tema inicial.");
  assert.doesNotMatch(html, /Datos locales · Actualización cada 20 s/, "El aviso antiguo sigue visible.");
  console.log("OK: diez temas, branding, gráfico, contadores e historial en Edge.");
} finally {
  fs.rmSync(profile, { recursive: true, force: true, maxRetries: 3, retryDelay: 100 });
}
