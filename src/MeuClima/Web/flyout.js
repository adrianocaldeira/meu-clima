// Bandeja: clima, regras ativas e controle rápido dos aparelhos.
(() => {
  'use strict';
  const { send, on, esc, nf, MODE, FAN, renderKeepFocus } = window.MC;
  const view = document.getElementById('view');
  const app = document.getElementById('app');

  let state = null;
  const expanded = new Set();
  // Valores mostrados antes da confirmação do host: { [deviceId]: { fields, sentAt } }.
  const overlay = {};
  const targetTimers = {};
  const TIMER_OPTS = [[0, 'Sem'], [3600, '1 h'], [7200, '2 h'], [14400, '4 h'], [28800, '8 h']];

  on('state', s => {
    state = s;
    for (const id of Object.keys(overlay)) {
      const d = s.devices.find(x => x.id === id);
      if (!d) { delete overlay[id]; continue; }
      if (targetTimers[id]) continue;
      // Mantém o valor otimista enquanto o host diz que o comando está pendente (a nuvem leva ~30 s).
      if (!d.pending) delete overlay[id];
    }
    render();
  });
  on('toast', MC.toast);

  const devView = d => overlay[d.id] ? Object.assign({}, d, overlay[d.id].fields) : d;
  const isPending = d => d.pending || !!overlay[d.id];

  function setOverlay(id, fields, sent = true) {
    const o = overlay[id] || (overlay[id] = { fields: {}, sentAt: 0 });
    Object.assign(o.fields, fields);
    if (sent) o.sentAt = Date.now();
  }

  function command(d, command, fields) {
    setOverlay(d.id, fields);
    send('command', { deviceId: d.id, command });
    render();
  }

  function bumpTarget(d, delta) {
    const v = devView(d);
    const next = Math.min(d.caps.max, Math.max(d.caps.min, v.targetC + delta));
    if (next === v.targetC) return;
    setOverlay(d.id, { targetC: next }, false);
    clearTimeout(targetTimers[d.id]);
    targetTimers[d.id] = setTimeout(() => {
      delete targetTimers[d.id];
      const value = overlay[d.id] ? overlay[d.id].fields.targetC : next;
      overlay[d.id].sentAt = Date.now();
      send('command', { deviceId: d.id, command: { kind: 'target', value } });
    }, 600);
    render();
  }

  // ---------- render ----------
  let lastHeight = 0;
  function reportHeight() {
    requestAnimationFrame(() => {
      const h = Math.ceil(app.getBoundingClientRect().height);
      if (h !== lastHeight) { lastHeight = h; send('resize', { height: h }); }
    });
  }

  function render() {
    if (!state) return;
    reportHeight();
    app.removeAttribute('aria-busy');
    document.getElementById('upd').textContent = state.updatedAt ? `atualizado ${state.updatedAt}` : 'sem leitura ainda';
    const fi = document.getElementById('foot-info');
    fi.textContent = MC.demo ? 'modo demonstração' : (state.weather && state.weather.readAt ? `clima de ${state.weather.readAt}` : '');
    const setupDone = state.setup && state.setup.credentials && state.setup.city && state.setup.pcDevice;
    const parts = [];
    if (state.account && state.account.error) parts.push(accountError(state.account.error));
    parts.push(weatherBand());
    if (!setupDone) parts.push(setupCard());
    else if (!state.devices.length && !state.updatedAt && !(state.account && state.account.error))
      parts.push(`<div class="empty"><b>Lendo os seus aparelhos…</b><span class="small">A primeira leitura da Electrolux pode levar alguns segundos.</span></div>`);
    else if (!state.devices.length) parts.push(`<div class="empty"><b>Nenhum aparelho encontrado</b><span class="small">Confira na central se a conta Electrolux certa está conectada.</span></div>`);
    else parts.push(`<div>${state.devices.map(deviceRow).join('')}</div>`);
    renderKeepFocus(view, parts.join(''));
  }

  function accountError(msg) {
    return `<div class="alert" role="alert"><div><b>Problema na conta Electrolux</b><span>${esc(msg)}</span></div>
      <button class="btn" data-act="central" data-tab="setup" data-k="acc-err">Resolver</button></div>`;
  }

  function weatherBand() {
    const w = state.weather;
    const pc = state.devices.find(d => d.isPcRoom);
    const cell = (lbl, v, s) => `<div><span class="lbl" title="${esc(lbl)}">${esc(lbl)}</span><span class="v tnum">${v}</span><span class="s tnum" title="${esc(s)}">${esc(s)}</span></div>`;
    const out = w ? cell('Fora', `${nf(w.tempC)} °C`, `sente ${nf(w.apparentC, 0)}° · ${nf(w.humidity, 0)}%`)
      : cell('Fora', '—', state.setup && !state.setup.city ? 'sem cidade' : 'sem dados');
    let room;
    if (pc) {
      const v = devView(pc);
      const s = !pc.connected ? 'sem conexão' : v.on ? `alvo ${v.targetC} · ${(MODE[v.mode] || v.mode).toLowerCase()}` : 'desligado';
      const off = pc.sensorOffsetC || 0;
      room = cell(pc.name, `${nf(pc.ambientC, 0)} °C`, off && pc.ambientC != null ? `${s} · sensor ${nf(pc.ambientC - off, 0)}` : s);
    } else room = cell('Cômodo do PC', '—', 'a definir');
    let peak;
    if (w && w.peakToday) peak = cell('Pico hoje', `${nf(w.peakToday.tempC, 0)} °C`, `às ${hourText(w.peakToday.time)}`);
    else if (w && w.peakTomorrow) peak = cell('Pico amanhã', `${nf(w.peakTomorrow.tempC, 0)} °C`, `às ${hourText(w.peakTomorrow.time)}`);
    else peak = cell('Pico hoje', '—', 'sem previsão');
    return `<div class="wx">${out}${room}${peak}</div>`;
  }
  const hourText = t => { const [h, m] = String(t || '').split(':'); return m && m !== '00' ? `${h}h${m}` : `${+h}h`; };

  /** Quem está no comando do aparelho agora, e por quê: você, uma regra agindo, de olho ou ninguém. */
  function who(d) {
    const ar = (state.activeRules || []).filter(r => r.deviceId === d.id);
    const cfg = id => (state.rules || []).find(r => r.id === id) || {};
    const semNome = t => String(t || '').startsWith(d.name + ' ') ? t.slice(d.name.length + 1) : t;
    const line = (kind, title, detail, extra = '') => `<div class="who ${kind}"><div><b>${esc(title)}</b><span class="tnum">${esc(detail)}</span></div>${extra}</div>`;
    if (d.pausedUntil)
      return line('manual', 'Você', `Você mexeu no ar. As regras voltam ${/^\d/.test(d.pausedUntil) ? 'às ' : ''}${d.pausedUntil}.`,
        `<button class="link" data-act="resume" data-id="${esc(d.id)}" data-k="resume:${esc(d.id)}">Retomar agora</button>`);
    const agindo = ar.find(r => (r.kind || 'acting') === 'acting' && !r.blockedBy);
    if (agindo) {
      const tipo = cfg(agindo.ruleId).tipo;
      const kind = tipo === 'janela-aberta' ? 'block' : tipo === 'modo-noite' ? 'night' : 'rule';
      const bar = agindo.progress != null ? `<div class="bar" role="progressbar" aria-label="Progresso" aria-valuemin="0" aria-valuemax="100" aria-valuenow="${Math.round(agindo.progress * 100)}"><i style="width:${Math.max(0, Math.min(1, agindo.progress)) * 100}%"></i></div>` : '';
      return line(kind, agindo.title, semNome(agindo.detail), bar);
    }
    const olho = ar.find(r => r.kind === 'watching');
    if (olho) return line('watch', olho.title, semNome(olho.detail));
    const espera = ar.find(r => r.kind === 'waiting');
    if (espera) return line('none', espera.title, semNome(espera.detail));
    const ligadas = (state.rules || []).filter(r => r.enabled && r.config && r.config.deviceId === d.id).map(r => r.name);
    return line('none', 'Nenhuma regra agindo', ligadas.length ? `Nenhuma regra está mexendo neste ar. Ligadas aqui: ${ligadas.join(', ')}.` : 'Nenhuma regra ligada para este aparelho.');
  }

  function setupCard() {
    const s = state.setup || {};
    const miss = [];
    if (!s.credentials) miss.push('conectar a conta Electrolux');
    if (!s.city) miss.push('escolher a cidade para o clima de fora');
    if (!s.pcDevice) miss.push('dizer em qual cômodo fica este PC');
    return `<div class="setup"><b>Configure o Meu Clima</b>
      <span class="small muted">Falta pouco para as automações funcionarem:</span>
      <ul>${miss.map(m => `<li>${m}</li>`).join('')}</ul>
      <button class="btn primary" data-act="central" data-tab="setup" data-k="setup">Configurar agora</button></div>`;
  }

  function deviceRow(d0) {
    const d = devView(d0);
    const id = esc(d.id);
    const off = !d.connected;
    const dis = off ? ' disabled' : '';
    const dot = off ? 'warn' : d.on ? (d.mode === 'HEAT' ? 'heat' : 'on') : '';
    const meta = off ? `amb&nbsp;${nf(d.ambientC, 0)}&nbsp;°C`
      : d.on ? `${esc((MODE[d.mode] || d.mode).toLowerCase())} · ${esc((FAN[d.fan] || d.fan).toLowerCase())} · amb&nbsp;${nf(d.ambientC, 0)}&nbsp;°C`
        : `desligado · amb&nbsp;${nf(d.ambientC, 0)}&nbsp;°C`;
    const chips = [];
    if (isPending(d0) && !off) chips.push(`<span class="chip pend" title="Comando enviado. A Electrolux leva cerca de 30 s para confirmar.">enviando ao ar…</span>`);
    if (off) chips.push(`<span class="chip warn">sem conexão${d.disconnectedSince ? ' desde ' + esc(d.disconnectedSince) : ''}</span>`);
    if (d.stopTimeSec > 0 && !d.nightMode) chips.push(`<span class="chip">desliga em ${durText(d.stopTimeSec)}</span>`);
    const noTarget = d.mode === 'FANONLY';
    const isOpen = expanded.has(d.id);
    return `<div class="dev${off ? ' offline' : ''}">
      <span class="dot ${dot}" aria-hidden="true"></span>
      <div style="min-width:0"><div class="n" title="${esc(d.name)}">${esc(d.name)}</div><div class="m tnum">${meta}</div></div>
      <div class="t">
        <button class="ib" data-act="dec" data-id="${id}" data-k="dec:${id}" aria-label="Diminuir alvo de ${esc(d.name)}"${off || noTarget || d.targetC <= d.caps.min ? ' disabled' : ''}>−</button>
        <span class="v tnum${d.on ? '' : ' off'}" aria-live="polite" aria-label="Alvo ${d.targetC} graus">${noTarget ? '—' : d.targetC}</span>
        <button class="ib" data-act="inc" data-id="${id}" data-k="inc:${id}" aria-label="Aumentar alvo de ${esc(d.name)}"${off || noTarget || d.targetC >= d.caps.max ? ' disabled' : ''}>+</button>
        <button class="pwr" data-act="power" data-id="${id}" data-k="pwr:${id}" aria-pressed="${!!d.on}" aria-label="Ligar ${esc(d.name)}"${dis}></button>
      </div>
      ${chips.length ? `<div class="chips">${chips.join('')}</div>` : ''}
      ${off ? '' : who(d)}
      ${off ? '' : `<button class="link tog" data-act="more" data-id="${id}" data-k="more:${id}" aria-expanded="${isOpen}">${isOpen ? 'Menos ajustes' : 'Mais ajustes'}</button>`}
      ${!off && isOpen ? moreBlock(d) : ''}
    </div>`;
  }

  function seg(label, act, id, opts, current) {
    return `<div class="kv"><span class="lbl">${label}</span><div class="seg" role="group" aria-label="${label}">${opts.map(([v, t]) =>
      `<button data-act="${act}" data-id="${esc(id)}" data-v="${esc(v)}" data-k="${act}:${esc(id)}:${esc(v)}" aria-pressed="${String(v) === String(current)}">${esc(t)}</button>`).join('')}</div></div>`;
  }

  function moreBlock(d) {
    const id = d.id;
    const fans = d.caps.fans.filter(f => !(f === 'TURBO' && d.mode === 'FANONLY'));
    const nightRule = (state.rules || []).find(r => r.tipo === 'modo-noite' && r.config && r.config.deviceId === id);
    const nc = nightRule ? nightRule.config : { durationHours: 4, stepC: 1, stepEveryMin: 60 };
    const nightDesc = `Sobe ${nf(nc.stepC)} °C a cada ${nc.stepEveryMin === 60 ? 'hora' : nc.stepEveryMin + ' min'} e desliga em ${nf(nc.durationHours)} h, mesmo com o PC desligado.`;
    const timerCur = TIMER_OPTS.some(([s]) => s === d.stopTimeSec) ? d.stopTimeSec : null;
    return `<div class="more" id="more-${esc(id)}">
      ${seg('Modo', 'mode', id, d.caps.modes.map(m => [m, MODE[m] || m]), d.mode)}
      ${seg('Ventilação', 'fan', id, fans.map(f => [f, FAN[f] || f]), d.fan)}
      <div class="row2">
        <div><span>Oscilar</span><button class="pwr" data-act="oscillate" data-id="${esc(id)}" data-k="osc:${esc(id)}" aria-pressed="${!!d.oscillate}" aria-label="Oscilar"></button></div>
        <div><span>Econômico</span><button class="pwr" data-act="eco" data-id="${esc(id)}" data-k="eco:${esc(id)}" aria-pressed="${!!d.eco}" aria-label="Econômico"></button></div>
      </div>
      ${seg('Desligar em', 'timer', id, TIMER_OPTS, timerCur)}
      <div class="night"><div><b>Modo noite${d.nightMode ? ' ativo' : ''}</b>${esc(nightDesc)}</div>
        <button class="btn${d.nightMode ? '' : ' primary'}" data-act="night" data-id="${esc(id)}" data-k="night:${esc(id)}">${d.nightMode ? 'Cancelar' : 'Ativar'}</button></div>
      ${d.pausedUntil ? '' : `<div class="row"><span class="small muted">As automações deste aparelho continuam ativas.</span><button class="link" data-act="pause" data-id="${esc(id)}" data-k="pause:${esc(id)}">Pausar 1 h</button></div>`}
    </div>`;
  }

  function durText(sec) {
    const h = Math.floor(sec / 3600), m = Math.round((sec % 3600) / 60);
    return h ? (m ? `${h}h${String(m).padStart(2, '0')}` : `${h} h`) : `${m} min`;
  }

  // ---------- eventos ----------
  document.addEventListener('click', e => {
    const b = e.target.closest('button[data-act]');
    if (!b || b.disabled) return;
    const act = b.dataset.act;
    const d0 = b.dataset.id && state && state.devices.find(x => x.id === b.dataset.id);
    const d = d0 && devView(d0);
    if (act !== 'menu') setMenu(false);
    switch (act) {
      case 'central': send('openCentral', b.dataset.tab ? { tab: b.dataset.tab } : {}); break;
      case 'menu': setMenu(document.getElementById('menu').hidden); break;
      case 'pauseAll':
        for (const x of state ? state.devices.filter(x => x.connected) : []) {
          setOverlay(x.id, { pausedUntil: 'daqui a 1 h' });
          send('pause', { deviceId: x.id, minutes: 60 });
        }
        render(); break;
      case 'checkUpdate': send('checkUpdate'); break;
      case 'quit': send('quit'); break;
      case 'dec': bumpTarget(d0, -1); break;
      case 'inc': bumpTarget(d0, +1); break;
      case 'power': command(d0, { kind: 'power', on: !d.on }, { on: !d.on }); break;
      case 'mode': {
        const f = { mode: b.dataset.v };
        if (b.dataset.v === 'FANONLY' && d.fan === 'TURBO') f.fan = 'HIGH';
        command(d0, { kind: 'mode', value: b.dataset.v }, f); break;
      }
      case 'fan': command(d0, { kind: 'fan', value: b.dataset.v }, { fan: b.dataset.v }); break;
      case 'oscillate': command(d0, { kind: 'oscillate', on: !d.oscillate }, { oscillate: !d.oscillate }); break;
      case 'eco': command(d0, { kind: 'eco', on: !d.eco }, { eco: !d.eco }); break;
      case 'timer': { const s = +b.dataset.v; command(d0, { kind: 'timer', seconds: s }, { stopTimeSec: s }); break; }
      case 'night': setOverlay(d0.id, { nightMode: !d.nightMode }); send('nightMode', { deviceId: d0.id, on: !d.nightMode }); render(); break;
      case 'pause': setOverlay(d0.id, { pausedUntil: 'daqui a 1 h' }); send('pause', { deviceId: d0.id, minutes: 60 }); render(); break;
      case 'resume': setOverlay(d0.id, { pausedUntil: null }); send('resume', { deviceId: d0.id }); render(); break;
      case 'more': if (expanded.has(d0.id)) expanded.delete(d0.id); else expanded.add(d0.id); render(); break;
    }
  });
  function setMenu(open) {
    document.getElementById('menu').hidden = !open;
    document.getElementById('menu-btn').setAttribute('aria-expanded', String(open));
    if (open) document.querySelector('#menu button').focus();
  }
  // A bandeja se esconde ao perder o foco; sem isso, reabriria com o menu aberto.
  window.addEventListener('blur', () => setMenu(false));
  document.addEventListener('mousedown', e => { if (!e.target.closest('#menu, #menu-btn')) setMenu(false); });
  document.addEventListener('keydown', e => {
    if (e.key !== 'Escape') return;
    if (!document.getElementById('menu').hidden) { setMenu(false); document.getElementById('menu-btn').focus(); }
    else send('closeFlyout');
  });

  send('ready');
})();
