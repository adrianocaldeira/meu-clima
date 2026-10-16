// Ponte UI ↔ host (contrato: docs/specs/2026-10-14-ponte-ui.md) e utilitários comuns às duas telas.
// Sem window.chrome.webview (HTML aberto direto no navegador) entra em modo demo com dados fictícios.
// Variações do demo pela URL: ?demo=setup (falta configurar), ?demo=erro (conta com erro), ?demo=vazio (sem histórico).
(() => {
  'use strict';
  const wv = window.chrome && window.chrome.webview;
  const handlers = {};

  function emit(type, data) {
    (handlers[type] || []).forEach(h => { try { h(data); } catch (e) { console.error(e); } });
  }
  function on(type, handler) { (handlers[type] = handlers[type] || []).push(handler); }
  on('state', s => applyTheme(s && s.settings && s.settings.theme));

  let send;
  if (wv) {
    wv.addEventListener('message', e => {
      let m = e.data;
      if (typeof m === 'string') { try { m = JSON.parse(m); } catch { return; } }
      if (m && m.type) emit(m.type, m.data);
    });
    send = (type, payload) => wv.postMessage(Object.assign({ type }, payload || {}));
  } else {
    send = createDemo(emit);
    document.documentElement.classList.add('demo');
  }

  /** "light"/"dark" fixam o tema; "system" (ou ausente) deixa o prefers-color-scheme do Windows decidir. */
  function applyTheme(theme) {
    const root = document.documentElement;
    if (theme === 'light' || theme === 'dark') root.dataset.theme = theme;
    else delete root.dataset.theme;
  }

  // ---------- utilitários ----------
  const ESC = { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' };
  const esc = v => String(v == null ? '' : v).replace(/[&<>"']/g, c => ESC[c]);
  const nf = (v, d = 1) => v == null || isNaN(v) ? '—' : Number(v).toLocaleString('pt-BR', { maximumFractionDigits: d, minimumFractionDigits: 0 });
  const money = v => v == null || isNaN(v) ? '—' : Number(v).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
  const hm = t => t ? String(t).slice(0, 5) : '';
  const MODE = { AUTO: 'Auto', COOL: 'Frio', HEAT: 'Quente', FANONLY: 'Ventilar', DRY: 'Secar' };
  const FAN = { AUTO: 'Auto', LOW: 'Baixa', MIDDLE: 'Média', HIGH: 'Alta', TURBO: 'Turbo' };
  // Enums de RuleConfig vêm com o nome do enum C#.
  const RMODE = { Auto: 'Auto', Cool: 'Frio', Heat: 'Quente', FanOnly: 'Ventilar' };
  const DAYS = ['dom', 'seg', 'ter', 'qua', 'qui', 'sex', 'sáb'];

  function daysText(days) {
    if (!days || !days.length) return 'nenhum dia';
    const s = [...days].sort((a, b) => a - b);
    if (s.length === 7) return 'todos os dias';
    if (s.join() === '1,2,3,4,5') return 'seg–sex';
    if (s.join() === '0,6') return 'fim de semana';
    const contiguous = s.every((d, i) => i === 0 || d === s[i - 1] + 1);
    if (contiguous && s.length > 2) return `${DAYS[s[0]]}–${DAYS[s[s.length - 1]]}`;
    return s.map(d => DAYS[d]).join(', ');
  }

  let toastTimer;
  function toast(data) {
    if (!data || !data.text) return;
    let el = document.getElementById('toast');
    if (!el) {
      el = document.createElement('div');
      el.id = 'toast'; el.className = 'toast'; el.setAttribute('role', 'status'); el.setAttribute('aria-live', 'polite');
      document.body.appendChild(el);
    }
    el.className = 'toast ' + (data.kind === 'error' ? 'error' : 'ok');
    el.textContent = data.text;
    el.hidden = false;
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => { el.hidden = true; }, data.kind === 'error' ? 8000 : 4000);
  }

  /** Re-renderiza um contêiner preservando o foco (pelo atributo data-k) e o cursor de campos de texto. */
  function renderKeepFocus(root, html) {
    const a = document.activeElement;
    const key = a && root.contains(a) ? a.getAttribute('data-k') : null;
    let sel = null;
    // Campo em edição mantém o que a pessoa digitou, mesmo que um `state` chegue no meio.
    const typed = key && a.tagName === 'INPUT' ? a.value : null;
    try { if (key && a.selectionStart != null) sel = [a.selectionStart, a.selectionEnd]; } catch { /* input sem seleção */ }
    const scroll = [...root.querySelectorAll('[data-scroll]')].map(e => [e.getAttribute('data-scroll'), e.scrollTop]);
    root.innerHTML = html;
    scroll.forEach(([k, top]) => { const e = root.querySelector(`[data-scroll="${k}"]`); if (e) e.scrollTop = top; });
    if (key) {
      const n = root.querySelector(`[data-k="${CSS.escape(key)}"]`);
      if (n) { if (typed != null && n.tagName === 'INPUT') n.value = typed; n.focus({ preventScroll: true }); if (sel) { try { n.setSelectionRange(sel[0], sel[1]); } catch { /* idem */ } } }
    }
  }

  window.MC = { send, on, demo: !wv, esc, nf, money, hm, MODE, FAN, RMODE, DAYS, daysText, toast, renderKeepFocus };

  // ======================================================================
  // Modo demo
  // ======================================================================
  function createDemo(emitFn) {
    const variant = new URLSearchParams(location.search).get('demo') || '';
    const pad = n => String(n).padStart(2, '0');
    const clock = (d = new Date()) => `${pad(d.getHours())}:${pad(d.getMinutes())}`;
    const inMin = m => clock(new Date(Date.now() + m * 60000));
    const later = (fn, ms = 250) => setTimeout(fn, ms);

    const caps = { modes: ['AUTO', 'COOL', 'HEAT', 'FANONLY'], fans: ['AUTO', 'LOW', 'MIDDLE', 'HIGH', 'TURBO'], min: 16, max: 30 };
    const devices = [
      { id: 'dev-escritorio', name: 'Escritório', model: 'XI09R', connected: true, disconnectedSince: null, on: true, mode: 'COOL', fan: 'AUTO',
        targetC: 24, ambientC: 25, eco: true, oscillate: false, stopTimeSec: 0, filter: 'GOOD', alerts: [], pending: false,
        pausedUntil: null, isPcRoom: true, nightMode: false, caps },
      { id: 'dev-sala', name: 'Sala', model: 'XI12F', connected: true, disconnectedSince: null, on: true, mode: 'AUTO', fan: 'AUTO',
        targetC: 24, ambientC: 26, eco: true, oscillate: true, stopTimeSec: 0, filter: 'GOOD', alerts: [], pending: false,
        pausedUntil: inMin(42), isPcRoom: false, nightMode: false, caps: Object.assign({}, caps, { modes: ['AUTO', 'COOL', 'HEAT', 'FANONLY', 'DRY'] }) },
      { id: 'dev-quarto', name: 'Quarto de hóspedes', model: 'XI09F', connected: false, disconnectedSince: '14:02', on: false, mode: 'COOL', fan: 'AUTO',
        targetC: 23, ambientC: 24, eco: false, oscillate: false, stopTimeSec: 0, filter: 'GOOD', alerts: [], pending: false,
        pausedUntil: null, isPcRoom: false, nightMode: false, caps },
    ];
    const rules = [
      { tipo: 'conforto', id: 'r-conforto', enabled: true, deviceId: 'dev-escritorio', minC: 23, maxC: 24, turboFromC: 26, maxTurboMin: 30,
        fanAtMin: true, allowHeat: false, preCoolApparentC: 30, preCoolLeadMin: 30, coolerOutsideC: 3, requirePresence: true,
        schedule: { days: [1, 2, 3, 4, 5], start: '08:00:00', end: '19:00:00' } },
      { tipo: 'sai-do-pc', id: 'r-saipc', enabled: true, deviceId: 'dev-escritorio', offOnLock: true, offOnLockMin: 0, offOnShutdown: true, ecoAfterMin: 20, ecoTargetC: 26, offAfterMin: 60, restoreOnReturn: false },
      { tipo: 'janela-aberta', id: 'r-janela', enabled: true, deviceId: 'dev-escritorio', riseC: 2, windowMin: 15, minOnMin: 15, cooldownMin: 60 },
      { tipo: 'janela-aberta', id: 'r-janela-sala', enabled: true, deviceId: 'dev-sala', riseC: 2, windowMin: 15, minOnMin: 15, cooldownMin: 60 },
    ];
    const S = {
      updatedAt: clock(),
      setup: { credentials: variant !== 'setup', city: variant !== 'setup', pcDevice: variant !== 'setup' },
      account: { error: variant === 'erro' ? 'O acesso à conta Electrolux expirou. Gere um novo token e cole na central.' : null },
      weather: variant === 'setup' ? null : {
        city: 'Campinas / SP', tempC: 31, apparentC: 33, humidity: 58, readAt: clock(new Date(Date.now() - 11 * 60000)),
        peakToday: { tempC: 34, time: '15:00' }, peakTomorrow: { tempC: 35, time: '14:00' } },
      devices: variant === 'setup' ? [] : devices,
      activeRules: [],
      rules: [],
      settings: { startWithWindows: true, weatherIntervalMin: 15, deviceIntervalMin: 1, notifications: true, averageWatts: 800, tariffPerKwh: 0.85,
        pcDeviceId: variant === 'setup' ? null : 'dev-escritorio', theme: 'system', checkUpdates: true },
      version: '0.1.1',
      update: { available: false, version: null, downloading: false },
    };
    let ruleConfigs = variant === 'setup' ? [] : rules;
    const devName = id => (S.devices.find(d => d.id === id) || {}).name || 'aparelho';

    function summary(c) {
      const dn = devName(c.deviceId);
      switch (c.tipo) {
        case 'conforto': return `${dn} · faixa ${c.minC}–${c.maxC} °C · frio econômico em ${c.maxC} °C${c.fanAtMin ? `, ventila em ${c.minC} °C` : ''}, turbo a partir de ${c.turboFromC} °C`;
        case 'sai-do-pc': return `${dn} · ${c.offOnLock ? (c.offOnLockMin ? `tela bloqueada ${c.offOnLockMin} min → desliga; ` : 'tela bloqueada → desliga na hora; ') : ''}sem uso ${c.ecoAfterMin} min → ${c.ecoTargetC} °C, ${c.offAfterMin} min → desliga${c.offOnShutdown ? '; PC desligado → desliga' : ''}`;
        case 'janela-aberta': return `${dn} · dentro sobe ${c.riseC} °C em ${c.windowMin} min no frio → desliga e avisa`;
        case 'modo-noite': return `${dn} · desliga em ${c.durationHours} h, sobe ${c.stepC} °C a cada ${c.stepEveryMin} min até ${c.maxTargetC} °C${c.dailyStart ? ` · todo dia às ${c.dailyStart.slice(0, 5)}` : ''}`;
        default: return '';
      }
    }
    const NAMES = { conforto: 'Conforto', 'sai-do-pc': 'Ausente', 'janela-aberta': 'Janela aberta', 'modo-noite': 'Modo noite' };

    function rebuild() {
      S.rules = ruleConfigs.map(c => ({ id: c.id, tipo: c.tipo, name: NAMES[c.tipo], enabled: c.enabled, summary: summary(c), config: c }));
      const cf = ruleConfigs.find(r => r.tipo === 'conforto' && r.enabled);
      const dev = cf && S.devices.find(d => d.id === cf.deviceId);
      S.activeRules = [];
      if (cf && dev && dev.on) {
        const turbo = dev.ambientC >= cf.turboFromC;
        S.activeRules.push({ ruleId: cf.id, deviceId: dev.id, title: turbo ? 'Conforto · turbo' : 'Conforto',
          detail: turbo ? `Dentro está ${dev.ambientC} °C, calor forte. Turbo até chegar a ${cf.maxC} °C, no máximo até ${inMin(18)}.`
            : dev.ambientC > cf.maxC ? `Dentro está ${dev.ambientC} °C, acima de ${cf.maxC} °C. Esfriando no modo econômico até ${cf.maxC} °C.`
              : `Dentro está ${dev.ambientC} °C. Mantendo ${cf.maxC} °C no modo econômico; ao chegar a ${cf.minC} °C, só ventila.`, progress: null,
          blockedBy: dev.pausedUntil ? `ajuste manual · volta às ${dev.pausedUntil}` : null });
      } else if (cf && dev && !dev.pausedUntil) {
        S.activeRules.push({ ruleId: cf.id, deviceId: dev.id, kind: 'watching', title: 'Conforto de olho',
          detail: dev.ambientC > cf.maxC ? `Dentro está ${dev.ambientC} °C, acima de ${cf.maxC} °C. Se continuar assim por 5 min, liga no modo econômico.`
            : `Dentro está ${dev.ambientC} °C, na faixa. Liga se passar de ${cf.maxC} °C por 5 min.`, progress: null, blockedBy: null });
      }
      const ja = ruleConfigs.find(r => r.tipo === 'janela-aberta' && r.enabled && r.deviceId === 'dev-sala');
      const sala = S.devices.find(d => d.id === 'dev-sala');
      if (ja && sala && sala.pausedUntil) {
        S.activeRules.push({ ruleId: ja.id, deviceId: sala.id, title: 'Janela aberta',
          detail: `Dentro subiu de 24 para 26 °C com o ar no frio: janela ou porta aberta? Ar desligado até ${inMin(35)} ou até você religar.`, progress: null,
          blockedBy: `ajuste manual · volta às ${sala.pausedUntil}` });
      }
      S.devices.forEach(d => {
        if (d.nightMode) {
          S.activeRules.push({ ruleId: 'modo-noite', deviceId: d.id, title: 'Modo noite', detail: `Em ${d.targetC} °C, subindo 1 °C a cada hora para você não sentir frio. Desliga às ${inMin(Math.round(d.stopTimeSec / 60))}.`, progress: 0.05, blockedBy: null });
        }
      });
      S.updatedAt = clock();
    }
    const pushState = () => { rebuild(); emitFn('state', JSON.parse(JSON.stringify(S))); };

    function applyCommand(d, c) {
      switch (c.kind) {
        case 'power': d.on = !!c.on; if (!d.on) d.nightMode = false; break;
        case 'mode': d.mode = c.value; if (d.mode === 'FANONLY' && d.fan === 'TURBO') d.fan = 'HIGH'; break;
        case 'fan': d.fan = c.value; break;
        case 'target': d.targetC = c.value; break;
        case 'timer': d.stopTimeSec = c.seconds; break;
        case 'oscillate': d.oscillate = !!c.on; break;
        case 'eco': d.eco = !!c.on; break;
      }
    }

    function demoInsights(days) {
      if (variant === 'vazio') {
        return { kpis: { runtimeTodayH: 0, runtimeYesterdayH: null, energyTodayKwh: 0, costToday: 0, energyMonthKwh: null, costMonth: null, minPerDegree7d: null, minPerDegree30d: null },
          chart: { from: new Date(Date.now() - days * 864e5).toISOString(), to: new Date().toISOString(), outside: [], devices: [] }, items: [] };
      }
      const to = new Date(); to.setSeconds(0, 0);
      const from = new Date(to.getTime() - days * 864e5);
      const step = (days === 1 ? 15 : 60) * 60000;
      const outside = [], office = [], sala = [];
      let inE = 26, inS = 26.5;
      for (let t = from.getTime(); t <= to.getTime(); t += step) {
        const dt = new Date(t), h = dt.getHours() + dt.getMinutes() / 60, dow = dt.getDay();
        const day = Math.floor((t - from.getTime()) / 864e5);
        const out = 24 + (6 + (day % 3)) * Math.sin((h - 9) / 24 * 2 * Math.PI) + 0.4 * Math.sin(t / 3.7e6);
        const onE = dow >= 1 && dow <= 5 && h >= 8.5 && h < 19 && out >= 24;
        const onS = h >= 19 && h < 23;
        inE += onE ? (23 - inE) * 0.35 : (Math.min(out, 29) - inE) * 0.12;
        inS += onS ? (24 - inS) * 0.3 : (Math.min(out, 28.5) - inS) * 0.1;
        const iso = dt.toISOString();
        outside.push([iso, Math.round(out * 10) / 10]);
        office.push([iso, Math.round(inE), onE]);
        sala.push([iso, Math.round(inS), onS]);
      }
      return {
        kpis: { runtimeTodayH: 10.6, runtimeYesterdayH: 9.1, energyTodayKwh: 5.8 * S.settings.averageWatts / 800, costToday: 5.8 * S.settings.averageWatts / 800 * S.settings.tariffPerKwh,
          energyMonthKwh: Math.round(150 * S.settings.averageWatts / 800), costMonth: Math.round(150 * S.settings.averageWatts / 800 * S.settings.tariffPerKwh),
          minPerDegree7d: 7, minPerDegree30d: 6 },
        chart: { from: from.toISOString(), to: to.toISOString(), outside,
          devices: [{ id: 'dev-escritorio', name: 'Escritório', points: office }, { id: 'dev-sala', name: 'Sala', points: sala }] },
        items: [
          { kind: 'warn', title: 'Quarto de hóspedes está sem conexão desde 14:02', body: 'Ficou fora do Wi-Fi 3 vezes esta semana. Verifique o sinal do roteador nesse cômodo.' },
          { kind: 'cool', title: 'O Escritório está esfriando 15% mais devagar', body: 'Leva 7 min por grau contra 6 min no mês passado, com o mesmo calor lá fora. Limpe o filtro.' },
          { kind: 'ok', title: '"Ausente" manteve o ar desligado por 2,3 h nesta semana', body: 'Cerca de 1,8 kWh a menos, pela estimativa.' },
          { kind: 'sun', title: 'Amanhã o pico será de 35 °C às 14:00', body: 'Sensação prevista de 37 °C: o Conforto liga o ar 30 min antes se dentro estiver em 24 °C.' },
        ],
      };
    }

    const CITIES = [
      { name: 'Campinas', region: 'São Paulo', latitude: -22.9, longitude: -47.06, timeZone: 'America/Sao_Paulo' },
      { name: 'Campina Grande', region: 'Paraíba', latitude: -7.23, longitude: -35.88, timeZone: 'America/Fortaleza' },
      { name: 'Campo Grande', region: 'Mato Grosso do Sul', latitude: -20.44, longitude: -54.65, timeZone: 'America/Campo_Grande' },
      { name: 'Curitiba', region: 'Paraná', latitude: -25.43, longitude: -49.27, timeZone: 'America/Sao_Paulo' },
      { name: 'Recife', region: 'Pernambuco', latitude: -8.05, longitude: -34.9, timeZone: 'America/Recife' },
    ];

    console.info('[Meu Clima] modo demo: sem host WebView2, dados fictícios.');

    return function demoSend(type, p) {
      p = p || {};
      console.debug('[demo] →', type, p);
      const dev = p.deviceId && S.devices.find(d => d.id === p.deviceId);
      switch (type) {
        case 'ready': later(pushState, 120); break;
        case 'command':
          if (!dev) return;
          dev.pending = true; dev.pausedUntil = inMin(60); applyCommand(dev, p.command);
          later(pushState, 200);
          later(() => { dev.pending = false; pushState(); }, 2500);
          break;
        case 'pause': if (dev) { dev.pausedUntil = inMin(p.minutes || 60); later(pushState); } break;
        case 'resume': if (dev) { dev.pausedUntil = null; later(pushState); } break;
        case 'nightMode':
          if (!dev) return;
          dev.nightMode = !!p.on; dev.stopTimeSec = p.on ? 4 * 3600 : 0; if (p.on) dev.on = true;
          later(pushState);
          break;
        case 'openCentral': window.open('central.html' + (p.tab ? `?tab=${encodeURIComponent(p.tab)}` : '') + (variant ? `${p.tab ? '&' : '?'}demo=${variant}` : ''), 'meuclima-central'); break;
        case 'closeFlyout': later(() => emitFn('toast', { kind: 'ok', text: 'Demo: a bandeja seria escondida.' })); break;
        case 'quit': later(() => emitFn('toast', { kind: 'ok', text: 'Demo: o app seria encerrado.' })); break;
        case 'getInsights': later(() => emitFn('insights', demoInsights(p.days === 7 ? 7 : 1)), 400); break;
        case 'saveRule': {
          const r = Object.assign({}, p.rule);
          if (!r.id) r.id = 'r-' + Math.random().toString(16).slice(2, 10);
          const i = ruleConfigs.findIndex(x => x.id === r.id);
          if (i >= 0) ruleConfigs[i] = r; else ruleConfigs.push(r);
          later(pushState); later(() => emitFn('toast', { kind: 'ok', text: 'Regra salva.' }));
          break;
        }
        case 'deleteRule': ruleConfigs = ruleConfigs.filter(x => x.id !== p.id); later(pushState); later(() => emitFn('toast', { kind: 'ok', text: 'Regra excluída.' })); break;
        case 'toggleRule': { const r = ruleConfigs.find(x => x.id === p.id); if (r) r.enabled = !!p.enabled; later(pushState); break; }
        case 'saveSettings':
          Object.assign(S.settings, p.settings || {});
          if (p.settings && 'pcDeviceId' in p.settings) { S.devices.forEach(d => { d.isPcRoom = d.id === p.settings.pcDeviceId; }); S.setup.pcDevice = !!p.settings.pcDeviceId; }
          later(pushState);
          break;
        case 'searchCity': {
          const q = String(p.query || '').trim().toLowerCase();
          later(() => emitFn('cities', CITIES.filter(c => c.name.toLowerCase().includes(q))), 500);
          break;
        }
        case 'setCity':
          S.setup.city = true;
          S.setup.cityName = p.city ? `${p.city.name} / ${p.city.region}` : null;
          S.weather = Object.assign({ tempC: 29, apparentC: 30, humidity: 61, readAt: clock(), peakToday: { tempC: 32, time: '15:00' }, peakTomorrow: null },
            S.weather || {}, { city: `${p.city.name} / ${p.city.region}` });
          later(pushState); later(() => emitFn('toast', { kind: 'ok', text: `Cidade definida: ${p.city.name}.` }));
          break;
        case 'setCredentials':
          later(() => {
            if (!p.apiKey || !p.refreshToken || p.refreshToken.length < 8) {
              emitFn('toast', { kind: 'error', text: 'Não foi possível conectar. Confira a API key e gere um novo refresh token.' });
              return;
            }
            S.setup.credentials = true; S.account.error = null;
            if (!S.devices.length) S.devices = devices;
            pushState();
            emitFn('toast', { kind: 'ok', text: `Conta conectada. ${S.devices.length} aparelhos encontrados.` });
          }, 900);
          break;
        case 'resize': break;
        case 'checkUpdate':
          later(() => { S.update = { available: true, version: '0.2.0', downloading: false }; pushState(); emitFn('toast', { kind: 'ok', text: 'Versão 0.2.0 disponível.' }); }, 800);
          break;
        case 'installUpdate':
          S.update.downloading = true; later(pushState);
          break;
        default: console.warn('[demo] mensagem fora do contrato:', type);
      }
    };
  }
})();
