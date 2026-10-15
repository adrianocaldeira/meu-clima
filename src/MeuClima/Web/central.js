// Central: regras, insights, ajustes e configuração (conta, cidade, cômodo do PC).
(() => {
  'use strict';
  const { send, on, esc, nf, money, hm, DAYS, daysText, renderKeepFocus } = window.MC;
  const side = document.getElementById('side');
  const main = document.getElementById('main');

  const IC = {
    brand: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true"><path d="M12 3v11"/><circle cx="12" cy="17" r="4"/><path d="M16 5h4M16 9h3"/></svg>',
    rules: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true"><path d="M4 7h10M4 17h6"/><circle cx="17" cy="7" r="3"/><circle cx="13" cy="17" r="3"/></svg>',
    insights: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true"><path d="M4 19V9M10 19V5M16 19v-7M22 19H2"/></svg>',
    settings: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true"><circle cx="12" cy="12" r="3"/><path d="M12 2v3M12 19v3M2 12h3M19 12h3M5 5l2 2M17 17l2 2M5 19l2-2M17 7l2-2"/></svg>',
    setup: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true"><path d="M12 21s-7-6.2-7-11a7 7 0 0 1 14 0c0 4.8-7 11-7 11z"/><circle cx="12" cy="10" r="2.5"/></svg>',
  };
  const INS_ICON = { warn: '!', cool: '↘', ok: '✓', sun: '☀' };

  // ---------- definição das regras (campos e padrões do contrato RuleConfig) ----------
  const num = (k, label, unit, min, max, step, hint) => ({ k, t: 'num', label, unit, min, max, step, hint });
  const bool = (k, label, hint) => ({ k, t: 'bool', label, hint });
  const TIPOS = {
    'conforto': {
      name: 'Conforto', desc: 'Mantém dentro na faixa que você escolher, gastando o mínimo: frio econômico no topo, só ventilar no piso e turbo só no calor forte.',
      defaults: { minC: 23, maxC: 24, turboFromC: 26, maxTurboMin: 30, fanAtMin: true, allowHeat: false, preCoolApparentC: 30, preCoolLeadMin: 30, coolerOutsideC: 3, requirePresence: true, schedule: null },
      sections: [
        { title: 'Faixa de conforto', fields: [{ k: 'deviceId', t: 'device', label: 'Aparelho' },
          num('minC', 'Mínima', '°C', 16, 30, 1, 'O sensor do aparelho mede graus inteiros.'),
          num('maxC', 'Máxima', '°C', 16, 30, 1, 'Acima dela por 5 min, liga em Frio econômico com este alvo.')] },
        { title: 'Como economiza', fields: [
          bool('fanAtMin', 'Só ventilar ao chegar na mínima', 'Desliga o compressor e mantém o ar circulando. Desligado: o ar desliga.'),
          bool('allowHeat', 'Aquecer abaixo da mínima', 'Desligado: nunca aquece. Por isso o Conforto não usa o modo Auto, que aquece sozinho.'),
          num('coolerOutsideC', 'Avisar quando a sensação lá fora estiver mais fresca em', '°C', 1, 15, 0.5, 'Abrir a janela resfria sem gastar energia.')] },
        { title: 'Calor forte', fields: [
          num('turboFromC', 'Turbo a partir de', '°C', 17, 35, 1, 'Dentro, por 5 min. O turbo vai até a máxima.'),
          num('maxTurboMin', 'Turbo por no máximo', 'min', 5, 120, 5),
          num('preCoolApparentC', 'Antecipar se a sensação prevista passar de', '°C', 20, 45, 0.5, 'Liga antes, com dentro já na máxima, para não precisar do turbo.'),
          num('preCoolLeadMin', 'Antecipar em', 'min', 5, 180, 5)] },
        { title: 'Quando vale', fields: [
          bool('requirePresence', 'Só com alguém no PC', 'Com a tela bloqueada ou sem uso do teclado e mouse, não liga nada.'),
          { k: 'schedule', t: 'schedule', label: 'Só em dias e horários' }] },
      ],
    },
    'sai-do-pc': {
      name: 'Ausente', desc: 'Tela bloqueada desliga o ar; sem uso do PC, alivia e depois desliga. Na volta, as regras decidem se ele liga de novo.',
      defaults: { offOnLock: true, offOnLockMin: 0, ecoAfterMin: 20, ecoTargetC: 26, offAfterMin: 60, restoreOnReturn: false },
      sections: [
        { title: 'Tela bloqueada', fields: [{ k: 'deviceId', t: 'device', label: 'Aparelho do cômodo do PC' },
          bool('offOnLock', 'Desligar ao bloquear a tela', 'Vale também quando o PC entra em suspensão.'),
          num('offOnLockMin', 'Depois de', 'min', 0, 60, 1, '0 = na hora. 3 min evita liga-desliga em pausas curtas.')] },
        { title: 'Sem uso do PC', fields: [
          num('ecoAfterMin', 'Aliviar depois de', 'min', 5, 240, 5, 'Sem uso do teclado e mouse, com a tela desbloqueada.'),
          num('ecoTargetC', 'Alvo ao aliviar', '°C', 16, 30, 1, 'Passa para Frio com este alvo.'),
          num('offAfterMin', 'Desligar depois de', 'min', 10, 480, 5)] },
        { title: 'Quando você volta', fields: [
          bool('restoreOnReturn', 'Voltar ao ajuste anterior', 'Desligado: as regras ativas, como o Conforto, decidem se o ar liga.')] },
      ],
    },
    'janela-aberta': {
      name: 'Janela aberta', desc: 'Se o ar está no frio e dentro esquenta mesmo assim, desliga e avisa.',
      defaults: { riseC: 2, windowMin: 15, minOnMin: 15, cooldownMin: 60 },
      sections: [{ title: 'Detecção', fields: [{ k: 'deviceId', t: 'device', label: 'Aparelho' },
        num('riseC', 'Dentro sobe', '°C', 1, 10, 1, 'O sensor do aparelho mede graus inteiros.'),
        num('windowMin', 'Em até', 'min', 5, 120, 5),
        num('minOnMin', 'Ligado no frio há pelo menos', 'min', 5, 120, 5),
        num('cooldownMin', 'Esperar antes de agir de novo', 'min', 10, 480, 5)] }],
    },
    'modo-noite': {
      name: 'Modo noite', desc: 'Programa o aparelho para desligar sozinho e sobe o alvo aos poucos durante a noite.',
      defaults: { durationHours: 4, stepC: 1, stepEveryMin: 60, maxTargetC: 28, dailyStart: null },
      sections: [{ title: 'Noite', fields: [{ k: 'deviceId', t: 'device', label: 'Aparelho' },
        num('durationHours', 'Desligar depois de', 'h', 1, 12, 1, 'Usa o timer do próprio aparelho: desliga mesmo com o PC desligado.'),
        num('stepC', 'Subir o alvo em', '°C', 1, 3, 1),
        num('stepEveryMin', 'A cada', 'min', 15, 240, 15),
        num('maxTargetC', 'Alvo máximo', '°C', 16, 30, 1),
        { k: 'dailyStart', t: 'optTime', label: 'Ativar todo dia às', hint: 'Desligado: ative pela bandeja, em "Mais ajustes".' }] }],
    },
  };
  const ORDER = Object.keys(TIPOS);

  // ---------- estado da tela ----------
  let state = null;
  let tab = new URLSearchParams(location.search).get('tab');
  let tabFromUser = !!tab;
  let insights = null, insightsDays = 1, insightsPending = false;
  let editing = null;      // { cfg, isNew, errors, confirmDelete }
  let picking = false;
  const draft = { apiKey: '', refreshToken: '', cityQuery: '' };
  let showKey = false, showTok = false, credBusy = false;
  let cities = null, citiesBusy = false;
  let updateChecking = false, updateChecked = false;

  const setupDone = () => state && state.setup && state.setup.credentials && state.setup.city && state.setup.pcDevice;
  const devName = id => { const d = state.devices.find(x => x.id === id); return d ? d.name : 'aparelho removido'; };

  on('state', s => {
    state = s;
    if (!tab) tab = setupDone() ? 'rules' : 'setup';
    render();
    if (tab === 'insights' && !insights && !insightsPending) requestInsights();
  });
  on('insights', data => { insights = data; insightsPending = false; if (tab === 'insights') render(); });
  on('cities', list => { cities = Array.isArray(list) ? list : []; citiesBusy = false; render(); });
  on('navigate', d => { if (d && d.tab) go(d.tab); });
  on('toast', t => {
    MC.toast(t);
    if (credBusy) {
      credBusy = false;
      if (t && t.kind === 'ok') {
        draft.apiKey = ''; draft.refreshToken = '';
        if (document.activeElement && document.activeElement.dataset.d) document.activeElement.blur();
      }
    }
    if (updateChecking) { updateChecking = false; updateChecked = !!t && t.kind === 'ok'; }
    render();
  });

  function go(t) {
    if (!['rules', 'insights', 'settings', 'setup'].includes(t)) return;
    tab = t; tabFromUser = true; editing = null; picking = false;
    render();
    main.scrollTop = 0;
    if (t === 'insights' && !insightsPending) requestInsights();
  }
  function requestInsights() { insightsPending = true; send('getInsights', { days: insightsDays }); }

  // ---------- render ----------
  function render() {
    if (!state) return;
    renderSide();
    let html;
    if (tab === 'rules') html = editing ? editorView() : picking ? pickerView() : rulesView();
    else if (tab === 'insights') html = insightsView();
    else if (tab === 'settings') html = settingsView();
    else html = setupView();
    renderKeepFocus(main, html);
  }

  function renderSide() {
    const incomplete = !setupDone();
    const item = (k, label, extra = '') => `<button data-go="${k}" data-k="go:${k}"${tab === k ? ' aria-current="page"' : ''}>${IC[k]}<span>${label}</span>${extra}</button>`;
    const setupItem = item('setup', incomplete ? 'Configuração' : 'Conta e local', incomplete ? '<span class="pend-dot" aria-label="pendente"></span>' : '');
    renderKeepFocus(side, `<div class="brand">${IC.brand}Meu Clima</div>
      ${incomplete ? setupItem : ''}${item('rules', 'Regras')}${item('insights', 'Insights')}${item('settings', 'Ajustes')}${incomplete ? '' : setupItem}
      <div class="ft"><span class="mono small muted">versão ${esc(state.version || '')}</span>${MC.demo ? '<span class="ex" style="justify-self:start">demonstração</span>' : ''}</div>`);
  }

  const accountAlert = () => state.account && state.account.error
    ? `<div class="alert" role="alert"><div><b>Problema na conta Electrolux</b><span>${esc(state.account.error)}</span></div>${tab !== 'setup' ? '<button class="btn" data-go="setup" data-k="alert-go">Resolver</button>' : ''}</div>` : '';

  // ---------- Regras ----------
  function liveBlock(r) {
    const blocked = !!r.blockedBy;
    return `<div class="live${blocked ? ' blocked' : ''}"><div class="top"><b>${esc(r.title)}</b>${blocked ? '<span class="chip warn">pausada</span>' : '<span class="chip cool">rodando</span>'}</div>
      ${r.progress != null ? `<div class="bar" role="progressbar" aria-label="Progresso" aria-valuemin="0" aria-valuemax="100" aria-valuenow="${Math.round(r.progress * 100)}"><i style="width:${Math.max(0, Math.min(1, r.progress)) * 100}%"></i></div>` : ''}
      <div class="d tnum">${esc([devName(r.deviceId), r.detail].filter(Boolean).join(' · '))}</div>
      ${blocked ? `<div class="d">pausada: ${esc(r.blockedBy)}</div>` : ''}</div>`;
  }

  function rulesView() {
    const rules = state.rules || [];
    // "De olho" e "em espera" não comandam nada: ficam fora de "Rodando agora".
    const active = (state.activeRules || []).filter(a => (a.kind || 'acting') === 'acting');
    const running = new Map(active.map(a => [a.ruleId, a.blockedBy ? "blocked" : "run"]));
    const sorted = [...rules].sort((a, b) => (running.has(b.id) - running.has(a.id)) || (ORDER.indexOf(a.tipo) - ORDER.indexOf(b.tipo)));
    const missing = ORDER.filter(t => !rules.some(r => r.tipo === t));
    return `${accountAlert()}
      <div class="hrow"><h2>Regras</h2><button class="btn primary" data-act="new" data-k="new">+ Nova regra</button></div>
      ${active.length ? `<h3 class="sec">Rodando agora</h3><div class="grid">${active.map(liveBlock).join('')}</div>` : ''}
      ${rules.length ? `<h3 class="sec">Suas regras</h3><div class="grid">${sorted.map(r => ruleCard(r, running.get(r.id))).join('')}</div>`
        : `<div class="empty"><b>Nenhuma regra ainda</b><span class="small">Comece por uma das sugestões abaixo ou crie a sua.</span></div>`}
      ${missing.length ? `<h3 class="sec">Sugestões</h3><div class="grid">${missing.map(t => `<div class="sg"><div><b>${TIPOS[t].name}</b><span>${TIPOS[t].desc}</span></div>
        <button class="btn" data-act="create" data-tipo="${t}" data-k="sug:${t}">Criar</button></div>`).join('')}</div>` : ''}`;
  }

  function ruleSource(r) {
    const c = r.config || {};
    const parts = [];
    if (c.deviceId) parts.push(devName(c.deviceId));
    if (r.tipo === 'conforto') parts.push(c.schedule ? `${daysText(c.schedule.days)} ${hm(c.schedule.start)}–${hm(c.schedule.end)}` : 'qualquer dia e hora');
    if (r.tipo === 'modo-noite') parts.push(c.dailyStart ? `todo dia às ${hm(c.dailyStart)}` : 'ativação pela bandeja');
    return parts.join(' · ');
  }

  function ruleCard(r, running) {
    const known = !!TIPOS[r.tipo];
    const c = r.config || {};
    const id = esc(r.id);
    let body = `<p class="sum">${esc(r.summary || '')}</p>`;
    if (r.tipo === 'conforto') {
      const p = v => `<span class="pill">${esc(v)}</span>`, pc = v => `<span class="pill c">${esc(v)}</span>`;
      body = `<div class="steps">
        <div class="step"><span class="k">Faixa</span><span>${pc(c.minC + '–' + c.maxC + ' °C')}${c.requirePresence ? ' com alguém no PC' : ''}</span></div>
        <div class="step"><span class="k">Acima</span><span>${p('Frio')} ${p('econômico')} alvo ${pc(c.maxC + ' °C')}</span></div>
        <div class="step"><span class="k">Turbo</span><span>a partir de ${pc(c.turboFromC + ' °C')} até ${c.maxC} °C, no máximo ${nf(c.maxTurboMin, 0)} min</span></div>
        <div class="step"><span class="k">Mínima</span><span>${c.fanAtMin ? `${p('Ventilar')} sem compressor` : 'desliga o ar'}${c.allowHeat ? ` · abaixo dela, ${p('Quente')}` : ''}</span></div>
        <div class="step"><span class="k">Antes</span><span>sensação prevista a partir de ${pc(nf(c.preCoolApparentC) + ' °C')}: liga ${nf(c.preCoolLeadMin, 0)} min antes</span></div>
      </div>`;
    }
    return `<div class="rule${running === 'run' ? ' running' : ''}${r.enabled ? '' : ' disabled'}">
      <div class="hd"><div><b>${esc(r.name || (TIPOS[r.tipo] || {}).name || r.tipo)}${running === 'run' ? '<span class="chip cool">rodando</span>' : running ? '<span class="chip warn">pausada</span>' : ''}${r.enabled ? '' : '<span class="chip">desativada</span>'}</b>
        <div class="src">${esc(ruleSource(r))}</div></div>
        <button class="pwr" data-act="toggle" data-id="${id}" data-k="tg:${id}" aria-pressed="${!!r.enabled}" aria-label="Regra ${esc(r.name)} ativa"></button></div>
      ${body}
      ${known ? `<div class="ft"><button class="btn" data-act="edit" data-id="${id}" data-k="ed:${id}">Editar</button></div>` : ''}
    </div>`;
  }

  function pickerView() {
    return `<div class="hrow"><h2>Nova regra</h2><button class="btn" data-act="cancel" data-k="cancel">Voltar</button></div>
      <p class="muted" style="margin:0">Escolha o que a regra faz. Você ajusta os detalhes no próximo passo.</p>
      <div class="picker">${ORDER.map(t => `<button data-act="create" data-tipo="${t}" data-k="pick:${t}"><b>${TIPOS[t].name}</b><span>${TIPOS[t].desc}</span></button>`).join('')}</div>`;
  }

  function newRule(tipo) {
    const def = TIPOS[tipo];
    const cfg = Object.assign({ tipo, enabled: true }, JSON.parse(JSON.stringify(def.defaults)));
    if (def.sections.some(s => s.fields.some(f => f.t === 'device'))) {
      const pc = state.settings && state.settings.pcDeviceId;
      cfg.deviceId = pc || (state.devices[0] && state.devices[0].id) || null;
    }
    editing = { cfg, isNew: true, errors: {}, confirmDelete: false };
    picking = false;
    render(); main.scrollTop = 0;
  }

  function editRule(id) {
    const r = state.rules.find(x => x.id === id);
    if (!r || !TIPOS[r.tipo]) return;
    const cfg = Object.assign({ tipo: r.tipo, id: r.id, enabled: r.enabled }, JSON.parse(JSON.stringify(r.config || {})));
    editing = { cfg, isNew: false, errors: {}, confirmDelete: false };
    render(); main.scrollTop = 0;
  }

  function fieldHtml(f, cfg, errors) {
    const v = cfg[f.k];
    const err = errors[f.k] ? `<span class="err" id="err-${f.k}">${esc(errors[f.k])}</span>` : '';
    const hint = f.hint ? `<span class="hint">${esc(f.hint)}</span>` : '';
    const inv = errors[f.k] ? ' invalid" aria-invalid="true" aria-describedby="err-' + f.k : '';
    const fid = 'f-' + f.k;
    switch (f.t) {
      case 'num':
        return `<div class="fld"><label for="${fid}">${f.label}</label><span class="unit"><input id="${fid}" class="inp num${inv}" type="number" inputmode="decimal" data-f="${f.k}" data-k="${fid}" min="${f.min}" max="${f.max}" step="${f.step}" value="${esc(v == null || (typeof v === 'number' && isNaN(v)) ? '' : v)}"><em>${f.unit}</em></span>${hint}${err}</div>`;
      case 'bool':
        return `<div class="fld"><span class="lb" id="${fid}-l">${f.label}</span><button class="pwr" data-fb="${f.k}" data-k="${fid}" aria-pressed="${!!v}" aria-labelledby="${fid}-l"></button>${hint}${err}</div>`;
      case 'select':
        return `<div class="fld"><label for="${fid}">${f.label}</label><select id="${fid}" class="inp" data-f="${f.k}" data-k="${fid}">${f.opts.map(([val, t]) => `<option value="${val}"${val === v ? ' selected' : ''}>${t}</option>`).join('')}</select>${hint}${err}</div>`;
      case 'device': {
        const devs = state.devices;
        const opts = devs.map(d => `<option value="${esc(d.id)}"${d.id === v ? ' selected' : ''}>${esc(d.name)}${d.isPcRoom ? ' (cômodo do PC)' : ''}</option>`).join('');
        const orphan = v && !devs.some(d => d.id === v) ? `<option value="${esc(v)}" selected>aparelho removido</option>` : '';
        return `<div class="fld"><label for="${fid}">${f.label}</label><select id="${fid}" class="inp${inv}" data-f="${f.k}" data-k="${fid}">${!v ? '<option value="" selected>Escolha…</option>' : ''}${orphan}${opts}</select>${hint}${err}</div>`;
      }
      case 'time':
        return `<div class="fld"><label for="${fid}">${f.label}</label><input id="${fid}" class="inp${inv}" type="time" data-f="${f.k}" data-time="1" data-k="${fid}" value="${esc(hm(v))}">${hint}${err}</div>`;
      case 'optTime':
        return `<div class="fld"><span class="lb" id="${fid}-l">${f.label}</span><span class="unit">
          ${v ? `<input class="inp" type="time" aria-labelledby="${fid}-l" data-f="${f.k}" data-time="1" data-k="${fid}-t" value="${esc(hm(v))}">` : ''}
          <button class="pwr" data-fopt="${f.k}" data-k="${fid}" aria-pressed="${!!v}" aria-labelledby="${fid}-l"></button></span>${hint}${err}</div>`;
      case 'schedule': {
        const s = v;
        let inner = '';
        if (s) {
          inner = `<div style="grid-column:1/-1;display:grid;gap:8px;padding-top:4px">
            <div class="days" role="group" aria-label="Dias da semana">${DAYS.map((n, i) => `<button data-day="${i}" data-k="day:${i}" aria-pressed="${s.days.includes(i)}">${n}</button>`).join('')}</div>
            <div class="unit"><label for="f-s-start" class="small">Das</label><input id="f-s-start" class="inp" type="time" data-sched="start" data-k="f-s-start" value="${esc(hm(s.start))}">
            <label for="f-s-end" class="small">às</label><input id="f-s-end" class="inp" type="time" data-sched="end" data-k="f-s-end" value="${esc(hm(s.end))}"></div></div>`;
        }
        return `<div class="fld"><span class="lb" id="${fid}-l">${f.label}</span><button class="pwr" data-fsched="1" data-k="${fid}" aria-pressed="${!!s}" aria-labelledby="${fid}-l"></button>
          ${s ? '' : '<span class="hint">Desligado: vale para qualquer dia e hora.</span>'}${inner}${err}</div>`;
      }
    }
    return '';
  }

  function editorView() {
    const { cfg, isNew, errors, confirmDelete } = editing;
    const def = TIPOS[cfg.tipo];
    const hasErr = Object.keys(errors).length > 0;
    const forms = def.sections.map(s => `<div class="card"><h3>${s.title}</h3><div>${s.fields.map(f => fieldHtml(f, cfg, errors)).join('')}</div></div>`).join('');
    const aside = `<div class="card"><h3>${def.name}</h3><p class="small muted" style="margin:0">${def.desc}</p>
      <div class="fld" style="border:0;padding:0"><span class="lb" id="f-en-l">Regra ativa</span><button class="pwr" data-fb="enabled" data-k="f-enabled" aria-pressed="${!!cfg.enabled}" aria-labelledby="f-en-l"></button></div></div>`;
    return `<div class="hrow"><h2>${isNew ? 'Nova regra' : 'Editar regra'} · ${def.name}</h2><button class="btn" data-act="cancel" data-k="cancel">Voltar sem salvar</button></div>
      ${hasErr ? `<div class="alert" role="alert"><div><b>Corrija os campos marcados</b><span>${esc(Object.values(errors).join(' '))}</span></div></div>` : ''}
      <div class="editor"><div class="forms">${forms}
        <div class="actions"><button class="btn primary" data-act="save" data-k="save">Salvar regra</button><button class="btn" data-act="cancel" data-k="cancel2">Cancelar</button><span class="grow"></span>
        ${isNew ? '' : `<button class="btn danger${confirmDelete ? ' confirm' : ''}" data-act="delete" data-k="delete">${confirmDelete ? 'Confirmar exclusão' : 'Excluir regra'}</button>`}</div>
      </div><div class="aside">${aside}</div></div>`;
  }

  function validate(cfg) {
    const e = {};
    TIPOS[cfg.tipo].sections.forEach(s => s.fields.forEach(f => {
      const v = cfg[f.k];
      if (f.t === 'num') {
        if (typeof v !== 'number' || isNaN(v)) e[f.k] = `${f.label}: informe um número.`;
        else if (v < f.min || v > f.max) e[f.k] = `${f.label}: use de ${nf(f.min)} a ${nf(f.max)} ${f.unit}.`;
        else if (Number.isInteger(f.step) && !Number.isInteger(v)) e[f.k] = `${f.label}: use um número inteiro.`;
      }
      if (f.t === 'device' && !v) e[f.k] = 'Escolha o aparelho.';
      if (f.t === 'time' && !v) e[f.k] = `${f.label}: informe o horário.`;
      if (f.t === 'schedule' && v) {
        if (!v.days.length) e[f.k] = 'Agenda: marque pelo menos um dia.';
        else if (!v.start || !v.end || v.start === v.end) e[f.k] = 'Agenda: o início e o fim precisam ser horários diferentes.';
      }
    }));
    if (cfg.tipo === 'conforto' && !e.minC && !e.maxC && cfg.maxC < cfg.minC)
      e.maxC = 'A máxima precisa ser igual ou maior que a mínima.';
    if (cfg.tipo === 'conforto' && !e.maxC && !e.turboFromC && cfg.turboFromC <= cfg.maxC)
      e.turboFromC = 'O turbo precisa começar acima da máxima.';
    if (cfg.tipo === 'sai-do-pc' && !e.offAfterMin && !e.ecoAfterMin && cfg.offAfterMin <= cfg.ecoAfterMin)
      e.offAfterMin = 'Desligar precisa vir depois de aliviar.';
    return e;
  }

  function saveEditing() {
    const cfg = editing.cfg;
    editing.errors = validate(cfg);
    if (Object.keys(editing.errors).length) {
      render();
      const first = main.querySelector('[aria-invalid="true"]');
      if (first) first.focus();
      return;
    }
    const rule = JSON.parse(JSON.stringify(cfg));
    if (!rule.id) delete rule.id;
    send('saveRule', { rule });
    editing = null;
    render();
  }

  // ---------- Insights ----------
  function insightsView() {
    const sel = `<div class="seg inline" role="group" aria-label="Período">${[[1, '24 h'], [7, '7 dias']].map(([d, t]) =>
      `<button data-days="${d}" data-k="days:${d}" aria-pressed="${insightsDays === d}">${t}</button>`).join('')}</div>`;
    const head = `${accountAlert()}<div class="hrow"><h2>Insights</h2>${sel}</div>`;
    if (!insights) return `${head}<p class="muted">Carregando o histórico…</p>`;
    const k = insights.kpis || {};
    const kpis = `<div class="kpis tnum">
      <div class="kpi"><span class="lbl">Ligado hoje</span><span class="v">${nf(k.runtimeTodayH)} h</span><span class="s">ontem ${k.runtimeYesterdayH == null ? '—' : nf(k.runtimeYesterdayH) + ' h'}</span></div>
      <div class="kpi"><span class="lbl">Energia hoje <span class="ex">estimativa</span></span><span class="v">${nf(k.energyTodayKwh)} kWh</span><span class="s">≈ ${money(k.costToday)} hoje${k.costMonth == null ? '' : ` · mês ≈ ${money(k.costMonth)} (${nf(k.energyMonthKwh, 0)} kWh)`}</span></div>
      <div class="kpi"><span class="lbl">Para baixar 1 °C</span><span class="v">${k.minPerDegree7d == null ? '—' : nf(k.minPerDegree7d) + ' min'}</span><span class="s">${k.minPerDegree7d == null ? 'ainda sem dados suficientes' : `média 7 dias · 30 dias: ${k.minPerDegree30d == null ? '—' : nf(k.minPerDegree30d) + ' min'}`}</span></div>
    </div>`;
    const ch = insights.chart || {};
    const outside = ch.outside || [];
    const devs = (ch.devices || []).filter(d => d.points && d.points.length);
    let charts;
    if (!outside.length && !devs.length) {
      charts = `<div class="empty"><b>Ainda não há histórico</b><span class="small">O histórico começa a contar a partir de agora. Volte em algumas horas para ver o gráfico de fora × dentro.</span></div>`;
    } else if (!devs.length) {
      charts = `<div class="card chartcard"><h3>Fora</h3>${chartSvg(outside, null, ch, 'Fora')}${legend(null)}</div>`;
    } else {
      charts = devs.map(d => `<div class="card chartcard"><h3>${esc(d.name)}</h3>${chartSvg(outside, d.points, ch, d.name)}${legend(d.name)}</div>`).join('');
    }
    const items = (insights.items || []);
    const list = items.length ? `<div class="card"><h3>O que notamos</h3><div>${items.map(i => `<div class="ins"><span class="ic ${esc(i.kind)}" aria-hidden="true">${INS_ICON[i.kind] || '•'}</span><div><b>${esc(i.title)}</b><span>${esc(i.body)}</span></div></div>`).join('')}</div></div>` : '';
    return `${head}${kpis}${charts}${list}`;
  }

  function legend(name) {
    return `<div class="legend"><span><i style="background:var(--heat)"></i>fora</span>${name ? `<span><i style="background:var(--cool)"></i>${esc(name)}</span><span><i style="background:var(--cool-soft);height:9px"></i>ar ligado</span>` : ''}</div>`;
  }

  function niceStep(span) {
    const raw = span / 4;
    return [1, 2, 5, 10, 20].find(s => s >= raw) || 20;
  }

  function chartSvg(outside, points, ch, name) {
    const W = 680, H = 210, L = 34, R = 12, T = 10, B = 24;
    const o = outside.map(p => [Date.parse(p[0]), +p[1]]).filter(p => isFinite(p[0]) && isFinite(p[1]));
    const d = (points || []).map(p => [Date.parse(p[0]), +p[1], !!p[2]]).filter(p => isFinite(p[0]));
    const all = o.concat(d);
    if (!all.length) return '';
    let t0 = Date.parse(ch.from), t1 = Date.parse(ch.to);
    if (!isFinite(t0)) t0 = Math.min(...all.map(p => p[0]));
    if (!isFinite(t1)) t1 = Math.max(...all.map(p => p[0]));
    if (t1 <= t0) t1 = t0 + 3600e3;
    const vals = all.map(p => p[1]).filter(isFinite);
    let lo = Math.min(...vals), hi = Math.max(...vals);
    const step = niceStep(Math.max(hi - lo, 2));
    lo = Math.floor((lo - 0.5) / step) * step; hi = Math.ceil((hi + 0.5) / step) * step;
    const x = t => L + (t - t0) / (t1 - t0) * (W - L - R);
    const y = v => T + (hi - v) / (hi - lo) * (H - T - B);

    const path = arr => {
      if (arr.length < 1) return '';
      const gaps = arr.slice(1).map((p, i) => p[0] - arr[i][0]).sort((a, b) => a - b);
      const med = gaps.length ? gaps[Math.floor(gaps.length / 2)] : 0;
      return arr.map((p, i) => ((i === 0 || (med && p[0] - arr[i - 1][0] > med * 3)) ? 'M' : 'L') + x(p[0]).toFixed(1) + ' ' + y(p[1]).toFixed(1)).join(' ');
    };
    const bands = [];
    for (let i = 0; i < d.length; i++) {
      if (!d[i][2]) continue;
      const a = d[i][0], b = i + 1 < d.length ? d[i + 1][0] : t1;
      const last = bands[bands.length - 1];
      if (last && Math.abs(last[1] - a) < 1000) last[1] = b; else bands.push([a, b]);
    }
    const yTicks = []; for (let v = lo; v <= hi + 1e-9; v += step) yTicks.push(v);
    const xTicks = [];
    const days = (t1 - t0) / 864e5;
    const first = new Date(t0); first.setMinutes(0, 0, 0);
    if (days <= 1.5) {
      const every = days <= 0.6 ? 2 : 3;
      for (let t = first.getTime(); t <= t1; t += 3600e3) {
        const dt = new Date(t); if (t < t0 || dt.getHours() % every) continue;
        xTicks.push([t, `${String(dt.getHours()).padStart(2, '0')}h`]);
      }
    } else {
      first.setHours(0);
      for (let t = first.getTime(); t <= t1; t += 864e5) {
        const dt = new Date(t); if (t < t0) continue;
        xTicks.push([t, `${DAYS[dt.getDay()]} ${dt.getDate()}`]);
      }
    }
    const peak = o.length ? o.reduce((m, p) => p[1] > m[1] ? p : m) : null;
    const fmtRange = `${nf(Math.min(...vals))} a ${nf(Math.max(...vals))} °C`;
    return `<svg class="curve" viewBox="0 0 ${W} ${H}" role="img" aria-label="Temperatura de fora${points ? ' e de ' + esc(name) : ''} no período, de ${fmtRange}${bands.length ? `; ar ligado em ${bands.length} intervalo(s)` : ''}">
      ${bands.map(([a, b]) => `<rect x="${x(a).toFixed(1)}" y="${T}" width="${Math.max(1, x(b) - x(a)).toFixed(1)}" height="${H - T - B}" fill="var(--cool-soft)"/>`).join('')}
      ${yTicks.map(v => `<line x1="${L}" x2="${W - R}" y1="${y(v).toFixed(1)}" y2="${y(v).toFixed(1)}" stroke="var(--line)"/><text class="svgt" x="${L - 5}" y="${(y(v) + 3).toFixed(1)}" text-anchor="end">${nf(v)}°</text>`).join('')}
      ${xTicks.map(([t, l]) => `<line x1="${x(t).toFixed(1)}" x2="${x(t).toFixed(1)}" y1="${H - B}" y2="${H - B + 3}" stroke="var(--ink-3)"/><text class="svgt" x="${x(t).toFixed(1)}" y="${H - 7}" text-anchor="middle">${l}</text>`).join('')}
      ${o.length ? `<path d="${path(o)}" fill="none" stroke="var(--heat)" stroke-width="2" stroke-linejoin="round"/>` : ''}
      ${d.length ? `<path d="${path(d)}" fill="none" stroke="var(--cool)" stroke-width="2" stroke-linejoin="round"/>` : ''}
      ${peak ? `<circle cx="${x(peak[0]).toFixed(1)}" cy="${y(peak[1]).toFixed(1)}" r="3.5" fill="var(--heat)"><title>Máxima de fora: ${nf(peak[1])} °C</title></circle>` : ''}
    </svg>`;
  }

  // ---------- Ajustes ----------
  function settingsView() {
    const s = state.settings || {};
    const segSet = (k, opts, cur, label) => `<div class="seg inline" role="group" aria-label="${label}">${opts.map(v =>
      `<button data-set="${k}" data-v="${v}" data-k="set:${k}:${v}" aria-pressed="${cur === v}">${v} min</button>`).join('')}</div>`;
    const u = state.update || {};
    let upd;
    if (u.downloading) upd = `<button class="btn" disabled>Baixando a versão ${esc(u.version || '')}…</button>`;
    else if (u.available) upd = `<button class="btn primary" data-act="install" data-k="install">Instalar versão ${esc(u.version || '')}</button>`;
    else upd = `<button class="btn" data-act="checkUpdate" data-k="check"${updateChecking ? ' disabled' : ''}>${updateChecking ? 'Verificando…' : 'Verificar atualização'}</button>`;
    const updText = u.downloading ? 'O Meu Clima reinicia sozinho ao terminar.' : u.available ? `A versão ${esc(u.version || '')} está disponível.` : updateChecked ? 'Você já está na versão mais recente.' : s.checkUpdates === false ? 'A busca automática está desligada.' : 'O Meu Clima também verifica sozinho ao iniciar.';
    const numIn = (k, label, unit, min, max, step) => `<span class="unit"><input class="inp num" type="number" inputmode="decimal" aria-label="${label}" data-setnum="${k}" data-k="sn:${k}" min="${min}" max="${max}" step="${step}" value="${esc(s[k])}"><em>${unit}</em></span>`;
    return `${accountAlert()}<h2>Ajustes</h2>
      <div class="card stack" style="gap:0"><h3>Geral</h3>
        <div class="set"><div><b>Iniciar com o Windows</b><span>Abre na bandeja ao entrar na sua conta do Windows.</span></div><button class="pwr" data-setb="startWithWindows" data-k="sb:sw" aria-pressed="${!!s.startWithWindows}" aria-label="Iniciar com o Windows"></button></div>
        <div class="set"><div><b>Tema</b><span>Sistema acompanha o claro ou escuro do Windows.</span></div><div class="seg inline" role="group" aria-label="Tema">${[['system', 'Sistema'], ['light', 'Claro'], ['dark', 'Escuro']].map(([v, t]) =>
          `<button data-settheme="${v}" data-k="theme:${v}" aria-pressed="${(s.theme || 'system') === v}">${t}</button>`).join('')}</div></div>
        <div class="set"><div><b>Procurar atualizações automaticamente</b><span>Consulta o GitHub ao iniciar e a cada 30 min. Desligado, só quando você pedir.</span></div><button class="pwr" data-setb="checkUpdates" data-k="sb:cu" aria-pressed="${s.checkUpdates !== false}" aria-label="Procurar atualizações automaticamente"></button></div>
        <div class="set"><div><b>Avisos do Windows</b><span>Regra começou, aparelho caiu, janela aberta, fora mais fresco.</span></div><button class="pwr" data-setb="notifications" data-k="sb:nt" aria-pressed="${!!s.notifications}" aria-label="Avisos do Windows"></button></div>
        <div class="set"><div><b>Atualizar o clima de fora</b><span>No Brasil a fonte muda de hora em hora.</span></div>${segSet('weatherIntervalMin', [15, 30, 60], s.weatherIntervalMin, 'Intervalo do clima')}</div>
        <div class="set"><div><b>Atualizar os aparelhos</b><span>Lê o estado na nuvem da Electrolux.</span></div>${segSet('deviceIntervalMin', [1, 2, 5], s.deviceIntervalMin, 'Intervalo dos aparelhos')}</div>
      </div>
      <div class="card stack" style="gap:0"><h3>Estimativa de energia</h3>
        <p class="small muted" style="margin:0 0 4px">A Electrolux não informa o consumo. O Meu Clima estima: horas com o compressor ligado × potência média, mais as horas só ventilando a ~40 W, × tarifa. A projeção do mês usa a média dos últimos 7 dias.</p>
        <div class="set"><div><b>Potência média do aparelho</b><span>Veja na etiqueta ou no manual; 9.000 BTU fica perto de 800 W.</span></div>${numIn('averageWatts', 'Potência média em watts', 'W', 100, 5000, 50)}</div>
        <div class="set"><div><b>Tarifa de energia</b><span>Total da conta de luz ÷ kWh do mês: já inclui impostos e bandeira.</span></div>${numIn('tariffPerKwh', 'Tarifa em reais por kWh', 'R$/kWh', 0.01, 10, 0.01)}</div>
      </div>
      <div class="card stack" style="gap:0"><h3>Versão</h3>
        <div class="set"><div><b>Meu Clima ${esc(state.version || '')}</b><span>${updText}</span></div>${upd}</div>
      </div>
      <div class="card stack"><h3>Sobre</h3>
        <p class="small muted" style="margin:0">Projeto de código aberto, sem afiliação com a Electrolux. Funciona só com ar-condicionado Electrolux com Wi-Fi, pela API oficial para desenvolvedores.</p>
        <p class="small muted" style="margin:0">Weather data by <a href="https://open-meteo.com/" target="_blank" rel="noopener">Open-Meteo.com</a> (<a href="https://creativecommons.org/licenses/by/4.0/" target="_blank" rel="noopener">CC BY 4.0</a>).</p>
        <p class="small muted" style="margin:0"><a href="https://github.com/adrianocaldeira/meu-clima" target="_blank" rel="noopener">Código e versões no GitHub</a> · licença MIT</p>
      </div>`;
  }

  function saveSetting(partial) {
    Object.assign(state.settings, partial);
    send('saveSettings', { settings: partial });
    render();
  }

  // ---------- Configuração ----------
  function setupView() {
    const s = state.setup || {};
    const incomplete = !setupDone();
    const st = (ok, txtOk, txtNo) => ok ? `<span class="chip ok">${txtOk}</span>` : `<span class="chip warn">${txtNo}</span>`;
    const credOk = s.credentials && !(state.account && state.account.error);
    const canConnect = draft.apiKey.trim() && (draft.refreshToken.trim() || s.credentials) && !credBusy;
    const cityList = citiesBusy ? '<p class="small muted" style="margin:0">Buscando…</p>'
      : cities == null ? '' : !cities.length ? '<p class="small muted" style="margin:0">Nenhuma cidade encontrada. Tente só o nome, sem o estado.</p>'
        : `<div class="cities" role="list">${cities.map((c, i) => `<button role="listitem" data-city="${i}" data-k="city:${i}"><span>${esc(c.name)}</span><span class="muted small">${esc(c.region || '')}</span></button>`).join('')}</div>`;
    const pc = state.settings ? state.settings.pcDeviceId : null;
    const devChoice = state.devices.length
      ? `<div class="choice" role="radiogroup" aria-label="Cômodo deste PC">${state.devices.map(d => `<button role="radio" data-pc="${esc(d.id)}" data-k="pc:${esc(d.id)}" aria-checked="${d.id === pc}">${esc(d.name)}</button>`).join('')}
         <button role="radio" data-pc="" data-k="pc:none" aria-checked="${!pc}">Nenhum destes</button></div>`
      : '<p class="small muted" style="margin:0">Conecte a conta Electrolux primeiro para listar os aparelhos.</p>';
    return `<h2>${incomplete ? 'Configure o Meu Clima' : 'Conta e local'}</h2>
      ${incomplete ? '<p class="muted" style="margin:-6px 0 0">Três passos e as automações começam a funcionar.</p>' : ''}
      ${accountAlert()}
      <div class="stack">
      <div class="card"><div class="hrow"><h3>1 · Conta Electrolux</h3>${st(credOk, 'conectada', s.credentials ? 'precisa de novo token' : 'não conectada')}</div>
        <ol class="steps-ol">
          <li>Abra <b>developer.electrolux.one</b> no navegador e entre com a <b>mesma conta do app Electrolux</b> do celular.</li>
          <li>No painel, crie uma <b>API key</b> e gere um <b>token</b>.</li>
          <li>Copie a API key e o <b>refresh token</b> e cole abaixo.</li>
        </ol>
        <div class="field"><label for="in-key">API key</label><div class="pw">
          <input id="in-key" class="inp" type="${showKey ? 'text' : 'password'}" autocomplete="off" spellcheck="false" data-d="apiKey" data-k="in-key" value="${esc(draft.apiKey)}">
          <button class="btn" data-act="showKey" data-k="show-key" aria-pressed="${showKey}" aria-controls="in-key">${showKey ? 'Ocultar' : 'Mostrar'}</button></div></div>
        <div class="field"><label for="in-tok">Refresh token${s.credentials ? ' <span class="small muted">(em branco: usa o já guardado)</span>' : ''}</label><div class="pw">
          <input id="in-tok" class="inp" type="${showTok ? 'text' : 'password'}" autocomplete="off" spellcheck="false" data-d="refreshToken" data-k="in-tok" value="${esc(draft.refreshToken)}">
          <button class="btn" data-act="showTok" data-k="show-tok" aria-pressed="${showTok}" aria-controls="in-tok">${showTok ? 'Ocultar' : 'Mostrar'}</button></div></div>
        <div class="actions"><button class="btn primary" data-act="connect" data-k="connect"${canConnect ? '' : ' disabled'}>${credBusy ? 'Conectando…' : s.credentials ? 'Conectar de novo' : 'Conectar'}</button>
          <span class="small muted">Fica guardado só neste PC, protegido pela sua conta do Windows.</span></div>
      </div>
      <div class="card"><div class="hrow"><h3>2 · Cidade</h3>${st(s.city, esc(s.cityName || (state.weather && state.weather.city) || 'definida'), 'não definida')}</div>
        <p class="small muted" style="margin:0">Usada para a temperatura de fora e a previsão do dia.</p>
        <div class="pw"><input class="inp" type="search" aria-label="Buscar cidade" placeholder="Nome da cidade" data-d="cityQuery" data-k="in-city" value="${esc(draft.cityQuery)}">
          <button class="btn" data-act="searchCity" data-k="search-city"${draft.cityQuery.trim().length < 2 || citiesBusy ? ' disabled' : ''}>Buscar</button></div>
        ${cityList}
      </div>
      <div class="card"><div class="hrow"><h3>3 · Em qual cômodo fica este PC?</h3>${st(s.pcDevice, 'definido', 'não definido')}</div>
        <p class="small muted" style="margin:0">As regras "Conforto" e "Ausente" agem no ar deste cômodo.</p>
        ${devChoice}
      </div>
      </div>`;
  }

  // ---------- eventos ----------
  document.addEventListener('click', e => {
    const b = e.target.closest('button');
    if (!b || b.disabled || !state) return;
    const ds = b.dataset;
    if (ds.go) return go(ds.go);
    if (ds.days) { const d = +ds.days; if (d !== insightsDays) { insightsDays = d; insights = null; render(); requestInsights(); } return; }
    if (ds.set) return saveSetting({ [ds.set]: +ds.v });
    // checkUpdates ausente conta como ligado (o botão mostra assim).
    if (ds.setb === 'checkUpdates') return saveSetting({ checkUpdates: state.settings.checkUpdates === false });
    if (ds.setb) return saveSetting({ [ds.setb]: !state.settings[ds.setb] });
    if (ds.settheme) {
      const root = document.documentElement;
      if (ds.settheme === 'system') delete root.dataset.theme; else root.dataset.theme = ds.settheme;
      return saveSetting({ theme: ds.settheme });
    }
    if (ds.city != null && cities) { const c = cities[+ds.city]; if (c) { send('setCity', { city: c }); cities = null; draft.cityQuery = ''; render(); } return; }
    if (ds.pc != null) { const id = ds.pc || null; state.devices.forEach(d => { d.isPcRoom = d.id === id; }); return saveSetting({ pcDeviceId: id }); }
    if (editing) {
      const cfg = editing.cfg;
      if (ds.fb) { cfg[ds.fb] = !cfg[ds.fb]; return render(); }
      if (ds.fopt) { cfg[ds.fopt] = cfg[ds.fopt] ? null : '22:00:00'; return render(); }
      if (ds.fsched) { cfg.schedule = cfg.schedule ? null : { days: [1, 2, 3, 4, 5], start: '08:00:00', end: '19:00:00' }; delete editing.errors.schedule; return render(); }
      if (ds.day != null) {
        const d = +ds.day, days = cfg.schedule.days;
        cfg.schedule.days = days.includes(d) ? days.filter(x => x !== d) : [...days, d].sort((a, z) => a - z);
        return render();
      }
    }
    switch (ds.act) {
      case 'new': picking = true; editing = null; render(); break;
      case 'create': newRule(ds.tipo); break;
      case 'edit': editRule(ds.id); break;
      case 'cancel': editing = null; picking = false; render(); break;
      case 'save': saveEditing(); break;
      case 'delete':
        if (!editing.confirmDelete) { editing.confirmDelete = true; render(); }
        else { send('deleteRule', { id: editing.cfg.id }); editing = null; render(); }
        break;
      case 'toggle': {
        const r = state.rules.find(x => x.id === ds.id);
        if (r) { r.enabled = !r.enabled; send('toggleRule', { id: r.id, enabled: r.enabled }); render(); }
        break;
      }
      case 'checkUpdate': updateChecking = true; send('checkUpdate'); render(); break;
      case 'install': send('installUpdate'); state.update.downloading = true; render(); break;
      case 'showKey': showKey = !showKey; render(); break;
      case 'showTok': showTok = !showTok; render(); break;
      case 'connect':
        credBusy = true;
        send('setCredentials', { apiKey: draft.apiKey.trim(), refreshToken: draft.refreshToken.trim() });
        render();
        break;
      case 'searchCity': doSearch(); break;
    }
  });

  function doSearch() {
    const q = draft.cityQuery.trim();
    if (q.length < 2) return;
    citiesBusy = true; cities = null;
    send('searchCity', { query: q });
    render();
  }

  main.addEventListener('input', e => {
    const t = e.target;
    if (t.dataset.d) {
      draft[t.dataset.d] = t.value;
      const k = t.dataset.d;
      if (k === 'apiKey' || k === 'refreshToken') {
        const btn = main.querySelector('[data-act="connect"]');
        if (btn) btn.disabled = !(draft.apiKey.trim() && (draft.refreshToken.trim() || (state && state.setup && state.setup.credentials))) || credBusy;
      }
      if (k === 'cityQuery') {
        const btn = main.querySelector('[data-act="searchCity"]');
        if (btn) btn.disabled = t.value.trim().length < 2 || citiesBusy;
      }
      return;
    }
    if (editing && t.dataset.f) {
      const f = t.dataset.f;
      if (t.type === 'number') editing.cfg[f] = t.value === '' ? NaN : parseFloat(t.value);
      else if (t.dataset.time) editing.cfg[f] = t.value ? t.value + ':00' : null;
      else editing.cfg[f] = t.value || null;
      if (editing.errors[f]) { delete editing.errors[f]; t.classList.remove('invalid'); t.removeAttribute('aria-invalid'); }
      return;
    }
    if (editing && t.dataset.sched && editing.cfg.schedule) {
      editing.cfg.schedule[t.dataset.sched] = t.value ? t.value + ':00' : '';
    }
  });

  main.addEventListener('change', e => {
    const t = e.target;
    if (!t.dataset.setnum) return;
    const k = t.dataset.setnum, v = parseFloat(t.value);
    const min = parseFloat(t.min), max = parseFloat(t.max);
    if (!isFinite(v) || v < min || v > max) {
      t.classList.add('invalid'); t.setAttribute('aria-invalid', 'true');
      MC.toast({ kind: 'error', text: `Use um valor entre ${nf(min, 2)} e ${nf(max, 2)}.` });
      return;
    }
    t.classList.remove('invalid'); t.removeAttribute('aria-invalid');
    if (state.settings[k] !== v) saveSetting({ [k]: v });
  });

  main.addEventListener('keydown', e => {
    if (e.key === 'Enter' && e.target.dataset.d === 'cityQuery') { e.preventDefault(); doSearch(); }
    if (e.key === 'Enter' && e.target.dataset.d && (e.target.dataset.d === 'apiKey' || e.target.dataset.d === 'refreshToken')) {
      const btn = main.querySelector('[data-act="connect"]'); if (btn && !btn.disabled) btn.click();
    }
  });

  send('ready');
})();
