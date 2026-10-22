// Just enough of Root.html and the game's HUD page (UI/CoreUI/CoreUI1.html) for page.js to run in Jint: the root
// document with its page iframes, the vuplex bridge, and clicks and other events that bubble. Test code only; nothing
// here ships.
var window = this;

var location = { protocol: 'https:' };

var intervals = [];

function setInterval(callback) {
  intervals.push(callback);

  return intervals.length;
}

// One run of every interval the script set, which is how its 400 ms page check runs.
function poll() {
  intervals.slice().forEach(function (callback) { callback(); });
}

// Held and never fired: the tests answer every press themselves, as the plugin does.
var timers = {};
var nextTimer = 1;

function setTimeout(callback) {
  timers[nextTimer] = callback;

  return nextTimer++;
}

function clearTimeout(id) {
  delete timers[id];
}

var FakeElement = class {
  constructor(tagName) {
    var element = this;

    // Browsers report HTML tag names in capitals, and page.js checks them that way.
    this.tagName = tagName.charAt(0) === '#' ? tagName : tagName.toUpperCase();
    this.id = '';
    this.parentElement = null;
    this.children = [];
    this.text = '';
    this.attrs = {};
    this.classes = [];
    this.listeners = {};
    this.style = {};
    this.value = '';
    this.disabled = false;
    this.scrollTop = 0;
    this.classList = {
      contains: function (name) { return element.classes.indexOf(name) >= 0; }
    };
  }

  get parentNode() { return this.parentElement; }

  get firstChild() { return this.children.length ? this.children[0] : null; }

  get nextSibling() { return this.sibling(1); }

  get previousSibling() { return this.sibling(-1); }

  sibling(step) {
    if (!this.parentElement) return null;

    return this.parentElement.children[this.parentElement.children.indexOf(this) + step] || null;
  }

  get className() { return this.classes.join(' '); }

  set className(value) { this.classes = String(value).split(' ').filter(function (name) { return name !== ''; }); }

  // As in a browser: an element's text is all of its descendants' text, and setting it replaces the children.
  get textContent() {
    return this.text + this.children.map(function (child) { return child.textContent; }).join('');
  }

  set textContent(value) {
    this.children.slice().forEach(function (child) { this.removeChild(child); }, this);
    this.text = value == null ? '' : String(value);
  }

  hasAttribute(name) { return Object.prototype.hasOwnProperty.call(this.attrs, name); }

  getAttribute(name) { return this.hasAttribute(name) ? this.attrs[name] : null; }

  setAttribute(name, value) { this.attrs[name] = String(value); }

  addEventListener(type, listener) {
    (this.listeners[type] = this.listeners[type] || []).push(listener);
  }

  // A fragment's children move into the parent, as in a browser.
  appendChild(child) {
    if (child.tagName === '#fragment') {
      child.children.slice().forEach(function (moved) { this.appendChild(moved); }, this);

      return child;
    }

    if (child.parentElement) child.parentElement.removeChild(child);

    child.parentElement = this;
    this.children.push(child);

    return child;
  }

  insertBefore(child, before) {
    if (!before) return this.appendChild(child);

    if (child.parentElement) child.parentElement.removeChild(child);

    child.parentElement = this;
    this.children.splice(this.children.indexOf(before), 0, child);

    return child;
  }

  replaceChild(child, old) {
    if (child.parentElement) child.parentElement.removeChild(child);

    this.children[this.children.indexOf(old)] = child;
    child.parentElement = this;
    old.parentElement = null;

    return old;
  }

  removeChild(child) {
    this.children = this.children.filter(function (c) { return c !== child; });
    child.parentElement = null;

    return child;
  }

  remove() {
    if (this.parentElement) this.parentElement.removeChild(this);
  }

  contains(other) {
    for (var element = other; element; element = element.parentElement) {
      if (element === this) return true;
    }

    return false;
  }

  matches(selector) {
    var element = this;

    return selectorSteps(selector).every(function (step) { return matchesStep(element, step); });
  }

  closest(selector) {
    for (var element = this; element; element = element.parentElement) {
      if (element.matches(selector)) return element;
    }

    return null;
  }

  querySelectorAll(selector) {
    return descendantsOf([this]).filter(function (element) { return element.matches(selector); });
  }

  querySelector(selector) {
    return this.querySelectorAll(selector)[0] || null;
  }
};

// Compound selectors of a tag, classes, ids and attributes ("iframe", ".ch-hud-btn", "[data-act]"); the script uses
// nothing else.
function selectorSteps(selector) {
  var steps = selector.match(/^[a-z]+|\.[\w-]+|#[\w-]+|\[[\w-]+\]/g) || [];

  if (steps.join('') !== selector) throw new Error('the fake page does not know the selector "' + selector + '"');

  return steps;
}

function matchesStep(element, step) {
  switch (step.charAt(0)) {
    case '.':
      return element.classList.contains(step.slice(1));
    case '#':
      return element.id === step.slice(1);
    case '[':
      return element.hasAttribute(step.slice(1, -1));
    default:
      return element.tagName === step.toUpperCase();
  }
}

function descendantsOf(roots) {
  var found = [];

  roots.forEach(function (root) {
    root.children.forEach(function walk(child) {
      found.push(child);
      child.children.forEach(walk);
    });
  });

  return found;
}

var FakeDocument = class {
  constructor() {
    this.head = new FakeElement('head');
    this.body = new FakeElement('body');
  }

  createElement(tagName) { return new FakeElement(tagName); }

  createElementNS(namespace, tagName) { return new FakeElement(tagName); }

  createTextNode(text) {
    var node = new FakeElement('#text');

    node.textContent = text;

    return node;
  }

  createDocumentFragment() { return new FakeElement('#fragment'); }

  getElementById(id) {
    return descendantsOf([this.head, this.body]).filter(function (element) { return element.id === id; })[0] || null;
  }

  querySelectorAll(selector) {
    return descendantsOf([this.head, this.body]).filter(function (element) { return element.matches(selector); });
  }

  querySelector(selector) {
    return this.querySelectorAll(selector)[0] || null;
  }
};

// Root.html's own document, which holds the page iframes.
var document = new FakeDocument();

// What the script posts to the plugin.
var posted = [];

window.vuplex = {
  postMessage: function (message) { posted.push(message); }
};

// The game showing its HUD: a fresh CoreUI1 iframe in Root.html with the tool column the bag and phone buttons sit in.
function openHud() {
  var frame = document.body.appendChild(new FakeElement('iframe'));
  var column = new FakeElement('div');

  frame.src = 'https://game.local/UI/CoreUI/CoreUI1.html';
  frame.contentDocument = new FakeDocument();
  column.className = 'vertical-tools';
  column.appendChild(new FakeElement('button'));
  frame.contentDocument.body.appendChild(column);

  return frame;
}

function hudPage() {
  return document.querySelectorAll('iframe').filter(function (frame) { return /CoreUI1/.test(frame.src); })[0].contentDocument;
}

// The game's cooking screen (UI/Cooking/Cooking.html) with a forecast: one pot card per recipe id, and the page's
// global State naming the same ids, as renderPredictionList leaves them.
function openCooking(recipeIds) {
  var frame = document.body.appendChild(new FakeElement('iframe'));
  var section = new FakeElement('div');
  var list = new FakeElement('div');

  frame.src = 'https://game.local/UI/Cooking/Cooking.html';
  frame.contentDocument = new FakeDocument();
  frame.contentWindow = { State: {} };
  section.id = 'potSection';
  list.id = 'predictionList';
  section.appendChild(list);
  frame.contentDocument.body.appendChild(section);
  forecast(frame, recipeIds);

  return frame;
}

// The cooking screen forecasting again, as it does whenever the pot changes: every card is rebuilt.
function forecast(frame, recipeIds) {
  var list = frame.contentDocument.getElementById('predictionList');

  list.textContent = '';
  frame.contentWindow.State.lastPredictionEntries = recipeIds.map(function (id) { return { RecipeId: id, Level: 2 }; });
  recipeIds.forEach(function (id) {
    var card = new FakeElement('div');
    var body = new FakeElement('div');

    card.className = 'pot-card known';
    body.className = 'pot-bd';
    card.appendChild(body);
    list.appendChild(card);
  });
}

// The rest of the station screen: the recipe book (#recipeSection with its title and one .book-item row per entry of
// State.lastRecipes, as renderRecipeListFromData draws them), the action buttons with the game's Cook button, and the
// fuel header. Presses of the Cook button are counted in cookPresses.
var cookPresses = 0;

function stationParts(frame, recipes, isCooking) {
  var doc = frame.contentDocument;
  var section = new FakeElement('div');
  var title = new FakeElement('div');
  var list = new FakeElement('div');
  var actions = new FakeElement('div');
  var cook = new FakeElement('button');
  var fuel = new FakeElement('div');

  section.id = 'recipeSection';
  title.id = 'recipePanelTitle';
  list.id = 'recipeList';
  recipes.forEach(function (recipe) {
    var row = new FakeElement('div');
    var body = new FakeElement('div');
    var name = new FakeElement('div');
    var state = new FakeElement('span');

    row.className = 'book-item' + (recipe.status === 0 ? ' insuff' : '');
    body.className = 'book-bd';
    name.className = 'book-nm';
    name.textContent = recipe.name;
    state.className = recipe.status === 0 ? 'st-fill' : 'st-ready';
    body.appendChild(name);
    row.appendChild(body);
    row.appendChild(state);
    list.appendChild(row);
  });
  section.appendChild(title);
  section.appendChild(list);
  actions.className = 'action-buttons';
  cook.id = 'cookBtn';
  cook.click = function () { cookPresses++; };
  actions.appendChild(cook);
  fuel.className = 'fuel-section-header';
  doc.body.appendChild(section);
  doc.body.appendChild(actions);
  doc.body.appendChild(fuel);
  frame.contentWindow.State.lastRecipes = recipes;
  frame.contentWindow.State.isCooking = !!isCooking;
}

function cookingPage() {
  return document.querySelectorAll('iframe').filter(function (frame) { return /Cooking\.html/.test(frame.src); })[0].contentDocument;
}

function hudButton() {
  return hudPage().querySelector('.ch-hud-btn');
}

function panel() {
  return document.getElementById('ch-root');
}

// The first button in the window for an action, or for an action and its arguments.
function panelButton(action, args) {
  return document.querySelectorAll('[data-act]').filter(function (node) {
    return node.getAttribute('data-act') === action && (args === undefined || node.getAttribute('data-args') === args);
  })[0];
}

function panelButtons(action) {
  return document.querySelectorAll('[data-act]').filter(function (node) { return node.getAttribute('data-act') === action; });
}

function field(name) {
  return document.querySelectorAll('input').filter(function (node) { return node.getAttribute('data-field') === name; })[0];
}

// An event of any type, bubbling from the target as focusin, focusout and input do in a browser.
function fire(target, type, extra) {
  var isStopped = false;
  var event = { type: type, target: target, stopPropagation: function () { isStopped = true; } };

  Object.keys(extra || {}).forEach(function (key) { event[key] = extra[key]; });

  for (var node = target; node && !isStopped; node = node.parentElement) {
    (node.listeners[type] || []).slice().forEach(function (listener) { listener(event); });
  }
}

// Typing into a text box: it takes focus, its value changes, and the page hears the input.
function type(input, text) {
  fire(input, 'focusin');
  input.value = text;
  fire(input, 'input');
}

// A click as the game's click-through delivers one: dispatched on whatever is under the pointer, greyed buttons
// included, bubbling up until a listener stops it (webui-core.js:33-36).
function click(target) {
  var isStopped = false;
  var event = { target: target, stopPropagation: function () { isStopped = true; } };

  for (var node = target; node && !isStopped; node = node.parentElement) {
    (node.listeners.click || []).slice().forEach(function (listener) { listener(event); });
  }
}
