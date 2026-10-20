// Edition d'une carte (V1, 20/10) : objets ajoutes (copies d'objets du jeu, modeles 3D importes), deplaces / tournes /
// mis a l'echelle au gizmo, poses au sol, objets du jeu masques ; annuler / retablir ; enregistrement automatique
// (projet.json par le programme : Projects.cs). Positions gardees dans le repere d'Unity (z inverse par rapport a three),
// comme le mod les relira (MapPacks.cs).
import * as THREE from "three";
import { TransformControls } from "./vendor/jsm/controls/TransformControls.js";
import { GLTFLoader } from "./vendor/jsm/loaders/GLTFLoader.js";
import { FBXLoader } from "./vendor/jsm/loaders/FBXLoader.js";
import { OBJLoader } from "./vendor/jsm/loaders/OBJLoader.js";
import { Viewer } from "./viewer.js";

const U = {   // repere d'Unity <-> three : z inverse ; quaternion (x, y, z, w) -> (-x, -y, z, w)
  posTo: (p, v) => v.set(p[0], p[1], -p[2]),
  posFrom: v => [r3(v.x), r3(v.y), r3(-v.z)],
  rotTo: (q, o) => o.set(-q[0], -q[1], q[2], q[3]),
  rotFrom: o => [r5(-o.x), r5(-o.y), r5(o.z), r5(o.w)],
};
const r3 = x => Math.round(x * 1000) / 1000, r5 = x => Math.round(x * 100000) / 100000;
const deg = THREE.MathUtils.radToDeg, rad = THREE.MathUtils.degToRad;
let nextId = 1;

export class Edition {
  constructor({ viewer, ui, dossier, data, send, onState }) {
    this.v = viewer; this.ui = ui; this.dossier = dossier; this.send = send; this.onState = onState || (() => { });
    this.proj = data;
    this.proj.objets = this.proj.objets || [];
    this.proj.masques = this.proj.masques || [];
    for (const o of this.proj.objets) nextId = Math.max(nextId, (o.id | 0) + 1);
    this.root = new THREE.Group();
    this.root.name = "ajouts";
    viewer.scene.add(this.root);
    viewer.extra = this.root;
    this.nodes = new Map();
    this.sel = null;          // { rec } ou { idx } (objet du jeu)
    this.hist = []; this.future = [];
    this.loaders = { glb: new GLTFLoader(), gltf: new GLTFLoader(), fbx: new FBXLoader(), obj: new OBJLoader() };
    // gizmo
    const tc = this.tc = new TransformControls(viewer.camera, viewer.renderer.domElement);
    tc.setSize(0.9);
    tc.addEventListener("dragging-changed", e => { viewer.blockPick = e.value; if (!e.value) this.commit(); });
    tc.addEventListener("objectChange", () => { this.fromNode(this.sel && this.sel.rec); this.renderProps(true); });
    viewer.scene.add(tc);
    viewer.onPick = hit => this.pick(hit);
    viewer.onDispose = () => this.dispose();
    this.key = e => this.onKey(e);
    window.addEventListener("keydown", this.key);
    this.snapshot = JSON.stringify({ objets: this.proj.objets, masques: this.proj.masques });
  }

  // ---------------------------------------------------------------- chargement
  start() {
    if (!this.v.info.objets.length || this.v.info.format < 2) this.ui.warn(T("cacheAncien"));
    for (const rec of this.proj.objets) this.build(rec);
    this.applyMasks();
    this.renderTools();
    this.renderList();
    this.renderProps();
  }

  build(rec) {
    const node = new THREE.Group();
    node.userData.rec = rec;
    this.toNode(rec, node);
    this.root.add(node);
    this.nodes.set(rec.id, node);
    if (rec.type === "jeu") {
      const idx = this.v.byPath.get(rec.src.p);
      if (idx === undefined) { rec.manque = true; node.add(placeholder()); return node; }
      delete rec.manque;
      node.add(this.gameMesh(idx));
    } else if (rec.type === "modele") this.loadModel(rec, node);
    return node;
  }

  // copie d'un objet du cache : meme maillage et memes matieres ; lot (sommets en coordonnees monde) : ramene dans le
  // repere de son objet (r) puis recentre sur sa boite (les objets regroupes ont souvent leur repere au centre de la carte :
  // le gizmo serait a des centaines de metres) -- le centre est garde dans la carte (src.c), le mod recentre pareil
  gameMesh(idx) {
    const ob = this.v.info.objets[idx];
    let g = this.v.geoOf(ob.m);
    if (ob.r) g = this.localGeo(idx).g;
    const mats = this.v.matsFor(ob, g);
    const m = new THREE.Mesh(g, mats.length === 1 ? mats[0] : mats);
    return m;
  }

  localGeo(idx) {
    this.local = this.local || new Map();
    if (!this.local.has(idx)) {
      const ob = this.v.info.objets[idx];
      const g = this.v.geoOf(ob.m).clone().applyMatrix4(Viewer.mat(ob.r).invert());
      g.computeBoundingBox();
      const c = g.boundingBox.getCenter(new THREE.Vector3());
      g.translate(-c.x, -c.y, -c.z);
      this.local.set(idx, { g, c });
    }
    return this.local.get(idx);
  }

  modelUrl(f) { return "https://projets.local/" + encodeURIComponent(this.dossier) + "/modeles/" + encodeURIComponent(f); }

  loadModel(rec, node) {
    const ext = rec.src.fichier.split(".").pop().toLowerCase();
    const ld = this.loaders[ext];
    if (!ld) { node.add(placeholder()); return; }
    ld.load(this.modelUrl(rec.src.fichier), res => {
      const obj = res.scene || res;
      obj.traverse(o => { if (o.isMesh) { o.castShadow = false; const ms = Array.isArray(o.material) ? o.material : [o.material]; ms.forEach(m => { if (m.map) m.map.colorSpace = THREE.SRGBColorSpace; }); } });
      const inner = new THREE.Group();
      inner.add(obj);
      if (rec.unite === undefined) {   // unite du fichier : un modele de plus de 300 « metres » est en centimetres
        const b = new THREE.Box3().setFromObject(obj), s = b.getSize(new THREE.Vector3());
        const big = Math.max(s.x, s.y, s.z);
        rec.unite = big > 300 ? 0.01 : big < 0.05 ? 100 : 1;
        this.save();
      }
      inner.scale.setScalar(rec.unite);
      node.add(inner);
      if (rec.poser) { delete rec.poser; this.dropToGround(rec, node); this.renderProps(); this.save(); }
    }, undefined, err => { console.error(err); node.add(placeholder()); this.ui.warn(T("modeleIllisible", rec.src.fichier)); });
  }

  toNode(rec, node) {
    U.posTo(rec.pos, node.position);
    U.rotTo(rec.rot, node.quaternion);
    node.scale.set(rec.ech[0], rec.ech[1], rec.ech[2]);
    node.updateMatrixWorld(true);
  }
  fromNode(rec) {
    if (!rec) return;
    const n = this.nodes.get(rec.id);
    rec.pos = U.posFrom(n.position); rec.rot = U.rotFrom(n.quaternion); rec.ech = [r3(n.scale.x), r3(n.scale.y), r3(n.scale.z)];
  }

  // ---------------------------------------------------------------- ajouts
  // devant la camera, a 18 m, sur le sol
  spot() {
    const c = this.v.camera, f = new THREE.Vector3(0, 0, -1).applyQuaternion(c.quaternion);
    f.y = 0; if (f.lengthSq() < 1e-4) f.set(0, 0, -1); f.normalize();
    const p = c.position.clone().addScaledVector(f, 18);
    const y = this.groundAt(p.x, p.z, c.position.y + 200);
    if (y !== null) p.y = y;
    return p;
  }

  groundAt(x, z, from, ignore) {
    const ray = new THREE.Raycaster(new THREE.Vector3(x, from, z), new THREE.Vector3(0, -1, 0), 0, 3000);
    const hits = ray.intersectObjects([this.v.groups[0], this.v.groups[1], this.v.groups[2], this.root], true)
      .filter(h => h.object.visible && (!ignore || !isUnder(h.object, ignore)));
    return hits.length ? hits[0].point.y : null;
  }

  addGame(idx, here) {
    const ob = this.v.info.objets[idx];
    if (!ob.p) { this.ui.warn(T("cacheAncien")); return; }
    const m = Viewer.mat(ob.r || ob.t), pos = new THREE.Vector3(), q = new THREE.Quaternion(), s = new THREE.Vector3();
    let c = null;
    if (ob.r) { c = this.localGeo(idx).c; m.multiply(new THREE.Matrix4().makeTranslation(c.x, c.y, c.z)); }
    m.decompose(pos, q, s);
    if (!here) { const p = this.spot(); pos.x = p.x; pos.z = p.z; pos.y = p.y; }
    const rec = { id: nextId++, nom: ob.n, type: "jeu", src: c ? { p: ob.p, n: ob.n, c: U.posFrom(c) } : { p: ob.p, n: ob.n }, pos: U.posFrom(pos), rot: U.rotFrom(q), ech: [r3(s.x), r3(s.y), r3(s.z)], col: "forme" };
    this.proj.objets.push(rec);
    const node = this.build(rec);
    if (!here) this.dropToGround(rec, node);
    this.select({ rec });
    this.commit();
  }

  importModel() { this.send({ type: "importerModele", dossier: this.dossier }); }
  onModel(m) {
    if (m.erreur || !m.fichier) { if (m.erreur) this.ui.warn(T("importEchec", m.erreur)); return; }
    const p = this.spot();
    const rec = { id: nextId++, nom: m.nom, type: "modele", src: { fichier: m.fichier }, pos: U.posFrom(p), rot: [0, 0, 0, 1], ech: [1, 1, 1], col: "boite", poser: true };
    this.proj.objets.push(rec);
    this.build(rec);
    this.select({ rec });
    this.commit();
  }

  duplicate() {
    const rec = this.sel && this.sel.rec;
    if (!rec) return;
    const c = JSON.parse(JSON.stringify(rec));
    c.id = nextId++;
    c.pos = [c.pos[0] + 2, c.pos[1], c.pos[2] + 2];
    this.proj.objets.push(c);
    this.build(c);
    this.select({ rec: c });
    this.commit();
  }

  remove() {
    const rec = this.sel && this.sel.rec;
    if (!rec) return;
    this.proj.objets = this.proj.objets.filter(o => o !== rec);
    const n = this.nodes.get(rec.id);
    if (n) { this.root.remove(n); this.nodes.delete(rec.id); }
    this.select(null);
    this.commit();
  }

  dropToGround(rec, node) {
    node = node || this.nodes.get(rec.id);
    node.updateMatrixWorld(true);
    const b = new THREE.Box3().setFromObject(node);
    if (b.isEmpty()) return;
    const y = this.groundAt(node.position.x, node.position.z, b.max.y + 100, node);
    if (y === null) return;
    node.position.y += y - b.min.y;
    node.updateMatrixWorld(true);
    this.fromNode(rec);
  }

  // objets du jeu masques dans cette carte
  isMasked(idx) { const ob = this.v.info.objets[idx]; return !!(ob && ob.p && this.proj.masques.some(m => m.p === ob.p)); }
  toggleMask(idx) {
    const ob = this.v.info.objets[idx];
    if (!ob || !ob.p) { this.ui.warn(T("cacheAncien")); return; }
    if (this.isMasked(idx)) this.proj.masques = this.proj.masques.filter(m => m.p !== ob.p);
    else this.proj.masques.push({ p: ob.p, n: ob.n });
    this.applyMasks();
    this.commit();
    this.renderProps();
  }
  applyMasks() {
    const set = new Set(this.proj.masques.map(m => m.p));
    this.v.instMeshes.forEach((mesh, i) => {
      if (!mesh) return;
      const masked = set.has(this.v.info.objets[i].p);
      if (masked) { mesh.visible = false; mesh.userData.masked = true; }
      else if (mesh.userData.masked) { mesh.visible = true; delete mesh.userData.masked; }
    });
  }

  // ---------------------------------------------------------------- selection
  pick(hit) {
    if (!hit) { this.select(null); return; }
    let o = hit.object;
    while (o && !o.userData.rec && o.parent) o = o.parent;
    if (o && o.userData.rec) this.select({ rec: o.userData.rec });
    else if (hit.object.userData.idx !== undefined) this.select({ idx: hit.object.userData.idx, point: hit.point });
    else this.select(null);
  }

  select(sel) {
    this.sel = sel;
    if (this.box) { this.v.scene.remove(this.box); this.box.geometry.dispose(); this.box = null; }
    if (sel && sel.rec) { this.tc.attach(this.nodes.get(sel.rec.id)); this.tc.setMode(this.mode || "translate"); }
    else this.tc.detach();
    if (sel && sel.idx !== undefined) { this.box = new THREE.BoxHelper(this.v.instMeshes[sel.idx], 0xffffff); this.box.userData.noPick = true; this.v.scene.add(this.box); }
    this.renderList();
    this.renderProps();
    this.renderTools();
  }

  setMode(m) {
    this.mode = m;
    if (m === "select") this.tc.enabled = false, this.tc.visible = false;
    else { this.tc.enabled = true; this.tc.visible = true; this.tc.setMode(m); }
    this.renderTools();
  }

  focus() {
    const n = this.sel && this.sel.rec ? this.nodes.get(this.sel.rec.id) : this.sel && this.sel.idx !== undefined ? this.v.instMeshes[this.sel.idx] : null;
    if (!n) return;
    const b = new THREE.Box3().setFromObject(n), c = b.getCenter(new THREE.Vector3()), r = Math.max(4, b.getSize(new THREE.Vector3()).length());
    const back = new THREE.Vector3(0, 0, 1).applyQuaternion(this.v.camera.quaternion);
    this.v.camera.position.copy(c).addScaledVector(back, r * 1.4).add(new THREE.Vector3(0, r * 0.4, 0));
  }

  // ---------------------------------------------------------------- historique et enregistrement
  commit() {
    const snap = JSON.stringify({ objets: this.proj.objets, masques: this.proj.masques });
    if (snap === this.snapshot) return;
    this.hist.push(this.snapshot);
    if (this.hist.length > 200) this.hist.shift();
    this.future = [];
    this.snapshot = snap;
    this.renderList();
    this.save();
  }
  undo() { if (!this.hist.length) return; this.future.push(this.snapshot); this.restore(this.hist.pop()); }
  redo() { if (!this.future.length) return; this.hist.push(this.snapshot); this.restore(this.future.pop()); }
  restore(snap) {
    const d = JSON.parse(snap);
    this.snapshot = snap;
    const selId = this.sel && this.sel.rec ? this.sel.rec.id : null;
    this.tc.detach();
    for (const n of this.nodes.values()) this.root.remove(n);
    this.nodes.clear();
    this.proj.objets = d.objets; this.proj.masques = d.masques;
    for (const rec of this.proj.objets) this.build(rec);
    this.applyMasks();
    const rec = selId !== null ? this.proj.objets.find(o => o.id === selId) : null;
    this.select(rec ? { rec } : null);
    this.save();
  }

  save() {
    clearTimeout(this.saveT);
    this.onState("enregistrement");
    this.saveT = setTimeout(() => {
      this.saveT = null;
      const data = JSON.stringify(this.proj, (k, v) => k === "manque" ? undefined : v, 1);
      this.send({ type: "enregistrer", dossier: this.dossier, data });
    }, 700);
  }

  onKey(e) {
    if (e.target.tagName === "INPUT" || e.target.tagName === "SELECT" || e.target.tagName === "TEXTAREA" || this.v.looking) return;
    const k = e.key.toLowerCase();
    if ((e.ctrlKey || e.metaKey) && k === "z") { e.preventDefault(); e.shiftKey ? this.redo() : this.undo(); return; }
    if ((e.ctrlKey || e.metaKey) && k === "y") { e.preventDefault(); this.redo(); return; }
    if ((e.ctrlKey || e.metaKey) && k === "d") { e.preventDefault(); this.duplicate(); return; }
    if (e.ctrlKey || e.metaKey || e.altKey) return;
    if (k === "v") this.setMode("select");
    else if (k === "g") this.setMode("translate");
    else if (k === "r") this.setMode("rotate");
    else if (k === "s") this.setMode("scale");
    else if (k === "f") this.focus();
    else if (k === "delete" || k === "suppr") this.remove();
    else if (k === "escape") this.select(null);
  }

  // enregistrement en attente : tout de suite (retour a l'accueil, fermeture)
  flush() {
    if (!this.saveT) return;
    clearTimeout(this.saveT); this.saveT = null;
    this.send({ type: "enregistrer", dossier: this.dossier, data: JSON.stringify(this.proj, (k, v) => k === "manque" ? undefined : v, 1) });
  }

  dispose() {
    window.removeEventListener("keydown", this.key);
    this.tc.detach(); this.tc.dispose();
    clearTimeout(this.saveT);
  }

  // ---------------------------------------------------------------- interface (panneaux de la visite)
  renderTools() {
    const el = this.ui.tools;
    if (!el) return;
    const mode = this.mode || "translate";
    const b = (id, icon, label, key, on, dis) => `<button class="tool${on ? " on" : ""}" data-t="${id}" ${dis ? "disabled" : ""} title="${esc(label)}${key ? " (" + key + ")" : ""}" aria-label="${esc(label)}">${ICON[icon]}</button>`;
    el.innerHTML = b("select", "select", T("tSelection"), "V", mode === "select")
      + b("translate", "move", T("tDeplacer"), "G", mode === "translate")
      + b("rotate", "rotate", T("tTourner"), "R", mode === "rotate")
      + b("scale", "scale", T("tEchelle"), "S", mode === "scale")
      + `<span class="sep"></span>`
      + b("palette", "box", T("tObjetJeu"), "", false)
      + b("import", "import", T("tImporter"), "", false)
      + b("route", "road", T("tRoute") + " — " + T("bientot"), "", false, true)
      + `<span class="sep"></span>`
      + b("undo", "undo", T("tAnnuler"), "Ctrl+Z", false, !this.hist.length)
      + b("redo", "redo", T("tRetablir"), "Ctrl+Y", false, !this.future.length);
    el.querySelectorAll("button[data-t]").forEach(btn => btn.onclick = () => {
      const t = btn.dataset.t;
      if (["select", "translate", "rotate", "scale"].includes(t)) this.setMode(t);
      else if (t === "palette") this.openPalette();
      else if (t === "import") this.importModel();
      else if (t === "undo") this.undo();
      else if (t === "redo") this.redo();
    });
  }

  renderList() {
    const el = this.ui.list;
    if (!el) return;
    const sel = this.sel && this.sel.rec;
    let h = `<div class="ph sub">${esc(T("mesAjouts"))}<span class="n">${this.proj.objets.length}</span></div>`;
    if (!this.proj.objets.length) h += `<p class="empty">${esc(T("ajoutsVide"))}</p>`;
    h += this.proj.objets.map(o => `<button class="row${o === sel ? " sel" : ""}${o.manque ? " manque" : ""}" data-id="${o.id}">${o.type === "modele" ? ICON.import : ICON.box}<span>${esc(o.nom)}</span><span class="n">${o.type === "modele" ? T("importe") : T("jeu")}</span></button>`).join("");
    if (this.proj.masques.length) {
      h += `<div class="ph sub">${esc(T("masques"))}<span class="n">${this.proj.masques.length}</span></div>`;
      h += this.proj.masques.map((m, i) => `<div class="row dim">${ICON.eyeoff}<span>${esc(m.n)}</span><button class="mini" data-unmask="${i}">${esc(T("reafficher"))}</button></div>`).join("");
    }
    el.innerHTML = h;
    el.querySelectorAll("button[data-id]").forEach(b => b.onclick = () => { const rec = this.proj.objets.find(o => o.id == b.dataset.id); if (rec) { this.select({ rec }); } });
    el.querySelectorAll("button[data-unmask]").forEach(b => b.onclick = () => { this.proj.masques.splice(+b.dataset.unmask, 1); this.applyMasks(); this.commit(); this.renderProps(); });
  }

  renderProps(live) {
    const el = this.ui.props;
    if (!el) return;
    const s = this.sel;
    if (live && s && s.rec && el.querySelector("[data-f]")) {   // (pendant un glisser du gizmo : seulement les valeurs)
      const vals = this.values(s.rec);
      el.querySelectorAll("input[data-f]").forEach(i => { if (document.activeElement !== i) i.value = vals[i.dataset.f]; });
      return;
    }
    if (!s) { el.innerHTML = `<div class="ph">${esc(T("proprietes"))}</div><p class="empty pad">${esc(T("propsVide"))}</p>`; return; }
    if (s.idx !== undefined) {
      const ob = this.v.info.objets[s.idx], masked = this.isMasked(s.idx);
      el.innerHTML = `<div class="ph">${esc(T("objetJeu"))}</div><div class="sec"><b class="nm">${esc(ob.n)}</b>
        <div class="kv"><span>${esc(T("chemin"))}</span><span>${esc(ob.p || "—")}</span></div>
        <button class="btn pri wide" data-a="copier">${ICON.box}${esc(T("copierCarte"))}</button>
        <button class="btn wide" data-a="masquer">${masked ? ICON.eye : ICON.eyeoff}${esc(masked ? T("reafficher") : T("masquerCarte"))}</button>
        <p class="hint">${esc(T("copierAide"))}</p></div>`;
      el.querySelector('[data-a="copier"]').onclick = () => this.addGame(s.idx, true);
      el.querySelector('[data-a="masquer"]').onclick = () => this.toggleMask(s.idx);
      return;
    }
    const rec = s.rec, v = this.values(rec);
    const num = (f, c) => `<label class="num"><b style="color:${c}">${f.slice(-1).toUpperCase()}</b><input data-f="${f}" value="${v[f]}" inputmode="decimal" spellcheck="false"></label>`;
    el.innerHTML = `<div class="ph">${esc(T("proprietes"))}</div>
      <div class="sec"><label class="sl" for="p-nom">${esc(T("nom"))}</label><input id="p-nom" class="txt" value="${esc(rec.nom)}" spellcheck="false">
        <div class="hint">${rec.type === "modele" ? esc(rec.src.fichier) : esc(T("objetJeu")) + " · " + esc(rec.src.n)}${rec.manque ? " · <span class=warn>" + esc(T("introuvableIci")) + "</span>" : ""}</div></div>
      <div class="sec"><div class="sl">${esc(T("position"))} (m)</div><div class="xyz">${num("px", "#ff6e6e")}${num("py", "#7cf2c4")}${num("pz", "#6aa8ff")}</div>
        <div class="sl">${esc(T("rotation"))} (°)</div><div class="xyz">${num("rx", "#ff6e6e")}${num("ry", "#7cf2c4")}${num("rz", "#6aa8ff")}</div>
        <div class="sl">${esc(T("echelle"))}</div><div class="xyz">${num("sx", "#ff6e6e")}${num("sy", "#7cf2c4")}${num("sz", "#6aa8ff")}</div>
        <button class="btn wide" data-a="sol">${ICON.down}${esc(T("poserSol"))}</button></div>
      <div class="sec"><div class="sl">${esc(T("collisions"))}</div><div class="seg">${["aucune", "boite", "forme"].map(c => `<button data-col="${c}" class="${rec.col === c ? "on" : ""}">${esc(T("col_" + c))}</button>`).join("")}</div></div>
      <div class="sec row2"><button class="btn" data-a="dup">${ICON.copy}${esc(T("dupliquer"))}</button><button class="btn danger" data-a="suppr">${ICON.trash}${esc(T("supprimer"))}</button></div>`;
    el.querySelector("#p-nom").onchange = e => { rec.nom = e.target.value.trim() || rec.nom; this.commit(); };
    el.querySelectorAll("input[data-f]").forEach(i => i.onchange = () => { this.setValue(rec, i.dataset.f, parseFloat(i.value.replace(",", "."))); });
    el.querySelectorAll("[data-col]").forEach(b => b.onclick = () => { rec.col = b.dataset.col; this.commit(); this.renderProps(); });
    el.querySelector('[data-a="sol"]').onclick = () => { this.dropToGround(rec); this.commit(); this.renderProps(); };
    el.querySelector('[data-a="dup"]').onclick = () => this.duplicate();
    el.querySelector('[data-a="suppr"]').onclick = () => this.remove();
  }

  // valeurs affichees : position d'Unity, rotation d'Unity en degres (Euler ZXY d'Unity = 'YXZ' de three, x et y inverses)
  values(rec) {
    const q = new THREE.Quaternion(); U.rotTo(rec.rot, q);
    const e = new THREE.Euler().setFromQuaternion(q, "YXZ");
    const f = x => (Math.round(x * 100) / 100).toString();
    return { px: f(rec.pos[0]), py: f(rec.pos[1]), pz: f(rec.pos[2]), rx: f(norm(-deg(e.x))), ry: f(norm(-deg(e.y))), rz: f(norm(deg(e.z))), sx: f(rec.ech[0]), sy: f(rec.ech[1]), sz: f(rec.ech[2]) };
  }
  setValue(rec, f, x) {
    if (!isFinite(x)) { this.renderProps(); return; }
    const v = this.values(rec);
    v[f] = x;
    rec.pos = [+v.px, +v.py, +v.pz];
    rec.ech = [+v.sx || 1, +v.sy || 1, +v.sz || 1];
    const q = new THREE.Quaternion().setFromEuler(new THREE.Euler(-rad(+v.rx), -rad(+v.ry), rad(+v.rz), "YXZ"));
    rec.rot = U.rotFrom(q);
    this.toNode(rec, this.nodes.get(rec.id));
    this.commit();
    this.renderProps(true);
  }


  // ---------------------------------------------------------------- compilation (.mwcmap : MapPacks.cs du mod)
  openCompile() {
    const dlg = this.ui.compile, p = this.proj;
    const models = p.objets.filter(o => o.type === "modele"), games = p.objets.filter(o => o.type === "jeu");
    const missing = p.objets.filter(o => o.manque).length;
    dlg.querySelector("#cp-nom").value = p.nom || "";
    dlg.querySelector("#cp-auteur").value = p.auteur || "";
    dlg.querySelector("#cp-version").value = p.version || "1.0";
    dlg.querySelector("#cp-desc").value = p.description || "";
    const files = new Set(models.map(m => m.src.fichier));
    dlg.querySelector("#cp-sum").innerHTML = [[games.length, T("cpCopies")], [models.length, T("cpModeles", files.size)], [p.masques.length, T("cpMasques")]]
      .map(([n, t]) => `<div><b>${n}</b><small>${esc(t)}</small></div>`).join("");
    const ok = s => `<div>${OK}${esc(s)}</div>`, warn = s => `<div>${WARN}${esc(s)}</div>`;
    dlg.querySelector("#cp-chk").innerHTML = ok(T("cpAucunFichier")) + (missing ? warn(T("cpManque", missing)) : ok(T("cpTousTrouves")))
      + (p.objets.length || p.masques.length ? "" : warn(T("cpVide")));
    dlg.querySelector("#cp-res").hidden = true;
    dlg.querySelector("#cp-go").disabled = false;
    dlg.hidden = false;
    dlg.querySelector("#cp-annuler").onclick = () => dlg.hidden = true;
    dlg.querySelector("#cp-go").onclick = () => {
      p.nom = dlg.querySelector("#cp-nom").value.trim() || p.nom;
      p.auteur = dlg.querySelector("#cp-auteur").value.trim();
      p.version = dlg.querySelector("#cp-version").value.trim() || "1.0";
      p.description = dlg.querySelector("#cp-desc").value.trim();
      this.save();
      dlg.querySelector("#cp-go").disabled = true;
      this.compile().catch(e => { this.onCompiled({ erreur: e.message || String(e) }); });
    };
  }

  onCompiled(m) {
    const dlg = this.ui.compile, res = dlg.querySelector("#cp-res");
    res.hidden = false;
    dlg.querySelector("#cp-go").disabled = false;
    if (m.erreur) { res.className = "err"; res.textContent = T("cpEchec", m.erreur); return; }
    res.className = "okbox";
    res.innerHTML = `<b>${esc(T("cpFini"))}</b><span>${esc(m.chemin)} · ${Math.max(1, Math.round(m.taille / 1024))} Ko</span>`
      + `<span>${esc(m.mod ? T("cpActive") : T("cpPasMod"))}</span><button class="btn small" id="cp-dir">${esc(T("ouvrirDossier"))}</button>`;
    res.querySelector("#cp-dir").onclick = () => this.send({ type: "dossier", chemin: m.dossier });
  }

  async compile() {
    const p = this.proj, files = {}, lines = ["MWCMAP\t1", "jeu\t" + p.jeu, "nom\t" + cl(p.nom), "auteur\t" + cl(p.auteur), "version\t" + cl(p.version), "description\t" + cl(p.description)];
    const modelIds = new Map(), texIds = new Map();
    let nextModel = 0;
    const f = x => (Math.round(x * 100000) / 100000).toString();
    const v3 = a => a.map(f).join(" ");
    for (const rec of p.objets) {
      if (rec.manque) continue;
      if (rec.type === "jeu") { lines.push(["objet", "jeu", rec.src.p, v3(rec.pos), v3(rec.rot), v3(rec.ech), rec.col, cl(rec.nom)].concat(rec.src.c ? [v3(rec.src.c)] : []).join("\t")); continue; }
      const key = rec.src.fichier + "|" + rec.unite;
      if (!modelIds.has(key)) {
        const node = this.nodes.get(rec.id);
        const chunks = await this.modelChunks(node);
        const ids = [];
        for (const ch of chunks) {
          const id = "m" + nextModel++;
          files["modeles/" + id + ".bin"] = b64(ch.bin);
          lines.push(["modele", id, "modeles/" + id + ".bin"].join("\t"));
          ch.mats.forEach((m, i) => {
            let tex = "-";
            if (m.image) {
              if (!texIds.has(m.image)) { const png = texPng(m.image); if (png) { const n = "textures/t" + texIds.size + ".png"; files[n] = png; texIds.set(m.image, n); } }
              tex = texIds.get(m.image) || "-";
            }
            lines.push(["matiere", id, i, m.c.map(f).join(" "), tex, m.cut ? 1 : 0].join("\t"));
          });
          ids.push(id);
        }
        modelIds.set(key, ids);
      }
      const ids = modelIds.get(key);
      if (ids.length) lines.push(["objet", "modele", ids.join(","), v3(rec.pos), v3(rec.rot), v3(rec.ech), rec.col, cl(rec.nom)].join("\t"));
    }
    for (const m of p.masques) lines.push("masque\t" + m.p);
    files["carte.txt"] = b64(new TextEncoder().encode(lines.join("\n") + "\n"));
    const slug = (p.nom || "carte").normalize("NFD").replace(/[̀-ͯ]/g, "").replace(/[^A-Za-z0-9]+/g, "-").replace(/^-|-$/g, "").toLowerCase() || "carte";
    this.send({ type: "compiler", dossier: this.dossier, jeu: p.jeu, nomFichier: slug + "-" + (p.version || "1.0").replace(/[^0-9A-Za-z.]+/g, "") + ".mwcmap", fichiers: files });
  }

  // maillages d'un modele importe, dans le repere de son objet (echelle de l'unite comprise), repere d'Unity (z inverse,
  // triangles retournes), en morceaux de 60000 sommets au plus (Unity 5.0 : indices 16 bits) ; un sous-maillage par matiere
  async modelChunks(node) {
    node.updateMatrixWorld(true);
    const inv = node.matrixWorld.clone().invert(), chunks = [];
    let cur = null;
    const flush = () => { if (cur && cur.n) chunks.push(packChunk(cur)); cur = null; };
    const meshes = [];
    node.traverse(o => { if (o.isMesh && !o.userData.noPick) meshes.push(o); });
    for (const mesh of meshes) {
      const g0 = mesh.geometry.index ? mesh.geometry.toNonIndexed() : mesh.geometry;
      const pos = g0.attributes.position, nor = g0.attributes.normal, uv = g0.attributes.uv;
      const m = new THREE.Matrix4().multiplyMatrices(inv, mesh.matrixWorld), nm = new THREE.Matrix3().getNormalMatrix(m);
      const mats = Array.isArray(mesh.material) ? mesh.material : [mesh.material];
      const groups = g0.groups.length ? g0.groups : [{ start: 0, count: pos.count, materialIndex: 0 }];
      const a = new THREE.Vector3(), nv = new THREE.Vector3();
      for (const gr of groups) {
        const mat = mats[gr.materialIndex] || mats[0];
        for (let t = gr.start; t + 2 < gr.start + gr.count; t += 3) {
          if (!cur || cur.n + 3 > 60000) { flush(); cur = { n: 0, pos: [], nor: [], uv: [], subs: new Map() }; }
          let sub = cur.subs.get(mat);
          if (!sub) { sub = []; cur.subs.set(mat, sub); }
          const base = cur.n;
          for (const k of [t, t + 2, t + 1]) {   // (triangles retournes : repere main gauche)
            a.fromBufferAttribute(pos, k).applyMatrix4(m);
            cur.pos.push(a.x, a.y, -a.z);
            if (nor) { nv.fromBufferAttribute(nor, k).applyMatrix3(nm).normalize(); cur.nor.push(nv.x, nv.y, -nv.z); } else cur.nor.push(0, 1, 0);
            if (uv) cur.uv.push(uv.getX(k), uv.getY(k)); else cur.uv.push(0, 0);
            cur.n++;
          }
          sub.push(base, base + 1, base + 2);
        }
      }
    }
    flush();
    return chunks;
  }

  // ---------------------------------------------------------------- palette des objets du jeu
  openPalette() {
    const objs = this.v.info.objets;
    if (!this.names) {
      const m = new Map();
      objs.forEach((o, i) => { if (!o.p) return; const e = m.get(o.n); if (e) e.c++; else m.set(o.n, { n: o.n, i, k: o.k, c: 1 }); });
      this.names = [...m.values()].sort((a, b) => a.n.localeCompare(b.n));
    }
    const dlg = this.ui.palette;
    dlg.hidden = false;
    const input = dlg.querySelector("input"), list = dlg.querySelector(".plist");
    const KC = ["#8fa86a", "#c9a46a", "#d07a5a", "#4f8a5b"];
    const draw = () => {
      const q = input.value.trim().toLowerCase();
      const hits = this.names.filter(e => !q || e.n.toLowerCase().includes(q)).slice(0, 300);
      list.innerHTML = hits.map(e => `<button class="row" data-i="${e.i}"><span class="sw" style="background:${KC[e.k] || KC[2]}"></span><span>${esc(e.n)}</span><span class="n">×${e.c}</span></button>`).join("") || `<p class="empty">${esc(T("rienTrouve"))}</p>`;
      list.querySelectorAll("button[data-i]").forEach(b => b.onclick = () => { dlg.hidden = true; this.addGame(+b.dataset.i, false); });
    };
    input.value = "";
    input.oninput = draw;
    draw();
    setTimeout(() => input.focus(), 30);
    dlg.querySelector("[data-close]").onclick = () => dlg.hidden = true;
  }
}

const OK = `<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="var(--ok)" stroke-width="2.6" stroke-linecap="round" aria-hidden="true"><path d="M5 12.5l4.5 4.5L19 7.5"/></svg>`;
const WARN = `<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="var(--warn)" stroke-width="2.4" stroke-linecap="round" aria-hidden="true"><path d="M12 3l9.5 17h-19z"/><path d="M12 10v4M12 17.5v.5"/></svg>`;
const cl = s => String(s ?? "").replace(/[\t\r\n]+/g, " ").trim();
function b64(bytes) {
  const u = bytes instanceof Uint8Array ? bytes : new Uint8Array(bytes.buffer || bytes);
  let s = "";
  for (let i = 0; i < u.length; i += 0x8000) s += String.fromCharCode.apply(null, u.subarray(i, i + 0x8000));
  return btoa(s);
}
// morceau -> format des maillages (comme maillages.bin) : n, drapeaux 3, k, k tailles, positions, normales, uv, indices
function packChunk(c) {
  const mats = [...c.subs.keys()], subs = mats.map(m => c.subs.get(m));
  const ni = subs.reduce((a, s) => a + s.length, 0);
  const buf = new ArrayBuffer(4 * (3 + subs.length + c.n * 8 + ni)), dv = new DataView(buf);
  let o = 0;
  const i32 = v => { dv.setInt32(o, v, true); o += 4; }, f32 = v => { dv.setFloat32(o, v, true); o += 4; };
  i32(c.n); i32(3); i32(subs.length);
  subs.forEach(s => i32(s.length));
  c.pos.forEach(f32); c.nor.forEach(f32); c.uv.forEach(f32);
  subs.forEach(s => s.forEach(i32));
  return { bin: new Uint8Array(buf), mats: mats.map(matInfo) };
}
function matInfo(m) {
  const c = m.color ? m.color.clone().convertLinearToSRGB() : new THREE.Color(1, 1, 1);
  return { c: [c.r, c.g, c.b, m.opacity ?? 1], image: m.map && m.map.image ? m.map.image : null, cut: !!(m.alphaTest > 0 || (m.transparent && m.map)) };
}
// image d'une texture -> PNG (base64), 1024 px au plus
function texPng(img) {
  try {
    const w0 = img.width || img.naturalWidth, h0 = img.height || img.naturalHeight;
    if (!w0 || !h0) return null;
    const k = Math.min(1, 1024 / Math.max(w0, h0));
    const cv = document.createElement("canvas");
    cv.width = Math.max(1, Math.round(w0 * k)); cv.height = Math.max(1, Math.round(h0 * k));
    cv.getContext("2d").drawImage(img, 0, 0, cv.width, cv.height);
    return cv.toDataURL("image/png").split(",")[1];
  } catch { return null; }
}

function placeholder() {
  const m = new THREE.Mesh(new THREE.BoxGeometry(2, 2, 2), new THREE.MeshBasicMaterial({ color: 0xff6e6e, wireframe: true }));
  m.position.y = 1;
  return m;
}
function isUnder(o, root) { for (; o; o = o.parent) if (o === root) return true; return false; }
function norm(a) { a = ((a % 360) + 360) % 360; return a > 180.0001 ? a - 360 : a; }
function esc(s) { return String(s ?? "").replace(/[&<>"]/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c])); }
const sv = d => `<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${d}</svg>`;
const ICON = {
  select: sv('<path d="M5 3l14 8-6 1.8L10 19z"/>'),
  move: sv('<path d="M12 3v18M3 12h18M12 3l-3 3M12 3l3 3M12 21l-3-3M12 21l3-3M3 12l3-3M3 12l3 3M21 12l-3-3M21 12l-3 3"/>'),
  rotate: sv('<path d="M20 12a8 8 0 1 1-2.3-5.6"/><path d="M20 4v4.5h-4.5"/>'),
  scale: sv('<rect x="4" y="10" width="10" height="10" rx="1.5"/><path d="M13 4h7v7M20 4l-8 8"/>'),
  box: sv('<path d="M12 3l8 4.5v9L12 21l-8-4.5v-9z"/><path d="M4 7.5l8 4.5 8-4.5M12 12v9"/>'),
  import: sv('<path d="M12 4v11M7.5 10.5L12 15l4.5-4.5"/><path d="M4 15v3a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-3"/>'),
  road: sv('<path d="M8 3L4 21M16 3l4 18"/><path d="M12 4v3M12 10.5v3M12 17v3"/>'),
  undo: sv('<path d="M9 14L4 9l5-5"/><path d="M4 9h10a6 6 0 0 1 0 12h-3"/>'),
  redo: sv('<path d="M15 14l5-5-5-5"/><path d="M20 9H10a6 6 0 0 0 0 12h3"/>'),
  eye: sv('<path d="M3 12s3.5-6 9-6 9 6 9 6-3.5 6-9 6-9-6-9-6z"/><circle cx="12" cy="12" r="2.5"/>'),
  eyeoff: sv('<path d="M3 12s3.5-6 9-6c1.6 0 3 .5 4.2 1.2M21 12s-3.5 6-9 6c-1.6 0-3-.5-4.2-1.2"/><path d="M4 20L20 4"/>'),
  down: sv('<path d="M12 4v12M7 11l5 5 5-5M4 20h16"/>'),
  copy: sv('<rect x="8" y="8" width="12" height="12" rx="2"/><path d="M16 8V6a2 2 0 0 0-2-2H6a2 2 0 0 0-2 2v8a2 2 0 0 0 2 2h2"/>'),
  trash: sv('<path d="M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3"/>'),
};
