// Convertisseur Voyage : bibliotheque des circuits d'Assetto Corsa, conversion (fenetre + progression), destinations
// converties (apercu 3D, fiche, etat, suppression). Messages avec le programme : MainForm.cs.
import { Viewer } from "./viewer.js";

const $ = id => document.getElementById(id);
const host = window.chrome && window.chrome.webview;
const send = m => host ? host.postMessage(m) : window.essaiSend && window.essaiSend(m);
const esc = s => String(s ?? "").replace(/[&<>"]/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c]));
const strip = s => String(s ?? "").replace(/<br\s*\/?>/gi, "\n").replace(/<[^>]+>/g, "").replace(/&nbsp;/g, " ").trim();
const dec = () => (LANG === "en" || LANG === "ar" ? "." : ",");
const num = n => Math.round(n).toLocaleString(LANG === "ar" ? "en" : LANG).replace(/,/g, LANG === "en" || LANG === "ar" ? "," : " ");
const size = n => n >= 1e9 ? (n / 1e9).toFixed(1).replace(".", dec()) + " " + T("go") : Math.max(1, Math.round(n / 1048576)) + " " + T("mo");
const urlEnc = p => p.split("/").map(encodeURIComponent).join("/");

let state = null;          // dernier etat recu
let page = "biblio";       // biblio | dest
let current = null;        // id de la destination affichee
let filtre = "tous";
let viewer = null;
let conv = null;           // fenetre de conversion : { t, l, mode, opts, ... }

// ---------------------------------------------------------------- fenetre
$("titlebar").addEventListener("mousedown", e => {
  if (e.button !== 0 || e.target.closest("button")) return;
  send({ type: "fenetre", action: "deplacer" });
});
$("titlebar").addEventListener("dblclick", e => { if (!e.target.closest("button")) send({ type: "fenetre", action: "agrandir" }); });
document.querySelectorAll(".wc").forEach(b => b.addEventListener("click", () => send({ type: "fenetre", action: b.dataset.act })));
$("btn-actualiser").addEventListener("click", () => send({ type: "actualiser" }));
document.addEventListener("contextmenu", e => { if (!e.target.closest("input, textarea, .log")) e.preventDefault(); });
document.addEventListener("keydown", e => {
  if (e.key === "Escape") {
    if (!$("m-confirm").hidden) closeConfirm();
    else if (!$("m-conv").hidden && conv && conv.mode !== "run") closeConv();
  }
  if (e.key === "F5") { e.preventDefault(); send({ type: "actualiser" }); }
});

let toastTimer = 0;
function toast(text, bad) {
  const t = $("toast");
  t.textContent = text; t.className = "toast" + (bad ? " bad" : ""); t.hidden = false;
  clearTimeout(toastTimer); toastTimer = setTimeout(() => { t.hidden = true; }, 3800);
}

// ---------------------------------------------------------------- langue
function setLang(l) {
  window.LANG = ["fr", "en", "ru", "ar"].includes(l) ? l : "en";
  $("lang").value = window.LANG;
  applyI18n();
  $("title").textContent = "Voyage · " + T("titre");
  document.title = $("title").textContent;
  renderAll();
}
$("lang").addEventListener("change", () => { try { localStorage.setItem("langue", $("lang").value); } catch { } setLang($("lang").value); });

// ---------------------------------------------------------------- etat
function kmOf(len) {
  const m = String(len ?? "").replace(",", ".").match(/[\d.]+/);
  let v = m ? parseFloat(m[0]) : 0;
  if (v > 100) v /= 1000;   // (longueur en metres dans la plupart des ui_track.json)
  return v;
}
const kmText = km => km > 0 ? km.toFixed(km < 10 ? 2 : 1).replace(/0$/, "").replace(".", dec()) + " km" : "";
const autoPrice = km => Math.round((300 + km * 60) / 50) * 50;
const destOf = id => state && state.destinations.find(d => d.id === id);
const trackName = t => {
  const c = t.configs.find(x => x.id === "") || t.configs[0];
  if (t.configs.length < 2) return c.nom || t.id;
  // plusieurs configurations : la partie commune des noms (« Nordschleife - Tourist » / « Nordschleife » -> « Nordschleife »)
  let p = t.configs.map(x => x.nom || "").reduce((a, b) => { let i = 0; while (i < a.length && i < b.length && a[i].toLowerCase() === b[i].toLowerCase()) i++; return a.slice(0, i); });
  p = p.replace(/[\s\-–—_:(]+$/, "").trim();
  return p.length >= 3 ? p : (c.nom || t.id);
};

function renderAll() {
  if (!state) return;
  renderSide();
  renderLibrary();
  if (page === "dest") {
    const d = destOf(current);
    if (!d) showLibrary();
    else {
      renderDestPane();
      if (viewer && viewer.key !== d.id + "|" + (d.date || "")) loadPreview();   // (reconvertie : nouveaux fichiers)
      else if (viewer) viewer.setDest(d);                                        // (point d'arrivee change)
    }
  }
  if (conv && !$("m-conv").hidden && conv.mode === "form") renderConv();
}

// ---------------------------------------------------------------- barre de gauche
function renderSide() {
  const ds = state.destinations;
  window.GAME = state.jeu || "mwc";
  document.body.className = window.GAME;
  document.querySelectorAll("#games button").forEach(b => b.classList.toggle("on", b.dataset.g === window.GAME));
  $("nb-circuits").textContent = state.circuits.length || "";
  $("nb-dest").textContent = ds.length || "";
  const box = $("dests");
  if (!state.mwc) box.innerHTML = `<p class="empty">${esc(T("destSansJeu"))}</p>`;
  else if (!ds.length) box.innerHTML = `<p class="empty">${esc(T("destVide"))}</p>`;
  else box.innerHTML = ds.map(d => `<button class="di${page === "dest" && current === d.id ? " on" : ""}" data-d="${esc(d.id)}">
      <span class="th" style="${d.apercu ? `background-image:url('https://dest.local/${urlEnc(d.id)}/apercu.png?v=${encodeURIComponent(d.date || "")}')` : ""}"><span class="dot ${esc(d.etat)}" title="${esc(T("etat_" + d.etat))}"></span></span>
      <span class="tx"><b>${esc(d.nom)}</b><small>${esc([d.pays, (d.prix || "?") + " mk"].filter(Boolean).join(" · "))}</small></span>
    </button>`).join("");
  box.querySelectorAll(".di").forEach(b => b.addEventListener("click", () => openDest(b.dataset.d)));
  $("nav-biblio").classList.toggle("on", page === "biblio");
  const pa = $("path-ac"), pm = $("path-mwc");
  pa.textContent = state.ac || T("introuvable"); pa.title = state.ac || ""; pa.classList.toggle("none", !state.ac);
  pm.textContent = state.mwc || T("introuvable"); pm.title = state.mwc || ""; pm.classList.toggle("none", !state.mwc);
  const me = $("mod-etat");
  const mv = state.modVoyage;
  me.className = "mod" + (state.mwc && (mv !== "ok" || !state.mscloader) ? " manque" : "");
  me.innerHTML = !state.mwc ? "" : `<span class="dot ${mv === "ok" && state.mscloader ? "ok" : "maj"}"></span><span class="grow">${esc(T(mv === "ok" ? "modOk" : mv === "ancien" ? "modAncien" : "modManque"))}${state.mscloader ? "" : `<br>${esc(T("sansLoader"))}`}</span>`
    + (mv === "absent" || mv === "ancien" ? `<button class="mini" id="mod-inst">${esc(T(mv === "ancien" ? "majMod" : "installerMod"))}</button>` : "");
  if ($("mod-inst")) $("mod-inst").addEventListener("click", () => send({ type: "installerMod" }));
}
$("nav-biblio").addEventListener("click", showLibrary);
document.querySelectorAll("#games button").forEach(b => b.addEventListener("click", () => {
  if (state && b.dataset.g === state.jeu) return;
  if (viewer) viewer.clear();
  showLibrary();
  send({ type: "jeu", id: b.dataset.g });
}));
document.querySelectorAll("[data-pick]").forEach(b => b.addEventListener("click", () => send({ type: "choisirDossier", quoi: b.dataset.pick })));

// ---------------------------------------------------------------- bibliotheque
function showLibrary() {
  page = "biblio"; current = null;
  $("biblio").hidden = false; $("dest").hidden = true;
  if (viewer) viewer.clear();
  renderSide();
}
$("q").addEventListener("input", renderLibrary);
$("tri").addEventListener("change", renderLibrary);
document.querySelectorAll("#filtre button").forEach(b => b.addEventListener("click", () => {
  filtre = b.dataset.f;
  document.querySelectorAll("#filtre button").forEach(x => x.classList.toggle("on", x === b));
  renderLibrary();
}));

function trackState(t) {
  let best = null;
  for (const c of t.configs) {
    const d = c.converti && destOf(c.dest);
    if (!d) continue;
    if (d.etat === "maj") return "maj";
    best = "ok";
  }
  return best;
}

function renderLibrary() {
  if (!state) return;
  const grid = $("grid"), blank = $("blank");
  if (!state.ac) {
    grid.hidden = true; blank.hidden = false;
    blank.innerHTML = `<div class="big"><svg width="30" height="30" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/><path d="M12 11v4M10 13h4"/></svg></div>
      <h2>${esc(T("acIntrouvable"))}</h2><p>${esc(T("acIntrouvableSous"))}</p>
      <button class="btn pri" id="pick-ac">${esc(T("choisirAc"))}</button>`;
    $("pick-ac").addEventListener("click", () => send({ type: "choisirDossier", quoi: "ac" }));
    return;
  }
  const q = $("q").value.trim().toLowerCase();
  let list = state.circuits.map(t => ({ t, name: trackName(t), c: t.configs.find(x => x.apercu) || t.configs[0], st: trackState(t) }));
  list = list.filter(x => {
    if (filtre === "aconv" && x.st) return false;
    if (filtre === "conv" && !x.st) return false;
    if (filtre === "kunos" && !x.t.officiel) return false;
    if (filtre === "mods" && x.t.officiel) return false;
    if (!q) return true;
    return [x.name, x.t.id, ...x.t.configs.flatMap(c => [c.nom, c.pays, c.ville, c.auteur])].some(s => String(s || "").toLowerCase().includes(q));
  });
  const tri = $("tri").value;
  list.sort((a, b) => tri === "pays" ? String(a.c.pays).localeCompare(b.c.pays) || a.name.localeCompare(b.name)
                    : tri === "long" ? kmOf(b.c.longueur) - kmOf(a.c.longueur)
                    : a.name.localeCompare(b.name));
  blank.hidden = list.length > 0; grid.hidden = !list.length;
  if (!list.length) {
    blank.innerHTML = `<div class="big"><svg width="30" height="30" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" aria-hidden="true"><circle cx="11" cy="11" r="7"/><path d="M20 20l-4-4"/></svg></div>
      <h2>${esc(T(state.circuits.length ? "aucunResultat" : "aucunCircuit"))}</h2><p>${esc(T(state.circuits.length ? "aucunResultatSous" : "aucunCircuitSous"))}</p>`;
    return;
  }
  grid.innerHTML = list.map(x => {
    const c = x.c, km = kmOf(c.longueur);
    const badges = [x.st === "ok" ? `<span class="bd ok">${esc(T("bConverti"))}</span>` : x.st === "maj" ? `<span class="bd maj">${esc(T("bMaj"))}</span>` : "",
                    x.t.officiel ? `<span class="bd kunos">${esc(T("bKunos"))}</span>` : "",
                    x.t.configs.length > 1 ? `<span class="bd">${esc(T("bConfigs", x.t.configs.length))}</span>` : ""].join("");
    return `<button class="tc" data-t="${esc(x.t.id)}">
      <span class="im" style="${c.apercu ? `background-image:url('${esc(c.apercu)}')` : ""}">${c.apercu ? "" : `<span class="noimg"><svg width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" aria-hidden="true"><path d="M4 18c3-6 5-11 9-11s4 6 7 6"/></svg></span>`}
        ${c.plan ? `<span class="ol" style="background-image:url('${esc(c.plan)}')"></span>` : ""}<span class="badges">${badges}</span></span>
      <span class="bo"><b>${esc(x.name)}</b><small>${esc([c.ville, c.pays].filter(Boolean).join(", ") || x.t.id)}</small>
        <span class="meta">${km ? `<span>${esc(kmText(km))}</span>` : ""}${c.stands ? `<span>${esc(T("stands", c.stands))}</span>` : ""}<span>${esc(size(x.t.taille))}</span></span></span>
    </button>`;
  }).join("");
  grid.querySelectorAll(".tc").forEach(b => b.addEventListener("click", () => openConv(b.dataset.t)));
}

// ---------------------------------------------------------------- fenetre de conversion
const ICON_INFO = `<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-hidden="true"><circle cx="12" cy="12" r="9"/><path d="M12 11v5M12 8h.01"/></svg>`;
const ICON_WARN = `<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-hidden="true"><path d="M12 3l9 16H3z"/><path d="M12 10v4M12 17h.01"/></svg>`;
const ICON_X = `<svg width="14" height="14" viewBox="0 0 12 12" aria-hidden="true"><path d="M2 2l8 8M10 2l-8 8" stroke="currentColor" stroke-width="1.4"/></svg>`;
const STEPS = ["sLecture", "sTextures", "sMaillages", "sCollisions", "sFiche"];
const WEIGHT = [0.12, 0.38, 0.38, 0.04, 0.08];

function openConv(tid, lid) {
  const t = state.circuits.find(x => x.id === tid);
  if (!t) return;
  const l = (lid != null && t.configs.find(c => c.id === lid)) || t.configs.find(c => !c.converti) || t.configs[0];
  conv = { t, mode: "form", opts: optsFor(l) };
  $("m-conv").hidden = false;
  renderConv();
}
function optsFor(l) {
  const d = l.converti && destOf(l.dest);
  return { l, nom: d ? d.nom : l.nom, prix: d ? +d.prix : autoPrice(kmOf(l.longueur)), textures: d ? +(d.textures || 1024) : 1024,
           point: d ? (d.point || "pit") : "pit", decor: d ? d.decor !== "0" : true };
}
function closeConv() { $("m-conv").hidden = true; conv = null; }

function renderConv() {
  const t = conv.t, o = conv.opts, l = o.l, km = kmOf(l.longueur);
  const exists = l.converti && destOf(l.dest);
  const box = $("conv");
  const art = `<div class="cv-art">
      <div class="im" style="${l.apercu ? `background-image:url('${esc(l.apercu)}')` : ""}">
        ${l.plan ? `<span class="ol" style="background-image:url('${esc(l.plan)}')"></span>` : ""}
        <div class="cap"><h2>${esc(l.nom || t.id)}</h2><span>${esc([l.ville, l.pays].filter(Boolean).join(", "))}</span></div></div>
      <div class="facts">
        <div><small>${esc(T("longueur"))}</small><b>${esc(kmText(km) || "—")}</b></div>
        <div><small>${esc(T("standsT"))}</small><b>${esc(l.stands || "—")}</b></div>
        <div><small>${esc(T("taille"))}</small><b>${esc(size(t.taille))}</b></div>
      </div>
    </div>`;
  let body;
  if (conv.mode === "form") {
    const desc = strip(l.description);
    body = `<div class="cv-top"><div><h2>${esc(T(exists ? "reconvertirTitre" : "convTitre"))}</h2>
        <div class="credit">${l.auteur ? T("circuitPar", `<b>${esc(l.auteur)}</b>`) : esc(T("auteurInconnu"))}${l.version ? ` · v${esc(l.version)}` : ""}</div></div>
        <button class="x" id="cv-x" aria-label="${esc(T("fermer"))}">${ICON_X}</button></div>
      ${desc ? `<p class="desc">${esc(desc)}</p>` : ""}
      ${t.configs.length > 1 ? `<div class="field"><span class="fl2">${esc(T("config"))}</span><div class="layouts">${t.configs.map(c => `<button class="lay${c === l ? " on" : ""}" data-l="${esc(c.id)}">${c.converti ? `<span class="dot ${esc((destOf(c.dest) || {}).etat || "ok")}"></span>` : ""}${esc(c.nom || c.id || T("configDefaut"))}</button>`).join("")}</div></div>` : ""}
      <div class="two">
        <label class="field"><span class="fl2">${esc(T("nomAgence"))}</span><input class="txt" id="cv-nom" value="${esc(o.nom)}" maxlength="40" spellcheck="false"></label>
        <label class="field"><span class="fl2">${esc(T("prixBillet"))}</span><span class="money"><input class="txt" id="cv-prix" type="number" min="50" max="20000" step="50" value="${o.prix}"><span>mk</span></span></label>
      </div>
      <div class="field"><span class="fl2">${esc(T("arrivee"))}<i>${esc(T("arriveeSous"))}</i></span>
        <div class="seg" id="cv-point">${["pit", "start", "hotlap"].map(p => `<button data-v="${p}"${o.point === p ? ' class="on"' : ""}>${esc(T("p_" + p))}</button>`).join("")}</div></div>
      <div class="field"><span class="fl2">${esc(T("texturesT"))}<i>${esc(T("texturesSous"))}</i></span>
        <div class="seg" id="cv-tex">${[[512, "tLeger"], [1024, "tNormal"], [2048, "tDetaille"]].map(([v, k]) => `<button data-v="${v}"${o.textures === v ? ' class="on"' : ""}>${esc(T(k))}<small>${v} px</small></button>`).join("")}</div></div>
      <label class="cb"><input type="checkbox" id="cv-decor"${o.decor ? " checked" : ""}><span>${esc(T("decor"))} <small>${esc(T("decorSous"))}</small></span></label>
      ${exists ? `<div class="info warn">${ICON_WARN}<span>${esc(T("remplace"))}</span></div>` : ""}
      ${!state.mwc ? `<div class="info warn">${ICON_WARN}<span>${esc(T("pasDeJeu"))}</span></div>` : ""}
      <div class="info">${ICON_INFO}<span>${esc(T("convInfo"))}</span></div>
      <div class="row end"><button class="btn" id="cv-annuler">${esc(T("annuler"))}</button>
        <button class="btn pri" id="cv-go"${state.mwc ? "" : " disabled"}><svg width="15" height="15" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M21 16v-2l-8-5V3.5a1.5 1.5 0 0 0-3 0V9l-8 5v2l8-2.5V19l-2 1.5V22l3.5-1 3.5 1v-1.5L13 19v-5.5z"/></svg>${esc(T(exists ? "reconvertir" : "convertir"))}</button></div>`;
  } else {
    const run = conv.mode === "run";
    const pct = Math.round(progressOf() * 100);
    body = `<div class="cv-top"><div><h2>${esc(T(run ? "convEnCours" : conv.mode === "done" ? "convFinie" : conv.mode === "cancel" ? "convAnnulee" : "convEchec"))}</h2>
        <div class="credit">${esc(o.nom)}</div></div>${run ? "" : `<button class="x" id="cv-x" aria-label="${esc(T("fermer"))}">${ICON_X}</button>`}</div>
      <div class="prog"><div class="prog-h"><b id="cv-pc">${pct} %</b><span id="cv-txt">${esc(conv.texte || "")}</span></div>
        <div class="bar" role="progressbar" aria-valuemin="0" aria-valuemax="100" aria-valuenow="${pct}"><span id="cv-bar" style="width:${pct}%"></span></div></div>
      <ol class="steps" id="cv-steps">${STEPS.map((k, i) => `<li class="${stepClass(i)}"><span class="ic">${stepClass(i) === "done" ? `<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M5 12l5 5 9-10"/></svg>` : ""}</span>${esc(T(k))}<span class="v">${stepClass(i) === "cur" ? Math.round(conv.f * 100) + " %" : ""}</span></li>`).join("")}</ol>
      ${conv.mode === "done" ? `<div class="done-box"><b>${esc(T("pret"))}</b><span>${esc(T("pretSous", T("aero_" + window.GAME)))}</span></div>
        <div class="sum"><div><b>${num(conv.res.triangles)}</b><small>${esc(T("triangles"))}</small></div><div><b>${conv.res.textures}</b><small>${esc(T("texturesN"))}</small></div>
          <div><b>${esc(size(conv.res.octets))}</b><small>${esc(T("surDisque"))}</small></div><div><b>${(conv.res.ms / 1000).toFixed(1).replace(".", dec())} s</b><small>${esc(T("duree"))}</small></div></div>
        ${conv.res.grises ? `<div class="info warn">${ICON_WARN}<span>${esc(T("grises", conv.res.grises))}</span></div>` : ""}` : ""}
      ${conv.mode === "fail" ? `<div class="err">${esc(conv.err || "")}</div>` : ""}
      <div class="log" id="cv-log">${esc(conv.log.join("\n"))}</div>
      <div class="row end">${run ? `<button class="btn" id="cv-stop">${esc(T("annuler"))}</button>`
        : conv.mode === "done" ? `<button class="btn" id="cv-fermer">${esc(T("fermer"))}</button><button class="btn pri" id="cv-voir">${esc(T("voirDest"))}</button>`
        : `<button class="btn" id="cv-fermer">${esc(T("fermer"))}</button><button class="btn pri" id="cv-retry">${esc(T("reessayer"))}</button>`}</div>`;
  }
  box.innerHTML = art + `<div class="cv-body">${body}</div>`;
  const on = (id, f) => { const e = $(id); if (e) e.addEventListener("click", f); };
  on("cv-x", closeConv); on("cv-annuler", closeConv); on("cv-fermer", closeConv);
  on("cv-stop", () => { send({ type: "annuler" }); $("cv-stop").disabled = true; });
  on("cv-voir", () => { const id = conv.res.id; closeConv(); openDest(id); });
  on("cv-retry", () => { conv.mode = "form"; renderConv(); });
  on("cv-go", startConv);
  const log = $("cv-log"); if (log) log.scrollTop = log.scrollHeight;
  if (conv.mode !== "form") return;
  const keep = () => { o.nom = $("cv-nom").value; o.prix = +$("cv-prix").value || o.prix; o.decor = $("cv-decor").checked; };
  box.querySelectorAll(".lay").forEach(b => b.addEventListener("click", () => { conv.opts = optsFor(t.configs.find(c => c.id === b.dataset.l)); renderConv(); }));
  box.querySelectorAll("#cv-point button").forEach(b => b.addEventListener("click", () => { keep(); o.point = b.dataset.v; renderConv(); }));
  box.querySelectorAll("#cv-tex button").forEach(b => b.addEventListener("click", () => { keep(); o.textures = +b.dataset.v; renderConv(); }));
  $("cv-nom").addEventListener("keydown", e => { if (e.key === "Enter") startConv(); });
}
function stepClass(i) {
  if (conv.mode === "done") return "done";
  return i < conv.step ? "done" : i === conv.step && conv.mode === "run" ? "cur" : "wait";
}
function progressOf() {
  if (conv.mode === "done") return 1;
  let p = 0;
  for (let i = 0; i < conv.step; i++) p += WEIGHT[i];
  return p + (WEIGHT[conv.step] || 0) * conv.f;
}
function startConv() {
  const o = conv.opts;
  o.nom = $("cv-nom").value.trim() || o.l.nom; o.prix = Math.max(50, Math.min(20000, +$("cv-prix").value || autoPrice(kmOf(o.l.longueur)))); o.decor = $("cv-decor").checked;
  if (viewer && current === o.l.dest) viewer.clear();   // (fichiers remplaces : l'apercu se recharge apres)
  conv.mode = "run"; conv.step = 0; conv.f = 0; conv.texte = ""; conv.log = []; conv.err = null; conv.res = null;
  renderConv();
  send({ type: "convertir", circuit: conv.t.id, config: o.l.id, nom: o.nom, prix: o.prix, textures: o.textures, point: o.point, decor: o.decor ? "True" : "False" });
}
function onProgress(m) {
  if (!conv || conv.mode !== "run") return;
  const rerender = m.etape !== conv.step;
  conv.step = m.etape; conv.f = m.f; if (m.texte) conv.texte = m.texte;
  if (rerender) { renderConv(); return; }
  const pct = Math.round(progressOf() * 100);
  $("cv-pc").textContent = pct + " %"; $("cv-bar").style.width = pct + "%"; $("cv-txt").textContent = conv.texte;
  const cur = $("cv-steps").querySelector("li.cur .v"); if (cur) cur.textContent = Math.round(conv.f * 100) + " %";
}
function onLog(text) {
  if (!conv) return;
  conv.log.push(text);
  const log = $("cv-log");
  if (log) { log.textContent = conv.log.join("\n"); log.scrollTop = log.scrollHeight; }
}

// ---------------------------------------------------------------- destination
function openDest(id) {
  if (!destOf(id)) return;
  page = "dest"; current = id;
  $("biblio").hidden = true; $("dest").hidden = false;
  renderSide();
  renderDestPane();
  loadPreview();
}
function loadPreview() {
  const d = destOf(current);
  if (!viewer) viewer = new Viewer($("c3d"), { onLoad: viewLoading, onTex: (i, n) => { const c = $("view-tex"); if (c) { c.textContent = T("chargeTex", i, n); c.hidden = i >= n; } } });
  $("view-tex").hidden = true;
  $("v-collision").classList.remove("on");
  viewer.load(`https://dest.local/${urlEnc(d.id)}/`, d.date || "", d);
}
function viewLoading(phase, f, err) {
  const box = $("view-load");
  if (phase === "fini") { box.hidden = true; return; }
  box.hidden = false;
  $("view-load-t").textContent = phase === "erreur" ? T("apercuErreur", err) : T("chargeMaillages", Math.round(f * 100));
  box.querySelector(".spin").hidden = phase === "erreur";
  box.querySelector(".bar").hidden = phase === "erreur";
  $("view-bar").style.width = Math.round(f * 100) + "%";
}
$("v-ensemble").addEventListener("click", () => viewer && viewer.frameAll());
$("v-arrivee").addEventListener("click", () => viewer && viewer.frameArrival());
$("v-collision").addEventListener("click", () => { if (!viewer) return; $("v-collision").classList.toggle("on", viewer.toggleCollision()); });

function renderDestPane() {
  const d = destOf(current), inf = d.infos || {};
  const pts = inf.points || {};
  const src = String(d.source || "").split("/");
  const t = state.circuits.find(x => x.id === src[0]);
  const l = t && t.configs.find(c => c.id === (src[1] || ""));
  const tri = $("view-tri");
  if (tri) { tri.textContent = T("triN", num(inf.triangles || 0)); tri.hidden = !inf.triangles; }
  const pane = $("dest-pane");
  pane.innerHTML = `<div class="dh">
      <h2>${esc(d.nom)}</h2><span class="pl">${esc([d.pays, d.auteur ? T("parAuteur", d.auteur) : ""].filter(Boolean).join(" · "))}</span>
      <span class="pill ${esc(d.etat)}"><span class="dot ${esc(d.etat)}"></span>${esc(T("etat_" + d.etat))}</span>
    </div>
    ${d.etat === "maj" ? `<div class="sec"><div class="info warn">${ICON_WARN}<span>${esc(T("majSous"))}</span></div><button class="btn pri wide" id="d-reconv">${esc(T("reconvertir"))}</button></div>`
      : d.etat === "introuvable" ? `<div class="sec"><div class="info warn">${ICON_WARN}<span>${esc(T("introuvableSous"))}</span></div></div>` : ""}
    <div class="sec">
      <span class="st">${esc(T("ficheAgence"))}</span>
      <label class="field"><span class="fl2">${esc(T("nomAgence"))}</span><input class="txt" id="d-nom" value="${esc(d.nom)}" maxlength="40" spellcheck="false"></label>
      <label class="field"><span class="fl2">${esc(T("prixBillet"))}</span><span class="money"><input class="txt" id="d-prix" type="number" min="50" max="20000" step="50" value="${esc(d.prix)}"><span>mk</span></span></label>
      <div class="field"><span class="fl2">${esc(T("arrivee"))}</span>
        <div class="seg" id="d-point">${["pit", "start", "hotlap"].map(p => `<button data-v="${p}"${(d.point || "pit") === p ? ' class="on"' : ""}${pts[p] ? "" : " disabled"}>${esc(T("p_" + p))}</button>`).join("")}</div></div>
      <button class="btn pri wide" id="d-save" disabled>${esc(T("enregistrer"))}</button>
      <p class="hint">${esc(T("ficheSous"))}</p>
    </div>
    <div class="sec">
      <span class="st">${esc(T("contenu"))}</span>
      <div class="kv">
        <span>${esc(T("triangles"))}</span><span>${num(inf.triangles || 0)}</span>
        <span>${esc(T("texturesN"))}</span><span>${inf.textures ?? "?"}${inf.texturesGrises ? " (" + inf.texturesGrises + " " + esc(T("grisesCourt")) + ")" : ""} · ${esc(d.textures || "?")} px</span>
        <span>${esc(T("collisions"))}</span><span>${num(inf.collisions || 0)}</span>
        <span>${esc(T("surDisque"))}</span><span>${esc(size(inf.octets || 0))}</span>
        <span>${esc(T("longueur"))}</span><span>${esc(kmText(kmOf(inf.longueur)) || "?")}</span>
        <span>${esc(T("decorK"))}</span><span>${esc(T(d.decor === "0" ? "non" : "oui"))}</span>
        <span>${esc(T("source"))}</span><span>${esc(d.source || "?")}</span>
        <span>${esc(T("convertiLe"))}</span><span>${esc(d.date || "?")}</span>
      </div>
    </div>
    <div class="sec">
      ${l ? `<button class="btn wide" id="d-reconv2">${esc(T("reconvertirOptions"))}</button>` : ""}
      <button class="btn wide" id="d-dossier">${esc(T("ouvrirDossier"))}</button>
      <button class="btn wide danger" id="d-suppr">${esc(T("supprimer"))}</button>
      <p class="hint">${esc(T("creditSous", d.auteur || T("auteurInconnuCourt")))}</p>
    </div>`;
  let point = d.point || "pit";
  const dirty = () => { $("d-save").disabled = $("d-nom").value.trim() === d.nom && String(+$("d-prix").value) === String(+d.prix) && point === (d.point || "pit") || !$("d-nom").value.trim(); };
  $("d-nom").addEventListener("input", dirty); $("d-prix").addEventListener("input", dirty);
  pane.querySelectorAll("#d-point button").forEach(b => b.addEventListener("click", () => {
    point = b.dataset.v; pane.querySelectorAll("#d-point button").forEach(x => x.classList.toggle("on", x === b)); dirty();
    if (viewer) viewer.showArrival(pts[point]);
  }));
  $("d-save").addEventListener("click", () => {
    send({ type: "modifier", id: d.id, nom: $("d-nom").value.trim(), prix: Math.max(50, Math.min(20000, +$("d-prix").value || +d.prix)), point: point !== (d.point || "pit") ? point : "" });
    $("d-save").disabled = true;
  });
  const reconv = () => openConv(t.id, l.id);
  if ($("d-reconv")) $("d-reconv").addEventListener("click", reconv);
  if ($("d-reconv2")) $("d-reconv2").addEventListener("click", reconv);
  $("d-dossier").addEventListener("click", () => send({ type: "ouvrirDossier", id: d.id }));
  $("d-suppr").addEventListener("click", () => confirmBox(T("supprTitre", d.nom), T("supprSous"), T("supprimer"), () => {
    if (viewer) viewer.clear();
    send({ type: "supprimer", id: d.id });
    showLibrary();
  }));
}

// ---------------------------------------------------------------- confirmation
function confirmBox(title, text, ok, act) {
  $("confirm").innerHTML = `<h2>${esc(title)}</h2><p class="sub" style="font-size:14px">${esc(text)}</p>
    <div class="row end"><button class="btn" id="cf-non">${esc(T("annuler"))}</button><button class="btn pri" id="cf-oui">${esc(ok)}</button></div>`;
  $("m-confirm").hidden = false;
  $("cf-non").addEventListener("click", closeConfirm);
  $("cf-oui").addEventListener("click", () => { closeConfirm(); act(); });
  $("cf-oui").focus();
}
function closeConfirm() { $("m-confirm").hidden = true; }

// ---------------------------------------------------------------- messages du programme
function onMessage(m) {
  switch (m.type) {
    case "etat": {
      const first = !state;
      const gameChanged = state && state.jeu !== m.jeu;
      state = m;
      window.GAME = m.jeu || "mwc";
      if (first || gameChanged) applyI18n();
      if (first && m.langue && !lang0) setLang(m.langue);   // (langue du lanceur, sauf choix fait ici)
      renderAll();
      if (first && page === "biblio") $("q").focus();
      break;
    }
    case "progression": onProgress(m); break;
    case "journal": onLog(m.texte); break;
    case "fini": if (conv) { conv.mode = "done"; conv.res = m; conv.texte = ""; renderConv(); } break;
    case "annule": if (conv) { conv.mode = "cancel"; renderConv(); } break;
    case "echec": if (conv) { conv.mode = "fail"; conv.err = m.texte; renderConv(); } else toast(m.texte, true); break;
    case "toast": toast(T("t_" + m.cle), m.cle !== "enregistre"); break;
    case "erreur": toast(m.texte, true); break;
  }
}
if (host) host.addEventListener("message", e => onMessage(e.data));
window.essaiRecv = onMessage;   // (essai hors du programme)

// ---------------------------------------------------------------- demarrage
let lang0 = null;
try { lang0 = localStorage.getItem("langue"); } catch { }
setLang(lang0 || (navigator.language || "en").slice(0, 2));
send({ type: "pret" });
