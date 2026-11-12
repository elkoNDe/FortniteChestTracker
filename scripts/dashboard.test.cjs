const test = require('node:test');
const assert = require('node:assert/strict');
const {
  periodStart, shiftPeriod, inRange, bucketsFor, lastNDaysStart, paginate, playersForMatch, visibleChartSeries
} = require('../src/FortniteChestTracker/Assets/dashboard.js');

const entry = (year, month, day, chests) => ({
  when: new Date(year, month - 1, day, 12),
  chests
});

test('las semanas empiezan el lunes, incluso en un cambio de año', () => {
  assert.equal(periodStart(new Date(2026, 0, 4, 12), 'week').getTime(),
    new Date(2025, 11, 29).getTime());
  assert.equal(periodStart(new Date(2026, 0, 5, 12), 'week').getTime(),
    new Date(2026, 0, 5).getTime());
  assert.equal(shiftPeriod(new Date(2026, 0, 5), 'week', -1).getTime(),
    new Date(2025, 11, 29).getTime());
});

test('meses completos en febrero de año bisiesto', () => {
  assert.equal(periodStart(new Date(2024, 1, 29, 21), 'month').getTime(),
    new Date(2024, 1, 1).getTime());
  assert.equal(shiftPeriod(new Date(2024, 1, 1), 'month', 1).getTime(),
    new Date(2024, 2, 1).getTime());
});

test('gráfica semanal agrupa correctamente a ambos lados de Año Nuevo e incluye semanas vacías', () => {
  const rows = [entry(2026, 12, 22, 2), entry(2026, 12, 27, 3),
    entry(2026, 12, 29, 5), entry(2027, 1, 5, 7)];
  const weeks = bucketsFor(rows, 'week', 4, new Date(2027, 0, 6, 15));
  assert.deepEqual(weeks.map(w => w.chests), [0, 5, 5, 7]);
  assert.deepEqual(weeks.map(w => w.games), [0, 2, 1, 1]);
});

test('agregados diarios y mensuales no confunden fechas del mismo mes en años distintos', () => {
  const rows = [entry(2025, 12, 30, 100), entry(2026, 11, 30, 4),
    entry(2026, 12, 30, 5)];
  assert.deepEqual(bucketsFor(rows, 'month', 2, new Date(2026, 11, 31)).map(x => x.chests), [4, 5]);
  assert.deepEqual(bucketsFor(rows, 'day', 2, new Date(2026, 11, 31)).map(x => x.chests), [5, 0]);
  assert.equal(lastNDaysStart(new Date(2026, 10, 10, 23), 7).getTime(),
    new Date(2026, 10, 4).getTime());
});

test('filtro semanal incluye el lunes y excluye el lunes siguiente', () => {
  const start = periodStart(new Date(2026, 10, 10), 'week');
  const end = shiftPeriod(start, 'week', 1);
  assert.equal(inRange(entry(2026, 11, 9, 1), start, end), true);
  assert.equal(inRange(entry(2026, 11, 15, 1), start, end), true);
  assert.equal(inRange(entry(2026, 11, 16, 1), start, end), false);
});

test('paginación de 135 partidas, última página y páginas fuera de rango', () => {
  const rows = Array.from({ length: 135 }, (_, i) => i);
  assert.deepEqual(paginate(rows, 1, 25).visible, rows.slice(0, 25));
  assert.deepEqual(paginate(rows, 3, 50).visible, rows.slice(100, 135));
  assert.equal(paginate(rows, 50, 25).page, 6);
  assert.equal(paginate(rows, -5, 25).page, 1);
  assert.deepEqual(paginate([], 1, 25), { page: 1, pages: 1, first: 0, visible: [] });
});

test('desglose por jugador, sin duplicar las partidas ni inventar datos', () => {
  const match = {
    player: 'Yo', chests: 8, destroyed: 1, ammo: 2,
    team: [
      { name: 'Compañera', owner: false, chests: 4, destroyed: 0, ammo: 1 },
      { name: 'Yo', owner: true, chests: 8, destroyed: 1, ammo: 2 },
      { name: 'Sin relacion', owner: false, chests: null, destroyed: null, ammo: null }
    ]
  };
  const players = playersForMatch(match);
  assert.equal(players.length, 3);
  assert.equal(players[0].owner, true);
  assert.equal(players[1].chests, 4);
  assert.equal(players[2].chests, null);
  assert.equal(match.chests, 8);
  assert.equal(playersForMatch({ player: 'Yo', chests: 8, destroyed: 1, ammo: 2 }).length, 1);
});

test('paginación con saltos de 10: 10, 20, 30, ... 100', () => {
  const matches = Array.from({length: 135}, (_, i) => i);
  for (let size = 10; size <= 100; size += 10) {
    assert.equal(paginate(matches, 1, size).visible.length, size);
    assert.equal(paginate(matches, 99, size).page, Math.ceil(135 / size));
  }
});

test('totales por periodo de party: personales sin duplicar y sin inventar compañeros', () => {
  const rows = [
    { when: new Date(2026, 10, 10, 12), chests: 5, party: 12 },
    { when: new Date(2026, 10, 10, 18), chests: 3, party: 3 },
    { when: new Date(2026, 10, 11, 12), chests: 8, party: 11 },
    { when: new Date(2026, 10, 12, 12), chests: 4 }
  ];
  const days = bucketsFor(rows, 'day', 3, new Date(2026, 10, 12, 20));
  assert.deepEqual(days.map(x => x.chests), [8, 8, 4]);
  assert.deepEqual(days.map(x => x.party), [15, 11, 4]);
  assert.deepEqual(days.map(x => x.games), [2, 1, 1]);
  const month = bucketsFor(rows, 'month', 1, new Date(2026, 10, 12));
  assert.equal(month[0].chests, 20);
  assert.equal(month[0].party, 30);
});

test('leyenda de series: ambas, solo tú, solo party y ninguna', () => {
  const match = { chests: 6, party: 13 };
  assert.deepEqual(visibleChartSeries(match, true, true).map(x => [x.id, x.value]),
    [['owner', 6], ['party', 13]]);
  assert.deepEqual(visibleChartSeries(match, true, false).map(x => [x.id, x.value]),
    [['owner', 6]]);
  assert.deepEqual(visibleChartSeries(match, false, true).map(x => [x.id, x.value]),
    [['party', 13]]);
  assert.deepEqual(visibleChartSeries(match, false, false), []);
  assert.equal(Math.max(1, ...visibleChartSeries(match, false, true).map(x => x.value)), 13);
  assert.equal(Math.max(1, ...visibleChartSeries(match, true, false).map(x => x.value)), 6);
});

test('selector visual: diez temas, persistencia y icono del EXE', () => {
  const fs = require('node:fs');
  const path = require('node:path');
  const dashboard = fs.readFileSync(path.join(__dirname, '../src/FortniteChestTracker/Dashboard.cs'), 'utf8');
  const client = fs.readFileSync(path.join(__dirname, '../src/FortniteChestTracker/Assets/dashboard.js'), 'utf8');
  for (const theme of ['arena', 'tactical', 'studio', 'prism', 'carbon', 'arcade', 'glacier', 'solar', 'ember', 'blueprint']) {
    assert.match(dashboard, new RegExp('<option value="' + theme + '">'));
    assert.ok(client.includes('"' + theme + '"'), 'Tema no disponible: ' + theme);
  }
  assert.match(dashboard, /id="theme-select"/);
  assert.match(dashboard, /<img src="\{\{iconDataUrl\}\}"/);
  assert.doesNotMatch(dashboard, /Datos locales · Actualización cada 20 s/);
  assert.match(client, /localStorage\.setItem\(THEME_KEY, theme\)/);
  assert.match(client, /document\.documentElement\.dataset\.theme = theme/);
});

test('los cuatro estilos nuevos tienen paleta propia y Tactical HUD no tiene barra izquierda', () => {
  const fs = require('node:fs');
  const path = require('node:path');
  const dashboard = fs.readFileSync(path.join(__dirname, '../src/FortniteChestTracker/Dashboard.cs'), 'utf8');
  for (const theme of ['carbon', 'arcade', 'glacier', 'solar']) {
    assert.ok(dashboard.includes('html[data-theme="' + theme + '"] {'),
      'No se declara el tema ' + theme);
    assert.ok(dashboard.includes('html[data-theme="' + theme + '"] .'),
      'El tema no tiene sus propios elementos visuales: ' + theme);
  }
  assert.doesNotMatch(dashboard, /html\[data-theme="tactical"\] \.hero \{ border-left:/);
  assert.doesNotMatch(dashboard, /inset 3px 0 0 #b8f47f18/);
  // The original single-file favicon and header branding must remain intact.
  assert.match(dashboard, /<link rel="icon" type="image\/x-icon" href="\{\{iconDataUrl\}\}">/);
});

test('los primeros cuatro temas tienen una identidad visual estructural', () => {
  const fs = require('node:fs');
  const path = require('node:path');
  const dashboard = fs.readFileSync(path.join(__dirname, '../src/FortniteChestTracker/Dashboard.cs'), 'utf8');
  for (const theme of ['arena', 'tactical', 'studio', 'prism']) {
    assert.ok(dashboard.includes('html[data-theme="' + theme + '"] body {'),
      'No se personaliza el fondo de ' + theme);
    assert.ok(dashboard.includes('html[data-theme="' + theme + '"] .hero {'),
      'No se personaliza el hero de ' + theme);
    assert.ok(dashboard.includes('html[data-theme="' + theme + '"] .chart-fill'),
      'No se personaliza la grafica de ' + theme);
  }
  assert.match(dashboard, /html\[data-theme="studio"\] h2 \{[\s\S]*?font-family: Georgia/);
  assert.match(dashboard, /html\[data-theme="prism"\] \.hero \{[\s\S]*?backdrop-filter: blur/);
  assert.match(dashboard, /html\[data-theme="tactical"\] \.metric \.num[\s\S]*?font-family: Consolas/);
});

test('Ember y Blueprint tienen paletas y controles especificos', () => {
  const fs = require('node:fs');
  const path = require('node:path');
  const dashboard = fs.readFileSync(path.join(__dirname, '../src/FortniteChestTracker/Dashboard.cs'), 'utf8');
  for (const theme of ['ember', 'blueprint']) {
    assert.ok(dashboard.includes('html[data-theme="' + theme + '"] {'));
    assert.ok(dashboard.includes('html[data-theme="' + theme + '"] .hero {'));
    assert.ok(dashboard.includes('html[data-theme="' + theme + '"] .chart-fill'));
  }
});

test('Carbon centra su adorno en Party y Solar Flare reduce el sol', () => {
  const fs = require('node:fs');
  const path = require('node:path');
  const dashboard = fs.readFileSync(path.join(__dirname, '../src/FortniteChestTracker/Dashboard.cs'), 'utf8');
  assert.match(dashboard, /html\[data-theme="carbon"\] \.hero::after \{ content: none; \}/);
  assert.match(dashboard,
    /html\[data-theme="carbon"\] \.hero-squad::after \{[\s\S]*?left: 50%; top: 50%;[\s\S]*?transform: translate\(-50%, -50%\)/);
  assert.match(dashboard,
    /html\[data-theme="solar"\] \.hero::after \{[\s\S]*?font-size: 96px;/);
  assert.doesNotMatch(dashboard, /html\[data-theme="solar"\] \.hero::after \{[^}]*font-size: 210px/);
});

test('adornos: posición, escala y capa común para todos los diseños', () => {
  const fs = require('node:fs');
  const path = require('node:path');
  const dashboard = fs.readFileSync(path.join(__dirname, '../src/FortniteChestTracker/Dashboard.cs'), 'utf8');
  const base = dashboard.match(/\.hero::after \{\s*content: "✦";[\s\S]*?\n\s*\}/);
  assert.ok(base, 'Falta la caja común de adornos.');
  assert.match(base[0], /width: 140px; height: 140px; right: 6%; top: 50%/);
  assert.match(base[0], /transform: translateY\(-50%\)/);
  assert.match(base[0], /align-items: center; justify-content: center; text-align: center/);
  assert.match(base[0], /pointer-events: none; z-index: 0/);

  const motifs = ['prism', 'arcade', 'glacier', 'solar', 'ember', 'blueprint'];
  for (const theme of motifs) {
    const selector = 'html[data-theme="' + theme + '"] .hero::after {';
    const start = dashboard.indexOf(selector);
    assert.ok(start >= 0, 'Falta adorno de ' + theme);
    const end = dashboard.indexOf('}', start);
    const motif = dashboard.slice(start + selector.length, end);
    assert.doesNotMatch(motif, /\b(?:top|right|left|bottom|transform|width|height)\s*:/,
      'El adorno de ' + theme + ' sobreescribe su posicion o caja');
    const size = Number(motif.match(/font-size:\s*(\d+)px/)?.[1]);
    assert.ok(size >= 96 && size <= 130, 'Escala fuera de rango en ' + theme);
  }
  const carbon = dashboard.match(/html\[data-theme="carbon"\] \.hero-squad::after \{([^}]*)\}/);
  assert.ok(carbon);
  assert.match(carbon[1], /left: 50%; top: 50%/);
  assert.match(carbon[1], /width: 140px; height: 140px/);
  assert.match(carbon[1], /align-items: center; justify-content: center/);
});

test('Arena tiene esquinas normales y Studio Light usa contraste legible', () => {
  const fs = require('node:fs');
  const path = require('node:path');
  const dashboard = fs.readFileSync(path.join(__dirname, '../src/FortniteChestTracker/Dashboard.cs'), 'utf8');
  assert.doesNotMatch(dashboard, /clip-path:\s*polygon\(/);
  const arena = dashboard.match(/html\[data-theme="arena"\] \.hero \{([^}]*)\}/);
  assert.ok(arena);
  assert.match(arena[1], /border-radius: 10px/);
  assert.match(dashboard, /:root \{[^}]*color: var\(--text, #f5f6ff\)/);
  assert.match(dashboard, /html\[data-theme="studio"\] h1 \{ color: #243047; \}/);
  assert.match(dashboard, /html\[data-theme="studio"\] h1 span \{ color: #b85a36; \}/);
  assert.match(dashboard, /html\[data-theme="studio"\] \.metric \.num,[\s\S]*?html\[data-theme="studio"\] \.chart-value \{ color: #243047; \}/);
  assert.match(dashboard, /html\[data-theme="studio"\], html\[data-theme="glacier"\] \{\s*color: var\(--text\)/);
});

test('Arena, Tactical HUD y Studio Light usan geometria CSS discreta sin glifos', () => {
  const fs = require('node:fs');
  const path = require('node:path');
  const dashboard = fs.readFileSync(path.join(__dirname, '../src/FortniteChestTracker/Dashboard.cs'), 'utf8');
  for (const theme of ['arena', 'tactical', 'studio']) {
    const selector = 'html[data-theme="' + theme + '"] .hero::after {';
    const start = dashboard.indexOf(selector);
    assert.ok(start >= 0, 'Falta el adorno de ' + theme);
    const end = dashboard.indexOf('}', start);
    const style = dashboard.slice(start + selector.length, end);
    assert.match(style, /content:\s*"";/, 'El adorno debe ser una forma CSS, no un caracter');
    assert.doesNotMatch(style, /\b(?:top|right|left|bottom|transform|width|height)\s*:/,
      'El tema no debe sobrescribir el marco comun');
    assert.match(style, /background:/, 'Falta el motivo grafico de ' + theme);
  }
  const arena = dashboard.slice(dashboard.indexOf('html[data-theme="arena"] .hero::after {'),
    dashboard.indexOf('}', dashboard.indexOf('html[data-theme="arena"] .hero::after {')));
  assert.match(arena, /linear-gradient\(112deg/);
  assert.match(arena, /background-size: 140px 100px/);
  const tactical = dashboard.slice(dashboard.indexOf('html[data-theme="tactical"] .hero::after {'),
    dashboard.indexOf('}', dashboard.indexOf('html[data-theme="tactical"] .hero::after {')));
  assert.equal((tactical.match(/linear-gradient\(/g) || []).length, 8);
  const studio = dashboard.slice(dashboard.indexOf('html[data-theme="studio"] .hero::after {'),
    dashboard.indexOf('}', dashboard.indexOf('html[data-theme="studio"] .hero::after {')));
  assert.match(studio, /border-radius: 50%/);
  assert.match(studio, /radial-gradient\(circle at center/);
  assert.match(studio, /transparent 0 44px, var\(--hero-ornament\) 45px 46px/);
});

test('v0.4.0: la aplicación usa bandeja de Windows sin consola y mantiene el modo de pruebas', () => {
  const fs = require('node:fs');
  const path = require('node:path');
  const read = file => fs.readFileSync(path.join(__dirname, '..', file), 'utf8');
  const project = read('src/FortniteChestTracker/FortniteChestTracker.csproj');
  const program = read('src/FortniteChestTracker/Program.cs');
  const tray = read('src/FortniteChestTracker/TrayAppContext.cs');
  const scanner = read('src/FortniteChestTracker/ReplayScanner.cs');
  const runner = read('src/FortniteChestTracker/MonitorRunner.cs');
  assert.match(project, /<OutputType>WinExe<\/OutputType>/);
  assert.match(project, /<UseWindowsForms>true<\/UseWindowsForms>/);
  assert.match(project, /<TargetFramework>net10\.0-windows<\/TargetFramework>/);
  assert.match(program, /\[STAThread\]/);
  assert.match(program, /Application\.Run\(tray\)/);
  for (const option of ['--self-test', '--once', '--backfill-squad', '--no-browser', '--headless'])
    assert.ok(program.includes(option), 'Falta opción de mantenimiento ' + option);
  for (const item of ['Abrir dashboard', 'Abrir carpeta de datos', 'Ver registro de errores',
      'Salir de FortniteChestTracker'])
    assert.ok(tray.includes(item), 'Falta acceso desde la bandeja: ' + item);
  assert.match(tray, /_notification\.DoubleClick/);
  assert.match(tray, /await _runner\.StopAsync\(\)/);
  assert.match(scanner, /public int PendingCount/);
  assert.match(scanner, /public bool FolderAvailable/);
  assert.match(scanner, /public Action<string>\? AnalysisStarted/);
  assert.match(runner, /TimeSpan\.FromSeconds\(6\)/);
  assert.match(runner, /Dashboard\.Write\(_store\.Snapshot\(\), _dataFolder, Current, exportCsv\)/);
});

test('v0.4.0: el estado offline queda incorporado en el HTML y detecta el monitor detenido', () => {
  const fs = require('node:fs');
  const path = require('node:path');
  const read = file => fs.readFileSync(path.join(__dirname, '..', file), 'utf8');
  const dashboard = read('src/FortniteChestTracker/Dashboard.cs');
  const client = read('src/FortniteChestTracker/Assets/dashboard.js');
  assert.match(dashboard, /id="monitor-indicator"/);
  assert.match(dashboard, /id="monitor-data"/);
  assert.match(dashboard, /id="monitor-title"/);
  assert.match(dashboard, /MonitorStatus\? monitor = null/);
  assert.match(dashboard, /if \(exportCsv\) ExportCsv/);
  assert.match(client, /Date\.now\(\) - lastCheck > 45000/);
  assert.match(client, /setInterval\(renderMonitorStatus, 5000\)/);
  assert.match(client, /box\.dataset\.state = current/);
  assert.match(client, /Monitor desconectado/);
});

test('Ticket dorado: fórmula y tarjeta compatibles con los diez estilos', () => {
  const fs = require('node:fs');
  const path = require('node:path');
  const read = file => fs.readFileSync(path.join(__dirname, '..', file), 'utf8');
  const odds = read('src/FortniteChestTracker/TicketOdds.cs');
  const store = read('src/FortniteChestTracker/TrackerStore.cs');
  const dashboard = read('src/FortniteChestTracker/Dashboard.cs');
  assert.match(odds, /BaseDenominator = 137_500/);
  assert.match(odds, /Math\.Log\(1\.0 - multiplier \/ BaseDenominator\)/);
  assert.match(odds, /Percent\(ownLogSurvival\), Percent\(partyLogSurvival\)/);
  assert.match(odds, /At\(11, 7, 19, 1\.5, true\)/);
  assert.match(odds, /At\(11, 8, 2, 2\.0, true\)/);
  assert.match(odds, /At\(11, 14, 19, 1\.5, false\)/);
  assert.match(odds, /if \(!ChestTypes\.IsChest\(e\.Kind\)/);
  assert.match(store, /FROM chest_events e[\s\S]*JOIN matches m/);
  assert.match(store, /ChestEvents = chestEvents\.TryGetValue/);
  assert.match(dashboard, /class="ticket-card"/);
  assert.match(dashboard, /id="ticket-chance"/);
  assert.match(dashboard, /id="ticket-own-chests"/);
  assert.match(dashboard, /id="ticket-party-chance"/);
  assert.match(dashboard, /id="ticket-next-hour"/);
  assert.match(dashboard, /html\[data-theme="ember"\] \.ticket-card/);
  assert.doesNotMatch(dashboard, /ticket-progress|ticket-progressbar/,
    'No utilizar barras de progreso que sugieran garantías falsas');
});

test('La tarjeta del ticket usa los rótulos compactos solicitados', () => {
  const fs = require('node:fs');
  const path = require('node:path');
  const dashboard = fs.readFileSync(path.join(__dirname,
    '../src/FortniteChestTracker/Dashboard.cs'), 'utf8');
  assert.match(dashboard, /Party: <strong id="ticket-party-chance">/);
  assert.match(dashboard, /Próxima Treasure Hour: /);
  assert.match(dashboard, /ticket.NextHourConfirmed \? " \(confirmada\)" : " \(prevista\)"/);
  assert.doesNotMatch(dashboard, /La party es aproximada:|Calendario offline; las franjas futuras son previsiones\.|Party observada:|Próxima franja:|sin confirmar/);
  assert.match(dashboard, /Cada cofre es independiente: no existe progreso garantizado/);
});
