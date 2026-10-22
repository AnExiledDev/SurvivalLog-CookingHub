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
  var ODDS_STYLE_ID = 'ch-odds-style';
  var NUDGES_ID = 'ch-nudges';
  var BUSY_TIMEOUT_MS = 5000;
  var TOAST_MS = 3500;
  var SVG_NS = 'http://www.w3.org/2000/svg';
  // A cooking pot with a lid and two handles.
  var POT_PATH = 'M11 3h2v2h-2zM5 7h14v2H5zM3 10h2v2H3zm16 0h2v2h-2zM5 10h14v7a3 3 0 0 1-3 3H8a3 3 0 0 1-3-3z';
  var TABS = ['cook', 'pantry', 'settings'];
  var TAB_WORD = { cook: 'tabCook', pantry: 'tabPantry', settings: 'tabSettings' };
  var FILTERS = [['all', 'fAll'], ['ready', 'fReady'], ['one', 'fOne'], ['short', 'fShort'], ['locked', 'fLocked']];
  // Fail, Normal, Good, Perfect: the order the odds arrive in.
  var TIER_WORD = ['tFail', 'tNormal', 'tGood', 'tPerfect'];
  var TIER_CLASS = ['ch-t-fail', 'ch-t-normal', 'ch-t-good', 'ch-t-perfect'];
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
    '.ch-row{display:grid;grid-template-columns:48px minmax(0,1fr) 110px 130px;gap:12px;align-items:center;padding:8px 10px;border-radius:4px;background:rgba(70,58,44,.55);border:1px solid rgba(220,188,120,.11)}',
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

    put(row, icon(recipe, 'ch-i48'), main, perf, button(say('fill'), 'fill', d.id, canFill(d), 'ch-hot'));

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

      if (isHud) placeHudButton(doc);
      else drawOdds(doc, frames[i].contentWindow);
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

  // ---------- odds on the cooking screen ----------

  var ODDS_CSS = [
    '.ch-odds{margin-top:5px;display:flex;flex-direction:column;gap:3px;font-size:11px;color:#c0c0c0;line-height:1.35}',
    '.ch-meter{display:flex;height:6px;border-radius:3px;overflow:hidden;background:rgba(10,6,2,.45)}.ch-meter i{display:block;height:100%}',
    '.ch-tiers{display:flex;flex-wrap:wrap;gap:2px 8px}.ch-tiers b{font-weight:700}',
    '.ch-t-perfect{color:#EBA308}.ch-t-good{color:#7cb342}.ch-t-normal{color:#8d9aa8}.ch-t-fail{color:#e53935}',
    '.ch-meter .ch-t-perfect{background:#EBA308}.ch-meter .ch-t-good{background:#7cb342}.ch-meter .ch-t-normal{background:#8d9aa8}.ch-meter .ch-t-fail{background:#e53935}',
    '.ch-why{color:#a89a82}.ch-why .ch-up{color:#b5e09a}.ch-why .ch-down{color:#ff8a80}',
    '#ch-nudges{margin:8px 0;padding:8px 10px;border-radius:4px;background:rgba(255,200,58,.09);border:1px solid rgba(255,200,58,.35);display:flex;flex-direction:column;gap:6px}',
    '#ch-nudges .ch-nh{font-size:12px;letter-spacing:2px;color:#fbb034;font-weight:700}',
    '.ch-nudge{display:flex;align-items:center;gap:8px;font-size:13px;color:#f0e8d8;padding:5px 8px;border-radius:4px;border:1px solid rgba(220,188,120,.25);background:linear-gradient(180deg,rgba(92,78,60,.9),rgba(66,55,42,.92));cursor:pointer;text-align:left}',
    '.ch-nudge:hover{border-color:#FFC83A}.ch-nudge .ch-gain{margin-left:auto;color:#b5e09a;font-weight:700;white-space:nowrap}'
  ].join('\n');

  // Draws only when the screen forecasts exactly the dishes the plugin read, in the same order, so odds never sit
  // under the wrong dish while one side catches up with the other.
  function drawOdds(doc, win) {
    var list = doc.getElementById('predictionList');
    var cards = list ? list.querySelectorAll('.pot-card') : [];
    var isMatch = overlay.show && !!list && sameDishes(win, cards.length);

    if (!isMatch) {
      clearOdds(doc);

      return;
    }

    addPageStyle(doc, ODDS_STYLE_ID, ODDS_CSS);

    for (var i = 0; i < cards.length; i++) {
      var odds = (overlay.lines || [])[i];
      // Under the dish's name and hint, in the card's text column.
      var holder = firstChildWithClass(cards[i], 'pot-bd') || cards[i];
      var drawn = firstChildWithClass(holder, 'ch-odds');

      if (drawn && drawn.getAttribute('data-version') === String(overlayVersion)) continue;
      if (drawn) drawn.remove();
      if (odds) holder.appendChild(oddsBlock(doc, odds));
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

    var nudges = doc.getElementById(NUDGES_ID);

    if (nudges) nudges.remove();
  }

  function firstChildWithClass(node, name) {
    for (var i = 0; i < node.children.length; i++) {
      if (node.children[i].classList.contains(name)) return node.children[i];
    }

    return null;
  }

  // A bar of the four chances, the chances in words, then why: the level's floor and each bonus.
  function oddsBlock(doc, odds) {
    var box = make(doc, 'div', 'ch-odds');
    var meter = make(doc, 'div', 'ch-meter');
    var tiers = make(doc, 'div', 'ch-tiers');
    var why = make(doc, 'div', 'ch-why');

    box.setAttribute('data-version', String(overlayVersion));

    for (var tier = 3; tier >= 0; tier--) {
      var chance = odds.chances[tier];

      if (!chance) continue;

      var part = make(doc, 'i', TIER_CLASS[tier]);
      part.style.width = chance + '%';
      meter.appendChild(part);

      var word = make(doc, 'span', TIER_CLASS[tier], say(TIER_WORD[tier]) + ' ');
      word.appendChild(make(doc, 'b', '', chance + '%'));
      tiers.appendChild(word);
    }

    why.appendChild(doc.createTextNode(say('why') + ': ' + say('rollFloor', odds.floor)));
    (odds.parts || []).forEach(function (p) {
      why.appendChild(doc.createTextNode(', ' + partName(p) + ' '));
      why.appendChild(make(doc, 'span', p.value < 0 ? 'ch-down' : 'ch-up', (p.value > 0 ? '+' : '') + p.value));
    });
    why.appendChild(doc.createTextNode('. ' + say('lowest') + ' ' + odds.lowest + '.'));

    box.appendChild(meter);
    box.appendChild(tiers);
    box.appendChild(why);

    return box;
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
    box.appendChild(make(doc, 'div', 'ch-nh', say('hub')));
    nudges.forEach(function (n) { box.appendChild(nudgeButton(doc, n)); });
    list.parentNode.insertBefore(box, list.nextSibling);
  }

  function nudgeButton(doc, n) {
    var name = itemOf(n.item).name;
    var node = make(doc, 'button', 'ch-nudge interactive-btn', n.kind === 'season' ? say('nSeason', name) : say('nSwap', name));

    node.setAttribute('data-interactive', '');

    if (n.gain != null) node.appendChild(make(doc, 'span', 'ch-gain', say('nGain', n.gain + '%')));

    node.addEventListener('click', function (e) {
      e.stopPropagation();
      sendAction(n.kind + '|' + n.item);
    });

    return node;
  }

  setInterval(patchPagesSafely, 400);

  window.__slcookinghub = { open: open, setData: setData, close: close, status: status, live: setLive, catalog: catalog, badge: setBadge, cooking: setCooking };
  send('ready');
})();
