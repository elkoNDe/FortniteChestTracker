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
  assert.match(html, /id="today-chests">[0-9]/, "El script no actualiza los cofres de hoy.");
  assert.match(html, /id="week-chests">[0-9]/, "El script no actualiza los cofres semanales.");
  assert.match(html, /id="month-chests">[0-9]/, "El script no actualiza los cofres mensuales.");
  assert.match(html, /id="history-page">Página 1 de 6</, "La paginación no se ha inicializado.");

  const history = html.match(/<tbody id="history-body">([\s\S]*?)<\/tbody>/);
  assert.ok(history, "No existe el historial en el DOM.");
  // Pagination is per match, not per player: 25 matches plus two squad
  // teammates under the first match = 27 table rows.
  assert.equal((history[1].match(/<tr[^>]*>/g) || []).length, 27,
    "No aparecen las 25 partidas con los jugadores de la primera escuadra.");
  assert.match(history[1], /rowspan="3"/,
    "La fecha y la posición de una escuadra deben aparecer una sola vez.");
  assert.match(html, /<th>Posición<\/th>/, "Falta la columna Posición.");
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

  assert.match(html, /data-unit="day"[^>]*aria-pressed="true"/, "El gráfico no inicia en Diario.");
  assert.match(html, /data-unit="month"/, "Falta la opción Mensual.");
  assert.doesNotMatch(html, /data-unit="week"/, "Semanal no debe aparecer en el selector del gráfico.");
  console.log("OK: gráfico Diario/Mensual, contadores y 25 partidas paginadas en Edge.");
} finally {
  fs.rmSync(profile, { recursive: true, force: true, maxRetries: 3, retryDelay: 100 });
}
