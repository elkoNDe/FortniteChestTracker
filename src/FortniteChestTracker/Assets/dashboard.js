(() => {
  "use strict";

  const PERIOD_COUNT = { day: 14, week: 12, month: 12 };
  const fmtNumber = new Intl.NumberFormat("es-ES");

  function atMidnight(value) {
    const d = new Date(value);
    d.setHours(0, 0, 0, 0);
    return d;
  }

  function periodStart(value, unit) {
    const d = atMidnight(value);
    if (unit === "week") d.setDate(d.getDate() - (d.getDay() + 6) % 7);
    if (unit === "month") d.setDate(1);
    return d;
  }

  function shiftPeriod(value, unit, count) {
    const d = new Date(value);
    if (unit === "month") d.setMonth(d.getMonth() + count);
    else d.setDate(d.getDate() + count * (unit === "week" ? 7 : 1));
    return d;
  }

  function inRange(match, start, end) {
    const time = match.when.getTime();
    return Number.isFinite(time) && time >= start.getTime() && time < end.getTime();
  }

  function bucketsFor(matches, unit, count, now) {
    const currentStart = periodStart(now, unit);
    const groups = [];
    for (let index = count - 1; index >= 0; index--) {
      const start = shiftPeriod(currentStart, unit, -index);
      const end = shiftPeriod(start, unit, 1);
      let chests = 0;
      let party = 0;
      let games = 0;
      for (const match of matches) {
        if (inRange(match, start, end)) {
          chests += match.chests;
          party += match.party ?? match.chests;
          games++;
        }
      }
      groups.push({ start, end, chests, party, games });
    }
    return groups;
  }

  function visibleChartSeries(group, showOwner, showParty) {
    const series = [];
    if (showOwner) series.push({ id: "owner", label: "Tú", value: group.chests });
    if (showParty) series.push({ id: "party", label: "Party", value: group.party });
    return series;
  }

  function paginate(records, requestedPage, size) {
    const pages = Math.max(1, Math.ceil(records.length / size));
    const page = Math.min(pages, Math.max(1, requestedPage));
    const first = (page - 1) * size;
    return { page, pages, first, visible: records.slice(first, first + size) };
  }

  function playersForMatch(match) {
    if (Array.isArray(match.team) && match.team.length) {
      // The owner is always first, even when the stored rows have a different order.
      return [...match.team].sort((a, b) => Number(b.owner) - Number(a.owner));
    }
    // Old matches without a squad analysis retain their personal counters.
    return [{ name: match.player, owner: true,
      chests: match.chests, destroyed: match.destroyed, ammo: match.ammo }];
  }

  function lastNDaysStart(now, days) {
    const start = atMidnight(now);
    start.setDate(start.getDate() - (days - 1));
    return start;
  }

  // Node can import and test the period calculations without a browser.
  if (typeof module !== "undefined" && module.exports) {
    module.exports = { periodStart, shiftPeriod, inRange, bucketsFor, lastNDaysStart, paginate, playersForMatch, visibleChartSeries };
    return;
  }

  // Offline heartbeat: the HTML is refreshed by the monitor every ~15 s.
  // If the EXE has exited or crashed, the browser itself marks it disconnected,
  // even when the last dashboard.html is still on disk.
  const monitorNode = document.getElementById("monitor-data");
  const monitorSnapshot = monitorNode ? JSON.parse(monitorNode.textContent || "null") : null;
  function renderMonitorStatus() {
    const box = document.getElementById("monitor-indicator");
    if (!box) return;
    const names = {
      starting: "Iniciando monitor", watching: "Monitor activo",
      waiting: "Esperando repetición", processing: "Analizando repetición",
      error: "Atención: revisar monitor", stopped: "Monitor detenido",
      offline: "Monitor desconectado"
    };
    const lastCheck = Date.parse(monitorSnapshot?.checkedUtc ?? "");
    let current = monitorSnapshot?.state || "stopped";
    if (monitorSnapshot && current !== "stopped" &&
        (!Number.isFinite(lastCheck) || Date.now() - lastCheck > 45000)) {
      current = "offline";
    }
    box.dataset.state = current;
    document.getElementById("monitor-title").textContent = names[current] || names.offline;
    let details;
    if (!monitorSnapshot) details = "Sin información del proceso";
    else if (current === "offline") details = "Sin comprobaciones recientes; revisa la bandeja";
    else if (current === "stopped") details = "El contador ya no está vigilando partidas";
    else {
      const time = Number.isFinite(lastCheck)
        ? new Date(lastCheck).toLocaleTimeString("es-ES", {
            hour: "2-digit", minute: "2-digit", second: "2-digit"
          }) : "—";
      const pending = Number(monitorSnapshot.pendingCount) || 0;
      details = "Última revisión " + time + " · " + pending + " pendiente" +
        (pending === 1 ? "" : "s");
      if (current === "error" && monitorSnapshot.detail)
        details = monitorSnapshot.detail;
    }
    document.getElementById("monitor-details").textContent = details;
  }
  renderMonitorStatus();
  setInterval(renderMonitorStatus, 5000);

  const dataNode = document.getElementById("match-data");
  const matches = JSON.parse(dataNode.textContent || "[]").map(item => ({
    ...item,
    when: new Date(item.date)
  }));
  const KEY = "fortnite-chesttracker-dashboard-v2";
  const initial = { unit: "day", filter: "all", page: 1, size: 10, range: null, showOwner: true, showParty: true };
  let state = { ...initial };
  try {
    const saved = JSON.parse(sessionStorage.getItem(KEY) || "{}");
    if (saved.unit === "day" || saved.unit === "month") state.unit = saved.unit;
    if (typeof saved.showOwner === "boolean") state.showOwner = saved.showOwner;
    if (typeof saved.showParty === "boolean") state.showParty = saved.showParty;
    if (["all", "today", "week", "month", "7days", "30days", "bucket"].includes(saved.filter)) state.filter = saved.filter;
    if (Number.isSafeInteger(saved.size) && saved.size >= 10 &&
        saved.size <= 100 && saved.size % 10 === 0) state.size = saved.size;
    if (Number.isSafeInteger(saved.page) && saved.page > 0) state.page = saved.page;
    if (saved.range && Number.isFinite(Date.parse(saved.range.start)) && Number.isFinite(Date.parse(saved.range.end)))
      state.range = saved.range;
    if (state.filter === "bucket" && !state.range) state.filter = "all";
  } catch (_) { /* file:// origins may disallow sessionStorage */ }

  const $ = id => document.getElementById(id);
  const themes = ["arena", "tactical", "studio", "prism", "carbon", "arcade", "glacier", "solar", "ember", "blueprint"];
  const themeSelect = $("theme-select");
  const THEME_KEY = "fortnite-chesttracker-theme";
  // A theme is a visual preference, independent of the session's chart/filter state.
  // localStorage can be unavailable on some file:// origins.
  let theme = "arena";
  try {
    const stored = localStorage.getItem(THEME_KEY);
    if (themes.includes(stored)) theme = stored;
  } catch (_) { /* Retain the default theme when localStorage is blocked. */ }
  function applyTheme(nextTheme) {
    if (!themes.includes(nextTheme)) return;
    theme = nextTheme;
    document.documentElement.dataset.theme = theme;
    themeSelect.value = theme;
    try { localStorage.setItem(THEME_KEY, theme); } catch (_) { }
  }
  applyTheme(theme);
  themeSelect.addEventListener("change", event => applyTheme(event.target.value));
  const modeButtons = Array.from(document.querySelectorAll("[data-unit]"));
  const legendButtons = Array.from(document.querySelectorAll("[data-series]"));
  const historyBody = $("history-body");

  function save() {
    try { sessionStorage.setItem(KEY, JSON.stringify(state)); } catch (_) { }
  }

  function formatDate(date, options) {
    return new Intl.DateTimeFormat("es-ES", options).format(date);
  }

  function bucketLabel(bucket, unit) {
    if (unit === "month") return formatDate(bucket.start, { month: "short", year: "2-digit" });
    if (unit === "day") return formatDate(bucket.start, { day: "2-digit", month: "2-digit" });
    return formatDate(bucket.start, { day: "2-digit", month: "2-digit" });
  }

  function rangeLabel(start, end) {
    const last = new Date(end.getTime() - 1);
    return formatDate(start, { day: "2-digit", month: "short", year: "numeric" }) +
      " – " + formatDate(last, { day: "2-digit", month: "short", year: "numeric" });
  }

  function currentStats() {
    const now = new Date();
    const sets = [
      { id: "today", start: periodStart(now, "day"), end: shiftPeriod(periodStart(now, "day"), "day", 1) },
      { id: "week", start: periodStart(now, "week"), end: shiftPeriod(periodStart(now, "week"), "week", 1) },
      { id: "month", start: periodStart(now, "month"), end: shiftPeriod(periodStart(now, "month"), "month", 1) }
    ];
    for (const set of sets) {
      const rows = matches.filter(m => inRange(m, set.start, set.end));
      $(set.id + "-chests").textContent = fmtNumber.format(rows.reduce((sum, m) => sum + m.chests, 0));
      $(set.id + "-party").textContent = fmtNumber.format(rows.reduce((sum, m) => sum + (m.party ?? m.chests), 0));
      $(set.id + "-games").textContent = rows.length + (rows.length === 1 ? " partida" : " partidas");
    }
  }

  function renderChart() {
    legendButtons.forEach(button => {
      const active = button.dataset.series === "owner" ? state.showOwner : state.showParty;
      button.setAttribute("aria-pressed", String(active));
      button.classList.toggle("inactive", !active);
    });
    modeButtons.forEach(button => {
      const active = button.dataset.unit === state.unit;
      button.classList.toggle("active", active);
      button.setAttribute("aria-pressed", String(active));
    });
    const now = new Date();
    const groups = bucketsFor(matches, state.unit, PERIOD_COUNT[state.unit], now);
    const chart = $("period-chart");
    chart.replaceChildren();
    const total = groups.reduce((sum, item) => sum + item.chests, 0);
    const partyTotal = groups.reduce((sum, item) => sum + item.party, 0);
    const games = groups.reduce((sum, item) => sum + item.games, 0);
    $("period-subtitle").textContent = "Últimos " + groups.length + " " +
      (state.unit === "day" ? "días" : "meses") +
      " · Pulsa una barra para filtrar el historial";
    $("period-total").textContent = "Tú: " + fmtNumber.format(total) + " cofres";
    $("period-party").textContent = "Party: " + fmtNumber.format(partyTotal) + " cofres";
    $("period-games").textContent = games + (games === 1 ? " partida" : " partidas");

    const highest = Math.max(1, ...groups.flatMap(item =>
      visibleChartSeries(item, state.showOwner, state.showParty).map(series => series.value)));
    for (const group of groups) {
      const bar = document.createElement("button");
      bar.type = "button";
      bar.className = "chart-bar";
      if (state.filter === "bucket" && state.range &&
          state.range.start === group.start.toISOString()) bar.classList.add("selected");
      bar.title = rangeLabel(group.start, group.end) + ": tú " + group.chests +
      " cofres, party " + group.party + " observados, en " + group.games + " partidas";
      bar.setAttribute("aria-label", bar.title + ". Filtrar historial");
      const series = visibleChartSeries(group, state.showOwner, state.showParty);
      const count = document.createElement("span");
      count.className = "chart-value";
      count.textContent = series.length
        ? series.map(item => fmtNumber.format(item.value)).join(" / ") : "—";
      const rail = document.createElement("span");
      rail.className = "chart-rail";
      for (const item of series) {
        const fill = document.createElement("span");
        fill.className = item.id === "party" ? "chart-fill chart-fill-party" : "chart-fill";
        fill.style.height = item.value > 0
          ? Math.max(2, Math.round(100 * item.value / highest)) + "%" : "0";
        rail.append(fill);
      }
      const label = document.createElement("span");
      label.className = "chart-label";
      label.textContent = bucketLabel(group, state.unit);
      bar.append(count, rail, label);
      bar.addEventListener("click", () => {
        state.filter = "bucket";
        state.range = { start: group.start.toISOString(), end: group.end.toISOString() };
        state.page = 1;
        renderChart();
        renderHistory();
        save();
        $("history-panel").scrollIntoView({ behavior: "smooth", block: "start" });
      });
      chart.append(bar);
    }
  }

  function filterRange() {
    const now = new Date();
    if (state.filter === "all") return null;
    if (state.filter === "bucket" && state.range)
      return { start: new Date(state.range.start), end: new Date(state.range.end) };
    if (state.filter === "today" || state.filter === "week" || state.filter === "month") {
      const unit = state.filter === "today" ? "day" : state.filter;
      const start = periodStart(now, unit);
      return { start, end: shiftPeriod(start, unit, 1) };
    }
    if (state.filter === "7days" || state.filter === "30days")
      return { start: lastNDaysStart(now, state.filter === "7days" ? 7 : 30), end: shiftPeriod(periodStart(now, "day"), "day", 1) };
    return null;
  }

  function makeCell(row, value, className) {
    const cell = document.createElement("td");
    cell.textContent = String(value);
    if (className) cell.className = className;
    row.append(cell);
  }

  function renderHistory() {
    const range = filterRange();
    const filtered = range ? matches.filter(m => inRange(m, range.start, range.end)) : matches;
    const result = paginate(filtered, state.page, state.size);
    state.page = result.page;
    $("history-filter").value = state.filter;
    $("page-size").value = String(state.size);

    const title = state.filter === "bucket" && range
      ? "Periodo seleccionado: " + rangeLabel(range.start, range.end)
      : "Historial de partidas";
    $("history-title").textContent = title;
    $("history-subtitle").textContent = range ? "Filtro: " + rangeLabel(range.start, range.end) : "";
    const first = result.first;
    const visible = result.visible;
    historyBody.replaceChildren();
    for (const match of visible) {
      const players = playersForMatch(match);
      for (const [index, player] of players.entries()) {
        const row = document.createElement("tr");
        row.className = (player.owner ? "squad-owner" : "squad-mate") +
          (index === 0 ? " group-start" : "");
        if (index === 0) {
          const date = document.createElement("td");
          date.rowSpan = players.length;
          date.className = "date-cell";
          const start = document.createElement("div");
          start.className = "date-start";
          start.textContent = "Inicio: " + formatDate(match.when, {
            day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit"
          });
          const end = document.createElement("div");
          end.className = "date-end";
          end.textContent = "Fin: " + (match.end ? formatDate(new Date(match.end), {
            day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit"
          }) : "—");
          date.append(start, end);
          row.append(date);
          const duration = document.createElement("td");
          duration.rowSpan = players.length;
          duration.textContent = match.duration || "—";
          row.append(duration);
        }
        makeCell(row, player.name || "Jugador desconocido", "player-cell");
        if (index === 0) {
          const position = document.createElement("td");
          position.rowSpan = players.length;
          position.textContent = Number.isInteger(match.placement) && match.placement > 0
            ? "#" + match.placement : "—";
          position.className = match.placement === 1 ? "emphasis" : "muted";
          position.title = match.placement === 1 ? "Victoria" :
            (match.placement == null ? "Posición no disponible en el histórico" : "Posición final");
          row.append(position);
        }
        const unknown = player.chests === null || player.chests === undefined;
        makeCell(row, unknown ? "—" : player.chests, unknown ? "unobserved" : "emphasis");
        makeCell(row, player.destroyed ?? "—", unknown ? "unobserved" : "");
        makeCell(row, player.ammo ?? "—", unknown ? "unobserved" : "");
        if (unknown)
          row.title = "Fortnite no ha permitido relacionar al jugador con su personaje; no significa cero cofres.";
        historyBody.append(row);
      }
    }
    $("history-empty").hidden = filtered.length !== 0;
    $("history-tablewrap").hidden = filtered.length === 0;
    $("history-page").textContent = "Página " + state.page + " de " + result.pages;
    $("history-count").textContent = filtered.length === 0
      ? "0 partidas"
      : "Mostrando " + (first + 1) + "–" + (first + visible.length) + " de " + fmtNumber.format(filtered.length) + " partidas";
    $("history-total").textContent = "Tú: " + fmtNumber.format(filtered.reduce((total, m) => total + m.chests, 0)) + " cofres";
    $("history-party").textContent = "Party: " + fmtNumber.format(filtered.reduce((total, m) => total + (m.party ?? m.chests), 0)) + " cofres observados";
    $("page-prev").disabled = state.page === 1;
    $("page-next").disabled = state.page === result.pages;
  }

  for (const button of legendButtons) {
    button.addEventListener("click", () => {
      if (button.dataset.series === "owner") state.showOwner = !state.showOwner;
      else state.showParty = !state.showParty;
      renderChart();
      save();
    });
  }
  for (const button of modeButtons) {
    button.addEventListener("click", () => {
      state.unit = button.dataset.unit;
      renderChart();
      save();
    });
  }
  $("history-filter").addEventListener("change", event => {
    state.filter = event.target.value;
    state.page = 1;
    renderChart();
    renderHistory();
    save();
  });
  $("page-size").addEventListener("change", event => {
    state.size = Number(event.target.value);
    state.page = 1;
    renderHistory();
    save();
  });
  $("page-prev").addEventListener("click", () => {
    state.page--;
    renderHistory();
    save();
  });
  $("page-next").addEventListener("click", () => {
    state.page++;
    renderHistory();
    save();
  });
  $("reset-filter").addEventListener("click", () => {
    state.filter = "all";
    state.range = null;
    state.page = 1;
    renderChart();
    renderHistory();
    save();
  });
  currentStats();
  renderChart();
  renderHistory();
})();
