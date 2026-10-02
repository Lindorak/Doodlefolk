"use strict";
/* StickFight Studio. The host pushes {t:"init"} once and {t:"state"} a few times a second; edits go back
   as small messages. Pages are built once per route and then patched live, never rebuilt under the user's
   fingers (controls the user touched recently aren't overwritten). */

const host = window.chrome && window.chrome.webview;
const QUICK = document.body.dataset.mode === "quick";
const POP = document.body.dataset.mode === "pop";
let INIT = null, S = null;
let route = { page: "cast", id: 0, sub: "personality", rel: null };
let current = null;          // { key, sig, update }
const $ = (q, r = document) => r.querySelector(q);

// ---------------- messaging ----------------

function send(m) { if (host) host.postMessage(m); else Mock.handle(m); }
const throttles = new Map();
/** Send at most every `ms` per key, always delivering the last value. */
function sendSoon(key, m, ms = 70) {
  let t = throttles.get(key);
  if (!t) { t = { last: 0, timer: 0, msg: null }; throttles.set(key, t); }
  t.msg = m;
  const now = performance.now();
  if (now - t.last >= ms) { t.last = now; send(m); return; }
  clearTimeout(t.timer);
  t.timer = setTimeout(() => { t.last = performance.now(); send(t.msg); }, ms - (now - t.last));
}

function receive(m) {
  switch (m.t) {
    case "init": INIT = m; applyTheme(); break;
    case "state": S = m; onState(); break;
    case "go": go(m.page, m.id); break;
    case "spawned": if (!QUICK) { go("figure", m.id); toast("Fresh off the pencil!"); } break;
    case "winstate": document.body.classList.toggle("max", !!m.max); break;
    case "toast": toast(m.text); break;
    case "pop": buildPop(m.kind, m.id); break;
    case "said": popSaid(m); break;
  }
}
if (host) host.addEventListener("message", e => receive(e.data));

// ---------------- tiny DOM helpers ----------------

function h(tag, attrs, ...kids) {
  const e = document.createElement(tag);
  for (const k in attrs || {}) {
    const v = attrs[k];
    if (v == null || v === false) continue;
    if (k.startsWith("on")) e.addEventListener(k.slice(2), v);
    else if (k === "class") e.className = v;
    else if (k === "style" && typeof v === "object") Object.assign(e.style, v);
    else if (k === "html") e.innerHTML = v;
    else e.setAttribute(k, v === true ? "" : v);
  }
  for (const c of kids.flat()) if (c != null && c !== false) e.append(c.nodeType ? c : document.createTextNode(c));
  return e;
}
const NS = "http://www.w3.org/2000/svg";
function s(tag, attrs, ...kids) {
  const e = document.createElementNS(NS, tag);
  for (const k in attrs || {}) {
    const v = attrs[k];
    if (v == null) continue;
    if (k.startsWith("on")) e.addEventListener(k.slice(2), v); else e.setAttribute(k, v);
  }
  for (const c of kids.flat()) if (c != null) e.append(c.nodeType ? c : document.createTextNode(c));
  return e;
}
/** append() that flattens arrays and skips null/false. */
function add(parent, ...kids) { parent.append(...kids.flat(Infinity).filter(k => k != null && k !== false)); }
function touched(input) { input.dataset.t = Date.now(); }
function idle(input) { return document.activeElement !== input && !(Date.now() - (+input.dataset.t || 0) < 900); }
function pct(v, lo = 0, hi = 1) { return Math.round((v - lo) / (hi - lo) * 100); }
function setRange(input, v) { if (idle(input)) { input.value = v; paintRange(input); } }
function paintRange(input) { input.style.setProperty("--p", pct(+input.value, +input.min, +input.max) + "%"); }
function shade(hex, k) {
  const n = parseInt(hex.slice(1), 16);
  const r = Math.round((n >> 16 & 255) * k), g = Math.round((n >> 8 & 255) * k), b = Math.round((n & 255) * k);
  return `rgb(${r},${g},${b})`;
}
let toastTimer = 0;
function toast(text) {
  const t = $("#toast"); t.textContent = text; t.classList.add("show");
  clearTimeout(toastTimer); toastTimer = setTimeout(() => t.classList.remove("show"), 1800);
}
/** A button that needs a second click to confirm. */
function armed(label, sure, action, cls = "btn danger") {
  const b = h("button", { class: cls, onclick: () => {
    if (b.classList.contains("armed")) { action(); return; }
    b.classList.add("armed"); b.textContent = sure;
    setTimeout(() => { b.classList.remove("armed"); b.textContent = label; }, 2500);
  } }, label);
  return b;
}
function check(label, hint, get, set) {
  const c = h("div", { class: "check", role: "checkbox", tabindex: 0 },
    s("svg", { class: "box", viewBox: "0 0 24 24" }, s("rect", { x: 2.5, y: 2.5, width: 19, height: 19, rx: 4 }), s("path", { d: "M6 12.5 L10.5 17 L19 6.5" })),
    h("div", { class: "lbl" }, label, hint ? h("small", null, hint) : null));
  const paint = () => c.classList.toggle("on", !!get());
  c.addEventListener("click", () => { set(!get()); c.classList.toggle("on"); touched(c); });
  c.update = () => { if (idle(c)) paint(); };
  paint();
  return c;
}
/** Hand-drawn dropdown (native <select> popups can't be styled). Same API as before: .sel.value, .set(v). */
let openMenu = null;
function select(options, value, onchange) {
  let cur = String(value), menu = null, hover = -1;
  const label = h("span", { class: "dd-label" });
  const caret = s("svg", { class: "dd-caret", viewBox: "0 0 12 12" }, s("path", { d: "M2.5 4.5 Q6 8.6 9.5 4.2" }));
  const btn = h("button", { class: "dd-btn", type: "button" }, label, caret);
  const wrap = h("span", { class: "select" }, btn);
  wrap.sel = { get value() { return cur; } };
  const paint = () => { const o = options.find(o => String(o.value) === cur); label.textContent = o ? o.label : ""; };
  function close() {
    if (!menu) return;
    menu.remove(); menu = null; wrap.classList.remove("open");
    if (openMenu === close) openMenu = null;
  }
  function choose(o) { cur = String(o.value); paint(); touched(btn); close(); btn.focus(); onchange(cur); }
  function mark(i) {
    hover = Math.max(0, Math.min(options.length - 1, i));
    [...menu.children].forEach((c, j) => c.classList.toggle("hover", j === hover));
    menu.children[hover].scrollIntoView({ block: "nearest" });
  }
  function open() {
    if (openMenu) openMenu();
    menu = h("div", { class: "dd-menu", role: "listbox" }, options.map((o, i) =>
      h("div", { class: "dd-item" + (String(o.value) === cur ? " on" : ""), role: "option",
        onpointerenter: () => mark(i), onpointerdown: e => { e.preventDefault(); choose(o); } }, o.label)));
    document.body.append(menu);
    const r = btn.getBoundingClientRect();
    const below = innerHeight - r.bottom - 12, above = r.top - 12;
    const up = below < 220 && above > below;
    menu.style.left = Math.min(r.left, innerWidth - Math.max(r.width, 220) - 8) + "px";
    menu.style.minWidth = r.width + "px";
    menu.style.maxHeight = Math.max(140, Math.min(360, up ? above : below)) + "px";
    if (up) menu.style.bottom = (innerHeight - r.top + 6) + "px"; else menu.style.top = (r.bottom + 6) + "px";
    wrap.classList.add("open");
    openMenu = close;
    hover = options.findIndex(o => String(o.value) === cur);
    if (hover >= 0) menu.children[hover].scrollIntoView({ block: "nearest" });
  }
  btn.addEventListener("click", () => { touched(btn); menu ? close() : open(); });
  btn.addEventListener("keydown", e => {
    if (!menu && (e.key === "ArrowDown" || e.key === "Enter" || e.key === " ")) { e.preventDefault(); open(); return; }
    if (!menu) return;
    if (e.key === "ArrowDown") { e.preventDefault(); mark(hover + 1); }
    else if (e.key === "ArrowUp") { e.preventDefault(); mark(hover - 1); }
    else if (e.key === "Enter" || e.key === " ") { e.preventDefault(); if (hover >= 0) choose(options[hover]); }
    else if (e.key === "Escape" || e.key === "Tab") { e.preventDefault(); close(); }
  });
  btn.addEventListener("blur", () => setTimeout(close, 120));
  wrap.set = v => { if (!menu && idle(btn)) { cur = String(v); paint(); } };
  paint();
  return wrap;
}
document.addEventListener("pointerdown", e => { if (openMenu && !e.target.closest(".dd-menu, .dd-btn")) openMenu(); });
document.addEventListener("scroll", () => openMenu && openMenu(), true);
function range(min, max, step, value, oninput, onchange) {
  const r = h("input", { type: "range", min, max, step, value });
  r.addEventListener("input", () => { touched(r); paintRange(r); oninput && oninput(+r.value); });
  if (onchange) r.addEventListener("change", () => { touched(r); onchange(+r.value); });
  paintRange(r);
  return r;
}

// ---------------- stick figure drawing ----------------

const J = { Head: 0, Neck: 1, Pelvis: 2, ElbowN: 3, HandN: 4, ElbowF: 5, HandF: 6, KneeN: 7, FootN: 8, KneeF: 9, FootF: 10 };
const STANDING = [[1, -55], [1, -47], [0, -26], [5, -37], [6, -27], [-3, -37], [-4, -27], [3, -13], [5, 0], [-2, -13], [-5, 0]];

/** Build a figure drawing; returns the svg with an .update(pose, hex) method. */
function figSvg(cls = "fig") {
  const svg = s("svg", { class: cls, viewBox: "-45 -76 90 84" });
  svg.append(s("path", { class: "ground", d: "M-36 2.5 Q-10 1.2 12 2.8 T38 2" }));
  const far = s("g", { "stroke-linecap": "round", "stroke-linejoin": "round", fill: "none", "stroke-width": 3.3 });
  const near = s("g", { "stroke-linecap": "round", "stroke-linejoin": "round", fill: "none", "stroke-width": 3.3 });
  const outline = s("g", { "stroke-linecap": "round", "stroke-linejoin": "round", fill: "none", "stroke-width": 4.9, stroke: "rgba(0,0,0,.25)" });
  const fp = s("path"), np = s("path"), op = s("path"), head = s("circle", { r: 6.5 }), headO = s("circle", { r: 7.3, fill: "rgba(0,0,0,.25)" });
  far.append(fp); near.append(np); outline.append(op);
  const lookBack = s("g"), lookFront = s("g");
  svg.append(lookBack, outline, headO, far, near, head, lookFront);
  svg.update = (pose, hex, look, facing) => {
    pose = pose && pose.length === 11 ? pose : STANDING;
    // Stand it on the ground line under its pelvis, whatever it's doing.
    let maxY = -1e9; for (const p of pose) maxY = Math.max(maxY, p[1]);
    const dx = -pose[J.Pelvis][0], dy = -maxY;
    const P = i => `${(pose[i][0] + dx).toFixed(1)} ${(pose[i][1] + dy).toFixed(1)}`;
    const nearD = `M${P(J.Neck)} L${P(J.Pelvis)} M${P(J.Pelvis)} L${P(J.KneeN)} L${P(J.FootN)} M${P(J.Neck)} L${P(J.ElbowN)} L${P(J.HandN)}`;
    const farD = `M${P(J.Neck)} L${P(J.ElbowF)} L${P(J.HandF)} M${P(J.Pelvis)} L${P(J.KneeF)} L${P(J.FootF)}`;
    np.setAttribute("d", nearD); fp.setAttribute("d", farD); op.setAttribute("d", nearD + farD);
    near.setAttribute("stroke", hex); far.setAttribute("stroke", shade(hex, 0.72));
    const hx = pose[J.Head][0] + dx, hy = pose[J.Head][1] + dy;
    head.setAttribute("cx", hx); head.setAttribute("cy", hy); head.setAttribute("fill", hex);
    headO.setAttribute("cx", hx); headO.setAttribute("cy", hy);
    drawLook(lookBack, lookFront, look, pose.map(p => [p[0] + dx, p[1] + dy]), facing || 1, hex);
  };
  return svg;
}

/** Outfit pieces on a Studio figure: same shape data as the desktop (head pieces, shoes), simple strokes for clothes. */
function drawLook(gBack, gFront, look, P, facing, hex) {
  gBack.replaceChildren(); gFront.replaceChildren();
  if (!look || !INIT || !INIT.lookParts) return;
  const LP = INIT.lookParts, fixed = INIT.fixedColours;
  const find = (list, key) => key ? list.find(p => p.key === key) : null;
  const V = i => ({ x: P[i][0], y: P[i][1] });
  const head = V(J.Head), neck = V(J.Neck), pel = V(J.Pelvis);
  let ux = head.x - neck.x, uy = head.y - neck.y; const ul = Math.hypot(ux, uy) || 1; ux /= ul; uy /= ul;
  const fx = -uy * facing, fy = ux * facing, hr = 6.5;
  const headMap = (x, y) => [head.x + fx * x * hr + ux * y * hr, head.y + fy * x * hr + uy * y * hr];
  const colOf = (c, main) => c === 0 ? main : c === 1 ? shade(main, 0.72) : c === 2 ? lighten(main) : fixed[c] || "#999";
  const ink = { stroke: "rgba(20,20,20,.75)", "stroke-width": 0.8, "stroke-linejoin": "round" };
  function shapes(list, map, unit, main, into) {
    for (const sh of list || []) {
      const p = sh.p, c = colOf(sh.c, main), pts = [];
      const pt = (x, y) => pts.push(map(x, y).map(v => v.toFixed(2)).join(","));
      if (sh.k === "l") { const a = map(p[0], p[1]), b = map(p[2], p[3]); into.append(s("line", { x1: a[0], y1: a[1], x2: b[0], y2: b[1], stroke: c, "stroke-width": sh.w * unit, "stroke-linecap": "round" })); continue; }
      if (sh.k === "c") { for (let i = 0; i + 1 < p.length; i += 2) pt(p[i], p[i + 1]); into.append(s("polyline", { points: pts.join(" "), fill: "none", stroke: c, "stroke-width": sh.w * unit, "stroke-linecap": "round" })); continue; }
      if (sh.k === "r") { pt(p[0], p[1]); pt(p[2], p[1]); pt(p[2], p[3]); pt(p[0], p[3]); }
      else if (sh.k === "o") { pt(p[0], p[1]); pt(p[2], p[1]); pt(p[2], p[3]); pt(p[0], p[3]); }
      else if (sh.k === "e") { for (let i = 0; i < 16; i++) { const a = i * Math.PI * 2 / 16; pt(p[0] + Math.cos(a) * p[2], p[1] + Math.sin(a) * p[3]); } }
      else if (sh.k === "p") { for (let i = 0; i + 1 < p.length; i += 2) pt(p[i], p[i + 1]); }
      into.append(s("polygon", { points: pts.join(" "), fill: c, ...ink }));
    }
  }
  const hair = find(LP.hair, look.hair), hat = find(LP.hat, look.hat);
  // Behind: cape, long hair, hood.
  if (look.back === "cape") {
    const bx = -facing;
    gBack.append(s("polygon", { points: `${neck.x + bx * 1.5},${neck.y} ${neck.x + bx * 4},${neck.y} ${pel.x + bx * 11},${pel.y + 9} ${pel.x + bx * 3},${pel.y + 11}`, fill: look.backColour, ...ink }));
  }
  if (hair) shapes(hair.back, headMap, hr, look.hairColour, gBack);
  if (hat) shapes(hat.back, headMap, hr, look.hatColour, gBack);
  if (look.hair === "ponytail") { const a = headMap(-0.95, 0.45); gBack.append(s("path", { d: `M${a[0]} ${a[1]} q${-facing * 3} 5 ${-facing * 2} 9`, stroke: look.hairColour, "stroke-width": 2.4, fill: "none", "stroke-linecap": "round" })); }
  // Clothes on the body.
  if (look.top) {
    const w = look.top === "tank" ? 5.6 : 6.8;
    gFront.append(s("line", { x1: neck.x, y1: neck.y + 1.2, x2: pel.x, y2: pel.y + 1.5, stroke: look.topColour, "stroke-width": w, "stroke-linecap": "round" }));
    if (look.top !== "tank") {
      const k = look.top === "hoodie" ? 1 : 0.55, e = V(J.ElbowN);
      gFront.append(s("line", { x1: neck.x, y1: neck.y, x2: neck.x + (e.x - neck.x) * k, y2: neck.y + (e.y - neck.y) * k, stroke: look.topColour, "stroke-width": 5.6, "stroke-linecap": "round" }));
    }
  }
  if (look.waist === "belt") gFront.append(s("line", { x1: pel.x - 2.8, y1: pel.y - 1.8, x2: pel.x + 2.8, y2: pel.y - 1.8, stroke: look.waistColour, "stroke-width": 1.8 }));
  if (look.waist === "skirt") gFront.append(s("polygon", { points: `${pel.x - 2.6},${pel.y - 2.5} ${pel.x + 2.6},${pel.y - 2.5} ${pel.x + 7.5},${pel.y + 10} ${pel.x - 7.5},${pel.y + 10}`, fill: look.waistColour, ...ink }));
  if (look.neck === "tie") gFront.append(s("polygon", { points: `${neck.x + facing * 0.6},${neck.y + 1.5} ${neck.x + facing * 2.2},${neck.y + 1.5} ${neck.x + facing * 2.6},${neck.y + 10} ${neck.x + facing * 1.4},${neck.y + 12}`, fill: look.neckColour }));
  if (look.neck === "bowtie") gFront.append(s("polygon", { points: `${neck.x + facing},${neck.y + 1.2} ${neck.x + facing},${neck.y - 1} ${neck.x + facing * 1},${neck.y + 3.4}`, fill: look.neckColour, stroke: look.neckColour, "stroke-width": 2.2, "stroke-linejoin": "round" }));
  if (look.neck === "scarf") gFront.append(s("line", { x1: neck.x - 2.2, y1: neck.y + 0.6, x2: neck.x + 2.2, y2: neck.y + 0.6, stroke: look.neckColour, "stroke-width": 2.6, "stroke-linecap": "round" }));
  // Head pieces.
  if (hair) shapes(hair.front, headMap, hr, look.hairColour, gFront);
  shapes(find(LP.beard, look.beard)?.front, headMap, hr, look.hairColour, gFront);
  shapes(find(LP.glasses, look.glasses)?.front, headMap, hr, hex, gFront);
  if (hat) shapes(hat.front, headMap, hr, look.hatColour, gFront);
  const shoe = find(LP.shoes, look.shoes);
  if (shoe) for (const j of [J.FootF, J.FootN]) {
    const f = V(j);
    shapes(shoe.front, (x, y) => [f.x + x * facing - facing * 0.5, f.y - y + 1.2], 1, j === J.FootF ? shade(look.shoeColour, 0.8) : look.shoeColour, gFront);
  }
}

// ---------------- routing ----------------

const PAGES = {};
function go(page, id) {
  route.page = page;
  if (id != null) route.id = id;
  if (page === "figure" && id != null) route.rel = null;
  if (page === "toys" && id) route.toy = id;
  current = null;
  onState();
}
function fig(id = route.id) { return S && S.figures.find(f => f.id === id); }

function onState() {
  if (!INIT || !S) return;
  if (QUICK) { applyTheme(); if (!$("#quick").firstChild) buildQuick(); quickUpdate(); return; }
  if (POP) { applyTheme(); if (pendingPop) buildPop(...pendingPop); else popUpdate(); return; }
  $("#fps").textContent = S.fpsNow ? `${S.fpsNow} fps` : "";
  applyTheme();
  if (route.page === "figure" && !fig()) route.page = "cast";
  const p = PAGES[route.page];
  const key = route.page + ":" + (route.page === "figure" ? route.id + ":" + route.sub : "");
  const sig = p.sig ? p.sig() : "";
  if (!current || current.key !== key || current.sig !== sig) {
    const root = $("#page");
    const scroll = current && current.key === key ? root.scrollTop : 0;
    root.replaceChildren();
    const update = p.build(root) || (() => {});
    root.firstElementChild && root.firstElementChild.classList.add("fade-in");
    root.scrollTop = scroll;
    current = { key, sig, update };
    for (const t of document.querySelectorAll(".tab")) t.classList.toggle("on", t.dataset.page === (route.page === "figure" ? "cast" : route.page));
  }
  current.update();
  crumbs();
}

function crumbs() {
  const c = $("#crumbs");
  const names = { cast: "Your cast", library: "Saved figures", fights: "Colours & fights", toys: "Things", pets: "Pets", paper: "The Stick Times", stickers: "Sticker book", settings: "Settings" };
  if (route.page === "figure") {
    const f = fig();
    c.innerHTML = "";
    c.append(h("span", null, "Your cast  ›  "), h("b", null, f ? f.name : ""));
  } else c.textContent = names[route.page] || "";
}

// ---------------- Cast ----------------

PAGES.cast = {
  sig: () => S.figures.map(f => f.id).join(",") + "|" + (S.casts ? S.casts.current + S.casts.others.map(c => c.name + c.saved).join(",") : "") + "|" + S.settings.colourBlind,
  build(root) {
    const n = S.figures.length;
    add(root, h("div", { class: "row" },
      h("h1", null, "Your cast"),
      h("span", { class: "spacer" }),
      n ? armed("Clear them all", "Really? Click again", () => send({ t: "clear", what: "figures" }), "btn small danger") : null));
    add(root, h("p", { class: "sub" }, n ? `${n} figure${n > 1 ? "s" : ""} living on your desktop. Pick one to see what makes them tick.` : "Nobody here yet. Draw someone!"));
    const grid = h("div", { class: "grid" });
    const cards = [];
    S.figures.forEach((f, i) => {
      const svg = figSvg();
      const name = h("span"), act = h("div", { class: "act" }), feels = h("div", { class: "feels" }), likes = h("div", { class: "likes" }), dot = h("span", { class: "dot" });
      const card = h("div", { class: "card", style: { "--tilt": `${((f.id * 37) % 7 - 3) * 0.35}deg` }, onclick: () => go("figure", f.id) },
        h("div", { class: "tape" }), h("div", { class: "name" }, dot, name), svg, act, feels, likes);
      grid.append(card);
      cards.push({ id: f.id, svg, name, act, feels, likes, dot });
    });
    grid.append(newFigureCard());
    add(root, grid);
    // Save slots.
    const casts = S.casts || { current: "", others: [] };
    const nameIn = h("input", { type: "text", placeholder: "Name", maxlength: 40, style: { width: "180px" } });
    add(root, h("h2", null, "Casts"),
      h("p", { class: "sub" }, `You're playing "${casts.current}". Keep several casts and switch between them: the one you leave is saved just as it is, with its things and pets.`),
      h("div", { class: "row" }, nameIn,
        h("button", { class: "btn small", onclick: () => nameIn.value.trim() && send({ t: "cast", op: "saveas", name: nameIn.value.trim() }) }, "Save this cast as…"),
        h("button", { class: "btn small", onclick: () => nameIn.value.trim() && send({ t: "cast", op: "new", name: nameIn.value.trim() }) }, "Start a new cast")),
      casts.others.length ? h("div", { class: "grid" }, casts.others.map(c => h("div", { class: "card" },
        h("div", { class: "name" }, c.name),
        h("div", { class: "hint" }, [c.figures.join(", "), c.pets ? `${c.pets} pet${c.pets > 1 ? "s" : ""}` : "", c.saved ? `saved ${c.saved}` : ""].filter(Boolean).join(" · ")),
        h("div", { class: "row" },
          h("button", { class: "btn small primary", onclick: () => send({ t: "cast", op: "load", name: c.name }) }, "Switch to this cast"),
          armed("Delete", "Sure?", () => send({ t: "cast", op: "delete", name: c.name }), "btn small danger"))))) : h("p", { class: "hint" }, "No other casts yet."));
    return () => {
      for (const c of cards) {
        const f = fig(c.id); if (!f) continue;
        c.svg.update(f.pose, f.hex, f.look, f.facing);
        c.name.textContent = f.name; c.dot.style.background = f.hex;
        c.act.textContent = f.activity;
        c.feels.textContent = f.feels;
        c.likes.textContent = f.tastes.describe;
      }
    };
  },
};

/** Read a calendar (.ics) file here in the page and send the next month's events as reminders. */
function importIcs(input) {
  const file = input.files && input.files[0];
  if (!file) return;
  const reader = new FileReader();
  reader.onload = () => {
    const text = String(reader.result).replace(/\r?\n[ \t]/g, "");
    const events = [];
    const now = new Date(), horizon = now.getTime() + 31 * 86400000;
    const pad = n => String(n).padStart(2, "0");
    const local = d => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
    let skipped = 0;
    for (const block of text.split("BEGIN:VEVENT").slice(1)) {
      const body = block.split("END:VEVENT")[0];
      const rawSummary = (body.match(/^SUMMARY[^:]*:(.*)$/m) || [])[1];
      // DTSTART;TZID="…":20261002T140000 (parameters may be quoted and contain colons), or ;VALUE=DATE:20261002 for all-day.
      const start = body.match(/^DTSTART((?:;[^:;"]*(?:"[^"]*")?)*):(\d{8})(T(\d{6})(Z?))?/m);
      if (!rawSummary || !start) continue;
      const summary = rawSummary.replace(/\\([\\;,nN])/g, (_, c) => /n/i.test(c) ? " " : c).trim().slice(0, 100);
      const d = start[2], allDay = !start[3], t = start[4] || "090000", utc = start[5] === "Z";
      let when = new Date(`${d.slice(0, 4)}-${d.slice(4, 6)}-${d.slice(6, 8)}T${t.slice(0, 2)}:${t.slice(2, 4)}:${t.slice(4, 6)}${utc ? "Z" : ""}`);
      if (isNaN(when)) continue;
      // Simple repeats (daily / weekly / monthly / yearly, with an interval): the next one from now.
      const rule = (body.match(/^RRULE:(.*)$/m) || [])[1];
      if (rule) {
        const freq = (rule.match(/FREQ=(\w+)/) || [])[1], every = +((rule.match(/INTERVAL=(\d+)/) || [])[1] || 1);
        const until = (rule.match(/UNTIL=(\d{8})/) || [])[1];
        const step = { DAILY: dt => dt.setDate(dt.getDate() + every), WEEKLY: dt => dt.setDate(dt.getDate() + 7 * every),
                       MONTHLY: dt => dt.setMonth(dt.getMonth() + every), YEARLY: dt => dt.setFullYear(dt.getFullYear() + every) }[freq];
        if (!step) { skipped++; continue; }
        for (let i = 0; i < 5000 && when < now; i++) step(when);
        if (until && local(when).replace(/-/g, "").slice(0, 8) > until) continue;
      }
      if (when < now || when.getTime() > horizon) continue;
      // Ten minutes before (or straight away, if it's sooner than that); all-day events in the morning.
      let remindAt = allDay ? when : new Date(Math.max(when.getTime() - 10 * 60000, now.getTime() + 60000));
      if (allDay && remindAt < now) remindAt = new Date(now.getTime() + 60000);
      events.push({ text: allDay ? `${summary} (today)` : `${summary} at ${pad(when.getHours())}:${pad(when.getMinutes())}`, when: local(remindAt) });
    }
    if (skipped) toast(`${skipped} repeating event${skipped > 1 ? "s" : ""} with an unusual pattern skipped.`);
    send({ t: "reminder", op: "import", items: events.slice(0, 100) });
    input.value = "";
  };
  reader.readAsText(file);
}

const TEAM_SYM = { Red: "▲", Blue: "●", Green: "■", Orange: "◆", Purple: "★", Yellow: "✚", Cyan: "⬟", Pink: "♥", Black: "✖", White: "○" };

function newFigureCard() {
  const preset = select([{ value: -1, label: "Random personality" }, ...INIT.presets.map((p, i) => ({ value: i, label: `${p.name}: ${p.blurb}` })),
    { value: -2, label: "Cursor hunter: hunts your cursor, relentlessly" }], -1, () => {});
  const spawn = color => { const v = +preset.sel.value; send({ t: "spawn", color, preset: v, hunter: v === -2 }); };
  return h("div", { class: "card new" },
    h("div", { class: "name" }, "Draw someone new"),
    h("div", { class: "hint" }, "Pick a colour to draw them in:"),
    h("div", { class: "swatches" }, INIT.palette.map((p, i) =>
      h("button", { class: "sw", title: p.name, style: { background: p.hex, color: "#111", "font-size": "11px", "text-shadow": "0 0 2px #fff" }, onclick: () => spawn(i) }, S.settings.colourBlind ? TEAM_SYM[p.name] || "" : ""))),
    preset,
    h("div", { class: "row" },
      h("button", { class: "btn small primary", onclick: () => spawn(-1) }, "Surprise me"),
      h("button", { class: "btn small", onclick: () => go("library") }, "From library…")));
}

// ---------------- Figure ----------------

const SUBS = [["personality", "Personality"], ["likes", "Likes & dislikes"], ["friends", "Friends"], ["moves", "Moves"], ["mood", "Mood"], ["look", "Look"], ["diary", "Diary"]];

PAGES.figure = {
  sig: () => {
    const f = fig();
    return f ? (route.sub === "friends" ? f.rels.map(r => r.id).join(",") : "") : "";
  },
  build(root) {
    const f = fig();
    const id = f.id;
    const big = figSvg();
    const name = h("input", { type: "text", value: f.name, maxlength: 40, spellcheck: "false" });
    const commit = () => { touched(name); send({ t: "fig", id, op: "rename", name: name.value }); };
    name.addEventListener("change", commit);
    name.addEventListener("keydown", e => { if (e.key === "Enter") name.blur(); });
    const act = h("div", { class: "act" }), feels = h("div", { class: "hint" });
    const sws = colourPicker(f.hex, hex => sendSoon("colour" + id, { t: "fig", id, op: "color", hex }));
    add(root, h("div", { class: "hero" }, big,
      h("div", { class: "meta" }, name, act, feels, sws,
        h("div", { class: "row" },
          h("button", { class: "btn small primary", title: "Wave them over to your cursor. Whether they come depends on how they feel about you.", onclick: () => send({ t: "fig", id, op: "call" }) }, "👋 Call them over"),
          h("button", { class: "btn small", onclick: () => { send({ t: "fig", id, op: "save" }); toast(`${fig(id)?.name || "Figure"} saved to your library`); } }, "★ Save to library"),
          armed("Erase", "Erase them? Click again", () => { send({ t: "fig", id, op: "remove" }); go("cast"); }, "btn small danger")))));
    const tabs = h("div", { class: "subtabs" }, SUBS.map(([k, label]) =>
      h("button", { class: "subtab" + (route.sub === k ? " on" : ""), onclick: () => { route.sub = k; current = null; onState(); } }, label)));
    add(root, tabs);
    const panel = h("div", { class: "panel" });
    add(root, panel);
    const sub = SUBPANELS[route.sub](panel, f);
    return () => {
      const f = fig(id); if (!f) return;
      big.update(f.pose, f.hex, f.look, f.facing);
      if (idle(name)) name.value = f.name;
      act.textContent = f.activity;
      feels.textContent = `${f.feels} · ${f.describe}`;
      sws.set(f.hex);
      sub && sub(f);
    };
  },
};

function colourPicker(hex, pick) {
  let cur = hex;
  const custom = h("input", { type: "color", value: hex });
  custom.addEventListener("input", () => { touched(custom); cur = custom.value; pick(custom.value); paint(); });
  const buttons = INIT.palette.map(p => h("button", { class: "sw", title: p.name, style: { background: p.hex }, onclick: () => { cur = p.hex; pick(p.hex); paint(); } }));
  const wrap = h("div", { class: "swatches" }, buttons, h("span", { class: "sw custom", title: "Any colour…" }, custom));
  function paint() { buttons.forEach((b, i) => b.classList.toggle("on", INIT.palette[i].hex.toLowerCase() === cur.toLowerCase())); }
  wrap.set = v => { if (idle(custom)) { cur = v; custom.value = v.toLowerCase(); paint(); } };
  paint();
  return wrap;
}

const TRAITS = [
  ["energy", "Energy", "Lazy ↔ hyper"], ["curiosity", "Curiosity", "Stays put ↔ explores everything"],
  ["bravery", "Bravery", "Jumpy ↔ fearless"], ["playfulness", "Playfulness", "Serious ↔ goofy"],
  ["aggression", "Aggression", "Gentle ↔ picks fights"], ["sociability", "Sociability", "Loner ↔ social butterfly"],
];

const SUBPANELS = {
  personality(panel, f) {
    const id = f.id;
    const local = { ...f.traits };
    let dragging = -1;
    const R = 100;
    const ang = i => -Math.PI / 2 + i * Math.PI * 2 / 6;
    const pt = (i, v) => [Math.cos(ang(i)) * R * v, Math.sin(ang(i)) * R * v];
    const svg = s("svg", { class: "radar", viewBox: "-160 -140 320 290" });
    for (const k of [0.25, 0.5, 0.75, 1]) svg.append(s("polygon", { class: "web", points: [0, 1, 2, 3, 4, 5].map(i => pt(i, k).join(",")).join(" ") }));
    for (let i = 0; i < 6; i++) {
      const [x, y] = pt(i, 1);
      svg.append(s("line", { class: "axis", x1: 0, y1: 0, x2: x, y2: y }));
      const [lx, ly] = pt(i, 1.24);
      svg.append(s("text", { x: lx, y: ly + 5, "text-anchor": "middle" }, TRAITS[i][1]));
    }
    const shape = s("polygon", { class: "shape" });
    svg.append(shape);
    const pts = TRAITS.map((t, i) => {
      const c = s("circle", { class: "pt", r: 7 });
      c.addEventListener("pointerdown", e => { dragging = i; c.classList.add("drag"); svg.setPointerCapture(e.pointerId); e.preventDefault(); });
      svg.append(c);
      return c;
    });
    const vals = TRAITS.map(() => s("text", { class: "v", "text-anchor": "middle" }));
    vals.forEach(v => svg.append(v));
    svg.addEventListener("pointermove", e => {
      if (dragging < 0) return;
      const m = svg.getScreenCTM().inverse();
      const p = new DOMPoint(e.clientX, e.clientY).matrixTransform(m);
      const a = ang(dragging), v = Math.max(0, Math.min(1, (p.x * Math.cos(a) + p.y * Math.sin(a)) / R));
      local[TRAITS[dragging][0]] = Math.round(v * 100) / 100;
      touchedT = Date.now();
      draw();
      sendSoon("trait" + dragging, { t: "fig", id, op: "trait", key: TRAITS[dragging][0], v: local[TRAITS[dragging][0]] });
    });
    const stop = () => { if (dragging >= 0) pts[dragging].classList.remove("drag"); dragging = -1; };
    svg.addEventListener("pointerup", stop); svg.addEventListener("pointercancel", stop);
    let touchedT = 0;
    function draw(hex) {
      if (hex) { shape.setAttribute("fill", hex + "55"); }
      shape.setAttribute("points", TRAITS.map((t, i) => pt(i, local[t[0]]).join(",")).join(" "));
      TRAITS.forEach((t, i) => {
        const [x, y] = pt(i, local[t[0]]);
        pts[i].setAttribute("cx", x); pts[i].setAttribute("cy", y);
        const [vx, vy] = pt(i, Math.max(local[t[0]], 0.12) + 0.13);
        vals[i].setAttribute("x", vx); vals[i].setAttribute("y", vy + 4);
        vals[i].textContent = Math.round(local[t[0]] * 100);
      });
    }
    draw(f.hex);
    const sliders = TRAITS.map(([k, label, tip]) => {
      const val = h("span", { class: "val" });
      const r = range(0, 1, 0.01, f.traits[k], v => { local[k] = v; touchedT = Date.now(); draw(); val.textContent = Math.round(v * 100); sendSoon("trait-" + k, { t: "fig", id, op: "trait", key: k, v }); });
      return { k, r, val, el: h("div", { class: "field" }, h("label", { title: tip }, label), r, val) };
    });
    const presets = h("div", { class: "row" }, INIT.presets.map((p, i) => h("button", { class: "chip", title: p.blurb, onclick: () => send({ t: "fig", id, op: "preset", v: i }) }, p.name)),
      h("button", { class: "chip", onclick: () => send({ t: "fig", id, op: "dice" }), title: "Roll a random personality" }, "🎲 Dice"));
    const blurb = h("p", { class: "sub" });
    const hunter = check("Cursor hunter", "Hunts your cursor across the screen, boxes it, and throws whatever it can grab at it. Never forgives you.",
      () => fig(id) && fig(id).hunter, v => send({ t: "fig", id, op: "hunter", v }));
    const GENDERS = [["Girl", "Girl"], ["Boy", "Boy"], ["Nonbinary", "Nonbinary"]];
    const genderChips = GENDERS.map(([k, l]) => { const c = h("button", { class: "chip", onclick: () => { touched(c); send({ t: "fig", id, op: "gender", v: k }); } }, l); c.key = k; return c; });
    const FOR = [["Girls", "girls"], ["Boys", "boys"], ["Nonbinary", "nonbinary folks"]];
    let attr = [...(f.attraction || [])];
    const forChips = FOR.map(([k, l]) => {
      const c = h("button", { class: "chip", onclick: () => { touched(c); attr = attr.includes(k) ? attr.filter(x => x !== k) : [...attr, k]; paintFor(); send({ t: "fig", id, op: "attraction", v: attr }); } }, l);
      c.key = k; return c;
    });
    function paintFor() { forChips.forEach(c => c.classList.toggle("on", attr.includes(c.key))); }
    const heart = h("p", { class: "hint" });
    const JOBS = [["None", "No job"], ["Shopkeeper", "🛒 Shopkeeper"], ["Chef", "🍳 Chef"], ["Builder", "🔨 Builder"], ["Entertainer", "🎭 Entertainer"], ["Teacher", "📚 Teacher"]];
    const jobChips = JOBS.map(([k, l]) => { const c = h("button", { class: "chip", onclick: () => { touched(c); send({ t: "fig", id, op: "job", v: k }); } }, l); c.key = k; return c; });
    const coins = h("span", { class: "hint" });
    const lifeLine = h("p", { class: "hint" });
    add(panel, h("div", { class: "split" }, svg, h("div", null,
      h("h2", { style: { marginTop: 0 } }, "Who they are"), blurb, sliders.map(x => x.el),
      h("h3", null, "Start from a type"), presets, h("div", { style: { marginTop: "12px" } }, hunter),
      h("h3", null, "Heart"),
      h("div", { class: "field" }, h("label", null, "They're a"), h("div", { class: "row tight" }, genderChips)),
      h("div", { class: "field" }, h("label", null, "Can fall for"), h("div", { class: "row tight" }, forChips)),
      heart,
      h("h3", null, "Work and life"),
      h("div", { class: "row tight" }, jobChips),
      h("div", { class: "row", style: { marginTop: "6px" } }, coins, h("button", { class: "btn small", onclick: () => send({ t: "fig", id, op: "coins", v: 5 }) }, "🪙 Give 5 coins")),
      lifeLine)));
    return f => {
      jobChips.forEach(c => { if (idle(c)) c.classList.toggle("on", c.key === (f.job || "None")); });
      coins.textContent = `🪙 ${f.coins ?? 0} coins`;
      lifeLine.textContent = [f.stage, f.ageYears ? `${f.ageYears} years old` : "", f.retired ? "Retired" : ""].filter(Boolean).join(" · ");
      genderChips.forEach(c => { if (idle(c)) c.classList.toggle("on", c.key === f.gender); });
      if (forChips.every(idle)) { attr = [...(f.attraction || [])]; paintFor(); }
      heart.textContent = f.sweetheart ? `Dating ${f.sweetheart} ♥` : f.crush ? `Has a crush on ${f.crush}…` : `Single. ${f.name} ${f.attractionText || ""}.`;
      blurb.textContent = f.describe + "." + (f.hunter ? " Out to get you." : "");
      hunter.update();
      if (dragging < 0 && Date.now() - touchedT > 900) { Object.assign(local, f.traits); draw(f.hex); }
      for (const x of sliders) { setRange(x.r, f.traits[x.k]); x.val.textContent = Math.round((idle(x.r) ? f.traits[x.k] : +x.r.value) * 100); }
      shape.setAttribute("fill", f.hex + "55");
    };
  },

  likes(panel, f) {
    const id = f.id;
    const LEVELS = [[-1, -2, "Hates"], [-0.55, -1, "Dislikes"], [0, 0, "Doesn't mind"], [0.55, 1, "Likes"], [1, 2, "Loves"]];
    const bucket = v => v < -0.7 ? -2 : v < -0.2 ? -1 : v <= 0.2 ? 0 : v <= 0.7 ? 1 : 2;
    const rows = [];
    const table = h("div", { class: "likes-table" });
    let group = null;
    for (const th of INIT.things) {
      if (th.group !== group) { group = th.group; table.append(h("h3", null, group)); }
      const faces = LEVELS.map(([v, b, label]) => {
        const btn = h("button", { class: `face f${b}`, title: label, onclick: () => { touched(btn); send({ t: "fig", id, op: "taste", key: th.key, v }); faces.forEach(x => x.classList.toggle("on", x === btn)); } }, faceSvg(b));
        btn.b = b;
        return btn;
      });
      table.append(h("div", { class: "thing" }, th.name), h("div", { class: "faces" }, faces));
      rows.push({ key: th.key, faces });
    }
    const fav = swatchRow(name => send({ t: "fig", id, op: "fav", v: name }));
    const hate = swatchRow(name => send({ t: "fig", id, op: "hate", v: name }));
    const summary = h("p", { class: "sub" });
    add(panel, h("div", { class: "split" },
      h("div", null, h("h2", { style: { marginTop: 0 } }, "Their taste"), summary,
        h("h3", null, "Favourite colour"), fav, h("h3", null, "Least favourite colour"), hate,
        h("p", { class: "hint", style: { marginTop: "14px" } }, "Figures who share likes become friends faster, chat about their favourite things, and seek each other out. Colours they like or dislike colour first impressions."),
        h("button", { class: "btn small", onclick: () => send({ t: "fig", id, op: "rollTastes" }) }, "🎲 Re-roll their likes")),
      table));
    return f => {
      summary.textContent = f.tastes.describe;
      fav.set(f.tastes.fav); hate.set(f.tastes.hate);
      for (const r of rows) {
        if (!r.faces.every(idle)) continue;
        const b = bucket(f.tastes.opinions[r.key] || 0);
        r.faces.forEach(x => x.classList.toggle("on", x.b === b));
      }
    };
  },

  friends(panel, f) {
    const id = f.id;
    const W = 230, H = 200;
    const svg = s("svg", { class: "webview", viewBox: `${-W} ${-H} ${W * 2} ${H * 2}` });
    const others = [{ id: "you", name: "You" }, ...f.rels.map(r => ({ id: r.id, name: r.name }))];
    const pos = {};
    others.forEach((o, i) => {
      const a = -Math.PI / 2 + i * Math.PI * 2 / others.length;
      pos[o.id] = [Math.cos(a) * 168, Math.sin(a) * 140];
    });
    const edges = {}, labels = {}, nodes = {};
    for (const o of others) {
      const [x, y] = pos[o.id];
      edges[o.id] = s("path", { class: "edge", d: `M0 0 Q${x * 0.5 + y * 0.08} ${y * 0.5 - x * 0.08} ${x} ${y}` });
      svg.append(edges[o.id]);
    }
    const centre = s("g", { class: "node" }, s("circle", { class: "bg", r: 34 }));
    const centreFig = figMini(); centre.append(centreFig);
    svg.append(centre);
    for (const o of others) {
      const [x, y] = pos[o.id];
      const g = s("g", { class: "node", transform: `translate(${x} ${y})`, onclick: () => { route.rel = o.id; drawCard(); paintSel(); } },
        s("circle", { class: "bg", r: 26 }));
      if (o.id === "you") g.append(s("path", { d: "M-6 -12 L-6 8 L-1 3 L3 11 L6 9.5 L2 2 L9 2 Z", fill: "var(--ink)", stroke: "none" }));
      else { const d = s("circle", { r: 9, cy: -4 }); g.append(d); g.dot = d; }
      g.append(s("text", { y: 44 }, o.name));
      nodes[o.id] = g;
      labels[o.id] = s("text", { class: "lbl", x: x * 0.5 + y * 0.06, y: y * 0.5 - x * 0.06 - 6 });
      svg.append(g, labels[o.id]);
    }
    const card = h("div", { class: "relcard" });
    add(panel, h("div", { class: "split wide" }, svg, h("div", null, card,
      h("p", { class: "hint", style: { marginTop: "12px" } }, "Lines show how they feel: green is fond, red is dislike, thicker is stronger. Feelings change as they play, chat, fight and get thrown around, and they're remembered between runs."))));
    if (route.rel == null) route.rel = "you";
    function paintSel() { for (const k in nodes) nodes[k].classList.toggle("sel", String(k) === String(route.rel)); }
    let cardUpdate = () => {};
    function drawCard() {
      card.replaceChildren();
      const f = fig(id); if (!f) return;
      if (route.rel === "you") {
        const fond = range(-1, 1, 0.01, f.fond, v => sendSoon("fond", { t: "fig", id, op: "fond", v }));
        const trust = range(0, 1, 0.01, f.trust, v => sendSoon("trust", { t: "fig", id, op: "trust", v }));
        const feel = h("div", { class: "big" });
        const mem = h("ul", { class: "memories" });
        card.append(h("div", { class: "hint" }, `How ${f.name} feels about you`), feel,
          h("h3", null, "Fondness"), h("div", { class: "relbar" }, fond, h("div", { class: "ends" }, h("span", null, "can't stand you"), h("span", null, "adores you"))),
          h("h3", null, "Trust"), h("div", { class: "relbar" }, trust, h("div", { class: "ends" }, h("span", null, "jumpy around your cursor"), h("span", null, "totally relaxed"))),
          h("h3", null, "What they remember"), mem,
          h("p", { class: "hint" }, "Fans come over to say hi and bring you balls; people who can't stand you run, glare, turn their back or box your cursor. Picking them up, throwing them, poking, petting, playing ball, and what you do to their friends all count, depending on what they like. Feelings fade slowly; grudge-holders take longer."));
        let memSig = "";
        cardUpdate = f => {
          feel.textContent = f.feels; setRange(fond, f.fond); setRange(trust, f.trust);
          const sig = f.memories.map(m => m.what + m.delta).join("|");
          if (sig === memSig) return;
          memSig = sig;
          mem.replaceChildren(...(f.memories.length ? f.memories.map(m => h("li", { class: m.delta > 0.005 ? "good" : m.delta < -0.005 ? "bad" : "" },
            h("span", null, m.what), h("span", { class: "ago" }, ago(m.ago)))) : [h("li", { class: "none" }, "Nothing yet. You're a stranger.")]));
        };
      } else {
        const r = f.rels.find(r => r.id === route.rel);
        if (!r) { route.rel = "you"; drawCard(); return; }
        const mine = range(-1, 1, 0.01, r.mine, v => sendSoon("aff", { t: "fig", id, op: "affinity", other: r.id, v }));
        const theirs = h("div", { class: "meter", style: { gridTemplateColumns: "1fr 44px" } }, h("div", { class: "bar" }, h("i")), h("span", { class: "n" }));
        const word = h("div", { class: "big" }), shared = h("p"), rel = h("p", { class: "hint" });
        const love = range(0, 1, 0.01, r.love || 0, v => sendSoon("love", { t: "fig", id, op: "love", other: r.id, v }));
        const loveWord = h("p", { class: "hint" });
        const breakup = armed("Break them up", "Really?", () => send({ t: "fig", id, op: "breakup" }), "btn small danger");
        const loveBox = h("div", null, h("h3", null, "Romance"), h("div", { class: "relbar" }, love, h("div", { class: "ends" }, h("span", null, "no spark"), h("span", null, "head over heels"))), loveWord, breakup);
        card.append(h("div", { class: "hint" }, `How ${f.name} feels about ${r.name}`), word,
          h("div", { class: "relbar" }, mine, h("div", { class: "ends" }, h("span", null, "can't stand them"), h("span", null, "best friends"))),
          h("h3", null, `${r.name} feels…`), theirs, loveBox, h("h3", null, "In common"), shared, rel,
          h("button", { class: "btn small", onclick: () => go("figure", r.id) }, `Open ${r.name}'s page`));
        cardUpdate = f => {
          const r2 = f.rels.find(x => x.id === route.rel); if (!r2) return;
          word.textContent = feelWord(r2.mine); setRange(mine, r2.mine);
          const k = (r2.theirs + 1) / 2;
          $("i", theirs).style.width = (k * 100) + "%"; $("i", theirs).style.setProperty("--fill", r2.theirs > 0.15 ? "var(--good)" : r2.theirs < -0.15 ? "var(--bad)" : "var(--meh)");
          $(".n", theirs).textContent = feelWord(r2.theirs).toLowerCase();
          shared.textContent = r2.shared.length ? `They both love ${r2.shared.join(", ").toLowerCase()}. (${Math.round(Math.max(0, r2.similarity) * 100)}% taste match)` : `Not much (${Math.round(Math.max(0, r2.similarity) * 100)}% taste match).`;
          rel.textContent = (r2.rival ? `⚔ Rivals: ${r2.won}–${r2.lost}. ` : r2.won + r2.lost > 0 ? `Head to head: ${r2.won}–${r2.lost}. ` : "") + (r2.club ? "In the same club. " : "") + `Colour rule: ${relWord(r2.relation)}. Change it in Colours & fights.`;
          loveBox.hidden = !r2.attracted && !(r2.love > 0.01);
          setRange(love, r2.love || 0);
          breakup.hidden = !r2.dating;
          loveWord.textContent = r2.dating ? `Dating ♥ (${r.name} ${r2.theirLove > 0.6 ? "is just as smitten" : r2.theirLove > 0.35 ? "feels it too" : "is cooling off"})`
            : r2.love > 0.7 ? `Head over heels. Working up the nerve to say something.` : r2.love > 0.45 ? `Has a crush on ${r2.name}.` : r2.love > 0.15 ? "A little spark." : "Just friends (for now).";
        };
      }
      cardUpdate(f);
    }
    drawCard(); paintSel();
    return f => {
      centreFig.update(f.pose, f.hex, f.look, f.facing);
      for (const o of others) {
        const v = o.id === "you" ? f.fond : (f.rels.find(r => r.id === o.id) || { mine: 0 }).mine;
        const e = edges[o.id];
        e.setAttribute("stroke", v > 0.15 ? "var(--good)" : v < -0.15 ? "var(--bad)" : "var(--meh)");
        e.setAttribute("stroke-width", (1.4 + Math.abs(v) * 6).toFixed(1));
        const r = f.rels.find(r => r.id === o.id);
        e.setAttribute("stroke-dasharray", r && r.relation === "Ignore" ? "4 6" : "");
        labels[o.id].textContent = r && r.dating ? "♥ dating" : r && r.love > 0.45 ? "♥ crush" : feelWord(v).toLowerCase();
        if (nodes[o.id].dot && r) nodes[o.id].dot.setAttribute("fill", r.hex);
      }
      cardUpdate(f);
    };
  },

  moves(panel, f) {
    const id = f.id;
    const KINDS = [["walk", "Walk"], ["run", "Run"], ["idle", "Standing around"], ["climb", "Climbing"], ["jump", "Jumping"], ["fight", "Fighting"], ["celebrate", "Celebrating"], ["rope", "Grappling hook"]];
    const desc = h("p", { class: "sub", style: { maxWidth: "640px" } });
    const boxes = KINDS.map(([k, label]) => {
      const opts = INIT.styles[k].map(o => ({ value: o.v, label: o.v === 0 ? "Auto (from personality)" : o.label }));
      const sel = select(opts, f.style.choice[k], v => send({ t: "fig", id, op: "style", key: k, v: +v }));
      const now = h("span", { class: "val", style: { textAlign: "left" } });
      return { k, sel, now, el: h("div", { class: "field", style: { gridTemplateColumns: "170px 280px 1fr" } }, h("label", null, label), sel, now) };
    });
    add(panel, h("h2", { style: { marginTop: 0 } }, "How they move"), desc, boxes.map(b => b.el),
      h("div", { class: "row", style: { marginTop: "14px" } },
        h("button", { class: "btn small", onclick: () => send({ t: "fig", id, op: "quirks" }), title: "Keep the styles, re-roll the little quirks (bounce, stride, posture...)" }, "🎲 Re-roll quirks"),
        h("span", { class: "hint" }, "Auto picks a style from their personality. Quirks make two figures with the same style still move differently.")));
    return f => {
      desc.textContent = f.style.describe;
      for (const b of boxes) {
        b.sel.set(f.style.choice[b.k]);
        b.now.textContent = f.style.choice[b.k] === 0 ? `→ ${f.style.resolved[b.k].toLowerCase()}` : "";
      }
    };
  },

  diary(panel, f) {
    // Their own words, newest first, grouped by day, on lined paper.
    const page = h("div", { class: "diary" });
    const empty = h("p", { class: "hint" }, `${f.name} hasn't written anything yet. Give it time: fights, friends, games, crushes and the things you do all end up in here.`);
    add(panel, h("h2", { style: { marginTop: 0 } }, `${f.name}${f.name.endsWith("s") ? "'" : "'s"} diary`), page, empty);
    let sig = "";
    const dayName = d => {
      const today = new Date(); today.setHours(0, 0, 0, 0);
      const day = new Date(d); day.setHours(0, 0, 0, 0);
      const diff = Math.round((today - day) / 86400000);
      return diff === 0 ? "Today" : diff === 1 ? "Yesterday" : day.toLocaleDateString(undefined, { weekday: "long", day: "numeric", month: "long" });
    };
    return f => {
      const d = f.diary || [];
      const now = d.map(e => e.at + e.text).join("|");
      if (now === sig) return;
      sig = now;
      empty.hidden = d.length > 0;
      const kids = [];
      let last = "";
      for (const e of d) {
        const when = new Date(e.at);
        const dn = dayName(when);
        if (dn !== last) { kids.push(h("h3", { class: "diary-day" }, dn)); last = dn; }
        kids.push(h("div", { class: "diary-entry" },
          h("span", { class: "diary-time" }, when.toLocaleTimeString(undefined, { hour: "numeric", minute: "2-digit" })),
          h("span", { class: "diary-mood" }, e.mood || ""),
          h("span", { class: "diary-text" }, e.text)));
      }
      page.replaceChildren(...kids);
    };
  },

  mood(panel, f) {
    const id = f.id;
    const M = [["stamina", "Energy left", "#43a047"], ["joy", "Joy", "#fbc02d"], ["sadness", "Sadness", "#5c8fd6"], ["fear", "Fear", "#8e6cc9"], ["annoyance", "Annoyance", "#e53935"], ["boredom", "Boredom", "#9e9e9e"], ["loneliness", "Loneliness", "#26a69a"], ["frustration", "Frustration", "#ef6c00"], ["weight", "Weight", "#8d6e63"]];
    const rows = M.map(([k, label, col]) => {
      const i = h("i", { style: { "--fill": col } }), n = h("span", { class: "n" });
      return { k, i, n, el: h("div", { class: "meter" }, h("label", null, label), h("div", { class: "bar" }, i), n) };
    });
    const hpI = h("i", { style: { "--fill": "#e53935" } }), hpN = h("span", { class: "n" });
    const status = h("p", { class: "sub" });
    // What's on their mind: the options weighed at the last decision, and the route they're following.
    const decided = h("p", { class: "sub" });
    const route = h("p", { class: "hint" });
    const thoughts = h("div", { class: "thoughts" });
    add(panel, h("div", { class: "split" },
      h("div", null, h("h2", { style: { marginTop: 0 } }, "Right now"), status, rows.map(r => r.el),
        h("h3", null, "Health"), h("div", { class: "meter" }, h("label", null, "HP"), h("div", { class: "bar" }, hpI), hpN),
        h("button", { class: "btn small", style: { marginTop: "8px" }, onclick: () => send({ t: "fig", id, op: "heal" }) }, "🩹 Patch them up"),
        h("h3", null, "Skills"), skillBox, h("p", { class: "hint" }, "Skills grow with practice: juggling, climbing, fighting, ball games, dancing and drawing all get better the more they do them."),
        h("div", { class: "row" }, h("button", { class: "btn small", onclick: () => send({ t: "fig", id, op: "party" }) }, "🎉 Throw them a party"), bday)),
      h("div", null, h("h2", { style: { marginTop: 0 } }, "What's on their mind"), decided, thoughts, route,
        h("p", { class: "hint" }, "Each time they decide, every option gets a score from their needs, mood, likes and personality. Bars show how likely each one was. Things they've done a lot lately score lower, and places they couldn't reach are skipped for a while."))));
    const skillBox = h("div", { class: "thoughts" });
    const bday = h("span", { class: "hint" });
    let thoughtSig = "", skillSig = "";
    return f => {
      decided.textContent = f.decision ? `Last decided to: ${f.decision.toLowerCase()} (${ago(f.decidedAgo)})` : "Hasn't had to decide anything yet.";
      route.textContent = f.route ? `Route: ${f.route}` : "";
      bday.textContent = [f.birthday ? `Birthday: ${f.birthday}` : "", f.family || ""].filter(Boolean).join(" · ");
      const ss = (f.skills || []).map(x => x.name + x.v).join("|");
      if (ss !== skillSig) {
        skillSig = ss;
        skillBox.replaceChildren(...(f.skills || []).map(x => h("div", { class: "meter" }, h("label", null, x.name), h("div", { class: "bar" }, h("i", { style: { width: (x.v * 100) + "%", "--fill": "var(--good)" } })), h("span", { class: "n" }, x.v >= 0.8 ? "great" : x.v >= 0.5 ? "good" : x.v >= 0.3 ? "okay" : "learning"))));
      }
      const sig = (f.thoughts || []).map(t => t.label + t.share).join("|");
      if (sig !== thoughtSig) {
        thoughtSig = sig;
        thoughts.replaceChildren(...(f.thoughts || []).map(t => h("div", { class: "meter" + (t.label === f.decision ? " picked" : "") },
          h("label", { title: t.label }, t.label), h("div", { class: "bar" }, h("i", { style: { width: (t.share * 100) + "%", "--fill": t.label === f.decision ? "var(--accent)" : "var(--pencil)" } })),
          h("span", { class: "n" }, Math.round(t.share * 100) + "%"))));
      }
      status.textContent = `${f.activity}. ${f.feels}.`;
      for (const r of rows) { const v = f.mood[r.k]; r.i.style.width = (v * 100) + "%"; r.n.textContent = Math.round(v * 100); }
      hpI.style.width = Math.max(0, f.hp) + "%"; hpN.textContent = Math.round(Math.max(0, f.hp));
    };
  },

  look(panel, f) {
    const id = f.id;
    const sizeVal = h("span", { class: "val" });
    const size = range(0.4, 3, 0.05, f.size, v => sizeVal.textContent = Math.round(v * 100) + "%", v => send({ t: "fig", id, op: "size", v }));
    const gear = select(INIT.gear.map(g => ({ value: g.v, label: g.label })), f.gear, v => send({ t: "fig", id, op: "gear", v: +v }));
    const LP = INIT.lookParts;
    const CLOTH = ["#E53935", "#1E88E5", "#43A047", "#FB8C00", "#8E24AA", "#FDD835", "#00ACC1", "#EC407A", "#2E2E2E", "#F4F4F4", "#6D4C41", "#283593"];
    const HAIR = ["#2B1D16", "#3B2A20", "#6D4C2F", "#A86B32", "#D9B262", "#E8D9A8", "#B33A1F", "#9E9E9E", "#F2F2F2", "#5C6BC0", "#EC407A", "#43A047"];
    const body = slot => LP.body.filter(b => b.slot === slot);
    const SLOTS = [
      ["hat", "Hat", LP.hat, "hatColour", CLOTH], ["hair", "Hair", LP.hair, "hairColour", HAIR], ["beard", "Facial hair", LP.beard, null, null],
      ["glasses", "Glasses", LP.glasses, null, null], ["top", "Top", body("top"), "topColour", CLOTH], ["neck", "Neck", body("neck"), "neckColour", CLOTH],
      ["waist", "Waist", body("waist"), "waistColour", CLOTH], ["back", "Back", body("back"), "backColour", CLOTH], ["shoes", "Shoes", LP.shoes, "shoeColour", CLOTH],
    ];
    const rows = SLOTS.map(([slot, label, parts, colKey, palette]) => {
      const sel = select([{ value: "", label: "None" }, ...parts.map(p => ({ value: p.key, label: p.name }))], f.look[slot] || "", v => send({ t: "fig", id, op: "look", slot, v }));
      let sw = null;
      if (colKey) {
        let cur = f.look[colKey];
        const btns = palette.map(c => h("button", { class: "sw mini", title: c, style: { background: c }, onclick: () => { cur = c; touched(wrap); send({ t: "fig", id, op: "look", slot: colKey, v: c }); paint(); } }));
        const wrap = h("div", { class: "swatches mini" }, btns);
        const paint = () => btns.forEach((b, i) => b.classList.toggle("on", palette[i].toLowerCase() === (cur || "").toLowerCase()));
        wrap.set = v => { if (idle(wrap)) { cur = v; paint(); } };
        paint();
        sw = wrap;
      }
      return { slot, colKey, sel, sw, el: h("div", { class: "field look-row" }, h("label", null, label), sel, sw || h("span")) };
    });
    add(panel, h("div", { class: "row" }, h("h2", { style: { margin: "0" } }, "Look"), h("span", { class: "spacer" }),
        h("button", { class: "btn small", onclick: () => send({ t: "fig", id, op: "rollLook" }) }, "🎲 New look"),
        h("button", { class: "btn small", onclick: () => send({ t: "fig", id, op: "plainLook" }) }, "Plain")),
      h("p", { class: "sub" }, "Every figure gets an outfit that suits them. Mix and match: capes and ponytails swing as they move."),
      rows.map(r => r.el),
      h("h3", null, "Body"),
      h("div", { class: "field" }, h("label", null, "Size"), size, sizeVal),
      h("div", { class: "field" }, h("label", null, "On their hands"), gear, h("span")),
      h("p", { class: "hint" }, "Their main colour is up top, next to their name."));
    return f => {
      setRange(size, f.size); if (idle(size)) sizeVal.textContent = Math.round(f.size * 100) + "%"; gear.set(f.gear);
      for (const r of rows) {
        r.sel.set(f.look[r.slot] || "");
        if (r.sw) { r.sw.set(f.look[r.colKey]); r.sw.style.visibility = f.look[r.slot] || (r.slot === "hair" && f.look.beard) ? "visible" : "hidden"; }
      }
    };
  },
};
function figMini() {
  const g = s("g", { transform: "translate(0 25) scale(0.74)" });
  const svg = figSvg();
  // Reuse the figure drawing's parts inside the node.
  for (const c of [...svg.childNodes]) if (!c.classList || !c.classList.contains("ground")) g.append(c);
  g.update = svg.update;
  return g;
}

function faceSvg(b) {
  const mouth = { "-2": "M8 21 Q14 15 20 21", "-1": "M9 20 Q14 17.5 19 20", "0": "M9 19 L19 19", "1": "M9 18 Q14 21.5 19 18", "2": "M8 17 Q14 24 20 17" }[b];
  const brows = b === -2 ? s("path", { d: "M8 9 L12 11 M20 9 L16 11" }) : null;
  const eyes = b === 2 ? s("path", { d: "M9.5 12 q1.5 -2 3 0 M15.5 12 q1.5 -2 3 0" }) : s("path", { d: "M11 12 v1 M17 12 v1" });
  return s("svg", { viewBox: "0 0 28 28" }, s("circle", { cx: 14, cy: 14, r: 11.5 }), eyes, s("path", { d: mouth }), brows);
}

function swatchRow(pick) {
  let cur = "";
  const btns = INIT.palette.map(p => h("button", { class: "sw", title: p.name, style: { background: p.hex }, onclick: () => { touched(wrap); cur = p.name; pick(p.name); paint(); } }));
  const wrap = h("div", { class: "swatches" }, btns);
  function paint() { btns.forEach((b, i) => b.classList.toggle("on", INIT.palette[i].name === cur)); }
  wrap.set = v => { if (idle(wrap)) { cur = v; paint(); } };
  return wrap;
}

function ago(sec) {
  return sec < 45 ? "just now" : sec < 3600 ? `${Math.round(sec / 60)} min ago` : `${Math.round(sec / 3600)} h ago`;
}

function feelWord(v) {
  return v > 0.75 ? "Best friends" : v > 0.4 ? "Friends" : v > 0.15 ? "Friendly" : v > -0.15 ? "Neutral" : v > -0.5 ? "Don't get along" : "Can't stand";
}
function relWord(r) { const x = INIT.relations.find(x => x.key === r); return x ? x.label.toLowerCase() : r; }

// ---------------- Library ----------------

PAGES.library = {
  sig: () => S.library.map(l => l.name).join("|"),
  build(root) {
    add(root, h("h1", null, "Saved figures"),
      h("p", { class: "sub" }, S.library.length ? "Your own characters. Spawn them back any time, as many as you like." : "Nothing saved yet. Open a figure and press “★ Save to library”."));
    const grid = h("div", { class: "grid" });
    for (const l of S.library) {
      const svg = figSvg(); svg.update(STANDING, l.hex);
      grid.append(h("div", { class: "card", style: { cursor: "default" } }, h("div", { class: "tape" }),
        h("div", { class: "name" }, h("span", { class: "dot", style: { background: l.hex } }), l.name), svg,
        h("div", { class: "act" }, l.describe), l.likes ? h("div", { class: "likes" }, l.likes) : null,
        h("div", { class: "row", style: { marginTop: "8px" } },
          h("button", { class: "btn small primary", onclick: () => { send({ t: "lib", op: "spawn", name: l.name }); toast(`${l.name} is on the way`); } }, "Spawn"),
          armed("Delete", "Sure?", () => send({ t: "lib", op: "delete", name: l.name }), "btn small danger"))));
    }
    add(root, grid);
  },
};


// ---------------- Pets ----------------

const NEED_NAMES = [["food", "Food"], ["water", "Water"], ["bathroom", "Bathroom"], ["energy", "Energy"], ["love", "Attention"], ["fun", "Fun"], ["calm", "Calm"], ["comfort", "Comfort"]];
const meterRow = (label, v, text, fill) => h("div", { class: "meter" }, h("label", null, label), h("div", { class: "bar" }, h("i", { style: { width: (Math.max(0, Math.min(1, v)) * 100) + "%", "--fill": fill || (v > 0.6 ? "var(--good)" : v > 0.3 ? "var(--meh)" : "var(--bad)") } })), h("span", { class: "n" }, text));
const petGlyph = k => ({ Cat: "🐈", Dog: "🐕", Parrot: "🦜", Rabbit: "🐇", Hamster: "🐹" })[k] || "🐾";

function petActions(p, close) {
  const act = (label, op, primary) => h("button", { class: "btn small" + (primary ? " primary" : ""), onclick: () => { send({ t: "pet", op, id: p.id }); if (close && (op === "spray")) close(); } }, label);
  return [act("🍖 Treat", "treat", true), p.kind !== "Parrot" ? act("Sit", "sit") : null, act("Come", "come"),
    p.kind === "Parrot" ? act(p.onCursor ? "Step down" : "Step up", "stepup") : act(p.leashed ? "Unleash" : "🦮 Leash", "leash"),
    act("💦 Spray bottle", "spray"), act(p.sick ? "🩺 Vet (sick!)" : "🩺 Vet", "vet"), act("🛁 Bath", "bath"), act("🪮 Brush", "brush")];
}

PAGES.pets = {
  sig: () => (S.pets || []).map(p => p.id).join(","),
  build(root) {
    add(root, h("h1", null, "Pets"),
      h("p", { class: "sub" }, "Real pets need real care: food and water, the litter box (or walks, for dogs), sleep, play and your attention. Click a bowl to fill it, a litter box to scoop it, a mess to clean it up. Kittens, puppies and chicks grow up over a few hours."),
      h("h2", null, "Adopt"),
      h("div", { class: "row" }, [["Cat", false, "🐈 Cat"], ["Cat", true, "Kitten"], ["Dog", false, "🐕 Dog"], ["Dog", true, "Puppy"], ["Parrot", false, "🦜 Parrot"], ["Parrot", true, "Chick"], ["Rabbit", false, "🐇 Rabbit"], ["Rabbit", true, "Bunny"], ["Hamster", false, "🐹 Hamster"]].map(([k, y, l]) =>
        h("button", { class: "chip", onclick: () => send({ t: "adopt", kind: k, young: y }) }, l))),
      h("h2", null, "Supplies"),
      h("div", { class: "row" }, [["foodbowl", "Food bowl"], ["waterbowl", "Water bowl"], ["litterbox", "Litter box"], ["peepad", "Pee pad"], ["petbed", "Pet bed"], ["perch", "Bird perch"], ["hamsterwheel", "Hamster wheel"], ["scratchpost", "Scratching post"], ["chewtoy", "Chew toy"], ["yarn", "Yarn"]].map(([k, l]) =>
        h("button", { class: "chip", onclick: () => send({ t: "supply", key: k }) }, l))),
      h("p", { class: "hint" }, "Training works like it does with real animals: spray within a couple of seconds of a misdeed (chasing the bird, scratching the couch, a scrap) and it slowly sinks in; spray late or for nothing and they just get upset. Treats right after something good (the litter box, coming when called) reinforce it. Parrots love being sprayed: it's a bath."));
    const list = h("div");
    add(root, list);
    const ups = [];
    if (!(S.pets || []).length) list.append(h("p", { class: "sub" }, "No pets yet. Adopt one above, or type \"a kitten\" in Things."));
    for (const p0 of S.pets || []) {
      const id = p0.id;
      const title = h("div", { class: "name" }), sub = h("div", { class: "act" }), needs = h("div", { class: "thoughts" }), train = h("div", { class: "thoughts" }), extra = h("div", { class: "hint" }), log = h("ul", { class: "memories" });
      const acts = h("div", { class: "row tight" });
      list.append(h("div", { class: "card wide", style: { cursor: "default", marginBottom: "14px" } }, h("div", { class: "tape" }), title, sub,
        h("div", { class: "split" }, h("div", null, h("h3", null, "Needs"), needs), h("div", null, h("h3", null, "Training"), train)), extra, acts, h("h3", null, "Lately"), log));
      let actSig = "";
      ups.push(() => {
        const p = (S.pets || []).find(x => x.id === id); if (!p) return;
        title.replaceChildren(h("span", { class: "dot", style: { background: p.hex } }), `${petGlyph(p.kind)} ${p.name}`, h("span", { class: "hint" }, `  ${p.female ? "♀" : "♂"} ${p.temper} ${p.species}${p.young ? ` · ${Math.round(p.age * 100)}% grown` : ""} · ${p.weightWord}${p.expecting ? " · expecting!" : ""}${p.mother ? ` · ${p.mother}'s` : ""}`));
        sub.textContent = `${p.activity}. ${p.mood}.${p.owner ? ` ${p.owner}'s favourite.` : ""}`;
        needs.replaceChildren(...NEED_NAMES.map(([k, l]) => meterRow(l, p.needs[k], Math.round(p.needs[k] * 100) + "%")),
          meterRow("Stamina", p.stamina, Math.round(p.stamina * 100) + "%"), meterRow("Health", p.health, p.sick ? p.sick : Math.round(p.health * 100) + "%", p.sick ? "var(--bad)" : null), meterRow("Clean", p.clean, Math.round(p.clean * 100) + "%"), meterRow("Loves you", (p.bond + 1) / 2, p.bond > 0.6 ? "adores you" : p.bond > 0.25 ? "likes you" : p.bond > -0.1 ? "warming up" : "wary"));
        train.replaceChildren(...p.habits.map(x => meterRow(x.name, x.v, Math.round(x.v * 100) + "%", "var(--accent)")), ...p.skills.map(x => meterRow(x.name, x.v, Math.round(x.v * 100) + "%", "var(--good)")));
        extra.textContent = [p.friends.length ? "Gets on with: " + p.friends.map(f => `${f.name} (${f.v > 0.45 ? "friends" : f.v > 0 ? "okay" : f.v > -0.4 ? "wary" : "enemies"})`).join(", ") : "", p.words ? `Says: ${p.words.map(w => "“" + w + "”").join(" ")}` : "", `Sprayed ${p.sprays}× · ${p.treats} treats · born ${p.born}`].filter(Boolean).join("  ·  ");
        const sig = [p.leashed, p.onCursor, p.wear].join();
        if (sig !== actSig)
        {
          actSig = sig;
          const wear = h("select", { class: "text", onchange: () => send({ t: "pet", op: "wear", id, v: wear.value }) }, ["", "bandana", "sweater", "bow", "raincoat"].map(o => h("option", { value: o }, o || "No clothes")));
          wear.value = p.wear || "";
          const wcol = h("input", { type: "color", value: p.wearColour || "#e53935", onchange: () => send({ t: "pet", op: "wearColour", id, v: wcol.value }) });
          acts.replaceChildren(...petActions(p).filter(Boolean),
            ...(p.tricks || []).map(tr => h("button", { class: "btn small", onclick: () => send({ t: "pet", op: "trick", id, v: tr }) }, "✨ " + tr)),
            wear, wcol, armed("Rehome", "Sure?", () => send({ t: "pet", op: "rehome", id }), "btn small danger"));
        }
        log.replaceChildren(...(p.log.length ? p.log.map(l => h("li", null, h("span", null, l.text), h("span", { class: "ago" }, ago(l.ago)))) : [h("li", { class: "none" }, "Nothing yet.")]));
      });
    }
    return () => ups.forEach(u => u());
  },
};


// ---------------- The Stick Times ----------------

PAGES.paper = {
  sig: () => S.paper ? JSON.stringify([S.paper.lead, S.paper.sections.map(s => s.items.length)]) : "",
  build(root) {
    send({ t: "sticker", key: "paper" });
    const p = S.paper;
    if (!p) { add(root, h("p", { class: "sub" }, "The presses are warming up…")); return; }
    const paper = h("div", { class: "paper" });
    add(root, paper);
    paper.append(
      h("div", { class: "masthead" }, h("div", { class: "mast-title" }, "The Stick Times"), h("div", { class: "mast-line" }, h("span", null, p.date), h("span", null, `Edition ${p.edition}`), h("span", null, `Weather: ${p.weather}`))),
      p.lead ? h("div", { class: "lead" }, h("div", { class: "lead-kicker" }, "This week"), h("h1", { class: "headline" }, p.lead.text)) : h("div", { class: "lead" }, h("h1", { class: "headline" }, "A quiet week on the desktop"), h("p", { class: "sub" }, "Nothing much happened. Give it time (or throw a party).")),
      h("div", { class: "columns" },
        h("div", { class: "col-main" }, ...p.sections.map(s => h("section", { class: "desk" }, h("h2", null, s.name), h("ul", { class: "stories" }, s.items.map(i => h("li", null, h("span", null, i.text), h("span", { class: "ago" }, i.when))))))),
        h("aside", { class: "col-side" }, ...p.features.map(f => h("div", { class: "box" }, h("h3", null, f.title), h("p", null, f.text))),
          h("div", { class: "box" }, h("h3", null, "By the numbers"), h("ul", { class: "nums" }, p.numbers.map(n => h("li", null, n)))))));
  },
};

// ---------------- Sticker book ----------------

PAGES.stickers = {
  sig: () => (S.stickers || []).filter(s => s.got).length + "",
  build(root) {
    const all = S.stickers || [], got = all.filter(s => s.got).length;
    add(root, h("h1", null, "Sticker book"), h("p", { class: "sub" }, `${got} of ${all.length} collected. Stickers come from things you do, and things you see happen.`));
    const grid = h("div", { class: "stickers" });
    for (const s of all)
      grid.append(h("div", { class: "sticker" + (s.got ? " got" : "") }, h("div", { class: "art" }, s.got ? s.art : "?"), h("div", { class: "st-title" }, s.got ? s.title : "???"), h("div", { class: "hint" }, s.got ? `Got it ${s.got}` : s.hint)));
    add(root, grid);
  },
};

// ---------------- Colours & fights ----------------

const REL_GLYPH = { Default: "·", Friends: "♥", Neutral: "–", Rivals: "⚔", Enemies: "☠", Ignore: "∅" };

PAGES.fights = {
  build(root) {
    const F = () => S.fight;
    const set = (key, v) => send({ t: "fight", key, v });
    const checks = [
      check("Fights happen", "Turn off for a peaceful desktop.", () => F().enabled, v => set("enabled", v)),
      check("They can punch your cursor", "Grumpy figures may box with it, knocking it across the screen.", () => F().punchCursor, v => set("punchCursor", v)),
      check("Show health bars", "Only while they're hurt.", () => F().healthBars, v => set("healthBars", v)),
    ];
    const fv = h("span", { class: "val" }), sv = h("span", { class: "val" }), rv = h("span", { class: "val" });
    const freq = range(0, 2, 0.05, F().frequency, v => { fv.textContent = v.toFixed(2) + "×"; sendSoon("freq", { t: "fight", key: "frequency", v }); });
    const str = range(0.25, 3, 0.05, F().strength, v => { sv.textContent = v.toFixed(2) + "×"; sendSoon("str", { t: "fight", key: "strength", v }); });
    const rev = range(3, 120, 1, F().reviveSeconds, v => { rv.textContent = v + "s"; sendSoon("rev", { t: "fight", key: "reviveSeconds", v }); });
    const death = select(INIT.deathRules.map(d => ({ value: d.key, label: d.label })), F().onZeroHealth, v => set("onZeroHealth", v));
    const relOpts = INIT.relations.filter(r => r.key !== "Default").map(r => ({ value: r.key, label: r.label }));
    const same = select(relOpts, F().sameColour, v => set("sameColour", v));
    const diff = select(relOpts, F().differentColour, v => set("differentColour", v));
    add(root, h("h1", null, "Colours & fights"), h("p", { class: "sub" }, "Colour decides who's friends, who's rivals and who's at war. Fights are live: every punch is decided in the moment."),
      h("div", { class: "split" }, h("div", null, checks,
        h("h2", null, "How rough"),
        h("div", { class: "field" }, h("label", null, "How often"), freq, fv),
        h("div", { class: "field" }, h("label", null, "Hit strength"), str, sv),
        h("div", { class: "field" }, h("label", null, "At zero health"), death, h("span")),
        h("div", { class: "field" }, h("label", null, "Back up after"), rev, rv)),
      h("div", null,
        h("h2", { style: { marginTop: 0 } }, "Who gets along"),
        h("div", { class: "field", style: { gridTemplateColumns: "150px 1fr" } }, h("label", null, "Same colour"), same),
        h("div", { class: "field", style: { gridTemplateColumns: "150px 1fr" } }, h("label", null, "Different colours"), diff),
        h("h3", null, "Exceptions (click a square to change it)"), matrix(),
        h("div", { class: "legend" }, Object.entries(REL_GLYPH).map(([k, g]) => h("span", null, `${g} ${k === "Default" ? "default" : k.toLowerCase()}`))))));
    const cells = [...root.querySelectorAll("[data-pair]")];
    return () => {
      checks.forEach(c => c.update());
      setRange(freq, F().frequency); setRange(str, F().strength); setRange(rev, F().reviveSeconds);
      if (idle(freq)) fv.textContent = F().frequency.toFixed(2) + "×";
      if (idle(str)) sv.textContent = F().strength.toFixed(2) + "×";
      if (idle(rev)) rv.textContent = Math.round(F().reviveSeconds) + "s";
      death.set(F().onZeroHealth); same.set(F().sameColour); diff.set(F().differentColour);
      for (const c of cells) {
        const r = F().pairs[c.dataset.pair];
        c.textContent = REL_GLYPH[r || "Default"];
        c.classList.toggle("def", !r);
        c.title = `${c.dataset.pair.replace("|", " & ")}: ${r ? relWord(r) : "default"}`;
      }
    };
  },
};

function matrix() {
  const names = INIT.palette.map(p => p.name);
  const order = ["Default", "Friends", "Neutral", "Rivals", "Enemies", "Ignore"];
  const key = (a, b) => (a < b ? `${a}|${b}` : `${b}|${a}`);
  const t = h("table", { class: "matrix" });
  t.append(h("tr", null, h("th"), INIT.palette.map(p => h("th", null, h("span", { class: "cdot", style: { background: p.hex }, title: p.name })))));
  INIT.palette.forEach((p, i) => {
    t.append(h("tr", null, h("th", null, h("span", { class: "cdot", style: { background: p.hex }, title: p.name })),
      names.map((n, j) => j < i ? h("td") : h("td", null, h("button", { "data-pair": key(p.name, n), onclick: e => {
        const k = e.currentTarget.dataset.pair, cur = S.fight.pairs[k] || "Default";
        const next = order[(order.indexOf(cur) + 1) % order.length];
        const [a, b] = k.split("|");
        send({ t: "fight", key: "pair", a, b, rel: next, v: 0 });
      } })))));
  });
  return t;
}

// ---------------- Things (objects + balls) ----------------

const VERB_WORDS = { Sit: "sit on it", Lie: "nap on it", Hammock: "swing in it", Bounce: "bounce on it", Stand: "climb on it",
  Eat: "eat it", Hide: "hide in it", Dance: "dance to it", Read: "read it", Warm: "warm up by it", Wield: "swing it", Shoot: "shoot it", Play: "play games with it",
  Ride: "ride it", Swim: "swim in it", Fish: "fish in it", Tend: "water it", Collect: "collect it", Lasso: "twirl it", Shelter: "stay dry under it", Create: "draw with it" };
const GROUPS = [["Out & about", ["Ride", "Swim", "Fish"]], ["Seats", ["Sit"]], ["Beds", ["Lie", "Hammock"]], ["Play & music", ["Bounce", "Dance", "Read"]], ["Food", ["Eat"]], ["Hide, climb & gather", ["Hide", "Stand", "Warm"]], ["Weapons", ["Wield", "Shoot"]], ["Sports", ["Play"]]];

function lighten(hex, k = 0.35) {
  const n = parseInt(hex.slice(1), 16);
  const mix = v => Math.round(v + (255 - v) * k);
  return `rgb(${mix(n >> 16 & 255)},${mix(n >> 8 & 255)},${mix(n & 255)})`;
}

/** Draw an object from the same shape data the desktop uses. */
function itemSvg(def, hex, cls = "doodle") {
  const fixed = INIT.fixedColours;
  const col = c => c === 0 ? hex : c === 1 ? shade(hex, 0.72) : c === 2 ? lighten(hex) : fixed[c] || "#999";
  const pad = 5;
  const svg = s("svg", { class: cls, viewBox: `${-def.w / 2 - pad} ${-def.h - pad - 4} ${def.w + pad * 2} ${def.h + pad * 2 + 4}` });
  const ink = { stroke: "var(--ink)", "stroke-width": 1.1, "stroke-linejoin": "round" };
  const draw = sh => {
    const p = sh.p, c = col(sh.c);
    switch (sh.k) {
      case "r": return s("rect", { x: Math.min(p[0], p[2]), y: -Math.max(p[1], p[3]), width: Math.abs(p[2] - p[0]), height: Math.abs(p[3] - p[1]), fill: c, ...ink });
      case "o": return s("rect", { x: p[0], y: -p[3], width: p[2] - p[0], height: p[3] - p[1], rx: p[4], fill: c, ...ink });
      case "e": return s("ellipse", { cx: p[0], cy: -p[1], rx: p[2], ry: p[3], fill: c, ...ink });
      case "p": { const pts = []; for (let i = 0; i + 1 < p.length; i += 2) pts.push(`${p[i]},${-p[i + 1]}`); return s("polygon", { points: pts.join(" "), fill: c, ...ink }); }
      case "l": return s("line", { x1: p[0], y1: -p[1], x2: p[2], y2: -p[3], stroke: c, "stroke-width": sh.w, "stroke-linecap": "round" });
      case "c": { const pts = []; for (let i = 0; i + 1 < p.length; i += 2) pts.push(`${p[i]},${-p[i + 1]}`); return s("polyline", { points: pts.join(" "), fill: "none", stroke: c, "stroke-width": sh.w, "stroke-linecap": "round" }); }
    }
    return null;
  };
  for (const over of [false, true]) for (const sh of def.shapes) if (sh.over === over && !sh.used) { const e = draw(sh); if (e) svg.append(e); }
  // Live-drawn parts.
  if (def.verbs.includes("Hammock")) svg.append(s("path", { d: "M-44 -42 Q0 -2 44 -42 L44 -40 Q0 4 -44 -40 Z", fill: hex, ...ink }));
  if (def.verbs.includes("Warm")) for (const [x, h] of [[-5, 13], [0, 17], [5, 12]]) svg.append(s("polygon", { points: `${x - 4.5},-3 ${x},${-h} ${x + 4.5},-3`, fill: fixed[19] }), s("polygon", { points: `${x - 2},-3 ${x},${-h * 0.6} ${x + 2},-3`, fill: fixed[20] }));
  const WHEELS = { bike: [[-13, 6, 6], [13, 6, 6]], skateboard: [[-7.5, 1.2, 1.4], [7.5, 1.2, 1.4]], gokart: [[-13, 4.2, 4.2], [13, 4.2, 4.2]] };
  for (const [x, y, r] of WHEELS[def.key] || []) svg.append(s("circle", { cx: x, cy: -y, r, fill: def.key === "bike" ? "none" : "#222", stroke: "#222", "stroke-width": def.key === "bike" ? 1.6 : 1 }));
  if (def.verbs.includes("Dance")) svg.append(s("text", { x: 4, y: -20, "font-size": 9, fill: "var(--ink)" }, "♪"));
  return svg;
}

function catalogDef(key) { return INIT.catalog.find(c => c.key === key); }

/** "Draw something…" box with suggestions. */
function summonBox(compact = false) {
  const input = h("input", { type: "text", class: "summon-input", placeholder: compact ? "Draw something… (try “pizza”)" : "Draw something… a comfy couch, a giant trampoline, pizza", spellcheck: "false", maxlength: 60 });
  const list = h("div", { class: "summon-list" });
  const go = text => { text = (text || input.value).trim(); if (!text) return; send({ t: "summon", text }); input.value = ""; list.replaceChildren(); list.classList.remove("open"); };
  let hover = -1, matches = [];
  const refresh = () => {
    const q = input.value.trim().toLowerCase().replace(/^(an?|the)\s+/, "");
    const word = q.split(/\s+/).pop() || "";
    matches = word.length < 1 ? [] : INIT.catalog.filter(c => c.name.toLowerCase().includes(word) || c.words.some(w => w.includes(word))).slice(0, compact ? 4 : 6);
    hover = -1;
    list.replaceChildren(...matches.map((c, i) => h("div", { class: "summon-item", onpointerdown: e => { e.preventDefault(); const pre = q.split(/\s+/).slice(0, -1).join(" "); go((pre ? pre + " " : "") + c.words[0]); } },
      itemSvg(c, c.hex, "doodle tiny"), h("span", null, c.name), h("small", null, c.verbs.map(v => VERB_WORDS[v]).join(" · ")))));
    list.classList.toggle("open", matches.length > 0);
  };
  input.addEventListener("input", refresh);
  input.addEventListener("keydown", e => {
    if (e.key === "Enter") { e.preventDefault(); if (hover >= 0 && matches[hover]) { list.children[hover].dispatchEvent(new PointerEvent("pointerdown")); } else go(); }
    else if (e.key === "ArrowDown" && matches.length) { e.preventDefault(); hover = (hover + 1) % matches.length; [...list.children].forEach((c, i) => c.classList.toggle("hover", i === hover)); }
    else if (e.key === "ArrowUp" && matches.length) { e.preventDefault(); hover = (hover - 1 + matches.length) % matches.length; [...list.children].forEach((c, i) => c.classList.toggle("hover", i === hover)); }
    else if (e.key === "Escape") { list.classList.remove("open"); }
  });
  input.addEventListener("blur", () => setTimeout(() => list.classList.remove("open"), 150));
  const pencil = s("svg", { viewBox: "0 0 24 24", class: "pencil" }, s("path", { d: "M4 20 L5.5 14.5 L16 4 L20 8 L9.5 18.5 Z M14 6 L18 10 M4 20 L9.5 18.5" }));
  return h("div", { class: "summon" + (compact ? " compact" : "") }, pencil, input,
    h("button", { class: "btn small primary", onclick: () => go() }, "Draw it"), list);
}

PAGES.toys = {
  sig: () => S.items.map(i => i.id).join(",") + "|" + S.props.map(p => p.id).join(","),
  build(root) {
    add(root, h("h1", null, "Things"),
      h("p", { class: "sub" }, "Type something and it appears on your desktop. They know what to do with it: sit, nap, bounce, eat, hide, dance, read, gather round. Drag things around with the mouse."),
      summonBox(),
      h("div", { class: "row", style: { margin: "10px 0 4px" } }, ["a comfy couch", "trampoline", "pizza", "hammock", "campfire", "a giant beanbag", "a watching chair", "box"].map(t =>
        h("button", { class: "chip", onclick: () => send({ t: "summon", text: t }) }, t))));

    add(root, h("h2", null, "On your desktop"));
    const list = h("div");
    const rows = [];
    for (const it of S.items) {
      const def = catalogDef(it.key); if (!def) continue;
      const sv = h("span", { class: "val" });
      const size = range(0.3, 3.5, 0.05, it.size, v => { sv.textContent = Math.round(v * 100) + "%"; sendSoon("is" + it.id, { t: "item", op: "size", id: it.id, v }); });
      const colour = h("input", { type: "color", value: it.hex.toLowerCase() });
      colour.addEventListener("input", () => { touched(colour); sendSoon("ic" + it.id, { t: "item", op: "color", id: it.id, hex: colour.value }); });
      const users = h("span", { class: "hint" });
      const pic = h("div", { class: "toy-pic" }, itemSvg(def, it.hex));
      add(list, h("div", { class: "toy" + (route.toy === it.id ? " sk" : "") }, pic,
        h("div", null, h("div", { style: { font: "18px var(--hand)" } }, it.name, " ", users),
          h("div", { class: "hint" }, "They'll " + def.verbs.map(v => VERB_WORDS[v]).join(", ")),
          h("div", { class: "ctl" }, h("label", null, "Size"), size, sv, h("label", null, "Colour"), colour, h("button", { class: "btn small", onclick: () => send({ t: "item", op: "flip", id: it.id }) }, "Flip"))),
        armed("Remove", "Sure?", () => send({ t: "item", op: "remove", id: it.id }), "btn small danger")));
      rows.push({ id: it.id, size, sv, users, pic, def, colour });
    }
    for (const p of S.props) {
      const sv = h("span", { class: "val" }), bv = h("span", { class: "val" });
      const size = range(0.3, 5, 0.05, p.size, v => { sv.textContent = Math.round(v * 100) + "%"; sendSoon("ps" + p.id, { t: "prop", op: "size", id: p.id, v }); });
      const bounce = range(0, 0.95, 0.01, p.bounce, v => { bv.textContent = Math.round(v * 100) + "%"; sendSoon("pb" + p.id, { t: "prop", op: "bounce", id: p.id, v }); });
      add(list, h("div", { class: "toy" + (route.toy === p.id ? " sk" : "") }, ballSvg(p.kind, p.hex),
        h("div", null, h("div", { style: { font: "18px var(--hand)" } }, p.name, p.held ? h("span", { class: "hint" }, ` (${p.held} has it)`) : null),
          h("div", { class: "ctl" }, h("label", null, "Size"), size, sv, h("label", null, "Bounce"), bounce, bv)),
        armed("Remove", "Sure?", () => send({ t: "prop", op: "remove", id: p.id }), "btn small danger")));
    }
    if (!S.items.length && !S.props.length) add(list, h("div", { class: "empty" }, "Nothing out right now. Draw something!"));
    add(root, list,
      h("div", { class: "row", style: { marginTop: "6px" } },
        INIT.propKinds.map(k => h("button", { class: "btn small", onclick: () => send({ t: "prop", op: "add", kind: k.key }) }, `+ ${k.name}`)),
        S.items.length ? armed("Clear all things", "Sure?", () => send({ t: "clear", what: "items" }), "btn small danger") : null,
        S.props.length ? armed("Clear all balls", "Sure?", () => send({ t: "clear", what: "balls" }), "btn small danger") : null));

    add(root, h("h2", null, "Everything they know how to use"));
    const TOWN = ["shopstall", "foodcart", "stage", "schoolboard", "fort", "treehouse", "lamp", "fairylights", "lantern", "finishflag"];
    const HIDDEN = ["buildsite", "dropping", "poop", "puddle", "seedpatch"];
    const inGroup = c => GROUPS.some(g => c.verbs.some(v => g[1].includes(v)));
    const sections = [["Town & lights", INIT.catalog.filter(c => TOWN.includes(c.key))],
      ...GROUPS.map(([title, verbs]) => [title, INIT.catalog.filter(c => !TOWN.includes(c.key) && c.verbs.some(v => verbs.includes(v)) && !GROUPS.slice(0, GROUPS.findIndex(g => g[0] === title)).some(g => c.verbs.some(v => g[1].includes(v))))]),
      ["Everything else", INIT.catalog.filter(c => !TOWN.includes(c.key) && !HIDDEN.includes(c.key) && !inGroup(c))]];
    for (const [title, defs] of sections) {
      if (!defs.length) continue;
      add(root, h("h3", null, title), h("div", { class: "catalog" }, defs.map(c =>
        h("button", { class: "cat-card", title: `${c.name}${c.verbs.some(v => VERB_WORDS[v]) ? `: they'll ${c.verbs.map(v => VERB_WORDS[v]).filter(Boolean).join(", ")}` : ""}. Also: ${c.words.join(", ")}`, onclick: () => { send({ t: "item", op: "add", key: c.key }); toast(`${c.name}!`); } },
          itemSvg(c, c.hex), h("span", null, c.name)))));
    }
    return () => {
      for (const r of rows) {
        const it = S.items.find(x => x.id === r.id); if (!it) continue;
        setRange(r.size, it.size); if (idle(r.size)) r.sv.textContent = Math.round(it.size * 100) + "%";
        r.users.textContent = it.users.length ? `(${it.users.join(", ")} using it)` : "";
        if (idle(r.colour) && r.colour.value !== it.hex.toLowerCase()) { r.colour.value = it.hex.toLowerCase(); r.pic.replaceChildren(itemSvg(r.def, it.hex)); }
      }
    };
  },
};
function ballSvg(kind, hex) {
  const g = s("svg", { viewBox: "-32 -32 64 64" });
  const outline = { fill: "none", stroke: "var(--ink)", "stroke-width": 2.2, filter: "var(--stroke-filter)" };
  if (kind === "SoccerBall") {
    g.append(s("circle", { r: 26, fill: "#fff" }), s("path", { d: "M0 -9 L8.6 -2.8 L5.3 7.3 L-5.3 7.3 L-8.6 -2.8 Z", fill: "#222" }),
      s("path", { d: "M0 -9 L0 -26 M8.6 -2.8 L24 -8 M5.3 7.3 L15 21 M-5.3 7.3 L-15 21 M-8.6 -2.8 L-24 -8", stroke: "#222", "stroke-width": 2, fill: "none" }));
  } else if (kind === "Basketball") {
    g.append(s("circle", { r: 26, fill: "#e8742c" }), s("path", { d: "M-26 0 H26 M0 -26 V26 M-18 -18 Q-6 0 -18 18 M18 -18 Q6 0 18 18", stroke: "#3a2214", "stroke-width": 2, fill: "none" }));
  } else if (kind === "BeachBall") {
    const cols = [hex, "#fff", "#fdd835", "#fff", "#1e88e5", "#fff"];
    cols.forEach((c, i) => {
      const a0 = i / 6 * Math.PI * 2, a1 = (i + 1) / 6 * Math.PI * 2;
      g.append(s("path", { d: `M0 0 L${Math.cos(a0) * 26} ${Math.sin(a0) * 26} A26 26 0 0 1 ${Math.cos(a1) * 26} ${Math.sin(a1) * 26} Z`, fill: c }));
    });
  } else g.append(s("circle", { r: 26, fill: hex }), s("path", { d: "M-12 -14 Q-6 -19 2 -18", stroke: "#fff8", "stroke-width": 4, fill: "none", "stroke-linecap": "round" }));
  g.append(s("circle", { r: 26, ...outline }));
  return g;
}

// ---------------- Settings ----------------

PAGES.settings = {
  build(root) {
    const st = () => S.settings;
    const setS = (key, v) => send({ t: "setting", key, v });
    const chips = INIT.fps.map(o => {
      const c = h("button", { class: "chip", onclick: () => { touched(c); setS("fps", o.value); } }, o.value > 0 ? `${o.label} fps` : o.label);
      c.fps = o.value;
      return c;
    });
    const cv = h("span", { class: "val" });
    const custom = range(15, 360, 1, st().fps > 0 ? st().fps : 60, v => { cv.textContent = v; sendSoon("fps", { t: "setting", key: "fps", v }); });
    const themes = [["auto", "Follow Windows"], ["paper", "Paper"], ["chalk", "Chalkboard"]].map(([k, l]) => {
      const c = h("button", { class: "chip", onclick: () => { touched(c); setS("theme", k); S.settings.theme = k; applyTheme(); } }, l);
      c.key = k;
      return c;
    });
    const smartFps = check("Save power when calm", "At \"Match monitor\", draws at half speed while nothing's moving fast. Looks the same, uses about half the CPU.", () => st().smartFps !== false, v => setS("smartFps", v));
    const g = () => st().gfx || {};
    const gfxChips = [["low", "Low"], ["medium", "Medium"], ["high", "High"], ["ultra", "Ultra"]].map(([k, l]) => {
      const c = h("button", { class: "chip", onclick: () => { touched(c); setS("gfxPreset", k); } }, l);
      c.key = k;
      return c;
    });
    const gfxPart = (part, v) => send({ t: "setting", key: "gfx", part, v });
    const shadowSel = select([{ value: 0, label: "Off" }, { value: 1, label: "Simple" }, { value: 2, label: "Soft" }], g().shadows ?? 2, v => gfxPart("shadows", +v));
    const faceSel = select([{ value: 0, label: "None (classic stick figures)" }, { value: 1, label: "Eyes" }, { value: 2, label: "Eyes and mouth" }], g().faces ?? 2, v => gfxPart("faces", +v));
    const gfxChecks = [
      check("Detailed art", "Wood grain, stitching, shine and extra parts on things, plus details on balls and pets. Off: the simple flat look.", () => g().detailedArt !== false, v => gfxPart("detail", v)),
      check("Shading", "Light and shade on figures, heads, things, balls and pets.", () => g().shading !== false, v => gfxPart("shading", v)),
      check("Drop shadows", "A faint shadow on the window behind them, so they stand out from your desktop.", () => g().dropShadows !== false, v => gfxPart("drop", v)),
      check("Motion trails", "Cartoon smears behind fast punches, kicks and flips.", () => !!g().trails, v => gfxPart("trails", v)),
    ];
    const vv = h("span", { class: "val" });
    const vol = range(0, 1, 0.01, st().volume ?? 0.55, v => { vv.textContent = Math.round(v * 100) + "%"; sendSoon("vol", { t: "setting", key: "volume", v }); });
    const voicesC = check("Babble voices", "They mumble along when they talk: their own little voice, higher or lower, quicker or shyer.", () => st().voices !== false, v => setS("voices", v));
    const soundC = check("Sound effects", "Footsteps, punches, bounces, boings, a radio that plays music... all made up on the fly.", () => st().sound, v => setS("sound", v));
    const checks = [
      check("Remember everyone between runs", "Figures, their feelings and the balls come back next time.", () => st().remember, v => setS("remember", v)),
      check("Show what they see", "Draws the window edges they can stand on and climb.", () => st().platforms, v => setS("platforms", v)),
      check("Hide the figures", "Pauses everything until you turn it back off.", () => st().hidden, v => setS("hidden", v)),
      check("Romance", "Crushes, blushing, confessions, couples holding hands, jealousy and breakups. Off: just friends.", () => st().romance !== false, v => setS("romance", v)),
      check("They ask for things", "A thought bubble when they want something (a snack, a ball, a bed). Click it to give it to them.", () => st().wishes !== false, v => setS("wishes", v)),
    ];
    const weatherChips = [["off", "Never"], ["rare", "Rarely"], ["sometimes", "Sometimes"], ["often", "Often"], ["real", "🌦 Your real weather"]].map(([k, l]) => {
      const c = h("button", { class: "chip", onclick: () => { touched(c); setS("weather", k); } }, l);
      c.key = k;
      return c;
    });
    const petMode = check("Just pets", "Only animals on your desktop. Your figures are kept safe and come back when you switch this off.", () => !!st().petMode, v => setS("petMode", v));
    const careChips = [["relaxed", "Relaxed"], ["normal", "Normal"], ["realistic", "Realistic"]].map(([k, l]) => { const c = h("button", { class: "chip", onclick: () => { touched(c); setS("petCare", k); } }, l); c.key = k; return c; });
    const staminaC = check("Stamina", "Everyone (figures and animals) gets out of puff with running, chasing and fighting, and needs to catch their breath.", () => st().stamina !== false, v => setS("stamina", v));
    const weightC = check("Weight", "Eating too much (and treats!) makes them chubbier, exercise slims them down. Heavier means slower and quicker to tire.", () => st().weight !== false, v => setS("weight", v));
    const lassoC = check("Figures can lasso your cursor", "A figure with a lasso may rope your cursor, spin it round and fling it. Only when you've left the mouse alone for a few seconds; move it yourself and it breaks free.", () => st().lassoCursor !== false, v => setS("lassoCursor", v));
    const breedC = check("Pet litters", "Bonded pairs of animals can have kittens, puppies, bunnies and so on.", () => st().petBreeding !== false, v => setS("petBreeding", v));
    const petHelpC = check("Figures help with the pets", "Your figures fill empty bowls and scoop the litter box now and then.", () => st().petHelp !== false, v => setS("petHelp", v));
    const placeIn = h("input", { type: "text", placeholder: "Your town or city", style: { width: "200px" } });
    const placeBtn = h("button", { class: "btn small", onclick: () => { if (placeIn.value.trim()) send({ t: "weatherPlace", v: placeIn.value.trim() }); } }, "Use this place");
    placeIn.addEventListener("keydown", e => { if (e.key === "Enter") placeBtn.click(); });
    const wxStatus = h("p", { class: "hint" });
    const realBox = h("div", { class: "field" }, h("label", null, "Real weather for"), placeIn, placeBtn);
    const eventsC = check("Town events", "Now and then the town holds a festival, a talent show or a race day.", () => st().events !== false, v => setS("events", v));
    const hapLine = h("p", { class: "hint" });
    const hapBtns = h("div", { class: "row" },
      h("button", { class: "btn small", onclick: () => send({ t: "happening", kind: "festival" }) }, "🎪 Hold a festival"),
      h("button", { class: "btn small", onclick: () => send({ t: "happening", kind: "talent" }) }, "🎤 Talent show"),
      h("button", { class: "btn small", onclick: () => send({ t: "happening", kind: "race" }) }, "🏁 Race day"),
      h("button", { class: "btn small", onclick: () => send({ t: "happening", kind: "stop" }) }, "Stop"));
    const calmC = check("Calm mode", "No fights, tournaments, cursor hunting or lassoing your cursor, no lightning flashes or thunderstorms, no freeze-frames on hits, and pets don't scrap.", () => !!st().calm, v => setS("calm", v));
    const cbC = check("Colour-blind badges", "Each colour team wears its own shape on its chest (▲ red, ● blue, ■ green, ◆ orange, ★ purple, ✚ yellow, ⬟ cyan, ♥ pink, ✖ black, ○ white), and the colour swatches show them too.", () => !!st().colourBlind, v => setS("colourBlind", v));
    const quietC = check("Quiet hours", "Hide everyone at set times (say, while you work) and bring them back afterwards.", () => !!st().pauseSchedule, v => setS("pauseSchedule", v));
    const qFrom = h("input", { type: "time", onchange: () => setS("pauseFrom", qFrom.value) }), qTo = h("input", { type: "time", onchange: () => setS("pauseTo", qTo.value) });
    const DAYS = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
    const dayChips = DAYS.map((d, i) => { const c = h("button", { class: "chip", onclick: () => { touched(c); const cur = new Set(st().pauseDays || []); cur.has(i) ? cur.delete(i) : cur.add(i); setS("pauseDays", [...cur].sort()); } }, d); c.key = i; return c; });
    const quietBox = h("div", null, h("div", { class: "field" }, h("label", null, "From"), qFrom, h("label", null, "to"), qTo), h("div", { class: "row tight" }, dayChips));
    const voiceC = check("Talk out loud", "A 🎤 Speak button (in the quick panel) listens once: say a figure's name and what to tell them (\"Sparky, come here\"), ask for something (\"make a pizza\"), or call for an event, a photo, a clip or weather. Uses Windows' own speech recognition on this PC; nothing is sent anywhere.", () => !!st().voiceInput, v => setS("voiceInput", v));
    const aiC = check("AI conversations", "When you talk to a figure, a language model answers in their voice (their personality, mood, likes, friends and feelings about you are sent along with what you said). Needs your own OpenAI API key and uses its credit. Off: they answer the usual way, all on this PC.", () => !!st().aiChat, v => setS("aiChat", v));
    const aiKey = h("input", { type: "password", placeholder: "sk-… (stored encrypted on this PC)", autocomplete: "off", style: { width: "240px" } });
    const aiModel = h("input", { type: "text", style: { width: "130px" }, onchange: () => setS("aiModel", aiModel.value.trim()) });
    const aiLine = h("p", { class: "hint" });
    const aiBox = h("div", null, h("div", { class: "field" }, h("label", null, "API key"), aiKey,
      h("button", { class: "btn small", onclick: () => { if (aiKey.value.trim()) { setS("aiKey", aiKey.value.trim()); aiKey.value = ""; } } }, "Save key"),
      h("button", { class: "btn small", onclick: () => setS("aiKey", "") }, "Remove key")),
      h("div", { class: "field" }, h("label", null, "Model"), aiModel), aiLine);
    const dlC = check("Notice downloads", "A cheer when a download finishes. Only the kind of file is noticed (a picture, a document…), from its extension; nothing is opened or read.", () => st().noticeDownloads !== false, v => setS("noticeDownloads", v));
    const frC = check("Notice when you're frustrated", "Lots of clicks in one spot, or a run of windows slammed shut: a friend comes over to check you're okay.", () => st().noticeFrustration !== false, v => setS("noticeFrustration", v));
    const remText = h("input", { type: "text", placeholder: "Remind me to…", maxlength: 120, style: { width: "220px" } });
    const remWhen = h("input", { type: "datetime-local" });
    const remRepeat = h("select", null, [["none", "Once"], ["daily", "Every day"], ["weekdays", "Weekdays"], ["weekly", "Every week"]].map(([v, l]) => h("option", { value: v }, l)));
    const remAdd = h("button", { class: "btn small", onclick: () => { if (remText.value.trim() && remWhen.value) { send({ t: "reminder", op: "add", text: remText.value.trim(), when: remWhen.value, repeat: remRepeat.value }); remText.value = ""; } } }, "Add reminder");
    const remList = h("div", { class: "thoughts" });
    const icsIn = h("input", { type: "file", accept: ".ics,text/calendar", style: { display: "none" }, onchange: () => importIcs(icsIn) });
    let remSig = "";
    const startC = check("Start with Windows", "Open StickFight when you sign in.", () => !!st().startWithWindows, v => setS("startWithWindows", v));
    const updC = check("Check for updates", "Once a day, ask GitHub whether there's a newer version. Nothing is downloaded until you click Update.", () => st().checkUpdates !== false, v => setS("checkUpdates", v));
    const batChips = [["off", "Off"], ["battery", "On battery"], ["always", "Always"]].map(([k, l]) => { const c = h("button", { class: "chip", onclick: () => { touched(c); setS("batterySaver", k); } }, l); c.key = k; return c; });
    const updLine = h("p", { class: "hint" });
    const updBtn = h("button", { class: "btn small primary", onclick: () => send({ t: "applyUpdate" }) }, "Update now");
    const installBtn = h("button", { class: "btn small", title: "Copies StickFight to your programs folder and adds it to the Start menu and Installed apps (no admin rights needed). Your figures stay as they are.", onclick: () => send({ t: "install" }) }, "Install StickFight");
    const modsLine = h("p", { class: "hint" });
    const jobsC = check("Jobs and coins", "Figures work (shopkeeper, chef, builder, entertainer, teacher), earn coins and spend them at the shop, the food cart and on tips. Put out a shop stall, food cart, stage or chalkboard from Things.", () => st().jobs !== false, v => setS("jobs", v));
    const paceChips = [["off", "Don't age"], ["slow", "A year a day"], ["fast", "A year an hour"]].map(([k, l]) => { const c = h("button", { class: "chip", onclick: () => { touched(c); setS("lifePace", k); } }, l); c.key = k; return c; });
    const babies = check("Babies", "Sweethearts who've been together a long while can have a little one, who grows up over a few hours.", () => st().babies !== false, v => setS("babies", v));
    const celebrations = check("Birthdays and holidays", "Parties on their birthdays; costumes at Halloween, hats at Christmas, fireworks at New Year.", () => st().celebrations !== false, v => setS("celebrations", v));
    const dayNight = check("Day and night", "Late at night they get sleepy; in the morning they say good morning. Follows your clock.", () => st().dayNight !== false, v => setS("dayNight", v));
    const screen = [
      check("Stand on text", "Lines of text and pictures in the window you're using become ledges they can walk, sit and land on.", () => st().screenTerrain, v => setS("screenTerrain", v)),
      check("React to words", "They wander over to words they know (pizza, cats, spiders...) and love them, hate them, or run.", () => st().screenReact, v => setS("screenReact", v)),
      check("Point out links", "Now and then one shows you a link in a bubble. It only opens if you click the bubble.", () => st().screenLinks, v => setS("screenLinks", v)),
      check("Music and videos", "Dance when an app plays music; sit down and watch when a video is on.", () => st().screenMedia, v => setS("screenMedia", v)),
      check("Notice your typing", "Fans cheer you on when you finish a long stretch of typing. Only the timing is noticed, never what you type.", () => st().noticeTyping !== false, v => setS("noticeTyping", v)),
      check("Notifications", "They look over when a notification pops up, and the bold ones jump on it.", () => st().notifications !== false, v => setS("notifications", v)),
    ];
    add(root, h("h1", null, "Settings"),
      h("h2", null, "Frame rate"), h("p", { class: "sub" }, `Your monitor runs at ${INIT.refresh} Hz. Lower is lighter on your computer; higher is smoother.`),
      h("div", { class: "row" }, chips),
      h("div", { class: "field", style: { marginTop: "8px" } }, h("label", null, "Or exactly"), custom, cv),
      smartFps,
      h("h2", null, "Graphics"),
      h("p", { class: "sub" }, "Presets set everything at once; change any part below to make your own."),
      h("div", { class: "row" }, gfxChips),
      h("div", { class: "field" }, h("label", null, "Shadows"), shadowSel),
      h("div", { class: "field" }, h("label", null, "Faces"), faceSel),
      gfxChecks,
      h("h2", null, "Sound"), soundC, voicesC, h("div", { class: "field" }, h("label", null, "Volume"), vol, vv),
      h("h2", null, "Look"), h("div", { class: "row" }, themes),
      h("h2", null, "Behaviour"), checks,
      h("h2", null, "Weather & time"),
      h("p", { class: "sub" }, "Now and then a shower, a storm or (in winter) snow. Or pick \"your real weather\" and type your town: the sky follows the actual weather there, and they feel the heat and the cold. Only then is anything looked up online (Open-Meteo, free and keyless): the place name once, then just its map coordinates every 20 minutes."),
      h("div", { class: "row" }, weatherChips),
      realBox, wxStatus,
      h("div", { class: "row", style: { marginTop: "8px" } }, ["Rain", "Storm", "Snow", "Clear"].map(k => h("button", { class: "btn small", onclick: () => send({ t: "sky", kind: k }) }, { Rain: "☂ Make it rain", Storm: "⚡ Storm", Snow: "❄ Make it snow", Clear: "☀ Clear skies" }[k]))),
      dayNight, celebrations, babies,
      h("h2", null, "Pets and bodies"),
      petMode,
      h("div", { class: "field" }, h("label", null, "Pet care"), h("div", { class: "row" }, careChips)),
      h("p", { class: "hint" }, "Relaxed: needs build slowly and there are no accidents. Normal: like real pets. Realistic: hungrier, thirstier, and they can't hold it as long."),
      staminaC, weightC, petHelpC, breedC, lassoC,
      h("h2", null, "Town"),
      jobsC,
      h("div", { class: "field" }, h("label", null, "Growing old"), h("div", { class: "row" }, paceChips)),
      eventsC, hapBtns, hapLine,
      h("p", { class: "hint" }, "With ageing on, figures count their years (while StickFight is running): kids go to school, and at 62 they become elders who go grey, slow down, use a cane and retire."),
      h("h2", null, "Reminders & your desktop"),
      h("p", { class: "sub" }, "Set a reminder and, when it's due, a figure brings it over to your cursor. You can also bring in events from a calendar file (.ics, exported from Outlook or Google Calendar): you'll be reminded 10 minutes before each one in the next month (times are read as this PC's local time unless the file says UTC). Everything stays on this PC."),
      h("div", { class: "row" }, remText, remWhen, remRepeat, remAdd, h("button", { class: "btn small", onclick: () => icsIn.click() }, "Import a calendar file…"), icsIn),
      remList, dlC, frC, voiceC, aiC, aiBox,
      h("h2", null, "Accessibility & quiet hours"),
      calmC, cbC, quietC, quietBox,
      h("h2", null, "Your screen"),
      h("p", { class: "sub" }, "They read the window you're using with Windows' accessibility tools and listen to which apps play sound. Everything stays on this PC: nothing is saved or sent anywhere. Some browsers run a little heavier while being read; turn these off if you notice."),
      screen,
      h("h2", null, "Battery saver"),
      h("p", { class: "sub" }, "A lighter frame rate (30 at most) and simpler graphics, to go easy on a laptop battery."),
      h("div", { class: "row" }, batChips),
      h("h2", null, "Mods"),
      h("p", { class: "sub" }, "Add your own objects (drawn as SVG or shapes), hats, names and jokes with JSON files in the mods folder. There's an example in there to start from; the guide is docs/MODDING.md on GitHub. Restart StickFight after changing them."),
      modsLine,
      h("div", { class: "row" }, h("button", { class: "btn small", onclick: () => send({ t: "openMods" }) }, "Open the mods folder"),
        h("button", { class: "btn small", onclick: () => window.open("https://github.com/Lindorak/StickFight/blob/main/docs/MODDING.md") }, "Modding guide")),
      h("h2", null, "Updates & installing"),
      updLine,
      h("div", { class: "row" }, updBtn, h("button", { class: "btn small", onclick: () => send({ t: "checkUpdate" }) }, "Check now"), installBtn),
      updC, startC,
      h("h2", null, "About"),
      h("p", null, `StickFight ${INIT.version || ""}: stick figures that live, play and fight on your desktop.`),
      h("div", { class: "row" },
        h("button", { class: "btn small", onclick: () => window.open("https://github.com/Lindorak/StickFight") }, "GitHub page"),
        armed("Quit StickFight", "Quit? Click again", () => send({ t: "quit" }), "btn small danger")));
    return () => {
      chips.forEach(c => { if (idle(c)) c.classList.toggle("on", c.fps === st().fps); });
      setRange(custom, st().fps > 0 ? st().fps : 60);
      if (idle(custom)) cv.textContent = st().fps > 0 ? st().fps : "–";
      themes.forEach(c => c.classList.toggle("on", c.key === st().theme));
      smartFps.update();
      gfxChips.forEach(c => { if (idle(c)) c.classList.toggle("on", c.key === g().preset); });
      shadowSel.set(g().shadows ?? 2); faceSel.set(g().faces ?? 2);
      gfxChecks.forEach(c => c.update());
      soundC.update(); voicesC.update(); setRange(vol, st().volume ?? 0.55); if (idle(vol)) vv.textContent = Math.round((st().volume ?? 0.55) * 100) + "%";
      checks.forEach(c => c.update());
      screen.forEach(c => c.update());
      weatherChips.forEach(c => { if (idle(c)) c.classList.toggle("on", c.key === (st().weather || "sometimes")); });
      dayNight.update(); celebrations.update(); babies.update(); petMode.update(); staminaC.update(); weightC.update(); petHelpC.update(); breedC.update(); lassoC.update(); jobsC.update(); eventsC.update(); dlC.update(); frC.update(); voiceC.update(); startC.update(); updC.update(); aiC.update();
      aiBox.style.display = st().aiChat ? "" : "none";
      if (idle(aiModel)) aiModel.value = st().aiModel || "gpt-5-mini";
      aiLine.textContent = (st().aiHasKey ? (st().aiEnvKey ? "Using the key from the OPENAI_API_KEY environment variable (or the one you saved). " : "A key is saved. ") : "No key yet. ") + (st().aiStatus || "");
      batChips.forEach(c => { if (idle(c)) c.classList.toggle("on", c.key === (st().batterySaver || "battery")); });
      updLine.textContent = `Version ${st().version || ""}${st().installed ? " (installed)" : ""}. ${st().updateStatus || ""}`;
      updBtn.style.display = st().updateReady ? "" : "none";
      installBtn.style.display = st().installed ? "none" : "";
      const md = st().mods || { loaded: [], errors: [] };
      modsLine.textContent = (md.loaded.length ? `Loaded: ${md.loaded.join(", ")} (${md.items} objects, ${md.hats} hats, ${md.jokes} jokes).` : "No mods yet.") + (md.errors.length ? ` Problems: ${md.errors.join("; ")}` : "");
      const rs = JSON.stringify(st().reminders || []);
      if (rs !== remSig) {
        remSig = rs;
        const list = st().reminders || [];
        remList.replaceChildren(...(list.length ? list.map(r => h("div", { class: "row tight" }, h("span", null, `⏰ ${r.when} · ${r.text}${r.repeat !== "none" ? ` (${r.repeat})` : ""}`),
          h("button", { class: "btn small", onclick: () => send({ t: "reminder", op: "delete", id: r.id }) }, "Remove"))) : [h("p", { class: "hint" }, "No reminders yet.")]));
      } calmC.update(); cbC.update(); quietC.update();
      quietBox.style.display = st().pauseSchedule ? "" : "none";
      if (idle(qFrom)) qFrom.value = st().pauseFrom || "09:00";
      if (idle(qTo)) qTo.value = st().pauseTo || "17:00";
      dayChips.forEach(c => { if (idle(c)) c.classList.toggle("on", (st().pauseDays || []).includes(c.key)); });
      realBox.style.display = st().weather === "real" ? "" : "none";
      if (idle(placeIn) && !placeIn.value) placeIn.value = st().weatherPlace || "";
      wxStatus.textContent = st().weather === "real" ? (st().weatherStatus || (st().weatherPlace ? st().weatherPlace : "Type your town or city and press \"Use this place\".")) : "";
      hapLine.textContent = st().happening ? `On now: ${st().happening}.` : "";
      paceChips.forEach(c => { if (idle(c)) c.classList.toggle("on", c.key === (st().lifePace || "off")); });
      careChips.forEach(c => { if (idle(c)) c.classList.toggle("on", c.key === (st().petCare || "normal")); });
    };
  },
};

// ---------------- theme + chrome ----------------

const dark = window.matchMedia("(prefers-color-scheme: dark)");
function applyTheme() {
  const t = S ? S.settings.theme : "auto";
  const chalk = t === "chalk" || (t === "auto" && dark.matches);
  document.documentElement.classList.toggle("chalk", chalk);
}
dark.addEventListener("change", applyTheme);

for (const b of document.querySelectorAll(".winbtn")) b.addEventListener("click", () => send({ t: b.dataset.win }));
for (const e of document.querySelectorAll(".edge")) e.addEventListener("pointerdown", ev => { ev.preventDefault(); send({ t: "resize", edge: e.dataset.edge }); });
if ($("#titlebar")) $("#titlebar").addEventListener("dblclick", e => { if (!e.target.closest(".winbtn")) send({ t: "max" }); });
document.addEventListener("keydown", e => { if ((QUICK || POP) && e.key === "Escape") send({ t: "close" }); });

// ---------------- tray quick panel ----------------

let quickUpdate = () => {};
function buildQuick() {
  const root = $("#quick");
  const hideC = check("Hide the figures", null, () => S.settings.hidden, v => send({ t: "setting", key: "hidden", v }));
  const fightC = check("Fights happen", null, () => S.fight.enabled, v => send({ t: "fight", key: "enabled", v }));
  const soundC = check("Sound", null, () => S.settings.sound, v => send({ t: "setting", key: "sound", v }));
  const petModeC = check("Just pets", null, () => S.settings.petMode, v => send({ t: "setting", key: "petMode", v }));
  const count = h("span", { class: "hint" });
  const stopG = h("button", { class: "btn small danger", onclick: () => send({ t: "game", kind: "stop" }) }, "Stop game");
  const gameNote = h("p", { class: "hint" });
  add(root,
    h("div", { class: "q-head" },
      s("svg", { class: "logo", viewBox: "-14 -30 28 34" }, s("g", { class: "logo-fig" }, s("circle", { cx: 0, cy: -23, r: 4.5 }), s("path", { d: "M0 -18 L0 -6 M0 -15 L-7 -9 M0 -15 L7 -21 M0 -6 L-5 3 M0 -6 L6 2" }))),
      h("span", { class: "brand-name" }, "StickFight"), h("span", { class: "spacer" }), count),
    h("h3", null, "Draw someone"),
    h("div", { class: "swatches" }, INIT.palette.map((p, i) => h("button", { class: "sw", title: p.name, style: { background: p.hex, color: "#111", "font-size": "11px", "text-shadow": "0 0 2px #fff" }, onclick: () => { send({ t: "spawn", color: i, preset: -1, quiet: true }); toast(`A ${p.name.toLowerCase()} one!`); } }, S.settings.colourBlind ? TEAM_SYM[p.name] || "" : ""))),
    h("h3", null, "Draw something"), summonBox(true),
    h("h3", null, "Toss in a toy"),
    h("div", { class: "q-toys" }, INIT.propKinds.map(k => h("button", { class: "q-toy", title: k.name, onclick: () => send({ t: "prop", op: "add", kind: k.key }) }, ballSvg(k.key, "#E53935"), h("span", null, k.name)))),
    h("h3", null, "Play with them"),
    h("div", { class: "row tight q-games" },
      h("button", { class: "btn small", onclick: () => send({ t: "game", kind: "HideSeek" }) }, "🙈 Hide & seek"),
      h("button", { class: "btn small", onclick: () => send({ t: "game", kind: "Tag" }) }, "🏃 Tag"),
      h("button", { class: "btn small", onclick: () => send({ t: "game", kind: "Catch" }) }, "⚾ Catch"),
      h("button", { class: "btn small", onclick: () => send({ t: "tourney" }) }, "🏆 Tournament"),
      h("button", { class: "btn small", title: "Saves a picture of them (and whatever's behind them) to Pictures\\StickFight", onclick: () => send({ t: "photo" }) }, "📷 Photo"),
      h("button", { class: "btn small", title: "Records 10 seconds of everyone (just them and their things, on paper) as an animated GIF in Pictures\\StickFight", onclick: () => send({ t: "record", seconds: 10 }) }, "🎬 Record a clip"),
      S.settings.voiceInput ? h("button", { class: "btn small", title: "Say something: a figure's name and what to tell them, \"make a pizza\", \"start a race\", \"make it snow\"…", onclick: () => send({ t: "listen" }) }, "🎤 Speak") : null,
      stopG),
    gameNote,
    h("h3", null, "Pets"),
    h("div", { class: "row tight q-games" },
      [["Cat", false, "🐈"], ["Cat", true, "Kitten"], ["Dog", false, "🐕"], ["Dog", true, "Puppy"], ["Parrot", false, "🦜"], ["Rabbit", false, "🐇"], ["Hamster", false, "🐹"]].map(([k, y, l]) => h("button", { class: "btn small", title: `Adopt a ${y ? (k === "Cat" ? "kitten" : "puppy") : k.toLowerCase()}`, onclick: () => send({ t: "adopt", kind: k, young: y }) }, l)),
      h("button", { class: "btn small", onclick: () => send({ t: "spray" }) }, "💦 Spray bottle")),
    h("div", { class: "q-checks" }, petModeC, hideC, fightC, soundC),
    h("div", { class: "row q-foot" },
      h("button", { class: "btn small primary", onclick: () => send({ t: "studio" }) }, "Open Studio"),
      h("span", { class: "spacer" }),
      armed("Quit", "Quit?", () => send({ t: "quit" }), "btn small danger")));
  quickUpdate = () => {
    hideC.update(); fightC.update(); soundC.update(); petModeC.update();
    const gm = S.game;
    stopG.hidden = !gm;
    gameNote.textContent = !gm ? "" : gm.kind === "HideSeek" ? `Hide & seek: found ${gm.found} of ${gm.players}` : gm.kind === "Tag" ? `Tag: ${gm.players} playing` : `Catch: ${gm.streak} in a row (best ${gm.best})`;
    const n = S.figures.length;
    count.textContent = `${n} figure${n === 1 ? "" : "s"} · ${S.fpsNow || 0} fps`;
  };
}
// ---------------- right-click menu ----------------
/* One small menu for whatever was right-clicked: a ball, an object or a figure. Quick changes apply live; anything
   bigger is one click away in the Studio. */

let popUpdate = () => {};
let popSaid = () => {};
let popKey = "", pendingPop = null;

function buildPop(kind, id) {
  const root = $("#pop");
  if (!root) return;
  if (!INIT || !S) { pendingPop = [kind, id]; return; }   // built as soon as the first state arrives
  pendingPop = null;
  popKey = kind + ":" + id;
  const find = () => kind === "prop" ? S.props.find(p => p.id === id) : kind === "item" ? S.items.find(i => i.id === id) : kind === "pet" ? (S.pets || []).find(p => p.id === id) : S.figures.find(f => f.id === id);
  let x = find();
  root.replaceChildren();
  if (!x) { popUpdate = () => {}; fit(); return; }
  const close = () => send({ t: "close" });
  const more = page => h("button", { class: "btn small", onclick: () => send({ t: "studio", page, id }) }, "More…");
  const head = (art, title, sub) => h("div", { class: "pop-head" }, h("span", { class: "pop-art" }, art), h("div", { class: "pop-name" }, h("div", { class: "pop-title" }, title), sub));
  const field = (label, ...kids) => h("div", { class: "pop-field" }, h("label", null, label), kids);
  const updates = [];

  if (kind === "pet") {
    const sub = h("div", { class: "hint" });
    const art = h("span", { class: "pop-pet" }, petGlyph(x.kind));
    const name = h("input", { class: "text", value: x.name, maxlength: 24, onchange: () => send({ t: "pet", op: "rename", id, v: name.value }) });
    const col = colourPicker(x.hex, hex => sendSoon("petc", { t: "pet", op: "color", id, hex }));
    const sz = range(0.5, 2.5, 0.05, x.size, v => sendSoon("pets", { t: "pet", op: "size", id, v }));
    const needs = h("div", { class: "thoughts" });
    const acts = h("div", { class: "row tight pop-acts" });
    const teach = x.kind === "Parrot" ? h("input", { class: "text", placeholder: "Say a word or phrase to teach…", maxlength: 32,
      onkeydown: e => { if (e.key === "Enter" && teach.value.trim()) { send({ t: "pet", op: "talk", id, v: teach.value }); teach.value = ""; } } }) : null;
    let actSig = "";
    add(root, head(art, x.name, sub), needs, acts, teach && field("Teach", teach), field("Name", name), field("Colour", col), field("Size", sz),
      h("div", { class: "row pop-foot" },
        h("button", { class: "btn small", onclick: () => send({ t: "studio", page: "pets" }) }, "Care & training…"),
        h("span", { class: "spacer" }),
        armed("Remove", "Sure?", () => { send({ t: "pet", op: "remove", id }); close(); }, "btn small danger")));
    updates.push(() => {
      sub.textContent = `${x.species || ""} · ${x.activity} · ${x.mood || ""}`;
      if (x.needs) needs.replaceChildren(...NEED_NAMES.slice(0, 6).map(([k, l]) => meterRow(l, x.needs[k], Math.round(x.needs[k] * 100) + "%")), meterRow("Weight", x.weight, x.weightWord, x.weight > 0.5 ? "var(--bad)" : "var(--good)"));
      const sig = [x.leashed, x.onCursor].join();
      if (sig !== actSig) { actSig = sig; acts.replaceChildren(...petActions(x, close).filter(Boolean)); fit(); }
      if (idle(name)) name.value = x.name;
      col.set(x.hex); setRange(sz, x.size);
    });
  } else if (kind === "prop") {
    const sub = h("div", { class: "hint" });
    const art = h("span");
    const paintArt = () => art.replaceChildren(ballSvg(x.kind, x.hex));
    paintArt();
    const col = colourPicker(x.hex, hex => { x.hex = hex; paintArt(); sendSoon("pc", { t: "prop", op: "color", id, hex }); });
    const sz = range(0.3, 3, 0.05, x.size, v => sendSoon("ps", { t: "prop", op: "size", id, v }));
    const bo = range(0, 0.95, 0.01, x.bounce, v => sendSoon("pb", { t: "prop", op: "bounce", id, v }));
    add(root, head(art, x.name, sub),
      field("Colour", col), field("Size", sz), field("Bounciness", bo),
      h("div", { class: "row pop-foot" },
        h("button", { class: "btn small primary", onclick: () => send({ t: "prop", op: "kick", id }) }, "Kick it!"),
        h("span", { class: "spacer" }), more("toys"),
        armed("Remove", "Sure?", () => { send({ t: "prop", op: "remove", id }); close(); }, "btn small danger")));
    updates.push(() => { sub.textContent = x.held ? `${x.held} has it` : "Drag it, throw it, or tweak it here."; col.set(x.hex); setRange(sz, x.size); setRange(bo, x.bounce); });
  } else if (kind === "item") {
    const def = catalogDef(x.key);
    const recolour = def && def.shapes.some(sh => sh.c < 3);
    const sub = h("div", { class: "hint" });
    const art = h("span");
    const paintArt = () => art.replaceChildren(def ? itemSvg(def, x.hex) : h("span"));
    paintArt();
    const col = recolour ? colourPicker(x.hex, hex => { x.hex = hex; paintArt(); sendSoon("ic", { t: "item", op: "color", id, hex }); }) : null;
    const sz = range(0.3, 3.5, 0.05, x.size, v => sendSoon("is", { t: "item", op: "size", id, v }));
    const tiltVal = h("span", { class: "val" });
    const tilt = d => { x.tilt = Math.max(-90, Math.min(90, d)); send({ t: "item", op: "tilt", id, v: x.tilt }); tiltVal.textContent = x.tilt ? `${x.tilt}°` : "upright"; };
    let music = null;
    if (def && def.verbs.includes("Dance"))
      music = h("button", { class: "btn small", onclick: () => { x.playing = !x.playing; send({ t: "item", op: "music", id }); music.textContent = x.playing ? "Music off" : "Music on"; } }, x.playing ? "Music off" : "Music on");
    const home = x.homeable ? h("select", { class: "text", onchange: () => send({ t: "item", op: "owner", id, v: +home.value }) },
      h("option", { value: 0 }, "Nobody"), S.figures.filter(f => !f.dead).map(f => h("option", { value: f.id }, f.name))) : null;
    if (home) home.value = String(x.owner || 0);
    add(root, head(art, x.name, sub),
      col && field("Colour", col), field("Size", sz), home && field("Home of", home),
      field("Turn", h("div", { class: "row tight" },
        h("button", { class: "btn small icon", title: "Lean left", onclick: () => tilt((x.tilt || 0) - 15) }, "⟲"),
        h("button", { class: "btn small icon", title: "Lean right", onclick: () => tilt((x.tilt || 0) + 15) }, "⟳"),
        h("button", { class: "btn small", onclick: () => tilt(0) }, "Upright"),
        h("button", { class: "btn small", onclick: () => send({ t: "item", op: "flip", id }) }, "Flip ⇄"), tiltVal)),
      h("div", { class: "row pop-foot" }, music, h("span", { class: "spacer" }), more("toys"),
        armed("Remove", "Sure?", () => { send({ t: "item", op: "remove", id }); close(); }, "btn small danger")));
    updates.push(() => {
      sub.textContent = x.users && x.users.length ? `${x.users.join(" & ")} ${x.users.length > 1 ? "are" : "is"} using it`
        : x.tilt ? "Tipped over: nobody can use it like this" : "Free to use";
      if (col) col.set(x.hex);
      setRange(sz, x.size);
      tiltVal.textContent = x.tilt ? `${x.tilt}°` : "upright";
    });
  } else {
    const sub = h("div", { class: "hint" });
    const dot = figSvg("fig pop-fig");
    const col = colourPicker(x.hex, hex => sendSoon("fc", { t: "fig", op: "color", id, hex }));
    const sz = range(0.4, 3, 0.05, x.size, v => sendSoon("fs", { t: "fig", op: "size", id, v }));
    const heal = h("button", { class: "btn small", onclick: () => send({ t: "fig", op: "heal", id }) }, "Heal");
    const hunt = h("button", { class: "btn small", onclick: () => send({ t: "fig", op: "hunter", id, v: !x.hunter }) });
    const feels = h("span", { class: "hint" });
    const reply = h("div", { class: "hint pop-reply" });
    const say = h("input", { class: "text", placeholder: `Say something to ${x.name}…`, maxlength: 140,
      onkeydown: e => { if (e.key === "Enter" && say.value.trim()) { send({ t: "fig", op: "talk", id, v: say.value }); reply.textContent = "“" + say.value.trim() + "”"; say.value = ""; } } });
    popSaid = m => { if (m.id === id) reply.textContent = `${x.name}: ${m.text}`; fit(); };
    const game = kind => h("button", { class: "btn small", onclick: () => { send({ t: "game", kind, id }); close(); } }, { HideSeek: "Hide & seek", Tag: "Tag", Catch: "Catch" }[kind]);
    add(root, head(dot, x.name, sub),
      field("Talk", say), reply,
      field("Play", h("div", { class: "row tight" }, game("Catch"), game("Tag"), game("HideSeek"))),
      field("Colour", col), field("Size", sz),
      h("div", { class: "row tight pop-acts" },
        h("button", { class: "btn small primary", onclick: () => { send({ t: "fig", op: "call", id }); close(); } }, "Come here"),
        h("button", { class: "btn small", onclick: () => send({ t: "fig", op: "dance", id }) }, "Dance!"), heal, hunt),
      h("div", { class: "row pop-foot" }, feels, h("span", { class: "spacer" }),
        h("button", { class: "btn small", onclick: () => send({ t: "studio", page: "figure", id }) }, "Open in Studio")));
    updates.push(() => {
      dot.update(x.pose, x.hex, x.look, x.facing);
      sub.textContent = x.dead ? "Gone" : x.activity;
      feels.textContent = x.feels || "";
      col.set(x.hex);
      setRange(sz, x.size);
      heal.hidden = !(x.hp < 99.5);
      hunt.textContent = x.hunter ? "Stop hunting" : "Hunt the cursor";
    });
  }
  popUpdate = () => {
    const n = find();
    if (!n) { if (popKey) close(); popKey = ""; return; }
    x = n;
    updates.forEach(u => u());
  };
  popUpdate();
  fit();
}

/** Tell the window how tall the menu is so it can size itself to fit. */
function fit() { requestAnimationFrame(() => send({ t: "fit", h: Math.ceil($("#pop").getBoundingClientRect().height) })); }

for (const t of document.querySelectorAll(".tab")) t.addEventListener("click", () => go(t.dataset.page));
document.addEventListener("keydown", e => { if (e.key === "Escape" && route.page === "figure") go("cast"); });

// ---------------- mock host (previewing the page in a normal browser) ----------------

const Mock = {
  handle(m) {
    if (m.t === "fig") {
      const f = this.state.figures.find(f => f.id === m.id);
      if (f && m.op === "trait") f.traits[m.key] = m.v;
      if (f && m.op === "rename") f.name = m.name;
      if (f && m.op === "color") f.hex = m.hex;
      if (f && m.op === "taste") f.tastes.opinions[m.key] = m.v;
      if (f && m.op === "fond") f.fond = m.v;
    }
    if (m.t === "setting") this.state.settings[m.key] = m.v;
    if (m.t === "fight") this.state.fight[m.key] = m.v;
  },
  async start() {
    try {
      const [i, st] = await Promise.all([fetch("mock-init.json").then(r => r.ok ? r.json() : null), fetch("mock-state.json").then(r => r.ok ? r.json() : null)]);
      if (i && st) {
        receive(i);
        this.state = st;
        const q = new URLSearchParams(location.search);
        if (q.get("theme")) st.settings.theme = q.get("theme");
        if (q.get("page")) { route.page = q.get("page"); route.id = +(q.get("id") || (st.figures[0] && st.figures[0].id) || 0); route.sub = q.get("sub") || "personality"; }
        setInterval(() => receive(JSON.parse(JSON.stringify(this.state))), 300);
        if (q.get("pop")) setTimeout(() => receive({ t: "pop", kind: q.get("pop"), id: +q.get("id") }), 80);
        return;
      }
    } catch (e) { }
    const things = ["PlayingBall", "Juggling", "Climbing", "Exploring", "Chatting", "HighFives", "Fighting", "Sparring", "Napping", "Tricks", "Dancing", "Sitting", "HighPlaces", "Taskbar", "Ledges", "SoccerBalls", "Basketballs", "BeachBalls", "YourCursor", "BeingPickedUp", "BeingThrown"];
    const group = k => ["HighPlaces", "Taskbar", "Ledges"].includes(k) ? "Places" : ["SoccerBalls", "Basketballs", "BeachBalls"].includes(k) ? "Toys" : ["YourCursor", "BeingPickedUp", "BeingThrown"].includes(k) ? "You" : "Things to do";
    const opts = (...l) => l.map((label, v) => ({ v, label: v === 0 ? "Auto" : label }));
    const palette = [["Red", "#E53935"], ["Blue", "#1E88E5"], ["Green", "#43A047"], ["Orange", "#FB8C00"], ["Purple", "#8E24AA"], ["Yellow", "#FDD835"], ["Cyan", "#00ACC1"], ["Pink", "#EC407A"], ["Black", "#262626"], ["White", "#F4F4F4"]];
    receive({
      t: "init", version: "0.4.0", refresh: 144,
      palette: palette.map(([name, hex]) => ({ name, hex })),
      presets: [["Balanced", "A bit of everything."], ["Explorer", "Always climbing to the next window."], ["Couch potato", "Sits, naps, sits again."], ["Hothead", "Swats first, asks later."]].map(([name, blurb]) => ({ name, blurb, traits: {} })),
      things: things.map(k => ({ key: k, name: k.replace(/([a-z])([A-Z])/g, "$1 $2"), group: group(k) })),
      styles: { walk: opts("", "Normal", "Bouncy", "Swagger"), run: opts("", "Sprinter", "Flailer"), idle: opts("", "Loose", "Arms Crossed"), climb: opts("", "Methodical"), jump: opts("", "Tuck"), fight: opts("", "Boxer", "Kicker"), celebrate: opts("", "Cheer", "Dance"), rope: opts("", "Never", "Rappel", "Haul", "Zip") },
      gear: [{ v: 0, label: "Bare hands" }, { v: 1, label: "Boxing gloves" }, { v: 2, label: "Brass knuckles" }],
      relations: ["Default", "Friends", "Neutral", "Rivals", "Enemies", "Ignore"].map(k => ({ key: k, label: k })),
      deathRules: [{ key: "KnockdownOnly", label: "Just knocked down" }, { key: "KnockOut", label: "Knocked out, then gets back up" }, { key: "Permanent", label: "Dies for good" }],
      propKinds: [{ key: "Ball", name: "Ball" }, { key: "SoccerBall", name: "Soccer ball" }, { key: "Basketball", name: "Basketball" }, { key: "BeachBall", name: "Beach ball" }],
      fps: [{ label: "Match monitor (144 Hz)", value: -1 }, { label: "30", value: 30 }, { label: "60", value: 60 }, { label: "72", value: 72 }, { label: "120", value: 120 }, { label: "Unlimited", value: 0 }],
    });
    const mk = (id, name, hex, acts) => ({
      id, name, hex, team: name, size: 1, gear: 0, activity: acts, feels: "Likes you", fond: 0.4, trust: 0.6, memories: [{ what: "Played ball with them", delta: 0.06, ago: 30 }, { what: "Threw them around (hated that)", delta: -0.15, ago: 400 }], hp: 80, ko: false, dead: false, facing: 1, pose: STANDING,
      mood: { stamina: 0.7, joy: 0.5, sadness: 0.1, fear: 0.05, annoyance: 0.2, boredom: 0.4, loneliness: 0.3 },
      traits: { energy: 0.6, curiosity: 0.7, bravery: 0.5, playfulness: 0.8, aggression: 0.3, sociability: 0.6 },
      describe: "Energetic, curious and playful",
      tastes: { opinions: Object.fromEntries(things.map((k, i) => [k, Math.round(Math.sin(i * 7 + id) * 100) / 100])), fav: "Green", hate: "Black", describe: "Loves dancing, juggling, high places; hates napping. Favourite colour: green." },
      style: { choice: { walk: 0, run: 0, idle: 0, climb: 0, jump: 0, fight: 0, celebrate: 0, rope: 0 }, resolved: { walk: "Bouncy", run: "Sprinter", idle: "Loose", climb: "Leaper", jump: "Flipper", fight: "Acrobat", celebrate: "Dance", rope: "Zip" }, describe: "Walks with a bounce, sprints, stands loose, fights with flips and flying kicks, and dances when it wins. Zips up a grappling hook." },
      rels: [],
    });
    const figs = [mk(1, "Sparky", "#E53935", "Juggling"), mk(2, "Bubbles", "#1E88E5", "Chatting with Flip"), mk(3, "Flip", "#43A047", "Chatting with Bubbles"), mk(4, "Rocco", "#FB8C00", "Climbing a rope"), mk(5, "Snooze", "#8E24AA", "Napping")];
    figs[3].feels = "Can't stand you"; figs[3].fond = -0.6; figs[4].feels = "Adores you"; figs[4].fond = 0.8;
    figs[3].tastes.describe = "Loves fighting, high places, your cursor; hates napping, dancing. Favourite colour: red.";
    figs[4].tastes.describe = "Loves napping, sitting, being picked up; hates exploring. Favourite colour: purple.";
    figs.forEach(f => f.rels = figs.filter(o => o !== f).map(o => ({ id: o.id, name: o.name, hex: o.hex, mine: Math.round(Math.sin(f.id * 3 + o.id) * 100) / 100, theirs: Math.round(Math.cos(f.id + o.id * 2) * 100) / 100, relation: "Neutral", similarity: 0.4, shared: ["Dancing"] })));
    this.state = {
      t: "state", figures: figs, props: [{ id: 7, kind: "SoccerBall", name: "Soccer ball", size: 1, bounce: 0.65, hex: "#E53935", held: null }, { id: 8, kind: "BeachBall", name: "Beach ball", size: 1.6, bounce: 0.8, hex: "#E53935", held: "Red" }],
      library: [{ name: "Sparky", hex: "#FB8C00", size: 1, describe: "Hyper and fearless", likes: "Loves climbing, tricks." }],
      fight: { enabled: true, punchCursor: true, frequency: 1, strength: 1, onZeroHealth: "KnockdownOnly", reviveSeconds: 15, healthBars: true, sameColour: "Friends", differentColour: "Neutral", pairs: { "Blue|Red": "Rivals" } },
      settings: { fps: 60, remember: true, platforms: false, hidden: false, theme: "auto" }, fpsNow: 60,
    };
    const q = new URLSearchParams(location.search);
    if (q.get("theme")) this.state.settings.theme = q.get("theme");
    if (q.get("page")) { route.page = q.get("page"); route.id = +(q.get("id") || 1); route.sub = q.get("sub") || "personality"; }
    let t = 0;
    setInterval(() => {
      t += 0.16;
      for (const f of this.state.figures) {
        const k = Math.sin(t * 3 + f.id), k2 = Math.cos(t * 3 + f.id);
        f.pose = STANDING.map(([x, y], i) => i === J.HandN ? [x + k * 6, y - 8 - k2 * 6] : i === J.FootN ? [x + k * 4, y - Math.max(0, k) * 5] : [x, y + (i < 2 ? Math.sin(t * 2) * 0.6 : 0)]);
      }
      receive(JSON.parse(JSON.stringify(this.state)));
    }, 160);
  },
};

if (host) send({ t: "ready" }); else Mock.start();
