// Cooking Hub's web half. Runs in Root.html, the document every game page iframe sits in. Draws the Cooking Hub window
// over all of them, puts a pot button with a spoiling-food badge in the HUD's tool column, and writes the quality odds
// under each dish the game's own cooking screen forecasts. Talks to the plugin through
// vuplex.postMessage("slmod|cookinghub|<verb>") and is driven by window.__slcookinghub.
(function () {
  if (window.__slcookinghub) return;

  var PREFIX = 'slmod|cookinghub|';
  var HUD_CLASS = 'ch-hud-btn';
  // Cooking Hub's place in the HUD's tool column when Exiled's Toolkit lays it out in columns of four.
  var DOCK_ORDER = '31';
  var HUD_STYLE_ID = 'ch-hud-style';
  var STATION_STYLE_ID = 'ch-station-style';
  var NUDGES_ID = 'ch-nudges';
  var BUSY_TIMEOUT_MS = 5000;
  var TOAST_MS = 3500;
  var SVG_NS = 'http://www.w3.org/2000/svg';
  // A cooking pot with a lid and two handles.
  var POT_PATH = 'M11 3h2v2h-2zM5 7h14v2H5zM3 10h2v2H3zm16 0h2v2h-2zM5 10h14v7a3 3 0 0 1-3 3H8a3 3 0 0 1-3-3z';
  var TABS = ['cook', 'pantry', 'queue', 'settings'];
  var TAB_WORD = { cook: 'tabCook', pantry: 'tabPantry', queue: 'tabQueue', settings: 'tabSettings' };
  var FILTERS = [['all', 'fAll'], ['ready', 'fReady'], ['one', 'fOne'], ['short', 'fShort'], ['locked', 'fLocked']];
  // Fail, Normal, Good, Perfect: the order the odds arrive in.
  var TIER_WORD = ['tFail', 'tNormal', 'tGood', 'tPerfect'];
  var PART_WORD = { fresh: 'partFresh', rotten: 'partRotten', exact: 'partExact', stove: 'partStove' };
  var SPOILING_HOURS = 24;

  var state = {
    data: { ready: false, note: '' }, isOpen: false, isLive: false, key: '', badge: 0, tab: 'cook', show: 'all', search: '',
    isTyping: false, isStale: false
  };
  var cat = { types: {}, items: {}, recipes: {} };
  var overlay = { show: false };
  var overlayVersion = 0;
  var ui = null;
  var shownShape = '';
  var toastTimer = 0;
  var isBusy = false;
  var busyTimer = 0;
  var hudVersion = 0;
  var hasReportedPageAccess = false;
  var hasReportedHudPlaced = false;
  var hasReportedOddsDrawn = false;

  // The plugin hands the words over before its first call, so a missing one only shows its key.
  function say(key) {
    var words = window.__slcookinghubWords || {};
    var text = words[key] || key;

    for (var i = 1; i < arguments.length; i++) text = text.split('{' + (i - 1) + '}').join(String(arguments[i]));

    return text;
  }

  function send(message) {
    try {
      if (window.vuplex) window.vuplex.postMessage(PREFIX + message);
    } catch (e) {}
  }

  // One press at a time: a double click would otherwise move food twice. Every press is answered by a status line,
  // which frees the buttons; the timer frees them anyway if that answer never comes.
  function sendAction(message) {
    if (isBusy) return;

    isBusy = true;
    clearTimeout(busyTimer);
    busyTimer = setTimeout(clearBusy, BUSY_TIMEOUT_MS);
    send(message);
  }

  function clearBusy() {
    isBusy = false;
    clearTimeout(busyTimer);
  }

  // ---------- DOM helpers ----------

  function el(tag, className, content) {
    return make(document, tag, className, content);
  }

  function make(doc, tag, className, content) {
    var node = doc.createElement(tag);

    if (className) node.className = className;
    if (content != null) node.textContent = content;

    return node;
  }

  function text(content) {
    return content ? document.createTextNode(content) : null;
  }

  function put(parent) {
    for (var i = 1; i < arguments.length; i++) {
      if (arguments[i]) parent.appendChild(arguments[i]);
    }

    return parent;
  }

  function act(node, action, args) {
    node.setAttribute('data-act', action);

    if (args != null) node.setAttribute('data-args', String(args));

    return node;
  }

  function button(content, action, args, isEnabled, extraClass) {
    var node = act(el('button', 'ch-btn' + (extraClass ? ' ' + extraClass : ''), content), action, args);

    if (isEnabled === false) node.disabled = true;

    return node;
  }

  // A row of choices where one is on; a count after a label shows how many it would show.
  function segment(choices, current, action) {
    var seg = el('div', 'ch-seg');

    choices.forEach(function (c) {
      var node = act(el('button', 'ch-sg' + (c[0] === current ? ' ch-on' : ''), c[1]), action, c[0]);

      if (c[2] != null) node.appendChild(el('span', 'ch-n', String(c[2])));

      seg.appendChild(node);
    });

    return seg;
  }

  function switchButton(isOn, action, args) {
    return act(el('button', 'ch-tog' + (isOn ? ' ch-on' : '')), action, args);
  }

  function badge(content, colorClass) {
    return el('span', 'ch-badge' + (colorClass ? ' ' + colorClass : ''), content);
  }

  function setRow(label, note, control) {
    return put(el('div', 'ch-setrow'), put(el('div', 'ch-lbl'), el('b', '', label), note ? el('small', '', note) : null), control);
  }

  function scroller(key, className) {
    var node = el('div', 'ch-well' + (className ? ' ' + className : ''));
    node.setAttribute('data-scroll', key);

    return node;
  }

  function icon(thing, sizeClass) {
    var box = el('div', 'ch-icon ' + sizeClass);
    var letter = String(thing.name || '?').charAt(0).toUpperCase();

    if (thing.icon) {
      var img = el('img');
      img.setAttribute('data-letter', letter);
      img.src = thing.icon;
      box.appendChild(img);
    } else {
      box.textContent = letter;
    }

    return box;
  }

  var CSS = [
    '#ch-root{position:fixed;inset:0;z-index:2147483000;display:none;align-items:center;justify-content:center;background:rgba(10,6,2,.45);font-family:"Noto Sans SC","Microsoft YaHei","PingFang SC",sans-serif;color:#f0e8d8;user-select:none;font-size:14px}',
    '#ch-root *{box-sizing:border-box}#ch-root button{font:inherit}',
    '.ch-plate{position:relative;width:min(1280px,94vw);height:min(860px,90vh);display:flex;flex-direction:column;gap:12px;padding:18px 22px;border-radius:4px;border:1px solid rgba(220,188,120,.28);background:radial-gradient(ellipse at 15% 0%,rgba(150,100,40,.30),transparent 55%),linear-gradient(180deg,#3a3129 0%,#2b241d 55%,#241e18 100%);box-shadow:0 30px 90px rgba(0,0,0,.7),inset 0 1px 0 rgba(255,230,180,.08)}',
    '.ch-plate::before{content:"";position:absolute;top:0;left:0;right:0;height:2px;background:linear-gradient(90deg,transparent 10%,#FFC83A 50%,transparent 90%);opacity:.6}',
    '.ch-head{display:flex;align-items:center;gap:16px}',
    '.ch-title{font-size:24px;font-weight:700;letter-spacing:5px;text-transform:uppercase;color:#fff;white-space:nowrap}',
    '.ch-title::before{content:"";display:inline-block;width:4px;height:20px;background:#FFC83A;margin-right:12px;vertical-align:-2px;box-shadow:0 0 10px rgba(255,200,58,.35)}',
    '.ch-sum{flex:1;min-width:0;font-size:13px;color:#a89a82;display:flex;flex-direction:column;gap:2px}',
    '.ch-sum b{color:#f0e8d8;font-weight:600}.ch-sum .ch-hot{color:#FFC83A}',
    '.ch-tabs{display:flex;gap:8px;border-bottom:1px solid rgba(220,188,120,.11);padding-bottom:10px}',
    '.ch-tab{font-size:15px;color:#cbbfa8;padding:7px 18px;border-radius:4px;border:1px solid rgba(220,188,120,.20);background:rgba(255,240,210,.05);display:flex;gap:8px;align-items:center;cursor:pointer}',
    '.ch-tab:hover{border-color:rgba(255,200,58,.6);color:#fff}.ch-tab.ch-on{color:#FFC83A;background:rgba(255,200,58,.13);border-color:#FFC83A;box-shadow:0 0 12px rgba(255,200,58,.18);font-weight:600}',
    '.ch-tab .ch-n{font-size:11px;padding:1px 6px;border-radius:7px;background:#D32F2F;color:#fff;font-weight:700}',
    '.ch-body{flex:1;min-height:0;display:flex;flex-direction:column;gap:10px}',
    '.ch-well{flex:1;min-height:0;overflow-y:auto;background:linear-gradient(180deg,rgba(58,48,38,.75),rgba(44,36,28,.75));border:1px solid rgba(220,188,120,.20);border-radius:4px;padding:8px}',
    '.ch-well::-webkit-scrollbar{width:6px}.ch-well::-webkit-scrollbar-thumb{background:rgba(255,200,58,.35);border-radius:3px}',
    '.ch-bar{display:flex;align-items:center;gap:10px;flex-wrap:wrap}.ch-grow{flex:1;min-width:0}',
    '.ch-seg{display:inline-flex;border:1px solid rgba(220,188,120,.20);border-radius:4px;overflow:hidden;flex:none}',
    '.ch-sg{font-size:13px;padding:5px 11px;color:#a89a82;background:none;border:none;border-right:1px solid rgba(220,188,120,.11);cursor:pointer;white-space:nowrap;display:flex;gap:6px;align-items:center}',
    '.ch-sg:last-child{border-right:none}.ch-sg:hover{color:#fff}.ch-sg.ch-on{background:#FFC83A;color:#2a1f10;font-weight:700}',
    '.ch-sg .ch-n{font-size:11px;opacity:.75}',
    '.ch-inp{font:inherit;font-size:13px;color:#f0e8d8;background:rgba(20,14,8,.45);border:1px solid rgba(220,188,120,.20);border-radius:4px;padding:5px 10px;width:240px;user-select:text}',
    '.ch-inp:focus{outline:none;border-color:#FFC83A}.ch-inp::placeholder{color:#8a7c66}',
    '.ch-rows{display:flex;flex-direction:column;gap:6px}',
    '.ch-acts{display:flex;gap:6px;justify-content:flex-end}',
    '.ch-qrow{display:grid;grid-template-columns:28px minmax(0,1fr) auto;gap:12px;align-items:center;padding:8px 10px;border-radius:4px;background:rgba(70,58,44,.55);border:1px solid rgba(220,188,120,.11)}',
    '.ch-row{display:grid;grid-template-columns:48px minmax(0,1fr) 90px auto;gap:12px;align-items:center;padding:8px 10px;border-radius:4px;background:rgba(70,58,44,.55);border:1px solid rgba(220,188,120,.11)}',
    '.ch-row:hover{background:rgba(84,70,52,.7)}.ch-row.ch-locked{opacity:.5}.ch-row.ch-ready{border-color:rgba(140,197,106,.35)}',
    '.ch-main{min-width:0;display:flex;flex-direction:column;gap:5px}',
    '.ch-nm{font-size:15px;font-weight:600;display:flex;flex-wrap:wrap;gap:4px 6px;align-items:center}',
    '.ch-needs{display:flex;flex-wrap:wrap;gap:6px;align-items:center;font-size:12px;color:#cbbfa8}',
    '.ch-need{display:inline-flex;align-items:center;gap:4px;padding:1px 6px 1px 2px;border-radius:4px;background:rgba(20,14,8,.35);border:1px solid transparent}',
    '.ch-need.ch-miss{border-color:rgba(229,57,53,.6);color:#ff8a80}.ch-need .ch-tag{color:#f3d38a}',
    '.ch-perf{font-size:13px;color:#a89a82;text-align:right}.ch-perf b{display:block;font-size:17px;color:#EBA308}',
    '.ch-lock{font-size:12px;color:#a89a82}',
    '.ch-badge{flex:none;font-size:11px;padding:2px 7px;border-radius:3px;background:rgba(255,240,210,.08);color:#cbbfa8;white-space:nowrap;font-weight:400}',
    '.ch-b-red{background:rgba(229,57,53,.28);color:#ff8a80}.ch-b-amber{background:rgba(255,200,58,.18);color:#FFC83A}.ch-b-green{background:rgba(140,197,106,.22);color:#b5e09a}',
    '.ch-btn{font-size:13px;color:#f0e8d8;padding:7px 12px;border-radius:4px;border:1px solid rgba(220,188,120,.20);background:linear-gradient(180deg,rgba(92,78,60,.9),rgba(66,55,42,.92));cursor:pointer;white-space:nowrap}',
    '.ch-btn:hover:not(:disabled){border-color:#FFC83A;background:linear-gradient(180deg,rgba(110,90,58,.95),rgba(80,64,40,.95))}',
    '.ch-btn.ch-hot{color:#2a1f10;font-weight:700;letter-spacing:1px;border-color:#ffd36a;background:linear-gradient(180deg,#ffd25a,#e8a91c);box-shadow:0 0 14px rgba(255,200,58,.25)}',
    '.ch-btn.ch-hot:hover:not(:disabled){background:linear-gradient(180deg,#ffdf80,#f0b52a)}',
    '.ch-btn:disabled{opacity:.4;cursor:default;box-shadow:none}',
    '.ch-btn.ch-x{width:32px;height:32px;padding:0;font-size:18px;line-height:30px;text-align:center;border-radius:50%}',
    '.ch-tog{width:40px;height:22px;padding:0;border-radius:11px;background:rgba(20,14,8,.5);border:1px solid rgba(220,188,120,.20);position:relative;flex:none;cursor:pointer}',
    '.ch-tog::after{content:"";position:absolute;top:3px;left:3px;width:14px;height:14px;border-radius:50%;background:#a89a82;transition:left .15s}',
    '.ch-tog.ch-on{background:rgba(255,200,58,.35);border-color:#FFC83A}.ch-tog.ch-on::after{left:21px;background:#FFC83A}',
    '.ch-setrow{display:flex;align-items:center;gap:14px;padding:10px 14px;border-radius:4px;background:rgba(70,58,44,.55);border:1px solid rgba(220,188,120,.11);margin-bottom:6px}',
    '.ch-lbl{flex:1;min-width:0}.ch-lbl b{display:block;font-size:14px;font-weight:600}.ch-lbl small{display:block;font-size:12px;color:#a89a82;margin-top:2px}',
    '.ch-prow{display:grid;grid-template-columns:minmax(0,1.4fr) 140px 120px minmax(0,1.6fr);gap:12px;align-items:center;padding:6px 10px;border-radius:4px;background:rgba(70,58,44,.55);font-size:13px}',
    '.ch-prow.ch-phead{background:none;color:#a89a82;font-size:12px;padding:2px 10px}',
    '.ch-pname{display:flex;align-items:center;gap:8px;min-width:0;white-space:nowrap;overflow:hidden}',
    '.ch-red{color:#ff8a80}.ch-dim{color:#7a6e5a}.ch-num{font-weight:600;color:#FFC83A;font-variant-numeric:tabular-nums}',
    '.ch-icon{flex:none;display:flex;align-items:center;justify-content:center;border-radius:4px;background:radial-gradient(circle,rgba(255,220,150,.16),rgba(255,220,150,.03) 70%);overflow:hidden;color:#cbbfa8;font-weight:600}',
    '.ch-icon img{width:100%;height:100%;object-fit:contain}',
    '.ch-i48{width:48px;height:48px;font-size:18px}.ch-i28{width:28px;height:28px;font-size:12px}.ch-i20{width:20px;height:20px;font-size:10px}',
    '.ch-empty{padding:24px;color:#a89a82;font-size:14px;text-align:center}',
    '.ch-sub{padding:2px 2px 8px;color:#a89a82;font-size:13px}',
    '.ch-lockmsg{padding:10px 14px;border-radius:4px;border:1px solid rgba(255,200,58,.6);background:rgba(255,200,58,.1);color:#FFC83A;font-size:14px}',
    '.ch-foot{display:flex;gap:12px;font-size:13px;color:#a89a82;align-items:center;border-top:1px solid rgba(220,188,120,.11);padding-top:10px}.ch-status{flex:1;color:#f0e8d8}',
    '.ch-kbd{font-size:12px;color:#cbbfa8;border:1px solid rgba(220,188,120,.4);border-bottom-width:2px;border-radius:4px;padding:0 6px;margin:0 4px}',
    '#ch-toast{position:fixed;left:50%;bottom:120px;transform:translateX(-50%);z-index:2147483001;pointer-events:none;display:none;max-width:640px;text-align:center;padding:10px 18px;border-radius:4px;border:1px solid rgba(255,200,58,.6);background:linear-gradient(180deg,rgba(64,52,40,.97),rgba(44,36,28,.97));color:#f0e8d8;font-family:"Microsoft YaHei","PingFang SC",sans-serif;font-size:14px;box-shadow:0 6px 20px rgba(0,0,0,.6)}'
  ].join('\n');

  function build() {
    if (ui && document.body.contains(ui.root)) return;

    addStyle();

    var root = el('div');
    var plate = el('div', 'ch-plate');
    var title = el('div', 'ch-title');
    var head = el('div', 'ch-head');
    var sum = el('div', 'ch-sum');
    var tabs = el('div', 'ch-tabs');
    var body = el('div', 'ch-body');
    var foot = el('div', 'ch-foot');
    var status = el('div', 'ch-status');
    var keys = el('div');
    var toast = el('div');

    root.id = 'ch-root';
    toast.id = 'ch-toast';
    put(head, title, sum, button('\u00d7', 'close', null, true, 'ch-x'));
    put(foot, status, keys);
    put(plate, head, tabs, body, foot);
    root.appendChild(plate);
    document.body.appendChild(root);
    document.body.appendChild(toast);

    root.addEventListener('click', onClick);
    root.addEventListener('input', onInput);
    root.addEventListener('focusin', onFocus);
    root.addEventListener('focusout', onBlur);
    root.addEventListener('keydown', onKey);
    // Clicks inside the window must not reach the game pages underneath.
    root.addEventListener('mousedown', function (e) { e.stopPropagation(); });
    // A broken icon path swaps to the first-letter placeholder; error events do not bubble, so capture them.
    root.addEventListener('error', function (e) {
      var img = e.target;

      if (img && img.tagName === 'IMG' && img.parentNode) img.parentNode.textContent = img.getAttribute('data-letter') || '?';
    }, true);

    ui = { root: root, title: title, sum: sum, tabs: tabs, body: body, status: status, keys: keys, toast: toast, dishes: null };
  }

  // The style outlives a lost window root, so a rebuild must not stack another copy of it.
  function addStyle() {
    if (document.getElementById('ch-style')) return;

    var style = el('style');
    style.id = 'ch-style';
    style.textContent = CSS;
    document.head.appendChild(style);
  }

  // Presses the window handles itself, without asking the plugin.
  var LOCAL = {
    tab: function (tab) {
      if (TABS.indexOf(tab) < 0) return;

      state.tab = tab;
      render();
    },
    show: function (show) {
      state.show = show;
      render();
    }
  };

  function onClick(e) {
    var target = e.target.closest('[data-act]');

    if (!target) return;

    e.stopPropagation();

    if (target.disabled) return;

    var action = target.getAttribute('data-act');
    var args = target.getAttribute('data-args');

    if (LOCAL[action]) {
      LOCAL[action](args);

      return;
    }

    // Opening or shutting the window gets no status line back, so it must not hold the buttons busy.
    if (action === 'close' || action === 'toggle') {
      send(action);

      return;
    }

    sendAction(args != null ? action + '|' + args : action);
  }

  // Text boxes: typing pauses the game's keys, and the window is not redrawn under the cursor.
  function onFocus(e) {
    if (!isTextBox(e.target)) return;

    state.isTyping = true;
    send('focus|1');
  }

  function onBlur(e) {
    if (!isTextBox(e.target)) return;

    state.isTyping = false;
    send('focus|0');

    if (state.isStale) render();
  }

  function onKey(e) {
    if (e.key === 'Escape' && isTextBox(e.target) && e.target.blur) e.target.blur();
  }

  // The search box narrows the dish list in place, so the box keeps its cursor.
  function onInput(e) {
    if (!e.target.getAttribute || e.target.getAttribute('data-field') !== 'search') return;

    state.search = e.target.value;
    fillDishes();
  }

  function isTextBox(node) {
    return !!node && node.tagName === 'INPUT';
  }

  // ---------- data ----------

  function catalog(data) {
    cat = { types: {}, items: {}, recipes: {} };

    ((data && data.types) || []).forEach(function (t) { cat.types[t.id] = t; });
    ((data && data.items) || []).forEach(function (i) { cat.items[i.id] = i; });
    ((data && data.recipes) || []).forEach(function (r) { cat.recipes[r.id] = r; });
    overlayVersion++;

    if (state.isOpen) render();
  }

  function typeOf(id) { return cat.types[id] || { id: id, name: '#' + id }; }

  function itemOf(id) { return cat.items[id] || { id: id, name: '#' + id }; }

  function recipeOf(id) { return cat.recipes[id] || { id: id, name: '#' + id }; }

  function dishes() { return state.data.dishes || []; }

  // Hours as the window shows them: one decimal under ten hours, whole hours to two days, then days.
  function timeLeft(h) {
    if (h >= 48) return say('days', Math.round(h / 24));

    return say('hours', h < 10 ? Math.round(h * 10) / 10 : Math.round(h));
  }

  // ---------- frame ----------

  function render() {
    if (!ui || !state.isOpen) return;

    // Typing into a box must not lose the cursor to a refresh; the refresh shows when the box loses focus.
    if (state.isTyping) {
      state.isStale = true;

      return;
    }

    state.isStale = false;

    var scrolls = keepScrolls();

    ui.title.textContent = say('title');
    ui.dishes = null;
    renderHead();
    renderTabs();
    ui.body.textContent = '';

    if (state.tab === 'settings') renderSettings();
    else if (!state.data.ready) ui.body.appendChild(el('div', 'ch-lockmsg', state.data.note || ''));
    else if (state.tab === 'pantry') renderPantry();
    else if (state.tab === 'queue') renderQueue();
    else renderCook();

    restoreScrolls(scrolls);
    renderKeys();
  }

  function keepScrolls() {
    var kept = {};

    ui.body.querySelectorAll('[data-scroll]').forEach(function (node) { kept[node.getAttribute('data-scroll')] = node.scrollTop; });

    return kept;
  }

  function restoreScrolls(kept) {
    ui.body.querySelectorAll('[data-scroll]').forEach(function (node) {
      var top = kept[node.getAttribute('data-scroll')];

      if (top) node.scrollTop = top;
    });
  }

  function renderHead() {
    var data = state.data;
    var stove = data.stove;

    ui.sum.textContent = '';

    if (!data.ready) return;

    ui.sum.appendChild(el('div', '', say('level', data.level, data.floor)));

    if (!stove) ui.sum.appendChild(el('div', '', say('stoveNone')));
    else if (stove.cooking) ui.sum.appendChild(el('div', 'ch-hot', say('stoveCooking')));
    else ui.sum.appendChild(el('div', 'ch-hot', say('stoveOpen', stove.room, stove.max)));
  }

  function renderTabs() {
    ui.tabs.textContent = '';

    TABS.forEach(function (tab) {
      var node = act(el('button', 'ch-tab' + (state.tab === tab ? ' ch-on' : ''), say(TAB_WORD[tab])), 'tab', tab);

      if (tab === 'pantry' && state.badge > 0) node.appendChild(el('span', 'ch-n', String(state.badge)));
      if (tab === 'queue' && queued().length > 0) node.appendChild(el('span', 'ch-n', String(queued().length)));

      ui.tabs.appendChild(node);
    });
  }

  function renderKeys() {
    ui.keys.textContent = '';

    if (state.key) put(ui.keys, el('span', 'ch-kbd', state.key), text('/ '));

    ui.keys.appendChild(text(say('escClose')));
  }

  // ---------- Cook tab ----------

  function renderCook() {
    var counts = { all: dishes().length, ready: 0, one: 0, short: 0, locked: 0 };
    var search = el('input', 'ch-inp');
    var list = scroller('dishes');

    dishes().forEach(function (d) { counts[d.state]++; });
    search.setAttribute('data-field', 'search');
    search.setAttribute('placeholder', say('search'));
    search.value = state.search;
    put(ui.body,
      put(el('div', 'ch-bar'), segment(FILTERS.map(function (f) { return [f[0], say(f[1]), counts[f[0]]]; }), state.show, 'show'), el('div', 'ch-grow'), search),
      list);
    ui.dishes = list;
    fillDishes();
  }

  function fillDishes() {
    if (!ui || !ui.dishes) return;

    var needle = state.search.trim().toLowerCase();
    var shown = dishes().filter(function (d) {
      return (state.show === 'all' || d.state === state.show) && (!needle || recipeOf(d.id).name.toLowerCase().indexOf(needle) >= 0);
    });
    var rows = el('div', 'ch-rows');

    ui.dishes.textContent = '';
    shown.forEach(function (d) { rows.appendChild(dishRow(d)); });
    ui.dishes.appendChild(shown.length ? rows : el('div', 'ch-empty', say('nothing')));
  }

  function dishRow(d) {
    var recipe = recipeOf(d.id);
    var row = el('div', 'ch-row' + (d.state === 'locked' ? ' ch-locked' : d.state === 'ready' ? ' ch-ready' : ''));
    var name = put(el('div', 'ch-nm'), el('span', '', recipe.name));
    var main = put(el('div', 'ch-main'), name);
    var perf = el('div', 'ch-perf');

    row.setAttribute('data-dish', String(d.id));

    if (d.isNew) name.appendChild(badge(say('bNew'), 'ch-b-amber'));
    if (d.spoiling) name.appendChild(badge(say('bSpoiling'), 'ch-b-red'));
    if (d.varies) name.appendChild(badge(say('bVaries')));

    if (d.state === 'locked') main.appendChild(el('div', 'ch-lock', d.craftLevel > 0 ? say('lockLevel', d.craftLevel) : say('lockLearn')));
    else main.appendChild(needsLine(d.needs || []));

    if (d.perfect != null) put(perf, el('b', '', d.perfect + '%'), text(say('tPerfect')));

    put(row, icon(recipe, 'ch-i48'), main, perf, put(el('div', 'ch-acts'), button(say('queue'), 'queue', d.id, canQueue(d)), button(say('fill'), 'fill', d.id, canFill(d), 'ch-hot')));

    return row;
  }

  function needsLine(needs) {
    var line = el('div', 'ch-needs');

    needs.forEach(function (need) {
      var chip = el('span', 'ch-need' + (need.have < need.want ? ' ch-miss' : ''));
      var count = el('span', '', need.have + '/' + need.want);

      if (need.type) put(chip, el('span', 'ch-tag', say('anyType', typeOf(need.id).name)), count);
      else put(chip, icon(itemOf(need.id), 'ch-i20'), el('span', '', itemOf(need.id).name), count);

      line.appendChild(chip);
    });

    return line;
  }

  // Fill needs a stove's cooking screen open on an empty pot that is not cooking and can make the dish.
  function canFill(d) {
    var stove = state.data.stove;

    if (!stove || stove.cooking || !stove.potEmpty || d.state !== 'ready') return false;

    return stove.takesAll || (stove.allowed || []).indexOf(d.id) >= 0;
  }

  function queued() { return state.data.queue || []; }

  // A dish waits in the queue once, and only while it is learned and there is room.
  function canQueue(d) {
    return d.state !== 'locked' && !state.data.queueFull && queued().indexOf(d.id) < 0;
  }

  // ---------- Queue tab ----------

  function renderQueue() {
    var well = scroller('queue');
    var rows = el('div', 'ch-rows');

    well.appendChild(el('div', 'ch-sub', say(queued().length ? 'queueKNote' : 'queueKNone')));
    queued().forEach(function (id, i) {
      var recipe = recipeOf(id);

      rows.appendChild(put(el('div', 'ch-qrow'), icon(recipe, 'ch-i28'), el('span', '', (i + 1) + '. ' + recipe.name), button(say('unqueue'), 'unqueue', id)));
    });
    well.appendChild(rows);
    ui.body.appendChild(well);
  }

  // ---------- Pantry tab ----------

  function renderPantry() {
    var shelves = state.data.pantry || [];
    var well = scroller('pantry');
    var rows = el('div', 'ch-rows');

    if (!shelves.length) {
      well.appendChild(el('div', 'ch-empty', say('pEmpty')));
      ui.body.appendChild(well);

      return;
    }

    rows.appendChild(put(el('div', 'ch-prow ch-phead'), el('span', '', say('pFood')), el('span', '', say('pCount')), el('span', '', say('pLeft')), el('span', '', say('pWhere'))));
    shelves.forEach(function (s) { rows.appendChild(shelfRow(s)); });
    well.appendChild(rows);
    ui.body.appendChild(well);
  }

  function shelfRow(s) {
    var item = itemOf(s.item);
    var count = put(el('span', ''), el('span', 'ch-num', String(s.count)));
    var left = s.hours == null ? el('span', 'ch-dim', say('pKeeps')) : el('span', s.hours <= SPOILING_HOURS ? 'ch-red' : '', timeLeft(s.hours));

    if (s.spoiled > 0) count.appendChild(badge(say('pSpoiled', s.spoiled), 'ch-b-red'));

    return put(el('div', 'ch-prow'),
      put(el('span', 'ch-pname'), icon(item, 'ch-i28'), el('span', '', item.name)),
      count,
      left,
      el('span', 'ch-dim', (s.places || []).join(', ')));
  }

  // ---------- Settings tab ----------

  function renderSettings() {
    var well = scroller('settings');
    var settings = state.data.settings || {};

    put(well,
      setRow(say('sSpoilFirst'), say('sSpoilFirstNote'), switchButton(!!settings.spoilFirst, 'spoilfirst', settings.spoilFirst ? '0' : '1')),
      setRow(say('sOdds'), say('sOddsNote'), switchButton(!!settings.odds, 'odds', settings.odds ? '0' : '1')),
      setRow(say('sKey'), say('sKeyNote'), el('span', 'ch-kbd', state.key || say('sKeyNone'))));
    ui.body.appendChild(well);
  }

  // ---------- API ----------

  function open(data) {
    build();
    state.data = data || state.data;
    state.isOpen = true;
    state.isTyping = false;
    shownShape = JSON.stringify(state.data);
    ui.status.textContent = '';
    ui.toast.style.display = 'none';
    ui.root.style.display = 'flex';
    render();
  }

  // Called about once a second while the window is open; the plugin only calls when the view changed.
  function setData(data) {
    state.data = data || state.data;

    var shape = JSON.stringify(state.data);

    if (shape === shownShape) return;

    shownShape = shape;
    render();
  }

  function close() {
    state.isOpen = false;

    if (state.isTyping) send('focus|0');

    state.isTyping = false;

    if (ui) ui.root.style.display = 'none';
  }

  function status(message) {
    build();
    clearBusy();

    if (state.isOpen) {
      ui.status.textContent = message;

      return;
    }

    // An empty status only clears the line; as a toast it would be an empty box.
    if (!message) {
      ui.toast.style.display = 'none';

      return;
    }

    ui.toast.textContent = message;
    ui.toast.style.display = 'block';
    clearTimeout(toastTimer);
    toastTimer = setTimeout(function () { ui.toast.style.display = 'none'; }, TOAST_MS);
  }

  // Whether a game is loaded, and the key that opens the window ('' when none): the HUD button only shows in a game,
  // and both it and the window name the key.
  function setLive(isLive, key) {
    state.isLive = !!isLive;
    state.key = key || '';
    hudVersion++;

    if (ui && state.isOpen) render();

    patchPagesSafely();
  }

  // The number on the HUD button and the Pantry tab: foods that spoil within a day.
  function setBadge(count) {
    state.badge = Math.max(0, count | 0);
    hudVersion++;

    if (ui && state.isOpen) renderTabs();

    patchPagesSafely();
  }

  // The odds for the game's cooking screen; drawn at once and kept up by the page check.
  function setCooking(data) {
    overlay = data || { show: false };
    overlayVersion++;
    patchPagesSafely();
  }

  // ---------- game pages ----------

  // The HUD and the cooking screen are same-origin iframes, so their documents can be reached from here; polled
  // because the game creates and drops those iframes whenever it likes, and redraws the forecast cards on its own.
  function patchPages() {
    var frames = document.querySelectorAll('iframe');

    for (var i = 0; i < frames.length; i++) {
      var src = frames[i].src || '';
      var isHud = /CoreUI1\.html/.test(src);
      var isCooking = /Cooking\.html/.test(src);

      if (!isHud && !isCooking) continue;

      var doc = null;

      try {
        doc = frames[i].contentDocument;
      } catch (e) {}

      if (!doc) {
        if (!hasReportedPageAccess) send('diag|cannot reach a game page document (root ' + location.protocol + ', iframe ' + src + ')');
        hasReportedPageAccess = true;
        continue;
      }

      if (isHud) {
        placeHudButton(doc);
      } else {
        drawStation(doc, frames[i].contentWindow);
        drawOdds(doc, frames[i].contentWindow);
      }
    }
  }

  function patchPagesSafely() {
    try {
      patchPages();
    } catch (e) {}
  }

  function addPageStyle(doc, id, css) {
    if (doc.getElementById(id)) return;

    var style = doc.createElement('style');
    style.id = id;
    style.textContent = css;
    (doc.head || doc.body).appendChild(style);
  }

  // ---------- HUD button ----------

  var HUD_CSS = [
    '.ch-hud-btn{position:relative}',
    '.ch-hud-btn .ch-hud-badge{position:absolute;top:-6px;right:-6px;min-width:18px;height:18px;padding:0 5px;border-radius:9px;background:#e53935;color:#fff;font-size:11px;font-weight:700;line-height:18px;text-align:center;pointer-events:none;box-shadow:0 1px 4px rgba(0,0,0,.6)}'
  ].join('\n');

  // First in the tool column: the column is anchored at the bottom, so the first child sits on top and the game's own
  // buttons do not move. The column hides itself in build mode, and the button with it.
  function placeHudButton(doc) {
    var column = doc.querySelector('.vertical-tools');
    var existing = doc.querySelector('.' + HUD_CLASS);

    if (!column || !state.isLive) {
      if (existing) existing.remove();

      return;
    }

    if (existing && existing.parentNode === column && existing.getAttribute('data-version') === String(hudVersion)) return;
    if (existing) existing.remove();

    addPageStyle(doc, HUD_STYLE_ID, HUD_CSS);

    var node = hudButton(doc);

    node.setAttribute('data-etk-dock', DOCK_ORDER);
    column.insertBefore(node, column.firstChild);

    if (!hasReportedHudPlaced) send('diag|HUD button placed in the tool column');
    hasReportedHudPlaced = true;
  }

  // The same markup as the game's bag button: its classes, an svg icon, and the hover tip its stylesheet shows. The
  // HUD page watches its body for new buttons and tells the game where they are, so a click on this one reaches it.
  function hudButton(doc) {
    var node = doc.createElement('button');

    node.className = 'ui-btn icon-sq interactive-btn has-entry-tip ' + HUD_CLASS;
    node.setAttribute('data-interactive', '');
    node.setAttribute('data-version', String(hudVersion));
    node.appendChild(potIcon(doc));

    if (state.badge > 0) node.appendChild(make(doc, 'span', 'ch-hud-badge', state.badge > 99 ? '99+' : String(state.badge)));

    node.appendChild(entryTip(doc));
    node.addEventListener('click', function (e) {
      e.stopPropagation();
      send('toggle');
    });

    return node;
  }

  function potIcon(doc) {
    var svg = doc.createElementNS(SVG_NS, 'svg');
    var path = doc.createElementNS(SVG_NS, 'path');

    svg.setAttribute('class', 'icon-svg');
    svg.setAttribute('viewBox', '0 0 24 24');
    path.setAttribute('d', POT_PATH);
    svg.appendChild(path);

    return svg;
  }

  function entryTip(doc) {
    var tip = make(doc, 'div', 'entry-tip');
    var title = make(doc, 'div', 'entry-tip-title', say('hub'));

    if (state.key) title.appendChild(make(doc, 'span', 'entry-tip-key', state.key));

    tip.appendChild(title);
    tip.appendChild(make(doc, 'div', 'entry-tip-body', say('tip')));

    return tip;
  }

  // ---------- the game's cooking screen ----------

  // Classes the game's own stylesheet already gives these pages (Cooking.html): its colours, its recipe rows.
  var STATION_CSS = [
    '.right-panel{width:400px!important}',
    '.cooking-system{min-height:780px!important}.recipe-section{min-height:300px!important}.prediction-list{max-height:none!important}',
    '.action-buttons>button{flex:1 1 0;min-width:0}.action-buttons>.ch-btn2{flex:0 0 auto}',
    '.ch-bar{flex-shrink:0}',
    '.ch-tabs{display:flex;gap:6px;margin-bottom:8px}',
    '.ch-tab{flex:1;text-align:center;padding:6px 4px;font-size:13px;border-radius:6px;cursor:pointer;color:var(--text-dark);background:rgba(25,20,18,.85);border:1px solid rgba(255,255,255,.1);white-space:nowrap}',
    '.ch-tab:hover{color:var(--text-main);border-color:rgba(251,176,52,.4)}',
    '.ch-tab.ch-on{color:var(--warm-gold);border-color:rgba(251,176,52,.6);background:rgba(251,176,52,.12);font-weight:bold}',
    '.ch-tab .ch-n{display:inline-block;min-width:16px;padding:0 4px;margin-left:4px;border-radius:8px;font-size:10px;line-height:16px;background:rgba(251,176,52,.25);color:var(--warm-gold)}',
    '.ch-tab .ch-n.ch-red{background:rgba(224,85,85,.35);color:#ffb4b4}',
    '.ch-tools{display:flex;gap:6px;margin-bottom:8px;align-items:center}',
    '.ch-search{flex:1;min-width:0;padding:6px 9px;border-radius:5px;border:1px solid rgba(255,255,255,.15);background:rgba(0,0,0,.35);color:var(--text-main);font-size:12px;font-family:inherit}',
    '.ch-chip{font-size:11px;padding:4px 8px;border-radius:10px;cursor:pointer;color:var(--text-dark);border:1px solid rgba(255,255,255,.14);white-space:nowrap}',
    '.ch-chip.ch-on{color:var(--warm-gold);border-color:rgba(251,176,52,.55);background:rgba(251,176,52,.1)}',
    '.ch-where{font-size:11px;margin-top:2px;color:#9fc88a;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}',
    '.ch-where.ch-miss{color:rgba(255,255,255,.4)}',
    '.ch-mini{display:flex;height:5px;border-radius:3px;overflow:hidden;margin-top:4px;background:rgba(0,0,0,.5);width:100%}.ch-mini i{display:block;height:100%}',
    '.ch-pp{font-size:11px;color:var(--q-perfect);flex-shrink:0;width:34px;text-align:right}',
    '.book-item.ch-storage .st-fill{color:#111;background:var(--warm-gold);border-color:var(--warm-gold)}',
    '.ch-view{overflow-y:auto;height:0;min-height:0;flex-grow:1;display:flex;flex-direction:column;gap:6px;padding-right:5px}',
    '.ch-view::-webkit-scrollbar{width:4px}.ch-view::-webkit-scrollbar-thumb{background:rgba(255,255,255,.2);border-radius:2px}',
    '.ch-sub{font-size:11px;color:var(--text-dark);opacity:.8;padding:2px 2px 0}',
    '.ch-sp{font-size:11px;padding:2px 6px;border-radius:4px;flex-shrink:0}',
    '.ch-sp.ch-red{background:rgba(224,85,85,.25);color:#ffb4b4}.ch-sp.ch-amber{background:rgba(251,176,52,.18);color:var(--warm-gold)}',
    '.ch-sp.ch-dim{background:rgba(255,255,255,.06);color:var(--text-dark)}.ch-sp.ch-bad{background:rgba(224,85,85,.5);color:#fff}',
    '.ch-x{font-size:11px;padding:2px 6px;border-radius:4px;cursor:pointer;color:var(--text-dark);border:1px solid rgba(255,255,255,.14);flex-shrink:0}',
    '.ch-odds{width:100%;margin-top:8px;display:grid;grid-template-columns:62px 1fr 38px;gap:3px 8px;align-items:center;font-size:11px}',
    '.ch-odds .ch-ot{color:var(--text-dark)}.ch-odds .ch-op{text-align:right;color:var(--text-main)}.ch-odds .ch-zero{opacity:.35}',
    '.ch-odds .ch-bar7{height:7px;border-radius:4px;background:rgba(0,0,0,.5);overflow:hidden}.ch-odds .ch-bar7 i{display:block;height:100%}',
    '.ch-why{width:100%;margin-top:6px;font-size:11px;color:var(--text-dark);display:flex;flex-wrap:wrap;gap:4px}',
    '.ch-why span{padding:1px 6px;border-radius:4px;background:rgba(255,255,255,.06)}',
    '.ch-why span.ch-up{color:#9fc88a}.ch-why span.ch-down{color:#ff9a9a;background:rgba(224,85,85,.15)}',
    '#ch-nudges{display:flex;flex-direction:column;gap:6px;margin-top:8px}',
    '.ch-nudge{display:flex;align-items:center;gap:8px;padding:6px 9px;border-radius:6px;cursor:pointer;font-size:12px;color:var(--text-main);background:rgba(251,176,52,.1);border:1px dashed rgba(251,176,52,.55);font-family:inherit;text-align:left}',
    '.ch-nudge:hover{background:rgba(251,176,52,.22)}.ch-nudge .ch-gain{margin-left:auto;color:var(--q-perfect);font-weight:bold;white-space:nowrap}',
    '.ch-stoveline{font-size:12px;color:var(--text-dark);margin:0 0 8px;display:flex;justify-content:space-between;gap:8px}',
    '.ch-stoveline b{color:var(--text-main);font-weight:600}',
    '.ch-btn2{background:rgba(251,176,52,.12);color:var(--warm-gold);border:1px solid rgba(251,176,52,.55);padding:10px;border-radius:6px;font-size:13px;font-weight:bold;cursor:pointer;font-family:inherit;white-space:nowrap}',
    '.ch-btn2:hover{background:rgba(251,176,52,.25)}',
    '.ch-fuelbtn{font-size:11px;padding:3px 8px;border-radius:4px;cursor:pointer;color:var(--warm-gold);background:rgba(251,176,52,.12);border:1px solid rgba(251,176,52,.45);margin-left:8px}'
  ].join('\n');

  // Best first, the order the odds are drawn in, with the colours the game gives each quality.
  var TIER_ORDER = [3, 2, 1, 0];
  var TIER_COLOR = ['#8a8a8a', 'rgba(255,255,255,.6)', 'var(--q-good)', 'var(--q-perfect)'];
  var STATION_TABS = [['book', 'tabBook'], ['now', 'tabNow'], ['pantry', 'tabPantry'], ['queue', 'tabQueue']];
  var STATION_FILTERS = [['all', 'fAll'], ['load', 'fCanLoad'], ['new', 'fNew'], ['locked', 'fLocked']];
  // Marks everything drawn on the cooking screen, so it can all be taken off again.
  var OURS = 'ch-st';

  // What the player chose on the cooking screen; the version counts up with each choice so the views redraw.
  var station = { tab: 'book', filter: 'all', search: '', version: 0, started: 0 };

  function drawStation(doc, win) {
    if (!overlay.show || !overlay.book) {
      clearStation(doc);

      return;
    }

    addPageStyle(doc, STATION_STYLE_ID, STATION_CSS);

    var section = doc.getElementById('recipeSection');
    var list = doc.getElementById('recipeList');

    if (section && list) {
      hideBookTitle(doc);
      drawBar(doc, section, list);
      drawBookRows(doc, win, list);
      drawView(doc, section);
    }

    drawStoveLine(doc);
    drawRepeat(doc);
    drawTopUp(doc);
    pressStart(doc, win);
  }

  function clearStation(doc) {
    doc.querySelectorAll('.' + OURS).forEach(function (node) { node.remove(); });

    var list = doc.getElementById('recipeList');

    if (!list) return;

    list.style.display = '';
    list.querySelectorAll('.book-item').forEach(function (row) { row.style.display = ''; });
  }

  // The tabs stand where the book's title was; hot pot mode hides the whole book, title included, on its own.
  function hideBookTitle(doc) {
    var title = doc.getElementById('recipePanelTitle');

    if (title && !(overlay.stove && overlay.stove.hotPot)) title.style.display = 'none';
  }

  function stationKey() {
    return overlayVersion + ':' + station.version;
  }

  // A button on a game page: the page tells the game where its interactive parts are, so a click on it reaches it,
  // and the click stops here so the row under it does not take it too.
  function pageButton(doc, tag, className, content, onPress) {
    var node = make(doc, tag, className + ' interactive-btn ' + OURS, content);

    node.setAttribute('data-interactive', '');
    node.addEventListener('click', function (e) {
      e.stopPropagation();

      if (!node.disabled) onPress();
    });

    return node;
  }

  function choose(change) {
    change();
    station.version++;
    patchPagesSafely();
  }

  // ---------- tabs and filters ----------

  // Built once per screen so the search box keeps its cursor; only the labels and the lit choice change after that.
  function drawBar(doc, section, list) {
    var bar = firstChildWithClass(section, 'ch-bar');

    if (!bar) {
      bar = buildBar(doc);
      section.insertBefore(bar, list);
    }

    var counts = tabCounts();

    kids(bar.children[0]).forEach(function (tab, i) {
      var id = STATION_TABS[i][0];
      var label = say(STATION_TABS[i][1]) + '|' + counts[id];

      tab.className = 'ch-tab interactive-btn' + (station.tab === id ? ' ch-on' : '');

      if (tab.getAttribute('data-label') === label) return;

      tab.setAttribute('data-label', label);
      tab.textContent = say(STATION_TABS[i][1]);

      if (counts[id]) tab.appendChild(make(doc, 'span', 'ch-n' + (id === 'pantry' ? ' ch-red' : ''), String(counts[id])));
    });

    var tools = bar.children[1];

    tools.style.display = station.tab === 'book' || station.tab === 'now' ? '' : 'none';
    kids(tools).slice(1).forEach(function (chip, i) {
      chip.className = 'ch-chip interactive-btn' + (station.filter === STATION_FILTERS[i][0] ? ' ch-on' : '');
    });
  }

  function buildBar(doc) {
    var bar = make(doc, 'div', 'ch-bar ' + OURS);
    var tabs = make(doc, 'div', 'ch-tabs');
    var tools = make(doc, 'div', 'ch-tools');
    var search = make(doc, 'input', 'ch-search');

    STATION_TABS.forEach(function (t) {
      tabs.appendChild(pageButton(doc, 'div', 'ch-tab', say(t[1]), function () { choose(function () { station.tab = t[0]; }); }));
    });

    search.setAttribute('placeholder', say('searchFood'));
    search.value = station.search;
    // Typing must not walk the player around, so the game's keys pause while the box has focus.
    search.addEventListener('focusin', function () { send('focus|1'); });
    search.addEventListener('focusout', function () { send('focus|0'); });
    search.addEventListener('input', function () { choose(function () { station.search = search.value; }); });
    tools.appendChild(search);

    STATION_FILTERS.forEach(function (f) {
      tools.appendChild(pageButton(doc, 'span', 'ch-chip', say(f[1]), function () { choose(function () { station.filter = f[0]; }); }));
    });

    bar.appendChild(tabs);
    bar.appendChild(tools);

    return bar;
  }

  function tabCounts() {
    return {
      book: 0,
      now: cookNowDishes().length,
      pantry: (overlay.pantry || []).filter(function (s) { return s.spoiled > 0 || (s.hours != null && s.hours <= SPOILING_HOURS); }).length,
      queue: (overlay.queue || []).length
    };
  }

  // ---------- the game's recipe book rows ----------

  // The game draws one row per entry of State.lastRecipes, in order, with an open group's variants between rows, so
  // rows are matched to recipes by counting rows, never by position among all children.
  function drawBookRows(doc, win, list) {
    var recipes = (win && win.State && win.State.lastRecipes) || [];
    var rows = kids(list).filter(function (child) { return child.classList.contains('book-item'); });
    var byId = bookById();

    list.style.display = station.tab === 'book' ? '' : 'none';

    if (rows.length !== recipes.length) return;

    rows.forEach(function (row, i) {
      var recipe = recipes[i];
      var dish = byId[recipe.recipeId];
      var isShown = matchesFilter(recipe, dish) && matchesSearch(recipe.name + ' ' + (recipe.costText || ''), dish);
      var variants = row.nextSibling;

      decorateRow(doc, row, recipe, dish);
      row.style.display = isShown ? '' : 'none';

      if (variants && variants.classList.contains('recipe-variants')) variants.style.display = isShown ? '' : 'none';
    });
  }

  function bookById() {
    var byId = {};

    (overlay.book || []).forEach(function (dish) { byId[dish.id] = dish; });

    return byId;
  }

  function matchesFilter(recipe, dish) {
    if (station.filter === 'load') return !!dish && dish.state === 'ready';
    if (station.filter === 'new') return !!dish && dish.isNew;
    if (station.filter === 'locked') return !!recipe.craftLocked || (!!dish && dish.state === 'locked');

    return true;
  }

  function matchesSearch(words, dish) {
    var needle = station.search.trim().toLowerCase();

    if (!needle) return true;

    var food = dish ? (dish.places || []).concat((dish.missing || []).map(needName)) : [];

    return (words + ' ' + food.join(' ')).toLowerCase().indexOf(needle) >= 0;
  }

  // Where the food is or what is missing, a bar of the odds, the Perfect chance, and Load when the game's own fill
  // would refuse (its rows only count the bag) but the player's storages cover the dish.
  function decorateRow(doc, row, recipe, dish) {
    var key = String(overlayVersion);

    if (row.getAttribute('data-ch') === key) return;

    if (!row.hasAttribute('data-ch-class')) row.setAttribute('data-ch-class', row.className);

    row.setAttribute('data-ch', key);
    row.className = row.getAttribute('data-ch-class');
    removeOurs(row);

    if (!dish) return;

    var body = firstChildWithClass(row, 'book-bd') || row;
    var gameButton = firstChildWithClass(row, 'st-fill') || firstChildWithClass(row, 'st-ready');
    var isGroup = !recipe.craftLocked && recipe.variantCount > 1;

    removeOurs(body);
    body.appendChild(whereLine(doc, dish));

    if (dish.chances) {
      body.appendChild(miniBar(doc, dish.chances));
      row.insertBefore(make(doc, 'span', 'ch-pp ' + OURS, dish.chances[3] + '%'), gameButton);
    }

    if (recipe.status !== 0 || recipe.craftLocked || isGroup || dish.state !== 'ready') return;

    row.className = row.className.split(' ').filter(function (name) { return name !== 'insuff'; }).join(' ') + ' fillable ch-storage';
    row.appendChild(pageButton(doc, 'span', 'st-fill', say('load'), function () { sendAction('fill|' + dish.id); }));
  }

  // A live HTMLCollection has no array methods.
  function kids(node) {
    return Array.prototype.slice.call(node.children);
  }

  function removeOurs(node) {
    kids(node).forEach(function (child) {
      if (child.classList.contains(OURS)) child.remove();
    });
  }

  function whereLine(doc, dish) {
    var missing = dish.missing || [];

    if (missing.length) return make(doc, 'div', 'ch-where ch-miss ' + OURS, say('missing', missing.map(needName).join(', ')));

    return make(doc, 'div', 'ch-where ' + OURS, (dish.places || []).join(', '));
  }

  function needName(need) {
    return need.type ? say('anyType', typeOf(need.id).name) : itemOf(need.id).name;
  }

  function miniBar(doc, chances) {
    var bar = make(doc, 'span', 'ch-mini ' + OURS);

    TIER_ORDER.forEach(function (tier) {
      var part = make(doc, 'i');

      part.style.width = chances[tier] + '%';
      part.style.background = TIER_COLOR[tier];
      bar.appendChild(part);
    });

    return bar;
  }

  // ---------- Cook now, Pantry and Queued ----------

  function drawView(doc, section) {
    var view = firstChildWithClass(section, 'ch-view');
    var key = station.tab + ':' + stationKey();

    if (station.tab === 'book') {
      if (view) view.remove();

      return;
    }

    if (view && view.getAttribute('data-key') === key) return;

    var top = view ? view.scrollTop : 0;
    var fresh = make(doc, 'div', 'ch-view ' + OURS);

    fresh.setAttribute('data-key', key);

    if (station.tab === 'now') fillNow(doc, fresh);
    else if (station.tab === 'pantry') fillPantry(doc, fresh);
    else fillQueue(doc, fresh);

    if (view) view.remove();

    section.appendChild(fresh);
    fresh.scrollTop = top;
  }

  // What this stove can make from the bag and every storage, the dish whose food spoils soonest first.
  function cookNowDishes() {
    return (overlay.book || []).filter(function (dish) { return dish.state === 'ready'; }).sort(function (a, b) {
      return (a.hours == null ? Infinity : a.hours) - (b.hours == null ? Infinity : b.hours);
    });
  }

  function fillNow(doc, view) {
    var shown = cookNowDishes().filter(function (dish) {
      return matchesFilter({}, dish) && matchesSearch(recipeOf(dish.id).name, dish);
    });

    view.appendChild(make(doc, 'div', 'ch-sub', say('nowNote')));

    if (!shown.length) view.appendChild(make(doc, 'div', 'ch-sub', say('nowNone')));

    shown.forEach(function (dish) {
      var row = viewRow(doc, recipeOf(dish.id), recipeOf(dish.id).name, whereLine(doc, dish), 'fillable');

      if (dish.chances) row.children[1].appendChild(miniBar(doc, dish.chances));
      if (dish.hours != null) row.appendChild(spoilChip(doc, dish.hours));
      if (dish.chances) row.appendChild(make(doc, 'span', 'ch-pp', dish.chances[3] + '%'));

      row.appendChild(pageButton(doc, 'span', 'st-fill', say('load'), function () { sendAction('fill|' + dish.id); }));
      view.appendChild(row);
    });
  }

  // Every food the player has, soonest to spoil first; a click puts one in the pot from wherever it is kept.
  function fillPantry(doc, view) {
    var shelves = overlay.pantry || [];

    view.appendChild(make(doc, 'div', 'ch-sub', shelves.length ? say('pantryNote') : say('pEmpty')));

    shelves.forEach(function (s) {
      var item = itemOf(s.item);
      var name = item.name + ' x' + s.count + (s.spoiled > 0 ? ', ' + say('pSpoiled', s.spoiled) : '');
      var row = viewRow(doc, item, name, make(doc, 'div', 'book-cost', (s.places || []).join(', ')), s.count > 0 ? 'fillable' : 'insuff');

      row.appendChild(s.spoiled > 0 && s.count === 0 ? make(doc, 'span', 'ch-sp ch-bad', say('spoiledWord')) : spoilChip(doc, s.hours));

      if (s.count > 0 && !overlay.cooking) {
        row.className += ' interactive-btn';
        row.setAttribute('data-interactive', '');
        row.addEventListener('click', function (e) {
          e.stopPropagation();
          sendAction('add|' + s.item);
        });
      }

      view.appendChild(row);
    });
  }

  // The dishes queued in the Cooking Hub window that this stove can cook; Load next fills the first one.
  function fillQueue(doc, view) {
    var queue = overlay.queue || [];
    var canLoad = !overlay.cooking && overlay.potEmpty;

    view.appendChild(make(doc, 'div', 'ch-sub', queue.length ? say('queueNote') : say('queueNone')));

    queue.forEach(function (id, i) {
      var recipe = recipeOf(id);
      var dish = bookById()[id];
      var row = viewRow(doc, recipe, (i + 1) + '. ' + recipe.name, dish ? whereLine(doc, dish) : null, i === 0 ? 'fillable' : '');

      if (i === 0 && canLoad) row.appendChild(pageButton(doc, 'span', 'st-fill', say('loadNext'), function () { sendAction('next'); }));
      else row.appendChild(make(doc, 'span', 'ch-sp ch-dim', say('waiting')));

      row.appendChild(pageButton(doc, 'span', 'ch-x', say('unqueue'), function () { sendAction('unqueue|' + id); }));
      view.appendChild(row);
    });
  }

  // A row in the game's own recipe row style: icon, then name over a second line.
  function viewRow(doc, thing, name, second, stateClass) {
    var row = make(doc, 'div', 'book-item' + (stateClass ? ' ' + stateClass : ''));
    var iconBox = make(doc, 'div', 'book-ico');
    var body = make(doc, 'div', 'book-bd');

    if (thing.icon) {
      var img = make(doc, 'img');
      img.src = thing.icon;
      iconBox.appendChild(img);
    }

    body.appendChild(make(doc, 'div', 'book-nm', name));

    if (second) body.appendChild(second);

    row.appendChild(iconBox);
    row.appendChild(body);

    return row;
  }

  function spoilChip(doc, hours) {
    if (hours == null) return make(doc, 'span', 'ch-sp ch-dim', say('pKeeps'));

    var tone = hours <= SPOILING_HOURS ? 'ch-red' : hours <= 3 * SPOILING_HOURS ? 'ch-amber' : 'ch-dim';

    return make(doc, 'span', 'ch-sp ' + tone, timeLeft(hours));
  }

  // ---------- the stove line, Repeat last, fuel ----------

  // Puts the node build makes under parent, before `before`, unless the one already there was drawn for the same key.
  function keepDrawn(doc, id, key, parent, before, build) {
    var existing = doc.getElementById(id);

    if (existing && existing.getAttribute('data-key') === key && existing.parentNode === parent) return;
    if (existing) existing.remove();
    if (!parent || !build) return;

    var node = build();

    node.id = id;
    node.setAttribute('data-key', key);
    parent.insertBefore(node, before);
  }

  function drawStoveLine(doc) {
    var actions = doc.querySelector('.action-buttons');
    var stove = overlay.stove;

    keepDrawn(doc, 'ch-stoveline', String(overlayVersion), actions && actions.parentNode, actions, stove && function () {
      var line = make(doc, 'div', 'ch-stoveline ' + OURS);
      var left = make(doc, 'span', '', stove.name + ': ');

      left.appendChild(make(doc, 'b', '', say('speed', stove.speed)));
      left.appendChild(doc.createTextNode(', '));
      left.appendChild(make(doc, 'b', '', say('quality', stove.bonus)));
      line.appendChild(left);

      if (stove.cookSecs != null) line.appendChild(make(doc, 'span', '', say('cookTime', cookTime(stove.cookSecs))));

      return line;
    });
  }

  function cookTime(seconds) {
    var minutes = Math.max(1, Math.round(seconds / 60));
    var hours = Math.floor(minutes / 60);

    return hours > 0 ? say('hm', hours, minutes % 60) : say('mins', minutes);
  }

  // Refills the pot with the dish this stove last started and starts it again: only while it stands empty and idle.
  function drawRepeat(doc) {
    var actions = doc.querySelector('.action-buttons');
    var isShown = overlay.last != null && !overlay.cooking && overlay.potEmpty;

    keepDrawn(doc, 'ch-repeat', overlayVersion + ':' + overlay.last, actions, null, isShown && function () {
      return pageButton(doc, 'button', 'ch-btn2', say('repeat', recipeOf(overlay.last).name), function () { sendAction('repeat'); });
    });
  }

  // Fuel of the kinds this stove already burns, from the bag and every storage.
  function drawTopUp(doc) {
    var header = doc.querySelector('.fuel-section-header');
    var fuel = overlay.stove && overlay.stove.fuel;
    var isShown = !!fuel && fuel.known && fuel.have > 0;

    keepDrawn(doc, 'ch-topup', overlayVersion + ':' + (fuel ? fuel.have : 0), header, null, isShown && function () {
      return pageButton(doc, 'span', 'ch-fuelbtn', say('topUp', fuel.have), function () { sendAction('topup'); });
    });
  }

  // Repeat last asks for the game's own Cook button once its pot is filled: the plugin raises the start token for a
  // few seconds, and only while the pot forecasts that dish. The button also stops a cooking stove, so it is pressed
  // only while nothing cooks, and once per token.
  function pressStart(doc, win) {
    var token = overlay.start || 0;
    var cook = doc.getElementById('cookBtn');
    var isIdle = !!win && !!win.State && !win.State.isCooking;

    if (!token || token === station.started || overlay.cooking || overlay.potEmpty) return;
    if (!cook || cook.disabled || !isIdle) return;

    station.started = token;
    cook.click();
  }

  // ---------- odds on the cooking screen ----------

  // Draws only when the screen forecasts exactly the dishes the plugin read, in the same order, so odds never sit
  // under the wrong dish while one side catches up with the other.
  function drawOdds(doc, win) {
    var list = doc.getElementById('predictionList');
    var cards = list ? list.querySelectorAll('.pot-card') : [];
    var isMatch = overlay.show && overlay.odds && !!list && sameDishes(win, cards.length);

    if (!isMatch) {
      clearOdds(doc);

      return;
    }

    addPageStyle(doc, STATION_STYLE_ID, STATION_CSS);

    for (var i = 0; i < cards.length; i++) {
      var odds = (overlay.lines || [])[i];
      // Under the dish's name and hint, in the card's text column.
      var holder = firstChildWithClass(cards[i], 'pot-bd') || cards[i];
      var drawn = firstChildWithClass(holder, 'ch-odds');

      if (drawn && drawn.getAttribute('data-version') === String(overlayVersion)) continue;
      if (drawn) drawn.remove();

      var why = firstChildWithClass(holder, 'ch-why');

      if (why) why.remove();

      if (odds) {
        holder.appendChild(oddsBlock(doc, odds));
        holder.appendChild(whyBlock(doc, odds));
      }
    }

    drawNudges(doc, list);

    if (!hasReportedOddsDrawn) send('diag|odds drawn on the cooking screen');
    hasReportedOddsDrawn = true;
  }

  function sameDishes(win, cardCount) {
    var entries = win && win.State && win.State.lastPredictionEntries;
    var ids = overlay.ids || [];

    if (!entries || entries.length !== ids.length || cardCount !== ids.length) return false;

    for (var i = 0; i < ids.length; i++) {
      if (entries[i].RecipeId !== ids[i]) return false;
    }

    return true;
  }

  function clearOdds(doc) {
    doc.querySelectorAll('.ch-odds').forEach(function (node) { node.remove(); });
    doc.querySelectorAll('.ch-why').forEach(function (node) { node.remove(); });

    var nudges = doc.getElementById(NUDGES_ID);

    if (nudges) nudges.remove();
  }

  function firstChildWithClass(node, name) {
    var children = kids(node);

    for (var i = 0; i < children.length; i++) {
      if (children[i].classList.contains(name)) return children[i];
    }

    return null;
  }

  // One line per quality, best first: its name, a bar, and the chance.
  function oddsBlock(doc, odds) {
    var box = make(doc, 'div', 'ch-odds');

    box.setAttribute('data-version', String(overlayVersion));

    TIER_ORDER.forEach(function (tier) {
      var chance = odds.chances[tier] || 0;
      var dim = chance ? '' : ' ch-zero';
      var bar = make(doc, 'span', 'ch-bar7' + dim);
      var fill = make(doc, 'i');

      fill.style.width = chance + '%';
      fill.style.background = TIER_COLOR[tier];
      bar.appendChild(fill);
      box.appendChild(make(doc, 'span', 'ch-ot' + dim, say(TIER_WORD[tier])));
      box.appendChild(bar);
      box.appendChild(make(doc, 'span', 'ch-op' + dim, chance + '%'));
    });

    return box;
  }

  // Why: the level's roll range, then each bonus and penalty.
  function whyBlock(doc, odds) {
    var why = make(doc, 'div', 'ch-why');

    why.appendChild(make(doc, 'span', '', say('whyLevel', overlay.level, odds.floor)));
    (odds.parts || []).forEach(function (p) {
      why.appendChild(make(doc, 'span', p.value < 0 ? 'ch-down' : 'ch-up', partName(p) + ' ' + (p.value > 0 ? '+' : '') + p.value));
    });

    return why;
  }

  function partName(p) {
    if (p.kind === 'season') return p.count === 1 ? say('partSeason1') : say('partSeasonN', p.count);

    return say(PART_WORD[p.kind] || p.kind);
  }

  // The changes that would raise the odds, as buttons under the forecast; only while the pot can still change.
  function drawNudges(doc, list) {
    var existing = doc.getElementById(NUDGES_ID);
    var nudges = overlay.cooking ? [] : overlay.nudges || [];

    if (!nudges.length) {
      if (existing) existing.remove();

      return;
    }

    if (existing && existing.getAttribute('data-version') === String(overlayVersion) && existing.previousSibling === list) return;
    if (existing) existing.remove();

    var box = make(doc, 'div');

    box.id = NUDGES_ID;
    box.setAttribute('data-version', String(overlayVersion));
    nudges.forEach(function (n) { box.appendChild(nudgeButton(doc, n)); });
    list.parentNode.insertBefore(box, list.nextSibling);
  }

  // A seasoning goes in as one more of it; a swap trades a spoiled ingredient for a fresh one of the same.
  function nudgeButton(doc, n) {
    var name = itemOf(n.item).name;
    var node = make(doc, 'button', 'ch-nudge interactive-btn', n.kind === 'season' ? say('nSeason', name) : say('nSwap', name));

    node.setAttribute('data-interactive', '');

    if (n.gain != null) node.appendChild(make(doc, 'span', 'ch-gain', say('nGain', n.gain + '%')));

    node.addEventListener('click', function (e) {
      e.stopPropagation();
      sendAction((n.kind === 'season' ? 'add' : 'swap') + '|' + n.item);
    });

    return node;
  }

  setInterval(patchPagesSafely, 400);

  window.__slcookinghub = { open: open, setData: setData, close: close, status: status, live: setLive, catalog: catalog, badge: setBadge, cooking: setCooking };
  send('ready');
})();
