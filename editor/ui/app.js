// Editeur de carte : ecrans (accueil, jeu introuvable, preparation, visite) et messages avec le programme (MainForm.cs).
import { Viewer } from "./viewer.js";

const $ = id => document.getElementById(id);
const host = window.chrome && window.chrome.webview;
const send = m => host ? host.postMessage(m) : window.essaiSend && window.essaiSend(m);
let state = null;          // dernier etat recu (jeux trouves, cartes preparees)
let pick = null;           // fenetre « introuvable » : { jeu, verif }
let prepGame = null;
let viewer = null;

// ---------------------------------------------------------------- fenetre
$("titlebar").addEventListener("mousedown", e => {
  if (e.button !== 0 || e.target.closest("button")) return;
  send({ type: "fenetre", action: "deplacer" });
});
$("titlebar").addEventListener("dblclick", e => { if (!e.target.closest("button")) send({ type: "fenetre", action: "agrandir" }); });
document.querySelectorAll(".wc").forEach(b => b.addEventListener("click", () => send({ type: "fenetre", action: b.dataset.act })));
$("btn-home").addEventListener("click", () => { if (viewer) { viewer.dispose(); viewer = null; } show("accueil"); send({ type: "reessayer" }); });
document.addEventListener("contextmenu", e => e.preventDefault());

function show(screen) {
  for (const s of ["accueil", "preparation", "visite"]) $(s).hidden = s !== screen;
  $("status").hidden = screen !== "visite";
  if (screen === "accueil") { document.body.className = ""; $("logo").textContent = "MS"; $("title").textContent = "My Seasons Car · " + (LANG === "fr" ? "Éditeur de carte" : LANG === "ru" ? "Редактор карты" : LANG === "ar" ? "محرّر الخرائط" : "Map editor"); }
}
function setGame(id) {
  document.body.className = id;
  $("logo").textContent = id === "msc" ? "MS" : "MW";
}
const game = id => state && state.jeux.find(j => j.id === id);
const esc = s => String(s ?? "").replace(/[&<>"]/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c]));
const size = n => n > 1e9 ? (n / 1e9).toFixed(1).replace(".", LANG === "en" || LANG === "ar" ? "." : ",") + ({ ru: " ГБ", en: " GB", ar: " غ.ب" }[LANG] || " Go")
                         : Math.round(n / 1e6) + ({ ru: " МБ", en: " MB", ar: " م.ب" }[LANG] || " Mo");

// ---------------------------------------------------------------- langue
function setLang(l) {
  window.LANG = ["fr", "en", "ru", "ar"].includes(l) ? l : "en";
  $("lang").value = window.LANG;
  applyI18n();
  if (!$("accueil").hidden) show("accueil");   // (titre de la fenetre dans la nouvelle langue)
  if (state) renderCards();
}
$("lang").addEventListener("change", () => { try { localStorage.setItem("langue", $("lang").value); } catch { } setLang($("lang").value); });

// ---------------------------------------------------------------- accueil
const HERO = {
  msc: `<svg viewBox="0 0 400 180" preserveAspectRatio="xMidYMid slice" aria-hidden="true"><rect width="400" height="180" fill="#2e2814"/><circle cx="318" cy="52" r="26" fill="#ffcd46" opacity=".9"/><path d="M0 122 L70 92 L130 112 L210 80 L290 106 L400 86 L400 180 L0 180Z" fill="#2c3a20"/><path d="M0 144 L90 126 L170 140 L260 122 L400 134 L400 180 L0 180Z" fill="#1f2b18"/><path d="M40 180 C120 152 210 150 400 142" stroke="#8b7a55" stroke-width="7" fill="none"/><g fill="#16200f"><path d="M60 134 l8 -26 l8 26z"/><path d="M84 132 l7 -22 l7 22z"/><path d="M300 126 l9 -28 l9 28z"/><path d="M326 128 l7 -22 l7 22z"/></g></svg>`,
  mwc: `<svg viewBox="0 0 400 180" preserveAspectRatio="xMidYMid slice" aria-hidden="true"><rect width="400" height="180" fill="#12203a"/><path d="M40 40 C120 10 220 70 360 30" stroke="#2ff0b0" stroke-width="10" opacity=".25" fill="none"/><path d="M0 118 L80 88 L150 110 L230 76 L310 102 L400 84 L400 180 L0 180Z" fill="#c9d8ea"/><path d="M0 144 L100 128 L180 142 L270 124 L400 136 L400 180 L0 180Z" fill="#e8f0f8"/><path d="M30 180 C140 154 230 148 400 144" stroke="#8fa3bb" stroke-width="7" fill="none"/><g fill="#2b3b52"><path d="M66 132 l8 -26 l8 26z"/><path d="M90 130 l7 -22 l7 22z"/><path d="M296 124 l9 -28 l9 28z"/><path d="M322 126 l7 -22 l7 22z"/></g></svg>`,
};

function renderCards() {
  const box = $("cards");
  box.innerHTML = "";
  for (const g of state.jeux) {
    const c = document.createElement("section");
    c.className = "card " + g.id;
    c.setAttribute("aria-label", g.nom);
    let body;
    if (!g.trouve) {
      body = `<div class="chead"><h2>${esc(g.nom)}</h2><span style="color:var(--warn)">${T("introuvable")}</span></div>
        <div class="path" style="color:#8a8a8a">${T("pasSteam")}</div>
        <div class="st"><span class="dot" style="background:var(--warn)"></span>${T("indiqueExe", g.exe)}</div>
        <div class="row"><button class="btn pri" data-a="choisir">${T("choisirExe", g.exe)}</button><button class="btn" data-a="reessayer">${T("reessayer")}</button></div>`;
    } else {
      const src = { steam: T("viaSteam"), lanceur: T("viaLanceur"), choisi: T("viaChoisi") }[g.source] || "";
      const st = g.carte ? `<span class="dot" style="background:var(--ok)"></span>${T("carteprete", size(g.carte.taille), g.carte.date)}`
                         : !g.lanceur ? `<span class="dot" style="background:var(--warn)"></span>${T("sansLanceur")}`
                         : `<span class="dot" style="background:#8a8a8a"></span>${T("cartePas")}`;
      const main = g.carte ? `<button class="btn pri" data-a="ouvrir">${T("ouvrir", g.nom)}</button><button class="btn" data-a="preparer" ${g.lanceur ? "" : "disabled"}>${T("refaire")}</button>`
                           : `<button class="btn pri" data-a="preparer" ${g.lanceur ? "" : "disabled"}>${T("preparer")}</button>`;
      body = `<div class="chead"><h2>${esc(g.nom)}</h2><span>${src}${g.version ? " · " + esc(g.version) : ""}</span></div>
        <div class="path" title="${esc(g.dossier)}">${esc(g.dossier)}\\${esc(g.exe)}</div>
        <div class="st">${st}</div>
        <div class="row">${main}<button class="btn" data-a="choisir">${T("changerDossier")}</button></div>`;
    }
    c.innerHTML = `<div class="hero">${HERO[g.id]}<span class="tag">${g.id.toUpperCase()}</span></div><div class="cbody">${body}</div>`;
    c.querySelectorAll("button[data-a]").forEach(b => b.addEventListener("click", () => cardAction(g, b.dataset.a)));
    box.appendChild(c);
  }
}

function cardAction(g, a) {
  if (a === "choisir") openPick(g.id);
  else if (a === "reessayer") send({ type: "reessayer" });
  else if (a === "preparer") startPrep(g.id);
  else if (a === "ouvrir") openMap(g.id);
}

// ---------------------------------------------------------------- jeu introuvable
function openPick(id) {
  const g = game(id);
  pick = { jeu: id, verif: null };
  document.body.className = id;
  $("ix-t").textContent = T("ouEst", g.nom);
  $("ix-sous").innerHTML = esc(T("ouEstSous", "\u0001")).replace("\u0001", `<span style="font-family:var(--mono);color:var(--ink)">${esc(g.exe)}</span>`);
  $("ix-chemin").value = g.trouve ? g.dossier + "\\" + g.exe : "";
  $("ix-chk").hidden = true;
  $("ix-valider").disabled = true;
  $("introuvable").hidden = false;
  if (g.trouve) send({ type: "verifier", jeu: id, chemin: $("ix-chemin").value });
}
function closePick() { pick = null; $("introuvable").hidden = true; document.body.className = ""; }
$("ix-annuler").addEventListener("click", closePick);
$("ix-parcourir").addEventListener("click", () => pick && send({ type: "choisirExe", jeu: pick.jeu }));
$("ix-chemin").addEventListener("change", () => pick && send({ type: "verifier", jeu: pick.jeu, chemin: $("ix-chemin").value.trim().replace(/^"|"$/g, "") }));
$("ix-valider").addEventListener("click", () => {
  if (!pick || !pick.verif || !pick.verif.ok) return;
  send({ type: "valider", jeu: pick.jeu, chemin: pick.verif.chemin, retenir: $("ix-retenir").checked });
  closePick();
});
// glisser-deposer : sur toute la fenetre pendant que « introuvable » est ouverte (chemin donne par WebView2)
window.addEventListener("dragover", e => { e.preventDefault(); if (pick) $("ix-drop").classList.add("over"); });
window.addEventListener("dragleave", e => { if (!e.relatedTarget) $("ix-drop").classList.remove("over"); });
window.addEventListener("drop", e => {
  e.preventDefault();
  $("ix-drop").classList.remove("over");
  if (!pick || !e.dataTransfer.files.length) return;
  host && host.postMessageWithAdditionalObjects({ type: "deposer", jeu: pick.jeu }, e.dataTransfer.files);
});

const ICON_OK = `<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="var(--ok)" stroke-width="2.6" stroke-linecap="round" aria-hidden="true"><path d="M5 12.5l4.5 4.5L19 7.5"/></svg>`;
const ICON_NO = `<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="var(--red)" stroke-width="2.6" stroke-linecap="round" aria-hidden="true"><path d="M6 6l12 12M18 6L6 18"/></svg>`;
const ICON_WARN = `<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="var(--warn)" stroke-width="2.4" stroke-linecap="round" aria-hidden="true"><path d="M12 3l9.5 17h-19z"/><path d="M12 10v4M12 17.5v.5"/></svg>`;
function onVerif(v) {
  if (!pick || v.jeu !== pick.jeu) return;
  const g = game(v.jeu);
  pick.verif = v;
  $("ix-chemin").value = v.chemin;
  const data = g.id === "msc" ? "mysummercar_Data" : "mywintercar_Data";
  const lines = [
    [v.exe, v.exe ? T("vExe", g.exe) : T("vExeNon", g.exe)],
    [v.data, v.data ? T("vData", data) : T("vDataNon", data)],
  ];
  let html = lines.map(([ok, t]) => `<div>${ok ? ICON_OK : ICON_NO}${esc(t)}</div>`).join("");
  if (v.ok) html += `<div>${v.version ? ICON_OK : ICON_WARN}${esc(v.version ? T("vVersion", v.version) : T("vVersionInconnue"))}</div>`
                 + `<div>${v.lanceur ? ICON_OK : ICON_WARN}${esc(v.lanceur ? T("vLanceur") : T("vLanceurNon"))}</div>`;
  $("ix-chk").innerHTML = html;
  $("ix-chk").hidden = false;
  $("ix-valider").disabled = !v.ok;
}

// ---------------------------------------------------------------- preparation
const STEPS = [
  { k: "eLancement", st: ["lancement", "chargement", "attente", "releve"] },
  { k: "eSol", st: ["sol"] }, { k: "eRoutes", st: ["routes"] }, { k: "eBatiments", st: ["batiments"] },
  { k: "eVegetation", st: ["vegetation"] }, { k: "eTextures", st: ["textures"] }, { k: "eCache", st: ["cache", "fini"] },
];
const PREP_ART = `<svg viewBox="0 0 400 400" aria-hidden="true"><rect width="400" height="400" fill="#121820"/><g id="tiles"></g>
  <g stroke="var(--acc)" stroke-width="1.2" fill="none" opacity=".25"><path d="M100 0v400M200 0v400M300 0v400M0 100h400M0 200h400M0 300h400"/></g></svg>`;

function startPrep(id) {
  const g = game(id);
  prepGame = id;
  setGame(id);
  $("title").textContent = g.nom + " · " + T("prepTitre");
  $("prep-sous").textContent = T("prepSous", g.nom);
  $("prep-err").hidden = true;
  $("prep-annuler").hidden = false; $("prep-ouvrir").hidden = true; $("prep-retour").hidden = true;
  $("prep-art").innerHTML = PREP_ART;
  renderSteps("lancement", 0);
  show("preparation");
  send({ type: "preparer", jeu: id });
}
function renderSteps(stage, pr) {
  const cur = STEPS.findIndex(s => s.st.includes(stage));
  $("steps").innerHTML = STEPS.map((s, i) => {
    const cls = stage === "fini" || i < cur ? "done" : i === cur ? "cur" : "wait";
    const icon = cls === "done" ? `<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" aria-hidden="true"><path d="M5 12.5l4.5 4.5L19 7.5"/></svg>` : "";
    const v = cls === "done" ? T("fait") : cls === "cur" ? "" : T("enAttente");
    return `<li class="${cls}"><span class="ic">${icon}</span><span>${T(s.k)}</span><span class="v">${v}</span></li>`;
  }).join("");
  const pc = Math.round(Math.max(0, Math.min(1, pr)) * 100);
  $("prep-pc").textContent = pc + " %";
  $("prep-bar").style.width = pc + "%";
  // apercu : les tuiles de la carte se remplissent avec l'avancement
  const tiles = document.querySelector("#tiles");
  if (tiles) {
    const n = Math.round(pc / 100 * 16);
    let h = "";
    for (let i = 0; i < n; i++) h += `<rect x="${(i % 4) * 100}" y="${Math.floor(i / 4) * 100}" width="100" height="100" fill="${document.body.classList.contains("msc") ? "#8fa86a" : "#d9e4f0"}" opacity="${0.75 + ((i * 7) % 5) * 0.05}"/>`;
    tiles.innerHTML = h;
  }
}
function onPrep(m) {
  if (m.jeu !== prepGame) return;
  if (m.erreur) {
    const why = { lanceur: T("errLanceur"), "jeu-ferme": T("errJeuFerme"), delai: T("errDelai"), annule: T("errAnnule"), "lanceur-delai": T("errLanceurDelai") }[m.erreur] || T("errAutre", m.erreur);
    $("prep-err").textContent = why;
    $("prep-err").hidden = false;
    $("prep-annuler").hidden = true; $("prep-retour").hidden = false;
    return;
  }
  renderSteps(m.etape, m.avance);
  $("prep-info").textContent = m.fini ? T("prepFini") : "";
  if (m.fini) { $("prep-annuler").hidden = true; $("prep-ouvrir").hidden = false; $("prep-retour").hidden = false; }
}
$("prep-annuler").addEventListener("click", () => send({ type: "annuler" }));
$("prep-retour").addEventListener("click", () => { show("accueil"); send({ type: "reessayer" }); });
$("prep-ouvrir").addEventListener("click", () => openMap(prepGame));

// ---------------------------------------------------------------- visite
async function openMap(id) {
  const g = game(id);
  setGame(id);
  $("title").textContent = T("carteDe", g.nom);
  $("st-jeu").textContent = g.nom + (g.version ? " " + g.version : "");
  show("visite");
  if (viewer) viewer.dispose();
  viewer = new Viewer({ canvas: $("c3d"), game: id, base: (host ? "https://carte.local/" : "/carte/") + id + "/", ui: {
    loading: $("loading"), loadingText: $("loading-t"), layers: $("layers"), sel: $("sel"), hints: $("hints"), speed: $("speed"),
    pos: $("st-pos"), obj: $("st-obj"), fps: $("st-fps"), lieu: $("st-lieu"),
    persp: $("v-persp"), top: $("v-dessus"), home: $("v-joueur"), esc } });
  window.__viewer = viewer;   // (essais : camera placee par le protocole de debogage, ou dans un navigateur)
  try { await viewer.load(); }
  catch (e) { $("loading-t").textContent = T("errCarte", e.message || e); }
}

// ---------------------------------------------------------------- messages du programme
host && host.addEventListener("message", e => onHostMessage(e));
function onHostMessage(e) {
  const m = e.data;
  if (m.type === "etat") {
    const first = !state;
    state = m;
    if (first) {
      // (langue du lanceur quand il nous lance ; sinon le dernier choix, sinon celle de Windows)
      let l = null; try { l = localStorage.getItem("langue"); } catch { }
      const sys = ["fr", "ru", "ar"].includes(m.langue) ? m.langue : "en";
      setLang(m.langueImposee ? sys : l || sys);
      if (m.ouvrir && game(m.ouvrir) && game(m.ouvrir).carte) { openMap(m.ouvrir); return; }
    }
    renderCards();
  }
  else if (m.type === "verification") onVerif(m);
  else if (m.type === "prep") onPrep(m);
  else if (m.type === "fenetre") document.body.classList.toggle("max", !!m.agrandie);
}
applyI18n();
if (host) send({ type: "pret" });
else import("./essai.js").then(m => m.start({ onMessage: data => window.dispatchEvent(new MessageEvent("essai", { data })) }));   // (page ouverte dans un navigateur : essais sans le programme)
window.addEventListener("essai", e => onHostMessage({ data: e.data }));
