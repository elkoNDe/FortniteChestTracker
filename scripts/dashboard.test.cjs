const test = require('node:test');
const assert = require('node:assert/strict');
const {
  periodStart, shiftPeriod, inRange, bucketsFor, lastNDaysStart, paginate, playersForMatch
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
