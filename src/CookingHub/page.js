// Cooking Hub's web half. Runs in Root.html, the document every game page iframe sits in. Adds tabs, food and odds to
// the game's own cooking screen. Talks to the plugin through
// vuplex.postMessage("slmod|cookinghub|<verb>") and is driven by window.__slcookinghub.
(function () {
  if (window.__slcookinghub) return;

  var PREFIX = 'slmod|cookinghub|';
  var STATION_STYLE_ID = 'ch-station-style';
  var NUDGES_ID = 'ch-nudges';
  var BUSY_TIMEOUT_MS = 5000;
  var SVG_NS = 'http://www.w3.org/2000/svg';
  // Fail, Normal, Good, Perfect: the order the odds arrive in.
  var TIER_WORD = ['tFail', 'tNormal', 'tGood', 'tPerfect'];
  var PART_WORD = { fresh: 'partFresh', rotten: 'partRotten', exact: 'partExact', stove: 'partStove' };
  var SPOILING_HOURS = 24;

  var cat = { types: {}, items: {}, recipes: {} };
  var overlay = { show: false };
  var overlayVersion = 0;
  // Counts up with each catalog, so rows naming food redraw in the new names.
  var catVersion = 0;
  // The version each recipe book row last arrived with, from one count shared by all rows; a row the plugin sends as
  // its id alone keeps its version, so the game's recipe row drawn for it is left as it is.
  var dishVersion = {};
  var dishCount = 0;
  var byIdMemo = { version: -1, map: {} };
  var countsMemo = { key: '', counts: null };
  var isBusy = false;
  var busyTimer = 0;
  var hasReportedPageAccess = false;
  var hasReportedOddsDrawn = false;
  // Root.html's iframes as a live list, held once instead of searched for on every page check.
  var frames = document.getElementsByTagName('iframe');
  // Something may still be drawn on a cooking screen; while nothing is and there is nothing to draw, a page check
  // does nothing at all.
  var isAnyDrawn = false;
  // The words the plugin last handed over; new ones (the game's language changed) draw everything again.
  var wordsSeen = window.__slcookinghubWords;
  var wordsVersion = 0;

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

  function make(doc, tag, className, content) {
    var node = doc.createElement(tag);

    if (className) node.className = className;
    if (content != null) node.textContent = content;

    return node;
  }

  // Hands a panel to Exiled's Toolkit to be drawn at the player's UI size. Before the Toolkit's script has loaded it
  // waits in a list the Toolkit reads; without the Toolkit nothing happens.
  function uiSize(node, options) {
    options = Object.assign({ mod: 'CookingHub' }, options);
    var toolkit = window.ExiledUi;

    if (toolkit) {
      toolkit.size(node, options);
      return;
    }

    var waiting = window.__exiledUiWait = window.__exiledUiWait || [];
    waiting.push([node, options]);
    if (waiting.length > 32) waiting.shift();
  }

  // The recipe book panel, which holds the tabs and every view, is drawn at the UI size as far as the whole cooking
  // screen still fits the window at the page's own size, so the game never draws its own panels smaller for it. The
  // page fits itself to the window on resize, so it is told after each new zoom. Handed over once per panel.
  function sizeBook(doc, win, panel) {
    if (!panel || panel.__chSized) return;

    var screen = doc.getElementById('cookingSystem');

    if (!screen) return;

    panel.__chSized = true;
    uiSize(panel, {
      fit: screen,
      pageFits: true,
      onZoom: function () { win.dispatchEvent(new win.Event('resize')); }
    });
  }

  // ---------- data ----------

  function catalog(data) {
    cat = { types: {}, items: {}, recipes: {} };

    ((data && data.types) || []).forEach(function (t) { cat.types[t.id] = t; });
    ((data && data.items) || []).forEach(function (i) { cat.items[i.id] = i; });
    ((data && data.recipes) || []).forEach(function (r) { cat.recipes[r.id] = r; });
    catVersion++;
    overlayVersion++;
  }

  function typeOf(id) { return cat.types[id] || { id: id, name: '#' + id }; }

  function itemOf(id) { return cat.items[id] || { id: id, name: '#' + id }; }

  function recipeOf(id) { return cat.recipes[id] || { id: id, name: '#' + id }; }

  // Hours as the pantry shows them: one decimal under ten hours, whole hours to two days, then days.
  function timeLeft(h) {
    if (h >= 48) return say('days', Math.round(h / 24));

    return say('hours', h < 10 ? Math.round(h * 10) / 10 : Math.round(h));
  }

  // ---------- API ----------

  // A press is done: the plugin shows what it did as an Exiled's Toolkit toast, so the page only frees its buttons.
  function status() {
    clearBusy();
  }

  // The odds for the game's cooking screen; drawn at once and kept up by the page check.
  function setCooking(data) {
    var next = data || { show: false };

    if (next.book) next.book = keepRows(next.book);

    overlay = next;
    overlayVersion++;
    patchPagesSafely();
  }

  // A book row the plugin sends as its recipe id alone is the row this page already has. Should the page not have it
  // (a page loaded afresh before the plugin heard so), it asks for everything again.
  function keepRows(book) {
    var before = bookById();
    var isMissing = false;
    var rows = book.map(function (row) {
      if (typeof row !== 'number') {
        dishVersion[row.id] = ++dishCount;

        return row;
      }

      if (before[row]) return before[row];

      isMissing = true;

      return { id: row };
    });

    if (isMissing) send('ready');

    return rows;
  }

  // ---------- game pages ----------

  // The cooking screen is a same-origin iframe, so its document can be reached from here; polled because the game
  // creates and drops that iframe whenever it likes, and redraws the forecast cards on its own.
  function patchPages() {
    noticeWords();

    if (!overlay.show && !isAnyDrawn) return;

    var isAllReached = true;

    for (var i = 0; i < frames.length; i++) {
      var src = frames[i].src || '';

      if (!/Cooking\.html/.test(src)) continue;

      var doc = null;

      try {
        doc = frames[i].contentDocument;
      } catch (e) {}

      if (!doc) {
        if (!hasReportedPageAccess) send('diag|cannot reach a game page document (root ' + location.protocol + ', iframe ' + src + ')');
        hasReportedPageAccess = true;
        isAllReached = false;
        continue;
      }

      drawStation(doc, frames[i].contentWindow);
      drawOdds(doc, frames[i].contentWindow);
    }

    // With nothing to show, every screen reached was just cleared.
    isAnyDrawn = overlay.show || !isAllReached;
  }

  // The words come over again whenever the game's language changes, and everything drawn in the old ones is drawn again.
  function noticeWords() {
    var words = window.__slcookinghubWords;

    if (words === wordsSeen) return;

    wordsSeen = words;
    wordsVersion++;
    catVersion++;
    overlayVersion++;
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
    '.ch-sorted>*{flex-shrink:0}',
    '.ch-sorts{flex-wrap:wrap}.ch-sorts>.ch-sub{margin:0}',
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
    '.ch-gives{display:flex;flex-wrap:wrap;gap:4px;margin-top:5px}',
    '.ch-give{display:inline-flex;align-items:center;gap:3px;padding:0 6px 0 4px;border:1px solid;border-radius:9px;font-size:11px;font-weight:700;line-height:16px;font-variant-numeric:tabular-nums}',
    '.ch-give svg{width:11px;height:11px;flex:none}',
    '.ch-give.ch-gain{color:#7fd67a;background:rgba(102,187,106,.13);border-color:rgba(102,187,106,.35)}',
    '.ch-give.ch-loss{color:#f07a76;background:rgba(239,83,80,.13);border-color:rgba(239,83,80,.35)}',
    '.ch-fuelbtn{font-size:11px;padding:3px 8px;border-radius:4px;cursor:pointer;color:var(--warm-gold);background:rgba(251,176,52,.12);border:1px solid rgba(251,176,52,.45);margin-left:8px}'
  ].join('\n');

  // Best first, the order the odds are drawn in, with the colours the game gives each quality.
  var TIER_ORDER = [3, 2, 1, 0];
  var TIER_COLOR = ['#8a8a8a', 'rgba(255,255,255,.6)', 'var(--q-good)', 'var(--q-perfect)'];
  // Not learned shows only while the plugin sends the recipes the game's book does not list yet.
  var STATION_TABS = [['book', 'tabBook'], ['now', 'tabNow'], ['unlearned', 'tabUnlearned'], ['queue', 'tabQueue'], ['pantry', 'tabPantry']];
  var STATION_FILTERS = [['all', 'fAll'], ['load', 'fCanLoad'], ['new', 'fNew'], ['locked', 'fLocked']];
  // Sort keys: an index into a dish's gives (satiety, morale, stamina, health, vitality), or the Perfect chance.
  var STATION_SORTS = [['none', 'sNone'], [0, 'sSatiety'], [2, 'sStamina'], [1, 'sMorale'], [3, 'sHealth'], [4, 'sVitality'], ['quality', 'sQuality']];
  // Marks everything drawn on the cooking screen, so it can all be taken off again.
  var OURS = 'ch-st';
  // The stats a card shows, in the sort chips' order: an index into gives, its name and a 12px icon (a bowl, a bolt,
  // a face, a cross, a heart).
  var GIVES_SHOWN = [
    [0, 'sSatiety', [['path', { d: 'M1 6.2h10c-.2 2.6-2.3 4.6-5 4.6S1.2 8.8 1 6.2z', fill: 'currentColor' }], ['path', { d: 'M4.4 1.2c-.7.9.7 1.5 0 2.8M7.6 1.2c-.7.9.7 1.5 0 2.8', fill: 'none', stroke: 'currentColor', 'stroke-width': '1.1', 'stroke-linecap': 'round' }]]],
    [2, 'sStamina', [['path', { d: 'M7.2.6 2 7h3.3l-.6 4.4L10 4.9H6.6z', fill: 'currentColor' }]]],
    [1, 'sMorale', [['circle', { cx: '6', cy: '6', r: '4.9', fill: 'none', stroke: 'currentColor', 'stroke-width': '1.2' }], ['circle', { cx: '4.3', cy: '4.9', r: '.85', fill: 'currentColor' }], ['circle', { cx: '7.7', cy: '4.9', r: '.85', fill: 'currentColor' }], ['path', { d: 'M3.7 7.1c1.2 1.6 3.4 1.6 4.6 0', fill: 'none', stroke: 'currentColor', 'stroke-width': '1.2', 'stroke-linecap': 'round' }]]],
    [3, 'sHealth', [['path', { d: 'M4.4 1h3.2v3.4H11v3.2H7.6V11H4.4V7.6H1V4.4h3.4z', fill: 'currentColor' }]]],
    [4, 'sVitality', [['path', { d: 'M6 10.8C2.4 8.3 1 6.5 1 4.4 1 2.9 2.2 1.7 3.6 1.7c1 0 1.9.5 2.4 1.3.5-.8 1.4-1.3 2.4-1.3 1.4 0 2.6 1.2 2.6 2.7 0 2.1-1.4 3.9-5 6.4z', fill: 'currentColor' }]]]
  ];

  // What the player chose on the cooking screen; the version counts up with each choice so the views redraw.
  var station = { tab: 'book', filter: 'all', sort: 'none', search: '', version: 0, started: 0 };

  function drawStation(doc, win) {
    if (!overlay.show || !overlay.book) {
      clearStation(doc);

      return;
    }

    addPageStyle(doc, STATION_STYLE_ID, STATION_CSS);
    delegate(doc);
    doc.__chStation = true;

    var section = doc.getElementById('recipeSection');
    var list = doc.getElementById('recipeList');

    if (station.tab === 'unlearned' && !overlay.unlearned) station.tab = 'book';

    if (section && list) {
      sizeBook(doc, win, section.parentNode);
      hideBookTitle(doc);
      drawBar(doc, section, list);
      drawBookRows(doc, win, list);
      drawView(doc, section, win);
    }

    drawStoveLine(doc);
    drawRepeat(doc);
    drawTopUp(doc);
    pressStart(doc, win);
  }

  // Runs once after the station was drawn, not on every page check while there is nothing to draw.
  function clearStation(doc) {
    if (!doc.__chStation) return;

    doc.__chStation = false;
    bookDrawn.list = null;
    doc.querySelectorAll('.' + OURS).forEach(function (node) { node.remove(); });
    doc.__chHover = null;
    hideOwnTip(doc);

    var list = doc.getElementById('recipeList');

    if (!list) return;

    list.style.display = '';
    list.querySelectorAll('.book-item').forEach(function (row) {
      row.style.display = '';

      // Its decoration went with the rest, so the row is drawn again when the station comes back.
      if (row.hasAttribute('data-ch')) row.setAttribute('data-ch', '');
    });
  }

  // The tabs stand where the book's title was; hot pot mode hides the whole book, title included, on its own.
  function hideBookTitle(doc) {
    var title = doc.getElementById('recipePanelTitle');

    if (title && !(overlay.stove && overlay.stove.hotPot)) setStyle(title, 'display', 'none');
  }

  function stationKey() {
    return overlayVersion + ':' + station.version;
  }

  // A button on a game page: the page tells the game where its interactive parts are, so a click on it reaches it.
  // The press is marked on the node and heard by the one listener on its document, so no node carries its own.
  function pageButton(doc, tag, className, content, onPress) {
    var node = make(doc, tag, className + ' interactive-btn ' + OURS, content);

    node.setAttribute('data-interactive', '');
    node.__chPress = onPress;

    return node;
  }

  // One listener per kind on each game page document, set once. Clicks are heard while they travel down, so a click on
  // one of our buttons stops before the row under it, and the game's own handlers there, can take it too. Tooltips
  // follow the cursor from one marked row to the next.
  function delegate(doc) {
    if (doc.__chDelegated) return;

    doc.__chDelegated = true;
    doc.addEventListener('click', function (e) {
      var node = marked(e.target, '__chPress');

      if (!node) return;

      e.stopPropagation();

      if (!node.disabled) node.__chPress();
    }, true);
    doc.addEventListener('mouseover', function (e) { pointAt(doc, marked(e.target, '__chTip')); }, true);
    doc.addEventListener('mouseout', function (e) { pointAt(doc, marked(e.relatedTarget, '__chTip')); }, true);
  }

  // The nearest node from here up that carries the mark.
  function marked(node, mark) {
    for (; node; node = node.parentNode) {
      if (node[mark]) return node;
    }

    return null;
  }

  // The cursor is now over row, or over none of ours: the tooltip the last one opened goes, and row's own shows.
  function pointAt(doc, row) {
    if (row === doc.__chHover) return;
    if (doc.__chHover) hideOwnTip(doc);

    doc.__chHover = row;

    if (row) row.__chTip();
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
    var drawnKey = stationKey();

    // Everything the bar shows comes from the overlay, the choices and the words, which the key counts.
    if (bar && bar.__chDrawn === drawnKey) return;

    var tabs = stationTabs();
    var tabIds = tabs.map(function (t) { return t[0]; }).join(',') + '|' + wordsVersion;

    // A new set of tabs builds the bar again; that only happens when the setting changes.
    if (bar && bar.getAttribute('data-tabs') !== tabIds) {
      bar.remove();
      bar = null;
    }

    if (!bar) {
      bar = buildBar(doc, tabs);
      bar.setAttribute('data-tabs', tabIds);
      section.insertBefore(bar, list);
    }

    var counts = tabCounts();

    kids(bar.children[0]).forEach(function (tab, i) {
      var id = tabs[i][0];
      var label = say(tabs[i][1]) + '|' + counts[id];

      setClass(tab, 'ch-tab interactive-btn' + (station.tab === id ? ' ch-on' : ''));

      if (tab.getAttribute('data-label') === label) return;

      tab.setAttribute('data-label', label);
      tab.textContent = say(tabs[i][1]);

      if (counts[id]) tab.appendChild(make(doc, 'span', 'ch-n' + (id === 'pantry' ? ' ch-red' : ''), String(counts[id])));
    });

    var tools = bar.children[1];

    var toolsShown = station.tab === 'book' || station.tab === 'now' || station.tab === 'unlearned' ? '' : 'none';

    setStyle(tools, 'display', toolsShown);
    kids(tools).slice(1, 1 + STATION_FILTERS.length).forEach(function (chip, i) {
      setClass(chip, 'ch-chip interactive-btn' + (station.filter === STATION_FILTERS[i][0] ? ' ch-on' : ''));
    });

    var sorts = bar.children[2];

    setStyle(sorts, 'display', toolsShown);
    kids(sorts).slice(1).forEach(function (chip, i) {
      setClass(chip, 'ch-chip interactive-btn' + (station.sort === STATION_SORTS[i][0] ? ' ch-on' : ''));
    });

    bar.__chDrawn = drawnKey;
  }

  // Writes only what differs, so a page check that finds nothing new changes nothing on the page.
  function setClass(node, value) {
    if (node.className !== value) node.className = value;
  }

  function setStyle(node, name, value) {
    if (node.style[name] !== value) node.style[name] = value;
  }

  function stationTabs() {
    return STATION_TABS.filter(function (t) { return t[0] !== 'unlearned' || !!overlay.unlearned; });
  }

  function buildBar(doc, shownTabs) {
    var bar = make(doc, 'div', 'ch-bar ' + OURS);
    var tabs = make(doc, 'div', 'ch-tabs');
    var tools = make(doc, 'div', 'ch-tools');
    var search = make(doc, 'input', 'ch-search');

    shownTabs.forEach(function (t) {
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

    var sorts = make(doc, 'div', 'ch-tools ch-sorts');

    sorts.appendChild(make(doc, 'span', 'ch-sub', say('sortBy')));
    STATION_SORTS.forEach(function (o) {
      sorts.appendChild(pageButton(doc, 'span', 'ch-chip', say(o[1]), function () { choose(function () { station.sort = o[0]; }); }));
    });

    bar.appendChild(tabs);
    bar.appendChild(tools);
    bar.appendChild(sorts);

    return bar;
  }

  // Worked out once per overlay and choice, not on every page check.
  function tabCounts() {
    var key = stationKey();

    if (countsMemo.key === key) return countsMemo.counts;

    countsMemo = {
      key: key,
      counts: {
        book: 0,
        now: (overlay.book || []).filter(isFree).length,
        unlearned: (overlay.book || []).filter(function (dish) { return dish.unlearned && isFree(dish); }).length,
        queue: (overlay.queue || []).length,
        pantry: (overlay.pantry || []).filter(function (s) { return s.spoiled > 0 || (s.hours != null && s.hours <= SPOILING_HOURS); }).length
      }
    };

    return countsMemo.counts;
  }

  // ---------- the game's recipe book rows ----------

  // The game draws one row per entry of State.lastRecipes, in order, with an open group's variants between rows, so
  // rows are matched to recipes by counting rows, never by position among all children.
  // The book is drawn again only when the overlay, a choice, the catalog, the game's recipes or its rows changed.
  var bookDrawn = { list: null, recipes: null, key: '', children: [] };
  var NO_RECIPES = [];

  function drawBookRows(doc, win, list) {
    var recipes = (win && win.State && win.State.lastRecipes) || NO_RECIPES;
    var key = stationKey() + ':' + catVersion;

    if (bookDrawn.list === list && bookDrawn.recipes === recipes && bookDrawn.key === key && sameChildren(list, bookDrawn.children)) return;

    bookDrawn = { list: list, recipes: recipes, key: key, children: kids(list) };

    var rows = bookDrawn.children.filter(function (child) { return child.classList.contains('book-item'); });
    var byId = bookById();

    var isSorted = station.sort !== 'none';

    setStyle(list, 'display', station.tab === 'book' ? (isSorted ? 'flex' : '') : 'none');
    setStyle(list, 'flexDirection', isSorted ? 'column' : '');
    list.classList.toggle('ch-sorted', isSorted);

    if (rows.length !== recipes.length) return;

    // Sorting moves rows by flex order, never in the page, so the game's own row counting still holds.
    var order = rows.map(function (row, i) { return i; });

    if (isSorted) {
      order.sort(function (a, b) {
        return sortValue(byId[recipes[b].recipeId]) - sortValue(byId[recipes[a].recipeId]) || a - b;
      });
    }

    var place = {};

    order.forEach(function (rowIndex, n) { place[rowIndex] = n; });

    rows.forEach(function (row, i) {
      var recipe = recipes[i];
      var dish = byId[recipe.recipeId];
      var isShown = matchesFilter(recipe, dish) && matchesSearch(recipe.name + ' ' + (recipe.costText || ''), dish);
      var variants = row.nextSibling;
      var hasVariants = variants && variants.classList.contains('recipe-variants');

      decorateRow(doc, row, recipe, dish);
      setStyle(row, 'display', isShown ? '' : 'none');
      setStyle(row, 'order', isSorted ? String(2 * place[i]) : '');

      if (hasVariants) {
        setStyle(variants, 'display', isShown ? '' : 'none');
        setStyle(variants, 'order', isSorted ? String(2 * place[i] + 1) : '');
      }
    });
  }

  function sameChildren(node, before) {
    var children = node.children;

    if (children.length !== before.length) return false;

    for (var i = 0; i < children.length; i++) {
      if (children[i] !== before[i]) return false;
    }

    return true;
  }

  // Higher is better; a dish with nothing known sorts last.
  function sortValue(dish) {
    if (!dish) return -Infinity;
    if (station.sort === 'quality') return dish.chances ? dish.chances[3] : -1;

    return dish.gives ? dish.gives[station.sort] : -1;
  }

  function bookById() {
    if (byIdMemo.version === overlayVersion) return byIdMemo.map;

    var byId = {};

    (overlay.book || []).forEach(function (dish) { byId[dish.id] = dish; });
    byIdMemo = { version: overlayVersion, map: byId };

    return byId;
  }

  // The player's food covers the dish once the food set aside for queued dishes is left alone.
  function isFree(dish) {
    return !!dish && dish.state === 'ready' && !dish.setAside;
  }

  function matchesFilter(recipe, dish) {
    if (station.filter === 'load') return isFree(dish);
    if (station.filter === 'new') return !!dish && dish.isNew;
    if (station.filter === 'locked') return !!recipe.craftLocked || (!!dish && dish.state === 'locked') || hasLockedVariant(recipe);

    return true;
  }

  // A row the game folds from several dishes that make the same food: a variant the cook cannot make yet has a LockText.
  function hasLockedVariant(recipe) {
    return /"LockText":"[^"]/.test(recipe.variantsJson || '');
  }

  function matchesSearch(words, dish) {
    var needle = station.search.trim().toLowerCase();

    if (!needle) return true;

    var food = dish ? (dish.places || []).concat((dish.missing || []).map(needName)) : [];

    return (words + ' ' + food.join(' ')).toLowerCase().indexOf(needle) >= 0;
  }

  // Where the food is or what is missing, a bar of the odds, the Perfect chance, Load when the game's own fill would
  // refuse (its rows only count the bag) but the player's storages cover the dish, or the pot can take the dish once
  // more, and Queue beside it. Food set aside
  // for queued dishes does not count, and a dish only it covers says so instead.
  // Keyed by what the row is drawn from, so a row whose dish did not change keeps its drawing through a new overlay.
  function decorateRow(doc, row, recipe, dish) {
    var key = [recipe.recipeId, dish ? dishVersion[dish.id] : '-', recipe.status, recipe.craftLocked ? 1 : 0, recipe.variantCount, overlay.queueFull ? 1 : 0, overlay.addsDish ? 1 : 0, catVersion].join(':');

    if (row.getAttribute('data-ch') === key) return;

    if (!row.hasAttribute('data-ch-class')) row.setAttribute('data-ch-class', row.className);

    var isGroup = !recipe.craftLocked && recipe.variantCount > 1;
    var isOpen = !!dish && !recipe.craftLocked && !isGroup && dish.state !== 'locked' && !dish.setAside;
    var gameClass = row.getAttribute('data-ch-class');

    row.setAttribute('data-ch', key);
    // The row's class is written once, and only when it differs, so the page restyles it only on a real change.
    setClass(row, isOpen && recipe.status === 0 && dish.state === 'ready'
      ? gameClass.split(' ').filter(function (name) { return name !== 'insuff'; }).join(' ') + ' fillable ch-storage'
      : gameClass);
    removeOurs(row);

    if (!dish) return;

    var body = firstChildWithClass(row, 'book-bd') || row;
    var gameButton = firstChildWithClass(row, 'st-fill') || firstChildWithClass(row, 'st-ready');

    removeOurs(body);
    body.appendChild(whereLine(doc, dish));
    addGives(doc, body, dish.gives);

    if (dish.chances) {
      body.appendChild(miniBar(doc, dish.chances));
      row.insertBefore(make(doc, 'span', 'ch-pp ' + OURS, dish.chances[3] + '%'), gameButton);
    }

    if (recipe.craftLocked || isGroup || dish.state === 'locked') return;

    if (dish.setAside) {
      row.appendChild(make(doc, 'span', 'ch-sp ch-dim ' + OURS, say('setAside')));

      return;
    }

    // The game offers no fill once the pot already makes the dish, but a cook who can make several dishes in one pot
    // can load it again: three Watermelons in a juicer make three Watermelon Juices.
    if (dish.state === 'ready' && (recipe.status === 0 || (recipe.status === 2 && overlay.addsDish))) {
      row.appendChild(pageButton(doc, 'span', 'st-fill', say('load'), function () { sendAction('fill|' + dish.id); }));
    }

    if (isFree(dish)) addQueueButton(doc, row, dish.id);
  }

  // Queue, beside Load: the dish is cooked on this stove once its finished dishes are taken out.
  function addQueueButton(doc, row, id) {
    if (overlay.queueFull) return;

    row.appendChild(pageButton(doc, 'span', 'ch-x', say('queue'), function () { sendAction('queue|' + id); }));
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

  // ---------- Cook now and Pantry ----------

  function drawView(doc, section, win) {
    var view = firstChildWithClass(section, 'ch-view');
    var key = station.tab + ':' + stationKey();

    if (station.tab === 'book') {
      if (view) {
        view.remove();
        doc.__chHover = null;
        hideOwnTip(doc);
      }

      return;
    }

    if (view && view.getAttribute('data-key') === key) return;

    // The view stays and keeps its scroll; only the rows whose drawing changed are made again.
    if (!view) view = section.appendChild(make(doc, 'div', 'ch-view ' + OURS));

    view.setAttribute('data-key', key);

    var rows = [];
    var add = function (rowKey, build) { rows.push([rowKey, build]); };

    if (station.tab === 'now') fillNow(doc, add, win);
    else if (station.tab === 'unlearned') fillUnlearned(doc, add, win);
    else if (station.tab === 'queue') fillQueue(doc, add, win);
    else fillPantry(doc, add, win);

    placeRows(doc, view, rows);
  }

  // Puts the rows in the view in order, keeping each node whose key it had last time and making only the new ones.
  // A key is everything the row is drawn from, so a kept row is already right.
  function placeRows(doc, view, rows) {
    var before = view.__chRows || {};
    var now = {};
    var prefix = station.tab + ':' + catVersion + ':' + (overlay.queueFull ? 1 : 0) + ':' + (overlay.cooking ? 1 : 0) + ':';

    rows.forEach(function (row, at) {
      var key = prefix + row[0];

      while (now[key]) key += '+';

      var node = before[key];

      if (node) delete before[key];
      else node = row[1]();

      now[key] = node;

      if (view.children[at] !== node) view.insertBefore(node, view.children[at] || null);
    });

    Object.keys(before).forEach(function (key) {
      var node = before[key];

      // A removed row never hears the cursor leave.
      if (node === doc.__chHover) pointAt(doc, null);

      node.remove();
    });

    view.__chRows = now;
  }

  // What this stove can make from the bag and every storage, the dish whose food spoils soonest first.
  // With a sort chosen, the dish that gives the most of it first.
  function cookNowDishes() {
    return (overlay.book || []).filter(isFree).sort(function (a, b) {
      if (station.sort !== 'none') {
        var by = sortValue(b) - sortValue(a);

        if (by) return by;
      }

      return (a.hours == null ? Infinity : a.hours) - (b.hours == null ? Infinity : b.hours);
    });
  }

  // Each fill hands add a key and a way to build every row it shows, in order; drawView keeps the rows it already has.
  function fillNow(doc, add, win) {
    var shown = cookNowDishes().filter(function (dish) {
      return matchesFilter({}, dish) && matchesSearch(recipeOf(dish.id).name, dish);
    });

    add('note', function () { return make(doc, 'div', 'ch-sub', say('nowNote')); });

    if (!shown.length) add('none', function () { return make(doc, 'div', 'ch-sub', say('nowNone')); });

    shown.forEach(function (dish) {
      add('d' + dish.id + ':' + dishVersion[dish.id], function () {
        var row = viewRow(doc, recipeOf(dish.id), recipeOf(dish.id).name, whereLine(doc, dish), 'fillable');

        if (dish.chances) row.children[1].appendChild(miniBar(doc, dish.chances));

        addGives(doc, row.children[1], dish.gives);
        tagForInfoPack(row, recipeOf(dish.id).dish);
        recipeTip(doc, win, row, dish.id);

        if (dish.unlearned) row.appendChild(make(doc, 'span', 'ch-sp ch-dim', say('unlearnedChip')));
        if (dish.hours != null) row.appendChild(spoilChip(doc, dish.hours));
        if (dish.chances) row.appendChild(make(doc, 'span', 'ch-pp', dish.chances[3] + '%'));

        row.appendChild(pageButton(doc, 'span', 'st-fill', say('load'), function () { sendAction('fill|' + dish.id); }));
        addQueueButton(doc, row, dish.id);

        return row;
      });
    });
  }

  // The recipes this stove cooks that the game's book does not list yet, the ones the player's food covers first.
  function fillUnlearned(doc, add, win) {
    var shown = (overlay.book || []).filter(function (dish) {
      return dish.unlearned && matchesFilter({}, dish) && matchesSearch(recipeOf(dish.id).name, dish);
    }).sort(function (a, b) {
      return (isFree(b) ? 1 : 0) - (isFree(a) ? 1 : 0) || (station.sort !== 'none' ? sortValue(b) - sortValue(a) : 0) || a.id - b.id;
    });

    add('note', function () { return make(doc, 'div', 'ch-sub', say('unlearnedNote')); });

    if (!shown.length) add('none', function () { return make(doc, 'div', 'ch-sub', say('unlearnedNone')); });

    shown.forEach(function (dish) {
      add('d' + dish.id + ':' + dishVersion[dish.id], function () {
        var recipe = recipeOf(dish.id);
        var row = viewRow(doc, recipe, recipe.name, whereLine(doc, dish), isFree(dish) ? 'fillable' : 'insuff');

        if (dish.chances) row.children[1].appendChild(miniBar(doc, dish.chances));

        addGives(doc, row.children[1], dish.gives);
        tagForInfoPack(row, recipe.dish);
        recipeTip(doc, win, row, dish.id);

        if (dish.showLevel > 0) row.appendChild(make(doc, 'span', 'ch-sp ch-dim', say('showsAt', dish.showLevel)));
        if (dish.chances) row.appendChild(make(doc, 'span', 'ch-pp', dish.chances[3] + '%'));

        if (isFree(dish)) {
          row.appendChild(pageButton(doc, 'span', 'st-fill', say('load'), function () { sendAction('fill|' + dish.id); }));
          addQueueButton(doc, row, dish.id);
        }

        return row;
      });
    });
  }

  // This stove's queue in the order it cooks, each with what its food needs and a button to take it off. A dish with
  // no food set aside is short and waits.
  function fillQueue(doc, add, win) {
    var queue = overlay.queue || [];
    var covered = overlay.queueCovered || [];
    var byId = bookById();

    add(queue.length ? 'note' : 'none', function () { return make(doc, 'div', 'ch-sub', queue.length ? say('queueNote') : say('queueNone')); });

    queue.forEach(function (id, i) {
      var dish = byId[id];
      var isCovered = covered[i] !== false;

      add('q' + i + ':' + id + ':' + (dish ? dishVersion[id] : '-') + ':' + (isCovered ? 1 : 0), function () {
        var recipe = recipeOf(id);
        var row = viewRow(doc, recipe, (i + 1) + '. ' + recipe.name, dish ? whereLine(doc, dish) : null, isCovered ? 'fillable' : 'insuff');

        if (dish) addGives(doc, row.children[1], dish.gives);

        recipeTip(doc, win, row, id);
        row.appendChild(make(doc, 'span', 'ch-sp ch-dim', say(isCovered ? 'waiting' : 'queueShortChip')));
        row.appendChild(pageButton(doc, 'span', 'ch-x', say('unqueue'), function () { sendAction('unqueue|' + id); }));

        return row;
      });
    });
  }

  // Every food the player has, soonest to spoil first; a click puts one in the pot from wherever it is kept.
  function fillPantry(doc, add, win) {
    var shelves = overlay.pantry || [];

    add(shelves.length ? 'note' : 'none', function () { return make(doc, 'div', 'ch-sub', shelves.length ? say('pantryNote') : say('pEmpty')); });

    shelves.forEach(function (s) {
      add('s' + JSON.stringify(s), function () {
        var item = itemOf(s.item);
        var name = item.name + ' x' + s.count + (s.spoiled > 0 ? ', ' + say('pSpoiled', s.spoiled) : '');
        var row = viewRow(doc, item, name, make(doc, 'div', 'book-cost', (s.places || []).join(', ')), s.count > 0 ? 'fillable' : 'insuff');

        addGives(doc, row.children[1], item.gives);
        tagForInfoPack(row, item.id);
        itemTip(doc, win, row, item);
        row.appendChild(s.spoiled > 0 && s.count === 0 ? make(doc, 'span', 'ch-sp ch-bad', say('spoiledWord')) : spoilChip(doc, s.hours));

        if (s.count > 0 && !overlay.cooking) {
          row.className += ' interactive-btn';
          row.setAttribute('data-interactive', '');
          row.__chPress = function () { sendAction('add|' + s.item); };
        }

        return row;
      });
    });
  }

  // A row in the game's own recipe row style: icon, then name over a second line.
  function viewRow(doc, thing, name, second, stateClass) {
    var row = make(doc, 'div', 'book-item' + (stateClass ? ' ' + stateClass : ''));
    var iconBox = make(doc, 'div', 'book-ico');
    var body = make(doc, 'div', 'book-bd');

    if (thing.icon) {
      var img = make(doc, 'img');
      img.src = fromRoot(thing.icon);
      iconBox.appendChild(img);
    }

    body.appendChild(make(doc, 'div', 'book-nm', name));

    if (second) body.appendChild(second);

    row.appendChild(iconBox);
    row.appendChild(body);

    return row;
  }

  // Icon paths are relative to Root.html's folder (WebUI), and an image made by a game page would look for them below
  // that page's own folder instead.
  function fromRoot(path) {
    var href = String(location.href || '');

    if (/^[a-z]+:|^\//i.test(path) || href.indexOf('/') < 0) return path;

    return href.slice(0, href.lastIndexOf('/') + 1) + path;
  }

  // What a dish or food restores, at the foot of its card: one chip per stat it changes, with that stat's icon.
  function addGives(doc, parent, gives) {
    var row = givesRow(doc, gives);

    if (row) parent.appendChild(row);
  }

  function givesRow(doc, gives) {
    var row = make(doc, 'div', 'ch-gives ' + OURS);

    GIVES_SHOWN.forEach(function (stat) {
      var value = gives && gives[stat[0]];

      if (!value) return;

      var chip = make(doc, 'span', 'ch-give ' + (value > 0 ? 'ch-gain' : 'ch-loss'));

      chip.setAttribute('title', say(stat[1]));
      chip.appendChild(statIcon(doc, stat[2]));
      chip.appendChild(make(doc, 'span', '', (value > 0 ? '+' : '') + value));
      row.appendChild(chip);
    });

    return row.children.length ? row : null;
  }

  function statIcon(doc, shapes) {
    var svg = doc.createElementNS(SVG_NS, 'svg');

    svg.setAttribute('viewBox', '0 0 12 12');
    svg.setAttribute('aria-hidden', 'true');
    shapes.forEach(function (shape) {
      var part = doc.createElementNS(SVG_NS, shape[0]);

      Object.keys(shape[1]).forEach(function (name) { part.setAttribute(name, shape[1][name]); });
      svg.appendChild(part);
    });

    return svg;
  }

  // Info Pack draws its own tooltip over any element tagged with an item id; the tag does nothing without it.
  function tagForInfoPack(row, itemId) {
    if (itemId > 0) row.setAttribute('data-slip-item', String(itemId));
  }

  // With Info Pack installed it marks the page (__slipTagged) and draws a tagged row's tooltip itself; the page's
  // tooltip as well would say the same things twice.
  function hasInfoPack(win) {
    return !!(win && win.__slipTagged);
  }

  // Without Info Pack: the cooking page's own tooltip, written the way its recipe rows write it (name first).
  // The tooltip is marked on the row and shown by the page document's one cursor listener (delegate).
  function recipeTip(doc, win, row, id) {
    row.__chTip = function () {
      if (!hasInfoPack(win)) showOwnTip(doc, recipeTipText(win, id));
    };
  }

  function recipeTipText(win, id) {
    var words = (win && win.i18nText) || {};
    var recipes = (win && win.State && win.State.lastRecipes) || [];

    for (var i = 0; i < recipes.length; i++) {
      var r = recipes[i];

      if (!r || r.recipeId !== id) continue;

      var status = r.status === 2 ? words.recipeReady : r.status === 1 ? words.recipeCraftable : words.recipeInsufficient;

      return [r.name, r.effectText, String(r.costText || '').replace(/\|/g, '\n'), status].filter(function (line) { return !!line; }).join('\n');
    }

    return recipeOf(id).name;
  }

  // Without Info Pack: a food row shows the tooltip through the page's own showItemTip, as its bag slots do.
  function itemTip(doc, win, row, item) {
    row.__chTip = function () {
      var tip = doc.getElementById('recipeTooltip');

      if (hasInfoPack(win)) return;

      if (win && typeof win.showItemTip === 'function') win.showItemTip({ name: item.name, configId: item.id, itemId: 0 });
      else showOwnTip(doc, item.name);

      if (tip) tip.__chOwned = true;
    };
  }

  function showOwnTip(doc, content) {
    var tip = doc.getElementById('recipeTooltip');

    if (!tip) return;

    tip.textContent = content;
    tip.style.whiteSpace = 'pre-line';
    tip.style.display = 'block';
    tip.__chOwned = true;
  }

  // Only a tooltip one of our rows opened; the page's own stay as the page left them.
  function hideOwnTip(doc) {
    var tip = doc.getElementById('recipeTooltip');

    if (!tip || !tip.__chOwned) return;

    tip.__chOwned = false;
    tip.style.display = 'none';
  }

  function spoilChip(doc, hours) {
    if (hours == null) return make(doc, 'span', 'ch-sp ch-dim', say('pKeeps'));

    var tone = hours <= SPOILING_HOURS ? 'ch-red' : hours <= 3 * SPOILING_HOURS ? 'ch-amber' : 'ch-dim';

    return make(doc, 'span', 'ch-sp ' + tone, timeLeft(hours));
  }

  // ---------- the stove line, Repeat last, fuel ----------

  // A part of the game's page found once and held on its document while it is still on the page.
  function heldPart(doc, slot, selector) {
    var node = doc[slot];

    if (node && node.isConnected) return node;

    return (doc[slot] = doc.querySelector(selector));
  }

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
    var actions = heldPart(doc, '__chActions', '.action-buttons');
    var stove = overlay.stove;

    var key = stove ? [stove.name, stove.speed, stove.bonus, stove.cookSecs, catVersion].join('|') : '';

    keepDrawn(doc, 'ch-stoveline', key, actions && actions.parentNode, actions, stove && function () {
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
    var actions = heldPart(doc, '__chActions', '.action-buttons');
    var isShown = overlay.last != null && !overlay.cooking && overlay.potEmpty;

    keepDrawn(doc, 'ch-repeat', isShown + ':' + catVersion + ':' + overlay.last, actions, null, isShown && function () {
      return pageButton(doc, 'button', 'ch-btn2', say('repeat', recipeOf(overlay.last).name), function () { sendAction('repeat'); });
    });
  }

  // Fuel of the kinds this stove already burns, from the bag and every storage.
  function drawTopUp(doc) {
    var header = heldPart(doc, '__chFuelHeader', '.fuel-section-header');
    var fuel = overlay.stove && overlay.stove.fuel;
    var isShown = !!fuel && fuel.known && fuel.have > 0;

    keepDrawn(doc, 'ch-topup', isShown + ':' + catVersion + ':' + (fuel ? fuel.have : 0), header, null, isShown && function () {
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
    var cards = list ? potCards(list) : NO_RECIPES;
    var isMatch = overlay.show && !!list && sameDishes(win, cards.length);

    if (!isMatch) {
      clearOdds(doc);

      return;
    }

    addPageStyle(doc, STATION_STYLE_ID, STATION_CSS);
    delegate(doc);
    doc.__chOdds = true;

    var drawn = oddsDrawnFrom();

    for (var i = 0; i < cards.length; i++) {
      var odds = overlay.odds ? (overlay.lines || [])[i] : null;
      // Under the dish's name and hint, in the card's text column.
      var holder = firstChildWithClass(cards[i], 'pot-bd') || cards[i];

      if (holder.getAttribute('data-ch-version') === drawn.odds) continue;

      holder.setAttribute('data-ch-version', drawn.odds);
      ['ch-odds', 'ch-why', 'ch-potgives'].forEach(function (name) {
        var drawn = firstChildWithClass(holder, name);

        if (drawn) drawn.remove();
      });

      if (odds) {
        holder.appendChild(oddsBlock(doc, odds));
        holder.appendChild(whyBlock(doc, odds));
      }

      // A dish the player has not learned keeps its secret, as the card's own name and picture do.
      var gives = cards[i].classList.contains('known') ? givesRow(doc, (overlay.lineGives || [])[i]) : null;

      if (gives) {
        gives.className += ' ch-potgives';
        holder.appendChild(gives);
      }
    }

    if (overlay.odds) drawNudges(doc, list, drawn.nudges);
    else clearNudges(doc);

    if (!hasReportedOddsDrawn) send('diag|odds drawn on the cooking screen');
    hasReportedOddsDrawn = true;
  }

  // Keys for what the odds and the nudges are drawn from, counting up only when that changed, so an overlay that
  // changed elsewhere (the pantry, the book) leaves the forecast cards as they are. Worked out once per overlay.
  var oddsMemo = { version: -1, odds: { text: '', count: 0 }, nudges: { text: '', count: 0 }, keys: null };

  function oddsDrawnFrom() {
    if (oddsMemo.version === overlayVersion) return oddsMemo.keys;

    oddsMemo.version = overlayVersion;
    oddsMemo.keys = {
      odds: countUp(oddsMemo.odds, JSON.stringify([overlay.odds, overlay.level, overlay.ids, overlay.lines, overlay.lineGives, catVersion])),
      nudges: countUp(oddsMemo.nudges, JSON.stringify([overlay.cooking, overlay.nudges, catVersion]))
    };

    return oddsMemo.keys;
  }

  function countUp(seen, text) {
    if (seen.text !== text) {
      seen.text = text;
      seen.count++;
    }

    return String(seen.count);
  }

  // The forecast list's cards as a live list, held on the list, so a page check does not search for them again.
  function potCards(list) {
    return list.__chCards || (list.__chCards = list.getElementsByClassName('pot-card'));
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

  // Runs once after odds were drawn, not on every page check while the forecast does not match.
  function clearOdds(doc) {
    if (!doc.__chOdds) return;

    doc.__chOdds = false;
    doc.querySelectorAll('.ch-odds').forEach(function (node) { node.remove(); });
    doc.querySelectorAll('.ch-why').forEach(function (node) { node.remove(); });
    doc.querySelectorAll('.ch-potgives').forEach(function (node) { node.remove(); });
    doc.querySelectorAll('[data-ch-version]').forEach(function (node) { node.setAttribute('data-ch-version', ''); });
    clearNudges(doc);
  }

  function clearNudges(doc) {
    var nudges = doc.getElementById(NUDGES_ID);

    if (nudges) nudges.remove();
  }

  // Runs on every page check, so it walks the live children instead of copying them.
  function firstChildWithClass(node, name) {
    var children = node.children;

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
  function drawNudges(doc, list, key) {
    var existing = doc.getElementById(NUDGES_ID);
    var nudges = overlay.cooking ? [] : overlay.nudges || [];

    if (!nudges.length) {
      if (existing) existing.remove();

      return;
    }

    if (existing && existing.getAttribute('data-version') === key && existing.previousSibling === list) return;
    if (existing) existing.remove();

    var box = make(doc, 'div');

    box.id = NUDGES_ID;
    box.setAttribute('data-version', key);
    nudges.forEach(function (n) { box.appendChild(nudgeButton(doc, n)); });
    list.parentNode.insertBefore(box, list.nextSibling);
  }

  // A seasoning goes in as one more of it; a swap trades a spoiled ingredient for a fresh one of the same.
  function nudgeButton(doc, n) {
    var name = itemOf(n.item).name;
    var node = make(doc, 'button', 'ch-nudge interactive-btn', n.kind === 'season' ? say('nSeason', name) : say('nSwap', name));

    node.setAttribute('data-interactive', '');

    if (n.gain != null) node.appendChild(make(doc, 'span', 'ch-gain', say('nGain', n.gain + '%')));

    node.__chPress = function () { sendAction((n.kind === 'season' ? 'add' : 'swap') + '|' + n.item); };

    return node;
  }

  setInterval(patchPagesSafely, 250);

  window.__slcookinghub = { status: status, catalog: catalog, cooking: setCooking };
  send('ready');
})();
