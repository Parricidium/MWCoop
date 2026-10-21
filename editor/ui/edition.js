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
import { buildCatalogue, search, catName, classify } from "./catalogue.js";

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
    this.proj.relief = this.proj.relief || [];
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
    tc.addEventListener("dragging-changed", e => { viewer.blockPick = e.value; if (e.value) this.multiStart(); else { this.multiEnd(); this.commit(); this.showGrid(); } });
    tc.addEventListener("objectChange", () => { if (this.multi.length) this.multiMove(); else this.fromNode(this.sel && this.sel.rec); this.renderProps(true); });
    viewer.scene.add(tc);
    this.multi = [];          // autres ajouts choisis (Ctrl + clic), en plus de sel.rec
    this.pivot = new THREE.Object3D(); viewer.scene.add(this.pivot);
    this.snap = false;
    viewer.onPick = (hit, e) => this.pick(hit, e);
    viewer.onDouble = e => this.onDouble(e);
    viewer.onPointer = (e, ph) => this.pointer(e, ph);
    viewer.onDispose = () => this.dispose();
    this.key = e => this.onKey(e);
    window.addEventListener("keydown", this.key);
    this.snapshot = this.snapshotOf();
  }
  snapshotOf() { return JSON.stringify({ objets: this.proj.objets, masques: this.proj.masques, relief: this.proj.relief }); }

  // ---------------------------------------------------------------- chargement
  start() {
    if (!this.v.info.objets.length || this.v.info.format < 2) this.ui.warn(T("cacheAncien"));
    if (this.proj.relief.length) this.replayRelief();
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
      node.add(this.gameMesh(idx, rec));
    } else if (rec.type === "modele") this.loadModel(rec, node);
    else if (rec.type === "route") { const m = this.roadMesh(rec, false); if (m) node.add(m); }
    else if (rec.type === "lumiere") this.lightNode(rec, node);
    else if (rec.type === "unite") this.unitNode(rec, node);
    else if (rec.type === "eau") {
      const m = new THREE.Mesh(new THREE.PlaneGeometry(1, 1, 4, 4).rotateX(-Math.PI / 2), rec.surface ? this.habitMat(rec.surface) : this.habitMat({ k: "eau" }));
      m.userData.cuit = true;
      node.add(m);
    }
    return node;
  }

  // copie d'un objet du cache : meme maillage et memes matieres ; lot (sommets en coordonnees monde) : ramene dans le
  // repere de son objet (r) puis recentre sur sa boite (les objets regroupes ont souvent leur repere au centre de la carte :
  // le gizmo serait a des centaines de metres) -- le centre est garde dans la carte (src.c), le mod recentre pareil
  gameMesh(idx, rec) {
    const ob = this.v.info.objets[idx];
    let g = this.v.geoOf(ob.m);
    if (ob.r) g = this.localGeo(idx).g;
    if (rec && rec.src.plante) g = this.plantGeo(idx, rec.src.plante);
    const mats = this.v.matsFor(ob, rec && rec.src.plante ? this.v.geoOf(ob.m) : g);   // (plante : les matieres de toute la foret, par rang)
    if (rec && rec.subs) mats.forEach((m, s) => { const h = rec.subs[s] || rec.subs["*"]; if (h) mats[s] = this.habitMat(h, m); });
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
      this.applyLooks(rec, node);
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
    ray.layers.enableAll();
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
    const recs = this.selected();
    if (!recs.length) return;
    const copies = recs.map(rec => {
      const c = JSON.parse(JSON.stringify(rec));
      c.id = nextId++;
      shiftRec(c, 2, 2);
      this.proj.objets.push(c);
      this.build(c);
      return c;
    });
    this.select({ rec: copies[0] });
    this.multi = copies.slice(1); this.attachGizmo(); this.renderList(); this.renderProps();
    this.commit();
  }

  remove() {
    const recs = this.selected();
    if (!recs.length) return;
    for (const rec of recs) {
      this.proj.objets = this.proj.objets.filter(o => o !== rec);
      const n = this.nodes.get(rec.id);
      if (n) { if (this.tc.object === n) this.tc.detach(); this.root.remove(n); this.nodes.delete(rec.id); }
    }
    this.select(null);
    this.commit();
  }

  // ---------------------------------------------------------------- multi-selection, aimantation, presse-papiers
  selected() { return this.sel && this.sel.rec ? [this.sel.rec].concat(this.multi.filter(r => r !== this.sel.rec)) : []; }
  toggleSel(rec) {
    if (!this.sel || !this.sel.rec) { this.select({ rec }); return; }
    if (rec === this.sel.rec) {
      if (!this.multi.length) { this.select(null); return; }
      const next = this.multi.shift();
      this.select({ rec: next }, true);
      return;
    }
    const i = this.multi.indexOf(rec);
    if (i >= 0) this.multi.splice(i, 1); else this.multi.push(rec);
    this.select(this.sel, true);
  }
  // le gizmo : sur l'ajout choisi, ou sur un pivot au centre de la selection
  attachGizmo() {
    const sel = this.sel;
    const on = sel && sel.rec && !["route", "select", "veg", "relief"].includes(this.mode) && !sel.face;
    if (on && this.multi.length) {
      const c = new THREE.Vector3(), recs = this.selected();
      for (const r of recs) c.add(this.recCenter(r));
      this.pivot.position.copy(c.multiplyScalar(1 / recs.length)); this.pivot.quaternion.identity(); this.pivot.scale.set(1, 1, 1);
      this.pivot.updateMatrixWorld(true);
      this.tc.attach(this.pivot); this.tc.setMode(this.mode || "translate");
    } else if (on && sel.rec.type !== "route") { this.tc.attach(this.nodes.get(sel.rec.id)); this.tc.setMode(this.mode || "translate"); }
    else this.tc.detach();
    this.applySnap();
  }
  recCenter(r) {
    if (r.type === "route") { const c = new THREE.Vector3(); for (const pt of r.pts) c.add(U.posTo(pt.p, new THREE.Vector3())); return r.pts.length ? c.multiplyScalar(1 / r.pts.length) : c; }
    return U.posTo(r.pos, new THREE.Vector3());
  }
  multiStart() {
    if (!this.multi.length) return;
    this.pivot.updateMatrixWorld(true);
    this.mstart = { pivot: this.pivot.matrix.clone().invert(), recs: this.selected().map(r => {
      const n = this.nodes.get(r.id); n.updateMatrix();
      return { r, m: n.matrix.clone(), pts: r.type === "route" ? JSON.parse(JSON.stringify(r.pts)) : null };
    }) };
  }
  multiMove() {
    if (!this.mstart) return;
    this.pivot.updateMatrix();
    const d = this.pivot.matrix.clone().multiply(this.mstart.pivot);
    for (const s of this.mstart.recs) {
      if (s.r.type === "route") {
        s.r.pts = s.pts.map(pt => {
          const P = U.posTo(pt.p, new THREE.Vector3()), P2 = P.clone().applyMatrix4(d);
          const o = { p: U.posFrom(P2) };
          for (const k of ["h", "hi"]) if (pt[k]) o[k] = U.posFrom(P.clone().add(U.posTo(pt[k], new THREE.Vector3())).applyMatrix4(d).sub(P2)).map(x => x);
          return o;
        });
        this.rebuildRoad(s.r, true);
        continue;
      }
      const n = this.nodes.get(s.r.id);
      d.clone().multiply(s.m).decompose(n.position, n.quaternion, n.scale);
      n.updateMatrixWorld(true);
      this.fromNode(s.r);
    }
  }
  multiEnd() {
    if (!this.mstart) return;
    for (const s of this.mstart.recs) if (s.r.type === "route") this.rebuildRoad(s.r, false);
    this.mstart = null;
    this.attachGizmo();
  }
  toggleSnap() { this.snap = !this.snap; this.applySnap(); this.showGrid(); this.renderTools(); }
  applySnap() {
    this.tc.setTranslationSnap(this.snap ? 1 : null);
    this.tc.setRotationSnap(this.snap ? rad(15) : null);
    this.tc.setScaleSnap(this.snap ? 0.25 : null);
  }
  // grille de 1 m sous la selection quand l'aimant est mis
  showGrid() {
    if (this.grid) { this.v.scene.remove(this.grid); this.grid.geometry.dispose(); this.grid = null; }
    const recs = this.selected();
    if (!this.snap || !recs.length) return;
    const c = this.recCenter(recs[0]), n = this.nodes.get(recs[0].id);
    const b = n ? new THREE.Box3().setFromObject(n) : null;
    this.grid = new THREE.GridHelper(80, 80, 0xffd23f, 0x888888);
    this.grid.material.transparent = true; this.grid.material.opacity = 0.35; this.grid.material.depthWrite = false;
    this.grid.position.set(Math.round(c.x), b && !b.isEmpty() ? b.min.y + 0.02 : c.y, Math.round(c.z));
    this.grid.userData.noPick = true;
    this.v.scene.add(this.grid);
  }
  // presse-papiers entre cartes (meme jeu pour les copies d'objets du jeu) : Ctrl+C / Ctrl+V
  copySel() {
    const recs = this.selected();
    if (!recs.length) return;
    const c = new THREE.Vector3(); for (const r of recs) c.add(this.recCenter(r)); c.multiplyScalar(1 / recs.length);
    try { localStorage.setItem("mwc-editeur-presse-papiers", JSON.stringify({ jeu: this.proj.jeu, dossier: this.dossier, centre: U.posFrom(c), recs })); } catch { }
    this.ui.warn(T("copies", recs.length));
  }
  async paste() {
    let clip = null;
    try { clip = JSON.parse(localStorage.getItem("mwc-editeur-presse-papiers") || "null"); } catch { }
    if (!clip || !clip.recs || !clip.recs.length) return;
    const recs = clip.recs.filter(r => r.type !== "jeu" || clip.jeu === this.proj.jeu);
    if (recs.length < clip.recs.length) this.ui.warn(T("collerAutreJeu"));
    if (!recs.length) return;
    const files = [];
    for (const r of recs) {
      if (r.type === "modele") files.push("modeles/" + r.src.fichier);
      for (const h of [r.habit, r.surface].concat(Object.values(r.subs || {}), (r.faces || []).map(f => f.habit))) if (h && h.k === "img") files.push(h.f);
    }
    if (files.length && clip.dossier !== this.dossier) await new Promise(res => { this.onCopied = res; this.send({ type: "copierFichiers", de: clip.dossier, vers: this.dossier, fichiers: files }); setTimeout(res, 8000); });
    const target = this.spot(), c = U.posTo(clip.centre, new THREE.Vector3()), dx = target.x - c.x, dz = -(target.z - c.z);
    const made = recs.map(r => {
      const n = JSON.parse(JSON.stringify(r));
      n.id = nextId++;
      shiftRec(n, dx, dz);
      this.proj.objets.push(n);
      this.build(n);
      return n;
    });
    this.select({ rec: made[0] });
    this.multi = made.slice(1); this.attachGizmo(); this.renderList(); this.renderProps();
    this.commit();
  }
  onFichiersCopies() { if (this.onCopied) { const f = this.onCopied; this.onCopied = null; f(); } }

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
    const set = new Set(this.proj.masques.filter(m => !m.u).map(m => m.p)), pres = this.proj.masques.filter(m => m.u).map(m => m.p);
    this.v.instMeshes.forEach((mesh, i) => {
      if (!mesh) return;
      const p = this.v.info.objets[i].p;
      const masked = set.has(p) || (p && pres.some(q => p === q || p.startsWith(q + "/")));
      if (masked && !mesh.userData.masked) { mesh.visible = false; mesh.userData.masked = true; this.v.setHidden(i, true); }
      else if (!masked && mesh.userData.masked) { mesh.visible = true; delete mesh.userData.masked; this.v.setHidden(i, false); }
    });
  }

  // ---------------------------------------------------------------- selection
  pick(hit, e) {
    if (!hit) { if (!(e && (e.ctrlKey || e.shiftKey))) this.select(null); return; }
    let o = hit.object;
    while (o && !o.userData.rec && o.parent) o = o.parent;
    if (o && o.userData.rec && e && (e.ctrlKey || e.shiftKey)) this.toggleSel(o.userData.rec);
    else if (o && o.userData.rec) this.select({ rec: o.userData.rec });
    else if (hit.object.userData.idx !== undefined) this.select({ idx: hit.object.userData.idx, point: hit.point });
    else this.select(null);
  }

  select(sel, keepMulti) {
    if (!sel || !sel.rec || !this.sel || this.sel.rec !== sel.rec) this.anchorSel = sel && sel.rec && sel.rec.type === "route" ? this.anchorSel : undefined;
    if (!keepMulti) this.multi = [];
    this.sel = sel;
    if (this.box) { this.v.scene.remove(this.box); this.box.geometry.dispose(); this.box = null; }
    this.attachGizmo();
    this.showRoadUi();
    if (sel && sel.idx !== undefined) { this.box = new THREE.BoxHelper(this.v.instMeshes[sel.idx], 0xffffff); this.box.userData.noPick = true; this.v.scene.add(this.box); }
    this.showFace(sel);
    this.showGrid();
    this.renderList();
    this.renderProps();
    this.renderTools();
  }

  setMode(m) {
    if (this.drawing && m !== "route") { const d = this.drawing; this.drawing = null; if (d.pts.length < 2) { this.select({ rec: d }); this.remove(); } else this.commit(); }
    this.mode = m;
    this.v.o.canvas.style.cursor = m === "route" || m === "veg" || m === "relief" ? "crosshair" : "";
    if (m !== "veg" && m !== "relief" && this.ring) this.ring.visible = false;
    if (m === "select" || m === "route" || m === "veg" || m === "relief") { this.tc.enabled = false; this.tc.visible = false; }
    else { this.tc.enabled = true; this.tc.visible = true; this.tc.setMode(m); }
    this.attachGizmo();
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
    const snap = this.snapshotOf();
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
    this.drawing = null; this.dragH = null;
    const selId = this.sel && this.sel.rec ? this.sel.rec.id : null;
    this.tc.detach();
    for (const n of this.nodes.values()) this.root.remove(n);
    this.nodes.clear();
    this.proj.objets = d.objets; this.proj.masques = d.masques;
    const reliefChanged = JSON.stringify(d.relief || []) !== JSON.stringify(this.proj.relief);
    this.proj.relief = d.relief || [];
    if (reliefChanged) this.replayRelief();
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
    if ((e.ctrlKey || e.metaKey) && k === "c") { e.preventDefault(); this.copySel(); return; }
    if ((e.ctrlKey || e.metaKey) && k === "v") { e.preventDefault(); this.paste(); return; }
    if ((e.ctrlKey || e.metaKey) && k === "a") { e.preventDefault(); if (this.proj.objets.length) { this.select({ rec: this.proj.objets[0] }); this.multi = this.proj.objets.slice(1); this.attachGizmo(); this.renderList(); this.renderProps(); } return; }
    if (e.ctrlKey || e.metaKey || e.altKey) return;
    if (this.mode === "route" && (k === "escape" || k === "enter")) { this.endRoad(); return; }
    if ((this.mode === "veg" || this.mode === "relief") && k === "escape") { this.setMode("translate"); this.renderProps(); return; }
    if ((k === "delete" || k === "suppr" || k === "backspace") && this.removeAnchor()) return;
    if (k === "v") this.setMode("select");
    else if (k === "p") this.startRoad();
    else if (k === "b") this.mode === "veg" ? this.setMode("translate") : this.startVeg();
    else if (k === "t") this.mode === "relief" ? this.setMode("translate") : this.startRelief();
    else if (k === "g") this.setMode("translate");
    else if (k === "r") this.setMode("rotate");
    else if (k === "s") this.setMode("scale");
    else if (k === "f") this.focus();
    else if (k === "m") this.toggleSnap();
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
      + b("route", "road", T("tRoute"), "P", mode === "route")
      + b("eau", "water", T("tEau"), "", false)
      + b("lumiere", "bulb", T("tLumiere"), "", false)
      + b("veg", "tree", T("tVegetation"), "B", mode === "veg")
      + b("relief", "mountain", T("tRelief"), "T", mode === "relief")
      + `<span class="sep"></span>`
      + b("aimant", "magnet", T("tAimant"), "M", this.snap)
      + b("undo", "undo", T("tAnnuler"), "Ctrl+Z", false, !this.hist.length)
      + b("redo", "redo", T("tRetablir"), "Ctrl+Y", false, !this.future.length);
    el.querySelectorAll("button[data-t]").forEach(btn => btn.onclick = () => {
      const t = btn.dataset.t;
      if (["select", "translate", "rotate", "scale"].includes(t)) this.setMode(t);
      else if (t === "palette") this.openPalette();
      else if (t === "import") this.importModel();
      else if (t === "route") this.mode === "route" ? this.endRoad() : this.startRoad();
      else if (t === "eau") this.addWater();
      else if (t === "lumiere") this.addLight();
      else if (t === "veg") this.mode === "veg" ? this.setMode("translate") : this.startVeg();
      else if (t === "relief") this.mode === "relief" ? this.setMode("translate") : this.startRelief();
      else if (t === "aimant") this.toggleSnap();
      else if (t === "undo") this.undo();
      else if (t === "redo") this.redo();
    });
  }

  renderList() {
    const el = this.ui.list;
    if (!el) return;
    const sel = this.sel && this.sel.rec, multi = new Set(this.multi);
    let h = `<div class="ph sub">${esc(T("mesAjouts"))}<span class="n">${this.proj.objets.length}</span></div>`;
    if (!this.proj.objets.length) h += `<p class="empty">${esc(T("ajoutsVide"))}</p>`;
    const veg = this.proj.objets.filter(o => o.veg).length;
    if (veg) h += `<button class="row" data-veg="1">${ICON.tree}<span>${esc(T("tVegetation"))}</span><span class="n">${veg}</span></button>`;
    h += this.proj.objets.filter(o => !o.veg).map(o => `<button class="row${o === sel || multi.has(o) ? " sel" : ""}${o.manque ? " manque" : ""}" data-id="${o.id}">${ICON[{ modele: "import", route: "road", eau: "water", lumiere: "bulb", unite: "gear" }[o.type] || "box"]}<span>${esc(o.nom)}</span><span class="n">${T({ modele: "importe", route: "tagRoute", eau: "tagEau", lumiere: "tagLumiere", unite: "tagUnite" }[o.type] || "jeu")}</span></button>`).join("");
    if (this.proj.masques.length) {
      h += `<div class="ph sub">${esc(T("masques"))}<span class="n">${this.proj.masques.length}</span></div>`;
      h += this.proj.masques.map((m, i) => `<div class="row dim">${ICON.eyeoff}<span>${esc(m.n)}${m.u ? " · " + esc(T("tagUnite")) : ""}</span><button class="mini" data-unmask="${i}">${esc(T("reafficher"))}</button></div>`).join("");
    }
    el.innerHTML = h;
    el.querySelectorAll("button[data-id]").forEach(b => b.onclick = ev => { const rec = this.proj.objets.find(o => o.id == b.dataset.id); if (!rec) return; if (ev.ctrlKey || ev.shiftKey) this.toggleSel(rec); else this.select({ rec }); });
    const vb = el.querySelector("button[data-veg]");
    if (vb) vb.onclick = () => this.startVeg();
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
    if (this.mode === "veg" && (!s || !s.rec)) { this.vegProps(el); return; }
    if (this.mode === "relief" && (!s || !s.rec)) { this.reliefProps(el); return; }
    if (!s) { el.innerHTML = `<div class="ph">${esc(T("proprietes"))}</div><p class="empty pad">${esc(T("propsVide"))}</p>`; return; }
    if (s.idx !== undefined) {
      const ob = this.v.info.objets[s.idx], masked = this.isMasked(s.idx);
      const kind = classify(ob.n, ob.p);
      el.innerHTML = `<div class="ph">${esc(T("objetJeu"))}</div><div class="sec"><b class="nm">${esc(ob.n)}</b>${kind && kind !== "junk" ? `<div class="hint">${esc(catName(kind, window.LANG))}</div>` : ""}
        <div class="kv"><span>${esc(T("chemin"))}</span><span>${esc(ob.p || "—")}</span></div>
        <button class="btn pri wide" data-a="copier">${ICON.box}${esc(T("copierCarte"))}</button>
        <button class="btn wide" data-a="masquer">${masked ? ICON.eye : ICON.eyeoff}${esc(masked ? T("reafficher") : T("masquerCarte"))}</button>
        <p class="hint">${esc(T("copierAide"))}</p></div>${this.unitsOf(s.idx).length ? `<div class="sec"><div class="sl">${esc(T("unitesTitre"))}</div>${this.unitsOf(s.idx).map(u => `<div class="look"><span class="lk"><b>${esc(u.n)}</b><small>${esc(T("uniteInfo", u.r, u.f.map(x => x.split(":")[1]).filter((x, i, a) => a.indexOf(x) === i).slice(0, 4).join(", ")))}</small></span><button class="mini" data-uc="${u.i}">${esc(T("uniteCopier"))}</button><button class="mini" data-um="${u.i}">${esc(this.isUnitMasked(u.p) ? T("reafficher") : T("uniteMasquer"))}</button></div>`).join("")}<p class="hint">${esc(T("unitesAide"))}</p></div>` : ""}`;
      el.querySelectorAll("[data-uc]").forEach(b => b.onclick = () => this.addUnit(+b.dataset.uc));
      el.querySelectorAll("[data-um]").forEach(b => b.onclick = () => this.toggleUnitMask(this.v.info.unites[+b.dataset.um]));
      el.querySelector('[data-a="copier"]').onclick = () => this.addGame(s.idx, true);
      el.querySelector('[data-a="masquer"]').onclick = () => this.toggleMask(s.idx);
      return;
    }
    const rec = s.rec, v = this.values(rec);
    if (this.multi.length && !s.face) {
      const n = this.selected().length;
      el.innerHTML = `<div class="ph">${esc(T("proprietes"))}</div><div class="sec"><b class="nm">${esc(T("selMulti", n))}</b><p class="hint">${esc(T("selMultiAide"))}</p>
        <button class="btn wide" data-a="sol">${ICON.down}${esc(T("poserSol"))}</button></div>
        <div class="sec row2"><button class="btn" data-a="dup">${ICON.copy}${esc(T("dupliquer"))}</button><button class="btn danger" data-a="suppr">${ICON.trash}${esc(T("supprimer"))}</button></div>`;
      el.querySelector('[data-a="sol"]').onclick = () => { for (const r of this.selected()) if (r.type !== "route") this.dropToGround(r); this.commit(); this.attachGizmo(); };
      el.querySelector('[data-a="dup"]').onclick = () => this.duplicate();
      el.querySelector('[data-a="suppr"]').onclick = () => this.remove();
      return;
    }
    if (rec.type === "route") { this.roadProps(el, rec); return; }
    if (rec.type === "lumiere") { this.lightProps(el, rec); return; }
    const num = (f, c) => `<label class="num"><b style="color:${c}">${f.slice(-1).toUpperCase()}</b><input data-f="${f}" value="${v[f]}" inputmode="decimal" spellcheck="false"></label>`;
    el.innerHTML = `<div class="ph">${esc(T("proprietes"))}</div>
      <div class="sec"><label class="sl" for="p-nom">${esc(T("nom"))}</label><input id="p-nom" class="txt" value="${esc(rec.nom)}" spellcheck="false">
        <div class="hint">${rec.type === "modele" ? esc(rec.src.fichier) : rec.type === "jeu" ? esc(T("objetJeu")) + " · " + esc(rec.src.n) : esc(T("tEau"))}${rec.manque ? " · <span class=warn>" + esc(T("introuvableIci")) + "</span>" : ""}</div></div>
      <div class="sec"><div class="sl">${esc(T("position"))} (m)</div><div class="xyz">${num("px", "#ff6e6e")}${num("py", "#7cf2c4")}${num("pz", "#6aa8ff")}</div>
        <div class="sl">${esc(T("rotation"))} (°)</div><div class="xyz">${num("rx", "#ff6e6e")}${num("ry", "#7cf2c4")}${num("rz", "#6aa8ff")}</div>
        <div class="sl">${esc(T("echelle"))}</div><div class="xyz">${num("sx", "#ff6e6e")}${num("sy", "#7cf2c4")}${num("sz", "#6aa8ff")}</div>
        <button class="btn wide" data-a="sol">${ICON.down}${esc(T("poserSol"))}</button></div>
      <div class="sec"><div class="sl">${esc(T("collisions"))}</div><div class="seg">${["aucune", "boite", "forme"].map(c => `<button data-col="${c}" class="${rec.col === c ? "on" : ""}">${esc(T("col_" + c))}</button>`).join("")}</div></div>
      ${this.lookSection(s)}
      <div class="sec row2"><button class="btn" data-a="dup">${ICON.copy}${esc(T("dupliquer"))}</button><button class="btn danger" data-a="suppr">${ICON.trash}${esc(T("supprimer"))}</button></div>`;
    this.bindLook(el, s);
    el.querySelector("#p-nom").onchange = e => { rec.nom = e.target.value.trim() || rec.nom; this.commit(); };
    el.querySelectorAll("input[data-f]").forEach(i => i.onchange = () => { this.setValue(rec, i.dataset.f, parseFloat(i.value.replace(",", "."))); });
    el.querySelectorAll("[data-col]").forEach(b => b.onclick = () => { rec.col = b.dataset.col; this.commit(); this.renderProps(); });
    el.querySelector('[data-a="sol"]').onclick = () => { this.dropToGround(rec); this.commit(); this.renderProps(); };
    el.querySelector('[data-a="dup"]').onclick = () => this.duplicate();
    el.querySelector('[data-a="suppr"]').onclick = () => this.remove();
  }

  // proprietes d'une route : nom, largeur, revetement (matieres du jeu proposees, ou une autre texture), collisions
  roadProps(el, rec) {
    const presets = this.surfacePresets("route");
    el.innerHTML = `<div class="ph">${esc(T("proprietes"))}</div>
      <div class="sec"><label class="sl" for="p-nom">${esc(T("nom"))}</label><input id="p-nom" class="txt" value="${esc(rec.nom)}" spellcheck="false">
        <div class="hint">${esc(T("routePoints", rec.pts.length))}${this.anchorSel !== undefined ? " · " + esc(T("routePointChoisi", this.anchorSel + 1)) : ""}</div></div>
      <div class="sec"><label class="sl" for="p-larg">${esc(T("largeur"))} (m)</label><input id="p-larg" class="txt" value="${rec.largeur || 6}" inputmode="decimal" spellcheck="false"></div>
      <div class="sec"><div class="sl">${esc(T("revetement"))}</div><div class="chips">${presets.map((h, i) => `<button class="mini${rec.surface && rec.surface.ref === h.ref ? " on" : ""}" data-pre="${i}">${esc(h.nom)}</button>`).join("")}</div>
        ${this.lookSection(Object.assign({}, { rec }))}</div>
      <div class="sec"><div class="sl">${esc(T("marquage"))}</div><div class="seg">${["aucun", "centre", "bords", "tout"].map(c => `<button data-mq="${c}" class="${(rec.marquage || "aucun") === c ? "on" : ""}">${esc(T("mq_" + c))}</button>`).join("")}</div>
        <label class="cb"><input type="checkbox" id="p-plat"${rec.plat !== false ? " checked" : ""}><span>${esc(T("routePlate"))}</span></label>
        <label class="cb"><input type="checkbox" id="p-talus"${rec.talus !== false ? " checked" : ""}><span>${esc(T("routeTalus"))}</span></label></div>
      <div class="sec"><div class="sl">${esc(T("collisions"))}</div><div class="seg">${["aucune", "forme"].map(c => `<button data-col="${c}" class="${rec.col === c ? "on" : ""}">${esc(T("col_" + c))}</button>`).join("")}</div></div>
      <p class="hint pad">${esc(T("routeAide"))}</p>
      <div class="sec row2"><button class="btn" data-a="dup">${ICON.copy}${esc(T("dupliquer"))}</button><button class="btn danger" data-a="suppr">${ICON.trash}${esc(T("supprimer"))}</button></div>`;
    el.querySelector("#p-nom").onchange = e => { rec.nom = e.target.value.trim() || rec.nom; this.commit(); };
    el.querySelector("#p-larg").onchange = e => { const w = parseFloat(e.target.value.replace(",", ".")); if (isFinite(w) && w > 0.5 && w < 60) { rec.largeur = w; this.rebuildRoad(rec, false); this.commit(); } this.renderProps(); };
    el.querySelectorAll("[data-pre]").forEach(b => b.onclick = () => { rec.surface = presets[+b.dataset.pre]; this.rebuildRoad(rec, false); this.commit(); this.renderProps(); });
    el.querySelectorAll("[data-mq]").forEach(b => b.onclick = () => { rec.marquage = b.dataset.mq; this.rebuildRoad(rec, false); this.commit(); this.renderProps(); });
    el.querySelector("#p-plat").onchange = e => { rec.plat = e.target.checked; this.rebuildRoad(rec, false); this.commit(); };
    el.querySelector("#p-talus").onchange = e => { rec.talus = e.target.checked; this.rebuildRoad(rec, false); this.commit(); };
    el.querySelectorAll("[data-col]").forEach(b => b.onclick = () => { rec.col = b.dataset.col; this.commit(); this.renderProps(); });
    el.querySelector('[data-a="dup"]').onclick = () => this.duplicate();
    el.querySelector('[data-a="suppr"]').onclick = () => this.remove();
    this.bindLook(el, { rec });
  }

  // section « Textures » : copie du jeu (ses matieres), modele (entier, faces), face choisie
  lookSection(s) {
    const rec = s.rec, row = (label, h, a, extra) => `<div class="look"><span class="sw tex"${h ? ` style="background-image:url('${esc(this.habitUrl(h))}')"` : ""}></span><span class="lk"><b>${esc(label)}</b><small>${esc(h ? this.habitName(h) : T("texDorigine"))}</small></span>${extra || ""}<button class="mini" data-look="${a}">${esc(T("texChanger"))}</button></div>`;
    let h = `<div class="sec"><div class="sl">${esc(T("textures"))}</div>`;
    if (s.face) {
      const f = (rec.faces || []).find(x => sameFace(x, s.face));
      h += row(T("texFace", s.face.tris.length), f && f.habit, "face") + `<button class="btn wide" data-a="objet">${esc(T("texRetourObjet"))}</button>`;
    } else if (rec.type === "jeu") {
      const idx = this.v.byPath.get(rec.src.p), ob = idx !== undefined ? this.v.info.objets[idx] : null;
      const names = ob ? ob.x.map(mi => mi >= 0 && this.v.info.matieres[mi] ? this.v.info.matieres[mi].n : "—") : [];
      const subs = rec.subs || {};
      if (names.length > 1) h += row(T("texToutes"), subs["*"], "*");
      names.forEach((n, i) => { h += row(n, subs[i] || subs["*"], String(i)); });
    } else if (rec.type === "modele") {
      h += row(T("texObjet"), rec.habit, "objet");
      (rec.faces || []).forEach((f, i) => { h += row(T("texFace", f.tris.length), f.habit, "f" + i, `<button class="mini x" data-unface="${i}" aria-label="${esc(T("supprimer"))}">×</button>`); });
      h += `<p class="hint">${esc(T("texAideFace"))}</p>`;
    } else if (rec.type === "eau") {
      h += `<div class="chips">${this.surfacePresets("eau").map((x, i) => `<button class="mini${rec.surface && rec.surface.ref === x.ref ? " on" : ""}" data-pre="${i}">${esc(x.nom)}</button>`).join("")}</div>`;
      h += row(T("texSurface"), rec.surface, "surface");
    } else if (rec.surface) h += row(T("texSurface"), rec.surface, "surface");
    return h + `</div>`;
  }
  bindLook(el, s) {
    const rec = s.rec;
    el.querySelectorAll("[data-look]").forEach(b => b.onclick = () => {
      const a = b.dataset.look;
      if (a === "face") this.openTextures({ rec, face: s.face });
      else if (a === "objet") this.openTextures({ rec });
      else if (a === "surface") this.openTextures({ rec, surface: true });
      else if (a[0] === "f") { const f = rec.faces[+a.slice(1)]; this.select({ rec, face: { m: f.m, tris: f.tris } }); this.openTextures({ rec, face: { m: f.m, tris: f.tris } }); }
      else this.openTextures({ rec, sub: a, label: b.closest(".look").querySelector("b").textContent });
    });
    el.querySelectorAll("[data-unface]").forEach(b => b.onclick = () => { rec.faces.splice(+b.dataset.unface, 1); if (!rec.faces.length) delete rec.faces; this.rebuild(rec); this.commit(); this.renderProps(); });
    if (rec.type === "eau") el.querySelectorAll("[data-pre]").forEach(btn => btn.onclick = () => { rec.surface = this.surfacePresets("eau")[+btn.dataset.pre]; this.rebuild(rec); this.commit(); this.renderProps(); });
    const back = el.querySelector('[data-a="objet"]');
    if (back) back.onclick = () => this.select({ rec });
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
    this.proj.compile = { fichier: String(m.chemin).split(/[\\/]/).pop(), date: new Date().toLocaleString() };   // (accueil : « compilee », « en jeu »)
    this.save();
    res.innerHTML = `<b>${esc(T("cpFini"))}</b><span>${esc(m.chemin)} · ${Math.max(1, Math.round(m.taille / 1024))} Ko</span>`
      + `<span>${esc(m.mod ? T("cpActive") : T("cpPasMod"))}</span><button class="btn small" id="cp-dir">${esc(T("ouvrirDossier"))}</button>`;
    res.querySelector("#cp-dir").onclick = () => this.send({ type: "dossier", chemin: m.dossier });
  }

  async compile() {
    const p = this.proj, files = {}, lines = ["MWCMAP\t2", "jeu\t" + p.jeu, "nom\t" + cl(p.nom), "auteur\t" + cl(p.auteur), "version\t" + cl(p.version), "description\t" + cl(p.description)];
    const modelIds = new Map(), texIds = new Map();
    let nextModel = 0;
    const f = x => (Math.round(x * 100000) / 100000).toString();
    const v3 = a => a.map(f).join(" ");
    for (const rec of p.objets) {
      if (rec.manque) continue;
      if (rec.type === "jeu") {
        lines.push(["objet", "jeu", rec.src.p, v3(rec.pos), v3(rec.rot), v3(rec.ech), rec.col, cl(rec.nom)].concat(rec.src.plante ? [rec.src.c ? v3(rec.src.c) : "-", "plante " + v3(rec.src.plante)] : rec.src.c ? [v3(rec.src.c)] : []).join("\t"));
        for (const [sub, h] of Object.entries(rec.subs || {})) lines.push(["habit", sub, await this.packRef(h, files)].concat(h.rep ? [h.rep.map(f).join(" ")] : []).join("\t"));
        continue;
      }
      if (rec.type === "unite") { lines.push(["objet", "unite", rec.src.p, v3(rec.pos), v3(rec.rot), v3(rec.ech), rec.col || "aucune", cl(rec.nom)].join("\t")); continue; }
      if (rec.type === "lumiere") {
        const hx = (rec.couleur || "#ffffff").replace("#", ""), rgb = [0, 2, 4].map(i => parseInt(hx.slice(i, i + 2), 16) / 255);   // (valeurs du code couleur : le jeu est en espace gamma)
        lines.push(["lumiere", v3(rec.pos), rgb.map(f).join(" "), f(rec.intensite), f(rec.portee), rec.nuit ? 1 : 0, rec.ombres ? 1 : 0, cl(rec.nom)].join("\t"));
        continue;
      }
      // modeles, routes, eau : maillages cuits ici (tenues et faces comprises : une matiere par sous-maillage) ; un meme
      // fichier sans tenue n'est ecrit qu'une fois
      const key = rec.type === "modele" && !rec.habit && !rec.faces ? rec.src.fichier + "|" + rec.unite : "r" + rec.id;
      if (!modelIds.has(key)) {
        const node = this.nodes.get(rec.id);
        const chunks = await this.modelChunks(node);
        const ids = [];
        for (const ch of chunks) {
          const id = "m" + nextModel++;
          files["modeles/" + id + ".bin"] = b64(ch.bin);
          lines.push(["modele", id, "modeles/" + id + ".bin"].join("\t"));
          for (let i = 0; i < ch.mats.length; i++) {
            const m = ch.mats[i];
            let tex = "-";
            if (m.habit) tex = await this.packRef(m.habit, files);   // (texture du jeu : son nom seulement, jamais l'image)
            else if (m.image) {
              if (!texIds.has(m.image)) { const png = texPng(m.image); if (png) { const n = "textures/t" + texIds.size + ".png"; files[n] = png; texIds.set(m.image, n); } }
              tex = texIds.get(m.image) || "-";
            }
            const rp = m.habit && m.habit.rep;
            lines.push(["matiere", id, i, m.c.map(f).join(" "), tex, m.cut ? 1 : 0].concat(rp ? [rp.map(f).join(" ")] : []).join("\t"));
          }
          ids.push(id);
        }
        modelIds.set(key, ids);
      }
      const ids = modelIds.get(key);
      if (ids.length) lines.push(["objet", "modele", ids.join(","), v3(rec.pos), v3(rec.rot), v3(rec.ech), rec.col || "aucune", cl(rec.nom)].join("\t"));
    }
    for (const m of p.masques) lines.push((m.u ? "masque-unite\t" : "masque\t") + m.p);
    if (p.relief && p.relief.length) {   // (sol sculpte : les objets du sol touches, puis les traits dans l'ordre)
      const cibles = new Set();
      for (const st of p.relief) for (const idx of this.terrainObjects()) { const b = this.terrainWorld(idx).box; if (!(st.x + st.r < b[0] || st.x - st.r > b[1] || st.z + st.r < b[2] || st.z - st.r > b[3])) cibles.add(this.v.info.objets[idx].p); }
      for (const c of cibles) lines.push("relief-cible\t" + c);
      for (const st of p.relief) lines.push(["relief", st.m, f(st.x), f(st.z), f(st.r), f(st.f), f(st.y)].join("\t"));
    }
    files["carte.txt"] = b64(new TextEncoder().encode(lines.join("\n") + "\n"));
    const slug = (p.nom || "carte").normalize("NFD").replace(/[̀-ͯ]/g, "").replace(/[^A-Za-z0-9]+/g, "-").replace(/^-|-$/g, "").toLowerCase() || "carte";
    this.send({ type: "compiler", dossier: this.dossier, jeu: p.jeu, nomFichier: slug + "-" + (p.version || "1.0").replace(/[^0-9A-Za-z.]+/g, "") + ".mwcmap", ancien: (p.compile && p.compile.fichier) || "", fichiers: files });
  }

  // reference d'une tenue dans le paquet : nom d'une texture / matiere du jeu, ou image perso copiee (textures/u<n>.<ext>)
  async packRef(h, files) {
    if (h.k !== "img") return h.ref;
    this.packImgs = this.packImgs || new Map();
    const n0 = this.packImgs.get(h.f);
    if (n0 && files[n0]) return n0;
    const ext = (h.f.split(".").pop() || "png").toLowerCase(), n = "textures/u" + Object.keys(files).filter(k => k.startsWith("textures/u")).length + "." + ext;
    files[n] = b64(new Uint8Array(await (await fetch(this.habitUrl(h))).arrayBuffer()));
    this.packImgs.set(h.f, n);
    return n;
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

  // ---------------------------------------------------------------- routes a la plume et eau (demande de JD, 20/10)
  // Route : rec.pts [{ p: point (repere d'Unity), h: poignee sortante, hi: poignee entrante (sinon -h) }], largeur, surface
  // (matiere du jeu : route, asphalte, gravier, terre...). Clic : un point ; clic glisse : ses poignees (courbe de Bezier,
  // comme la plume de Photoshop) ; Alt : poignee sortante seule ; les points et poignees se reprennent a la souris ; la
  // route epouse le sol (rayons vers le bas, en cache). Eau : un plan (deplacer, tourner, echelle) a la matiere d'eau du jeu.
  surfacePresets(type) {
    const ms = this.v.info.matieres, out = [];
    // (eau : l'eau simple d'abord -- celle du jeu, Water4, depend des scripts de reflet du lac et sort grise sur un plan a nous)
    if (type === "eau") out.push({ k: "eau", nom: T("eauSimple"), ref: "eau:" });
    const want = type === "talus" ? [/^ROADSIDE$/i, /^mwc_dirtroad$/i, /^GRAVEL$/i] : type === "eau" ? [/^Water4Simple$/i, /^Water4Adv/i] : [/^ROAD$/, /^ASPHALT$/i, /^mwc_asphalt$/i, /^GRAVEL$/i, /^DIRTROAD$/i, /^mwc_dirtroad$/i, /^mwc_iceroad$/i];
    for (const re of want) { const mi = ms.findIndex(m => re.test(m.n)); if (mi >= 0 && !out.some(h => h.mi === mi)) out.push({ k: "mat", mi, nom: ms[mi].n, ref: "mat:" + ms[mi].n }); }
    return out;
  }
  defaultSurface(type) { return this.surfacePresets(type)[0] || null; }

  startRoad() {
    this.drawing = null;
    this.setMode("route");
    this.ui.warn(T("routeAide"));
  }
  endRoad() {
    const rec = this.drawing;
    this.drawing = null;
    if (rec && rec.pts.length < 2) { this.select({ rec }); this.remove(); }
    this.setMode("translate");
    if (rec && rec.pts.length >= 2) { this.select({ rec }); this.commit(); }
  }

  addWater() {
    const p = this.spot();
    const rec = { id: nextId++, nom: T("eau"), type: "eau", pos: U.posFrom(p.add(new THREE.Vector3(0, 0.3, 0))), rot: [0, 0, 0, 1], ech: [20, 1, 20], surface: this.defaultSurface("eau"), col: "aucune" };
    this.proj.objets.push(rec);
    this.build(rec);
    this.select({ rec });
    this.setMode("scale");
    this.commit();
  }

  // ---------------------------------------------------------------- relief sculpte (demande de JD, 21/10)
  // Le sol du jeu (Grass1/Grass2/Fields de MSC, Grass de MWC : maillages lisibles, non regroupes) modifie par des traits de
  // pinceau gardes dans le projet (proj.relief : { m: monter|creuser|aplanir|adoucir, x, z, r, f, y }, repere d'Unity) ; le mod
  // rejoue les memes traits sur le sol de chaque joueur (MapPacks.ApplyRelief). Attenuation (1 - d2/r2)^2 ; aplanir : vers la
  // hauteur du clic ; adoucir : vers la hauteur moyenne du cercle.
  terrainObjects() {
    if (this.terrIdx) return this.terrIdx;
    const o = this.v.info.objets, ms = this.v.info.matieres;
    this.terrIdx = [];
    o.forEach((x, i) => { if (x.k === 0 && !x.r && x.p && x.x.some(mi => ms[mi] && /^(TERRAIN|SNOW)$/i.test(ms[mi].n))) this.terrIdx.push(i); });
    return this.terrIdx;
  }
  // sommets d'un objet du sol en coordonnees du monde d'Unity, et le retour vers la geometrie (repere three du maillage)
  terrainWorld(idx) {
    this.terrW = this.terrW || new Map();
    if (this.terrW.has(idx)) return this.terrW.get(idx);
    const mesh = this.v.instMeshes[idx], g = mesh.geometry, P = g.attributes.position, M = mesh.matrix, n = P.count;
    this.reliefOrig = this.reliefOrig || new Map();
    if (!this.reliefOrig.has(idx)) this.reliefOrig.set(idx, P.array.slice());
    const xs = new Float64Array(n), ys = new Float64Array(n), zs = new Float64Array(n), v = new THREE.Vector3();
    let mnx = 1e18, mxx = -1e18, mnz = 1e18, mxz = -1e18;
    for (let k = 0; k < n; k++) { v.fromBufferAttribute(P, k).applyMatrix4(M); xs[k] = v.x; ys[k] = v.y; zs[k] = -v.z; mnx = Math.min(mnx, v.x); mxx = Math.max(mxx, v.x); mnz = Math.min(mnz, -v.z); mxz = Math.max(mxz, -v.z); }
    const w = { xs, ys, zs, box: [mnx, mxx, mnz, mxz], inv: M.clone().invert() };
    this.terrW.set(idx, w);
    return w;
  }
  writeTerrain(idx) {
    const w = this.terrW.get(idx), mesh = this.v.instMeshes[idx], g = mesh.geometry, P = g.attributes.position, v = new THREE.Vector3();
    for (let k = 0; k < P.count; k++) { v.set(w.xs[k], w.ys[k], -w.zs[k]).applyMatrix4(w.inv); P.setXYZ(k, v.x, v.y, v.z); }
    P.needsUpdate = true;
    g.computeVertexNormals(); g.computeBoundingSphere(); g.computeBoundingBox();
    this.v.refreshObject(idx);
  }
  applyStroke(st, write) {
    const touched = [];
    for (const idx of this.terrainObjects()) {
      const w = this.terrainWorld(idx), b = w.box;
      if (st.x + st.r < b[0] || st.x - st.r > b[1] || st.z + st.r < b[2] || st.z - st.r > b[3]) continue;
      if (reliefStroke(w.xs, w.ys, w.zs, st)) touched.push(idx);
    }
    if (write) for (const idx of touched) this.writeTerrain(idx);
    this.hcache = null;
    return touched;
  }
  replayRelief() {
    if (this.reliefOrig) for (const [idx, arr] of this.reliefOrig) { const P = this.v.instMeshes[idx].geometry.attributes.position; P.array.set(arr); P.needsUpdate = true; }
    this.terrW = new Map();
    const all = new Set();
    for (const st of this.proj.relief) for (const idx of this.applyStroke(st, false)) all.add(idx);
    if (this.reliefOrig) for (const idx of this.reliefOrig.keys()) all.add(idx);
    for (const idx of all) { this.terrainWorld(idx); this.writeTerrain(idx); }
  }
  reliefState() { return this.relief || (this.relief = { m: "monter", r: 10, f: 0.4 }); }
  startRelief() { this.reliefState(); this.setMode("relief"); this.select(null); this.renderProps(); }
  reliefPointer(e, ph) {
    const R = this.reliefState();
    if (ph === "down") {
      if (e.button !== 0 || e.altKey) return false;
      const p = this.groundPoint(e.clientX, e.clientY);
      if (!p) return true;
      this.sculpting = { p, y: p.y, t: 0 };
      this.reliefDab(p, e.shiftKey);
      return true;
    }
    if (ph === "move") {
      const p = this.groundPoint(e.clientX, e.clientY);
      this.showBrush(p, R.r, this.mode === "relief");
      if (this.sculpting && p && (p.distanceTo(this.sculpting.p) > R.r * 0.3 || performance.now() - this.sculpting.t > 150)) { this.sculpting.p = p; this.reliefDab(p, e.shiftKey); }
      return !!this.sculpting;
    }
    if (ph === "up") {
      if (!this.sculpting) return false;
      this.sculpting = null;
      for (const r of this.proj.objets) if (r.type === "route") this.rebuildRoad(r, false);   // (les routes suivent le nouveau sol)
      this.commit();
      return true;
    }
    return false;
  }
  reliefDab(p, inverse) {
    const R = this.reliefState();
    let m = R.m;
    if (inverse && m === "monter") m = "creuser"; else if (inverse && m === "creuser") m = "monter";
    const st = { m, x: r3(p.x), z: r3(-p.z), r: R.r, f: R.f, y: r3(this.sculpting ? this.sculpting.y : p.y) };
    this.sculpting.t = performance.now();
    const touched = this.applyStroke(st, true);
    if (touched.length) this.proj.relief.push(st);
    else if (!this.warnedRelief) { this.warnedRelief = true; this.ui.warn(T("reliefHors")); }
  }
  reliefProps(el) {
    const R = this.reliefState(), n = this.proj.relief.length;
    el.innerHTML = `<div class="ph">${esc(T("tRelief"))}</div>
      <div class="sec"><div class="seg">${["monter", "creuser", "aplanir", "adoucir"].map(m => `<button data-rm="${m}" class="${R.m === m ? "on" : ""}">${esc(T("rl_" + m))}</button>`).join("")}</div></div>
      <div class="sec"><label class="sl" for="r-r">${esc(T("vegRayon"))} : ${R.r} m</label><input id="r-r" type="range" min="2" max="60" value="${R.r}">
        <label class="sl" for="r-f">${esc(T("reliefForce"))} : ${R.f} m</label><input id="r-f" type="range" min="1" max="30" value="${Math.round(R.f * 10)}"></div>
      <p class="hint pad">${esc(T("reliefAide"))}</p>
      <div class="sec"><div class="hint">${esc(T("reliefCompte", n))}</div><button class="btn wide danger" data-a="tout"${n ? "" : " disabled"}>${ICON.trash}${esc(T("reliefToutEffacer"))}</button></div>`;
    el.querySelectorAll("[data-rm]").forEach(b => b.onclick = () => { R.m = b.dataset.rm; this.renderProps(); });
    el.querySelector("#r-r").oninput = e => { R.r = +e.target.value; el.querySelector('label[for="r-r"]').textContent = T("vegRayon") + " : " + R.r + " m"; };
    el.querySelector("#r-f").oninput = e => { R.f = +e.target.value / 10; el.querySelector('label[for="r-f"]').textContent = T("reliefForce") + " : " + R.f + " m"; };
    el.querySelector('[data-a="tout"]').onclick = () => { this.proj.relief = []; this.replayRelief(); for (const r of this.proj.objets) if (r.type === "route") this.rebuildRoad(r, false); this.commit(); this.renderProps(); };
  }

  // ---------------------------------------------------------------- vegetation au pinceau (demande de JD, 21/10)
  // Les forets du jeu (TREES1, BUSHES2...) sont UN maillage chacune : des quads croises (deux par arbre, meme axe). Une plante =
  // les quads dont les centres se touchent (moins de 35 cm). Le paquet garde le centre de la plante dans le repere du maillage
  // (« plante x y z », repere d'Unity) : le mod refait les memes plantes et prend la plus proche (MapPacks.PlantMesh).
  upAxis(ob) {   // axe du maillage qui monte dans le monde (ligne y de la matrice d'Unity), en repere three (z inverse)
    const t = ob.t, row = [t[4], t[5], -t[6]];
    let a = 0; for (let i = 1; i < 3; i++) if (Math.abs(row[i]) > Math.abs(row[a])) a = i;
    return { a, sign: Math.sign(row[a]) || 1 };
  }
  plantClusters(idx) {
    this.plants = this.plants || new Map();
    if (this.plants.has(idx)) return this.plants.get(idx);
    const g = this.v.geoOf(this.v.info.objets[idx].m), P = g.attributes.position, I = g.index.array, nv = P.count, nt = I.length / 3;
    const par = new Int32Array(nv); for (let i = 0; i < nv; i++) par[i] = i;
    const f = x => { while (par[x] !== x) { par[x] = par[par[x]]; x = par[x]; } return x; };
    for (let t = 0; t < nt; t++) { const a = f(I[t * 3]); par[f(I[t * 3 + 1])] = a; par[f(I[t * 3 + 2])] = a; }
    const comps = new Map(), order = [];
    for (let t = 0; t < nt; t++) {
      const r = f(I[t * 3]);
      let c = comps.get(r);
      if (!c) { c = { mn: [1e9, 1e9, 1e9], mx: [-1e9, -1e9, -1e9], tris: [] }; comps.set(r, c); order.push(c); }
      c.tris.push(t);
      for (let k = 0; k < 3; k++) { const v = I[t * 3 + k]; for (let a = 0; a < 3; a++) { const x = P.array[v * 3 + a]; if (x < c.mn[a]) c.mn[a] = x; if (x > c.mx[a]) c.mx[a] = x; } }
    }
    const cells = new Map(), clusters = [];
    for (const c of order) {
      const ce = [0, 1, 2].map(a => (c.mn[a] + c.mx[a]) / 2), key = ce.map(x => Math.floor(x / 0.5));
      let hit = null;
      for (let dx = -1; dx <= 1 && !hit; dx++) for (let dy = -1; dy <= 1 && !hit; dy++) for (let dz = -1; dz <= 1 && !hit; dz++) {
        for (const cl of cells.get((key[0] + dx) + "," + (key[1] + dy) + "," + (key[2] + dz)) || []) if (Math.hypot(cl.c[0] - ce[0], cl.c[1] - ce[1], cl.c[2] - ce[2]) < 0.35) { hit = cl; break; }
      }
      if (!hit) { hit = { c: ce, tris: [] }; clusters.push(hit); const k = key.join(","); if (!cells.has(k)) cells.set(k, []); cells.get(k).push(hit); }
      for (const t of c.tris) hit.tris.push(t);
    }
    this.plants.set(idx, clusters);
    return clusters;
  }
  // geometrie d'une plante (repere du maillage, three), posee par son pied ; cle : centre dans le repere d'Unity
  plantGeo(idx, key) {
    this.pgeo = this.pgeo || new Map();
    const k = idx + "|" + key.join(",");
    if (this.pgeo.has(k)) return this.pgeo.get(k);
    const ob = this.v.info.objets[idx], g0 = this.v.geoOf(ob.m), cls = this.plantClusters(idx), want = [key[0], key[1], -key[2]];
    let best = cls[0], bd = 1e18;
    for (const c of cls) { const d = (c.c[0] - want[0]) ** 2 + (c.c[1] - want[1]) ** 2 + (c.c[2] - want[2]) ** 2; if (d < bd) { bd = d; best = c; } }
    const P = g0.attributes.position, N = g0.attributes.normal, UV = g0.attributes.uv, I = g0.index.array;
    const grp = new Int32Array(I.length / 3);
    for (const gr of g0.groups) for (let t = gr.start / 3; t < (gr.start + gr.count) / 3; t++) grp[t] = gr.materialIndex;
    const tris = best.tris.slice().sort((a, b) => grp[a] - grp[b] || a - b);
    const pos = [], nor = [], uv = [], mn = [1e9, 1e9, 1e9], mx = [-1e9, -1e9, -1e9];
    for (const t of tris) for (let c = 0; c < 3; c++) { const v = I[t * 3 + c]; for (let a = 0; a < 3; a++) { const x = P.array[v * 3 + a]; pos.push(x); mn[a] = Math.min(mn[a], x); mx[a] = Math.max(mx[a], x); } if (N) nor.push(N.getX(v), N.getY(v), N.getZ(v)); if (UV) uv.push(UV.getX(v), UV.getY(v)); }
    const up = this.upAxis(ob), o = [0, 1, 2].map(a => a === up.a ? (up.sign > 0 ? mn[a] : mx[a]) : (mn[a] + mx[a]) / 2);
    for (let i = 0; i < pos.length; i++) pos[i] -= o[i % 3];
    const g = new THREE.BufferGeometry();
    g.setAttribute("position", new THREE.Float32BufferAttribute(pos, 3));
    if (N) g.setAttribute("normal", new THREE.Float32BufferAttribute(nor, 3)); else g.computeVertexNormals();
    if (UV) g.setAttribute("uv", new THREE.Float32BufferAttribute(uv, 2));
    for (let j = 0; j < tris.length;) { let e = j; while (e < tris.length && grp[tris[e]] === grp[tris[j]]) e++; g.addGroup(j * 3, (e - j) * 3, grp[tris[j]]); j = e; }
    this.pgeo.set(k, g);
    return g;
  }
  // pinceau : especes (objets du jeu : forets ou objets seuls), rayon, densite, ecart de taille, collisions
  brushState() {
    if (!this.brush) {
      const o = this.v.info.objets, esp = [];
      o.forEach((x, i) => { if (x.k === 3 && !x.r && x.p && /^(TREES|BUSHES)/.test(x.n) && !esp.some(j => o[j].n === x.n) && esp.length < 3) esp.push(i); });
      this.brush = { especes: esp, rayon: 6, densite: 4, variation: 0.25, col: "aucune" };
    }
    return this.brush;
  }
  startVeg() { this.brushState(); this.setMode("veg"); this.select(null); this.renderProps(); }
  // une touche du pinceau autour du point p (three) ; Maj : efface les plantes du pinceau dans le rayon
  dab(p, erase) {
    const b = this.brushState(), o = this.v.info.objets;
    if (erase) {
      const gone = this.proj.objets.filter(r => r.veg && Math.hypot(r.pos[0] - p.x, -r.pos[2] - p.z) < b.rayon);
      for (const r of gone) { this.proj.objets.splice(this.proj.objets.indexOf(r), 1); const n = this.nodes.get(r.id); if (n) { this.root.remove(n); this.nodes.delete(r.id); } }
      return gone.length;
    }
    if (!b.especes.length) { this.ui.warn(T("vegSansEspece")); return 0; }
    let made = 0;
    for (let i = 0; i < b.densite; i++) {
      const a = Math.random() * Math.PI * 2, d = Math.sqrt(Math.random()) * b.rayon;
      const x = p.x + Math.cos(a) * d, z = p.z + Math.sin(a) * d, y = this.heightAt(x, z, p.y);
      if (y === null) continue;
      const idx = b.especes[Math.floor(Math.random() * b.especes.length)], ob = o[idx];
      const m = Viewer.mat(ob.t), q = new THREE.Quaternion(), sc = new THREE.Vector3(), tr = new THREE.Vector3();
      m.decompose(tr, q, sc);
      q.premultiply(new THREE.Quaternion().setFromAxisAngle(new THREE.Vector3(0, 1, 0), Math.random() * Math.PI * 2));
      const k = 1 + (Math.random() * 2 - 1) * b.variation;
      const rec = { id: nextId++, nom: ob.n, type: "jeu", veg: true, pos: U.posFrom(new THREE.Vector3(x, y - 0.05, z)), rot: U.rotFrom(q), ech: [r3(sc.x * k), r3(sc.y * k), r3(sc.z * k)], col: b.col };
      const cls = this.plantClusters(idx);
      if (cls.length > 1) { const c = cls[Math.floor(Math.random() * cls.length)].c; rec.src = { p: ob.p, n: ob.n, plante: [r3(c[0]), r3(c[1]), r3(-c[2])] }; }
      else rec.src = { p: ob.p, n: ob.n };
      this.proj.objets.push(rec);
      this.build(rec);
      made++;
    }
    return made;
  }
  vegPointer(e, ph) {
    if (ph === "down") {
      if (e.button !== 0 || e.altKey) return false;
      const p = this.groundPoint(e.clientX, e.clientY);
      if (p) { this.dab(p, e.shiftKey); this.painting = { p }; }
      return true;
    }
    if (ph === "move") {
      const p = this.groundPoint(e.clientX, e.clientY);
      this.showBrush(p);
      if (this.painting && p && p.distanceTo(this.painting.p) > this.brushState().rayon * 0.6) { this.dab(p, e.shiftKey); this.painting.p = p; }
      return !!this.painting;
    }
    if (ph === "up") { if (!this.painting) return false; this.painting = null; this.commit(); this.renderList(); return true; }
    return false;
  }
  showBrush(p, radius, on) {
    if (!this.ring) {
      this.ring = new THREE.Mesh(new THREE.RingGeometry(0.94, 1, 48).rotateX(-Math.PI / 2), new THREE.MeshBasicMaterial({ color: 0x7cf2c4, transparent: true, opacity: 0.8, depthTest: false }));
      this.ring.renderOrder = 10; this.ring.userData.noPick = true; this.v.scene.add(this.ring);
    }
    this.ring.visible = !!p && (on !== undefined ? on : this.mode === "veg");
    if (p) { this.ring.position.set(p.x, p.y + 0.15, p.z); this.ring.scale.setScalar(radius || this.brushState().rayon); }
  }
  vegProps(el) {
    const b = this.brushState(), o = this.v.info.objets, n = this.proj.objets.filter(r => r.veg).length;
    el.innerHTML = `<div class="ph">${esc(T("tVegetation"))}</div>
      <div class="sec"><div class="sl">${esc(T("vegEspeces"))}</div><div class="chips">${b.especes.map((i, j) => `<button class="mini on" data-esp="${j}" title="${esc(T("retirer"))}">${esc(o[i].n)} ×</button>`).join("")}</div>
        <button class="btn wide" data-a="esp">${ICON.box}${esc(T("vegAjouterEspece"))}</button></div>
      <div class="sec"><label class="sl" for="b-r">${esc(T("vegRayon"))} : ${b.rayon} m</label><input id="b-r" type="range" min="1" max="40" value="${b.rayon}">
        <label class="sl" for="b-d">${esc(T("vegDensite"))} : ${b.densite}</label><input id="b-d" type="range" min="1" max="30" value="${b.densite}">
        <label class="sl" for="b-v">${esc(T("vegVariation"))} : ${Math.round(b.variation * 100)} %</label><input id="b-v" type="range" min="0" max="60" value="${Math.round(b.variation * 100)}"></div>
      <div class="sec"><div class="sl">${esc(T("collisions"))}</div><div class="seg">${["aucune", "forme"].map(c => `<button data-col="${c}" class="${b.col === c ? "on" : ""}">${esc(T("col_" + c))}</button>`).join("")}</div></div>
      <p class="hint pad">${esc(T("vegAide"))}</p>
      <div class="sec"><div class="hint">${esc(T("vegCompte", n))}</div><button class="btn wide danger" data-a="tout"${n ? "" : " disabled"}>${ICON.trash}${esc(T("vegToutEffacer"))}</button></div>`;
    el.querySelectorAll("[data-esp]").forEach(x => x.onclick = () => { b.especes.splice(+x.dataset.esp, 1); this.renderProps(); });
    el.querySelector('[data-a="esp"]').onclick = () => this.openPalette(i => { if (!b.especes.includes(i)) b.especes.push(i); this.renderProps(); });
    const rg = (id, f, k) => el.querySelector(id).oninput = e => { b[f] = +e.target.value * k; el.querySelector(`label[for="${id.slice(1)}"]`).textContent = T({ rayon: "vegRayon", densite: "vegDensite", variation: "vegVariation" }[f]) + " : " + (f === "variation" ? Math.round(b[f] * 100) + " %" : b[f] + (f === "rayon" ? " m" : "")); };
    rg("#b-r", "rayon", 1); rg("#b-d", "densite", 1); rg("#b-v", "variation", 0.01);
    el.querySelectorAll("[data-col]").forEach(x => x.onclick = () => { b.col = x.dataset.col; this.renderProps(); });
    el.querySelector('[data-a="tout"]').onclick = () => { for (const r of this.proj.objets.filter(r => r.veg)) { const nd = this.nodes.get(r.id); if (nd) { this.root.remove(nd); this.nodes.delete(r.id); } } this.proj.objets = this.proj.objets.filter(r => !r.veg); this.commit(); this.renderProps(); };
  }

  // ---------------------------------------------------------------- ensembles qui fonctionnent (carte format 4)
  // Un objet du jeu et ses voisins qui ont des automates (porte et poignee, boite aux lettres...) : info.unites[u] = { p, n,
  // r (rendus), f (automates), t (matrice) } ; copie entiere (le mod fait Instantiate) ou masquage entier.
  unitsOf(idx) { const ob = this.v.info.objets[idx], U2 = this.v.info.unites || []; return (ob && ob.u ? ob.u : []).map(i => Object.assign({ i }, U2[i])).filter(u => u.p); }
  addUnit(ui) {
    const u = this.v.info.unites[ui], m = Viewer.mat(u.t), pos = new THREE.Vector3(), q = new THREE.Quaternion(), sc = new THREE.Vector3();
    m.decompose(pos, q, sc);
    const rec = { id: nextId++, nom: u.n, type: "unite", src: { p: u.p, n: u.n }, pos: U.posFrom(pos), rot: U.rotFrom(q), ech: [r3(sc.x), r3(sc.y), r3(sc.z)], col: "aucune" };
    this.proj.objets.push(rec);
    this.build(rec);
    this.select({ rec });
    this.commit();
    this.ui.warn(T("uniteCopiee"));
  }
  unitNode(rec, node) {
    const U2 = this.v.info.unites || [], u = U2.find(x => x.p === rec.src.p);
    if (!u) { rec.manque = true; node.add(placeholder()); return; }
    const inv = Viewer.mat(u.t).invert(), pre = u.p + "/";
    this.v.info.objets.forEach((o, i) => {
      if (o.p !== u.p && !(o.p && o.p.startsWith(pre))) return;
      const g = this.v.geoOf(o.m), mats = this.v.matsFor(o, g);
      const m = new THREE.Mesh(g, mats.length === 1 ? mats[0] : mats);
      m.matrixAutoUpdate = false;
      m.matrix.copy(inv).multiply(o.r ? new THREE.Matrix4() : Viewer.mat(o.t));   // (regroupe : sommets dans le monde)
      node.add(m);
    });
  }
  isUnitMasked(p) { return this.proj.masques.some(m => m.u && m.p === p); }
  toggleUnitMask(u) {
    if (this.isUnitMasked(u.p)) this.proj.masques = this.proj.masques.filter(m => !(m.u && m.p === u.p));
    else this.proj.masques.push({ p: u.p, n: u.n, u: true });
    this.applyMasks(); this.commit(); this.renderProps();
  }

  // lumiere : boule lumineuse (choisissable) + vraie lumiere ponctuelle pour les 8 premieres et la choisie
  addLight() {
    const p = this.spot();
    const rec = { id: nextId++, nom: T("lumiere"), type: "lumiere", pos: U.posFrom(p.add(new THREE.Vector3(0, 3, 0))), rot: [0, 0, 0, 1], ech: [1, 1, 1], couleur: "#ffd9a0", intensite: 1.5, portee: 12, nuit: true, ombres: false, col: "aucune" };
    this.proj.objets.push(rec);
    this.build(rec);
    this.select({ rec });
    this.commit();
  }
  lightNode(rec, node) {
    const col = new THREE.Color(rec.couleur || "#ffffff");
    const ball = new THREE.Mesh(new THREE.SphereGeometry(0.25, 16, 12), new THREE.MeshBasicMaterial({ color: col }));
    ball.userData.cuit = true; ball.userData.lumiere = true;
    node.add(ball);
    const n = this.proj.objets.filter(o => o.type === "lumiere").indexOf(rec);
    if (n < 8 || (this.sel && this.sel.rec === rec)) {
      const l = new THREE.PointLight(col, (rec.intensite || 1) * 25, rec.portee || 12, 2);
      l.userData.noPick = true;
      node.add(l);
    }
  }
  lightProps(el, rec) {
    el.innerHTML = `<div class="ph">${esc(T("proprietes"))}</div>
      <div class="sec"><label class="sl" for="p-nom">${esc(T("nom"))}</label><input id="p-nom" class="txt" value="${esc(rec.nom)}" spellcheck="false"></div>
      <div class="sec"><div class="sl">${esc(T("position"))} (m)</div><div class="xyz">${["px", "py", "pz"].map((f, i) => `<label class="num"><b style="color:${["#ff6e6e", "#7cf2c4", "#6aa8ff"][i]}">${f.slice(-1).toUpperCase()}</b><input data-f="${f}" value="${this.values(rec)[f]}" inputmode="decimal" spellcheck="false"></label>`).join("")}</div></div>
      <div class="sec"><label class="sl" for="p-col">${esc(T("couleur"))}</label><input id="p-col" type="color" class="couleur" value="${esc(rec.couleur || "#ffffff")}">
        <label class="sl" for="p-int">${esc(T("intensite"))}</label><input id="p-int" class="txt" value="${rec.intensite}" inputmode="decimal">
        <label class="sl" for="p-por">${esc(T("portee"))} (m)</label><input id="p-por" class="txt" value="${rec.portee}" inputmode="decimal">
        <label class="cb"><input type="checkbox" id="p-nuit"${rec.nuit ? " checked" : ""}><span>${esc(T("nuitSeule"))}</span></label>
        <label class="cb"><input type="checkbox" id="p-omb"${rec.ombres ? " checked" : ""}><span>${esc(T("ombresLum"))}</span></label></div>
      <div class="sec row2"><button class="btn" data-a="dup">${ICON.copy}${esc(T("dupliquer"))}</button><button class="btn danger" data-a="suppr">${ICON.trash}${esc(T("supprimer"))}</button></div>`;
    const num = (id, f, lo, hi) => el.querySelector(id).onchange = e => { const x = parseFloat(e.target.value.replace(",", ".")); if (isFinite(x) && x >= lo && x <= hi) { rec[f] = x; this.rebuild(rec); this.commit(); } this.renderProps(); };
    el.querySelector("#p-nom").onchange = e => { rec.nom = e.target.value.trim() || rec.nom; this.commit(); };
    el.querySelectorAll("input[data-f]").forEach(i => i.onchange = () => this.setValue(rec, i.dataset.f, parseFloat(i.value.replace(",", "."))));
    el.querySelector("#p-col").oninput = e => { rec.couleur = e.target.value; this.rebuild(rec); };
    el.querySelector("#p-col").onchange = () => this.commit();
    num("#p-int", "intensite", 0, 20); num("#p-por", "portee", 0.5, 200);
    el.querySelector("#p-nuit").onchange = e => { rec.nuit = e.target.checked; this.commit(); };
    el.querySelector("#p-omb").onchange = e => { rec.ombres = e.target.checked; this.commit(); };
    el.querySelector('[data-a="dup"]').onclick = () => this.duplicate();
    el.querySelector('[data-a="suppr"]').onclick = () => this.remove();
  }

  // point du sol sous le curseur (sol, routes, batiments du jeu)
  groundPoint(x, y) {
    const ray = this.v.rayAt(x, y);
    ray.far = 5000;
    const h = ray.intersectObjects([this.v.groups[0], this.v.groups[1], this.v.groups[2]], true).filter(h => h.object.visible)[0];
    return h ? h.point.clone() : null;
  }
  // hauteur du sol (sol et routes du jeu), en cache au demi-metre
  heightAt(x, z, from) {
    this.hcache = this.hcache || new Map();
    const kx = Math.round(x * 2) / 2, kz = Math.round(z * 2) / 2, key = kx + "_" + kz;
    if (this.hcache.has(key)) return this.hcache.get(key);
    // (sol, routes, et les surfaces plates des objets du jeu : pelouse d'une cour, parking... -- pas un toit ni une table :
    // plus de 2,5 m au-dessus du point de la route)
    const ray = new THREE.Raycaster(new THREE.Vector3(kx, from + 60, kz), new THREE.Vector3(0, -1, 0), 0, 400);
    ray.layers.enableAll();
    const nv = new THREE.Vector3();
    const h = ray.intersectObjects([this.v.groups[0], this.v.groups[1], this.v.groups[2]], true)
      .filter(h => h.object.visible && h.point.y < from + 2.5 && (!h.face || nv.copy(h.face.normal).transformDirection(h.object.matrixWorld).y > 0.8))[0];
    const y = h ? h.point.y : null;
    this.hcache.set(key, y);
    return y;
  }

  // controles de Bezier d'un troncon (repere three)
  roadCtrl(rec, i) {
    const a = rec.pts[i], b = rec.pts[i + 1];
    const P0 = U.posTo(a.p, new THREE.Vector3()), P3 = U.posTo(b.p, new THREE.Vector3());
    const P1 = P0.clone().add(U.posTo(a.h || [0, 0, 0], new THREE.Vector3()));
    const P2 = P3.clone().add(U.posTo(b.hi || (b.h || [0, 0, 0]).map(x => -x), new THREE.Vector3()));
    return [P0, P1, P2, P3];
  }
  roadMesh(rec, preview) {
    if (rec.pts.length < 2) return null;
    const w = rec.largeur || 6, across = 4;
    const sm = rec.surface && rec.surface.k === "mat" ? this.v.info.matieres[rec.surface.mi] : null;
    const tu = sm && sm.r ? sm.r[0] || 1 : 1, tv = sm && sm.r ? sm.r[1] || 1 : 1;
    // texture allongee (traces de pneus de MWC, 2048 x 512) : la route suit sa largeur (u), une repetition = largeur x rapport
    const im = rec.surface && rec.surface.k === "mat" && this.v.mats[rec.surface.mi] && this.v.mats[rec.surface.mi].map ? this.v.mats[rec.surface.mi].map.image : null;
    const alongU = !!(im && im.width > im.height * 1.5), aspect = alongU ? im.width / im.height : 1;
    const pts = [], tans = [];
    for (let i = 0; i + 1 < rec.pts.length; i++) {
      const [A, B, C, D] = this.roadCtrl(rec, i);
      const len = A.distanceTo(B) + B.distanceTo(C) + C.distanceTo(D);
      const n = Math.max(2, Math.ceil(len / 1.5));
      for (let k = i ? 1 : 0; k <= n; k++) {
        const t = k / n, u = 1 - t;
        pts.push(new THREE.Vector3().addScaledVector(A, u * u * u).addScaledVector(B, 3 * u * u * t).addScaledVector(C, 3 * u * t * t).addScaledVector(D, t * t * t));
        const tg = new THREE.Vector3().addScaledVector(B.clone().sub(A), 3 * u * u).addScaledVector(C.clone().sub(B), 6 * u * t).addScaledVector(D.clone().sub(C), 3 * t * t);
        if (tg.setY(0).lengthSq() < 1e-6) tg.copy(t < 0.5 ? C.clone().sub(A) : D.clone().sub(B)).setY(0);   // (point sans poignee : vers le point suivant)
        if (tg.lengthSq() < 1e-6) tg.copy(D).sub(A).setY(0);
        tans.push(tg);
      }
    }
    // troncons : centre, perpendiculaire, hauteurs du sol en travers ; route plate en travers (« plat », par defaut) au plus
    // haut du sol sous elle : le sol ne la traverse plus ; talus de chaque cote jusqu'au sol (1,5 m, « talus ») ; marquages
    const flat = rec.plat !== false, sections = [];
    let dist = 0;
    pts.forEach((c, j) => {
      if (j) dist += c.distanceTo(pts[j - 1]);
      const t = tans[j].setY(0); if (t.lengthSq() < 1e-8) t.copy(j ? pts[j].clone().sub(pts[j - 1]).setY(0) : new THREE.Vector3(1, 0, 0)); t.normalize();
      const perp = new THREE.Vector3(-t.z, 0, t.x), hs = [];
      for (let k = 0; k <= across; k++) {
        const q = c.clone().addScaledVector(perp, w * (k / across - 0.5));
        const y = preview ? null : this.heightAt(q.x, q.z, c.y);
        hs.push(y === null ? c.y : y);
      }
      const top = Math.max(...hs);
      sections.push({ c, perp, dist, ys: hs.map(h => (flat ? top : h) + 0.06) });
    });
    const grp = new THREE.Group();
    const strip = (cols, mat, uvOf) => {   // bande de colonnes (une liste de points par troncon)
      const pos = [], uv = [], ind = [], nc = cols(sections[0]).length;
      sections.forEach((sec, j) => {
        cols(sec).forEach((q, k) => { pos.push(q.x, q.y, q.z); const u = uvOf(sec, k, nc); uv.push(u[0], u[1]); });
        if (j) for (let k = 0; k + 1 < nc; k++) { const a = (j - 1) * nc + k, b = a + nc; ind.push(a, a + 1, b, a + 1, b + 1, b); }
      });
      const g = new THREE.BufferGeometry();
      g.setAttribute("position", new THREE.Float32BufferAttribute(pos, 3));
      g.setAttribute("uv", new THREE.Float32BufferAttribute(uv, 2));
      g.setIndex(ind); g.computeVertexNormals();
      const m = new THREE.Mesh(g, mat); m.userData.cuit = true; grp.add(m);
    };
    const at = (sec, off, y) => sec.c.clone().addScaledVector(sec.perp, off).setY(y);
    const surf = rec.surface ? this.habitMat(rec.surface) : new THREE.MeshLambertMaterial({ color: 0x555555 });
    strip(sec => sec.ys.map((y, k) => at(sec, w * (k / across - 0.5), y)), surf,
      (sec, k) => alongU ? [sec.dist / (w * aspect) / tu, k / across / tv] : [k / across / tu, sec.dist / w / tv]);
    if (rec.talus !== false && !preview) {
      const side = this.surfacePresets("talus")[0], mat = side ? this.habitMat(side) : surf;
      for (const sg of [-1, 1]) strip(sec => {
        const e = sg < 0 ? 0 : across, off = w * (e / across - 0.5), o2 = off + sg * 1.6;
        const q2 = at(sec, o2, 0), h2 = this.heightAt(q2.x, q2.z, sec.c.y);
        const pair = [at(sec, off, sec.ys[e] - 0.01), at(sec, o2, (h2 === null ? sec.ys[e] - 0.6 : h2) - 0.05)];
        return sg < 0 ? pair.reverse() : pair;   // (meme sens que la chaussee : faces vers le haut)
      }, mat, (sec, k) => [k, sec.dist / 3]);
    }
    // marquages : ligne du centre en tirets (3 m sur 9), lignes de bord continues ; blancs, 1 cm au-dessus
    if (rec.marquage && rec.marquage !== "aucun" && !preview) {
      const white = this.markMat || (this.markMat = new THREE.MeshLambertMaterial({ color: 0xf2f2f2 }));
      const lines = [];
      if (rec.marquage === "centre" || rec.marquage === "tout") lines.push({ off: 0, dash: true });
      if (rec.marquage === "bords" || rec.marquage === "tout") lines.push({ off: -w / 2 + 0.3, dash: false }, { off: w / 2 - 0.3, dash: false });
      const pos = [], ind = [];
      for (const ln of lines) for (let j = 1; j < sections.length; j++) {
        const s0 = sections[j - 1], s1 = sections[j];
        if (ln.dash && ((s0.dist + s1.dist) / 2) % 9 > 3) continue;
        const yAt = (sec, off) => { const f = (off / w + 0.5) * across, k = Math.min(across - 1, Math.floor(f)), r = f - k; return sec.ys[k] * (1 - r) + sec.ys[k + 1] * r + 0.012; };
        const b = pos.length / 3;
        for (const sec of [s0, s1]) for (const d of [-0.06, 0.06]) { const q = at(sec, ln.off + d, yAt(sec, ln.off + d)); pos.push(q.x, q.y, q.z); }
        ind.push(b, b + 1, b + 2, b + 1, b + 3, b + 2);
      }
      if (pos.length) {
        const g = new THREE.BufferGeometry();
        g.setAttribute("position", new THREE.Float32BufferAttribute(pos, 3));
        g.setAttribute("uv", new THREE.Float32BufferAttribute(new Array(pos.length / 3 * 2).fill(0), 2));
        g.setIndex(ind); g.computeVertexNormals();
        const m = new THREE.Mesh(g, white); m.userData.cuit = true; grp.add(m);
      }
    }
    return grp;
  }
  rebuildRoad(rec, preview) {
    const node = this.nodes.get(rec.id);
    if (!node) return;
    for (const c of [...node.children]) { node.remove(c); if (c.geometry) c.geometry.dispose(); }
    const m = this.roadMesh(rec, preview);
    if (m) node.add(m);
    this.showRoadUi();
  }
  // points (blancs, le choisi en jaune) et poignees (bleues) de la route choisie, toujours visibles
  showRoadUi() {
    if (this.roadUi) { this.v.scene.remove(this.roadUi); this.roadUi.traverse(o => o.geometry && o.geometry.dispose()); this.roadUi = null; }
    const rec = this.sel && this.sel.rec && this.sel.rec.type === "route" ? this.sel.rec : null;
    if (!rec) return;
    const g = new THREE.Group(), anchors = [], handles = [], lines = [];
    rec.pts.forEach((pt, i) => {
      const P = U.posTo(pt.p, new THREE.Vector3());
      anchors.push(P.x, P.y, P.z);
      for (const k of ["h", "hi"]) {
        const off = k === "h" ? pt.h : pt.hi || (pt.h || [0, 0, 0]).map(x => -x);
        if (!off || (Math.abs(off[0]) + Math.abs(off[1]) + Math.abs(off[2]) < 1e-3)) continue;
        const Q = P.clone().add(U.posTo(off, new THREE.Vector3()));
        handles.push(Q.x, Q.y, Q.z); lines.push(P.x, P.y, P.z, Q.x, Q.y, Q.z);
      }
    });
    const pts = (arr, color, size) => { const geo = new THREE.BufferGeometry(); geo.setAttribute("position", new THREE.Float32BufferAttribute(arr, 3)); const o = new THREE.Points(geo, new THREE.PointsMaterial({ color, size, sizeAttenuation: false, depthTest: false })); o.renderOrder = 10; return o; };
    if (lines.length) { const lg = new THREE.BufferGeometry(); lg.setAttribute("position", new THREE.Float32BufferAttribute(lines, 3)); const l = new THREE.LineSegments(lg, new THREE.LineBasicMaterial({ color: 0x6aa8ff, depthTest: false })); l.renderOrder = 9; g.add(l); }
    g.add(pts(anchors, 0xffffff, 11));
    if (handles.length) g.add(pts(handles, 0x6aa8ff, 9));
    if (this.anchorSel !== undefined && rec.pts[this.anchorSel]) { const P = U.posTo(rec.pts[this.anchorSel].p, new THREE.Vector3()); g.add(pts([P.x, P.y, P.z], 0xffd23f, 14)); }
    g.traverse(o => o.userData.noPick = true);
    this.roadUi = g;
    this.v.scene.add(g);
  }
  // point ou poignee sous le curseur (a 10 px pres)
  handleAt(e, rec) {
    const r = this.v.o.canvas.getBoundingClientRect(), cam = this.v.camera;
    let best = null, bd = 10;
    const test = (P, i, kind) => {
      const s = P.clone().project(cam);
      if (s.z > 1) return;
      const d = Math.hypot((s.x + 1) / 2 * r.width + r.left - e.clientX, (1 - s.y) / 2 * r.height + r.top - e.clientY);
      if (d < bd) { bd = d; best = { i, kind }; }
    };
    rec.pts.forEach((pt, i) => {
      const P = U.posTo(pt.p, new THREE.Vector3());
      if (pt.h) test(P.clone().add(U.posTo(pt.h, new THREE.Vector3())), i, "h");
      test(P.clone().add(U.posTo(pt.hi || (pt.h || [0, 0, 0]).map(x => -x), new THREE.Vector3())), i, "hi");
      test(P, i, "p");
    });
    return best;
  }
  pointer(e, ph) {
    if (this.mode === "veg") return this.vegPointer(e, ph);
    if (this.mode === "relief") return this.reliefPointer(e, ph);
    const road = this.sel && this.sel.rec && this.sel.rec.type === "route" ? this.sel.rec : null;
    if (ph === "down") {
      if (e.button !== 0 || e.altKey && this.mode !== "route") return false;
      if (road) {
        const hd = this.handleAt(e, road);
        if (hd) { this.dragH = Object.assign({ rec: road, x: e.clientX, y: e.clientY }, hd); this.anchorSel = hd.i; this.showRoadUi(); this.renderProps(); return true; }
      }
      if (this.mode !== "route") return false;
      let p = this.groundPoint(e.clientX, e.clientY);
      const snapTo = this.anchorNear(e, this.drawing);   // (raccord : le point d'une autre route a 12 px)
      if (snapTo) p = snapTo;
      if (!p) return true;
      let rec = this.drawing;
      if (!rec) {
        rec = { id: nextId++, nom: T("route"), type: "route", pts: [], largeur: 6, surface: this.defaultSurface("route"), col: "forme", pos: [0, 0, 0], rot: [0, 0, 0, 1], ech: [1, 1, 1] };
        this.proj.objets.push(rec); this.drawing = rec; this.build(rec); this.select({ rec });
      }
      rec.pts.push({ p: U.posFrom(p), h: [0, 0, 0] });
      this.anchorSel = rec.pts.length - 1;
      this.dragH = { rec, i: this.anchorSel, kind: "new", x: e.clientX, y: e.clientY };
      this.rebuildRoad(rec, true);
      this.renderList();
      return true;
    }
    if (!this.dragH) return false;
    if (ph === "move") {
      const d = this.dragH, pt = d.rec.pts[d.i];
      if (d.kind === "new" && Math.hypot(e.clientX - d.x, e.clientY - d.y) < 4) return true;
      const P = U.posTo(pt.p, new THREE.Vector3());
      if (d.kind === "p") { const g = this.groundPoint(e.clientX, e.clientY); if (g) pt.p = U.posFrom(g); }
      else {
        const ray = this.v.rayAt(e.clientX, e.clientY).ray, q = new THREE.Vector3();
        if (!ray.intersectPlane(new THREE.Plane(new THREE.Vector3(0, 1, 0), -P.y), q)) return true;
        const off = U.posFrom(q.sub(P)).map(x => x);
        if (d.kind === "hi") { pt.hi = off; if (!e.altKey) pt.h = off.map(x => -x); }
        else { pt.h = off; if (!e.altKey) delete pt.hi; }
      }
      this.rebuildRoad(d.rec, true);
      return true;
    }
    if (ph === "up") { const d = this.dragH; this.dragH = null; this.rebuildRoad(d.rec, false); if (!this.drawing) this.commit(); this.renderProps(); return true; }
    return false;
  }
  anchorNear(e, except) {
    const r = this.v.o.canvas.getBoundingClientRect(), cam = this.v.camera;
    let best = null, bd = 12;
    for (const o of this.proj.objets) {
      if (o.type !== "route" || o === except) continue;
      for (const pt of o.pts) {
        const P = U.posTo(pt.p, new THREE.Vector3()), s = P.clone().project(cam);
        if (s.z > 1) continue;
        const d = Math.hypot((s.x + 1) / 2 * r.width + r.left - e.clientX, (1 - s.y) / 2 * r.height + r.top - e.clientY);
        if (d < bd) { bd = d; best = P; }
      }
    }
    return best;
  }
  removeAnchor() {
    const rec = this.sel && this.sel.rec;
    if (!rec || rec.type !== "route" || this.anchorSel === undefined) return false;
    rec.pts.splice(this.anchorSel, 1);
    this.anchorSel = rec.pts.length ? Math.min(this.anchorSel, rec.pts.length - 1) : undefined;
    if (rec.pts.length < 2) { this.remove(); return true; }
    this.rebuildRoad(rec, false); this.commit(); this.renderProps();
    return true;
  }

  // ---------------------------------------------------------------- textures (demande de JD, 20/10)
  // Une « tenue » (habit) : { k: "tex", t, nom, ref: "tex:<nom>" } texture du jeu (retrouvee par son nom chez chacun :
  // aucune image du jeu dans le paquet), { k: "mat", mi, nom, ref: "mat:<nom>" } matiere du jeu entiere (cartes preparees
  // avant le format 3 : pas de noms de textures), { k: "img", f: "textures/x.png" } image perso du projet ; rep : [u, v].
  // Copies d'objets du jeu : rec.subs { "<sous-maillage>" | "*": habit } ; modeles : rec.habit (tout le modele) et
  // rec.faces [{ m: rang du maillage, tris: [triangles], habit }] (double-clic : une face, triangles voisins du meme plan).
  habitMat(h, base) {
    this.hmats = this.hmats || new Map();
    const key = JSON.stringify(h) + (base && base.alphaTest ? "|cut" : "");
    if (this.hmats.has(key)) return this.hmats.get(key);
    let mat;
    if (h.k === "eau" || h.k === "lac") mat = new THREE.MeshLambertMaterial({ color: this.v.o.game === "msc" ? 0x3f6f96 : 0x8fb0cc, transparent: true, opacity: 0.72, depthWrite: false });
    else if (h.k === "mat" && this.v.mats[h.mi]) {
      mat = this.v.mats[h.mi].clone();
      if (mat.map && h.rep) { mat.map = mat.map.clone(); mat.map.repeat.set(h.rep[0], h.rep[1]); mat.map.needsUpdate = true; }
    } else {
      mat = new THREE.MeshLambertMaterial({ color: 0xffffff });
      if (base && base.alphaTest) { mat.alphaTest = base.alphaTest; mat.side = THREE.DoubleSide; }
      const tex = new THREE.TextureLoader().load(this.habitUrl(h), () => { mat.needsUpdate = true; });
      tex.colorSpace = THREE.SRGBColorSpace; tex.wrapS = tex.wrapT = THREE.RepeatWrapping; tex.anisotropy = 4;
      if (h.rep) tex.repeat.set(h.rep[0], h.rep[1]);
      mat.map = tex;
    }
    mat.userData.habit = h;
    this.hmats.set(key, mat);
    return mat;
  }
  habitUrl(h) {
    if (h.k === "img") return "https://projets.local/" + encodeURIComponent(this.dossier) + "/" + h.f.split("/").map(encodeURIComponent).join("/");
    if (h.k === "tex") return this.v.texUrl(h.t);
    const m = this.v.info.matieres[h.mi];
    return m && m.t >= 0 ? this.v.texUrl(m.t) : "";
  }
  habitName(h) { return h ? (h.k === "img" ? h.f.split("/").pop() : h.nom) : ""; }

  // textures du jeu a proposer : une par image du cache (son nom, format 3 ; sinon la 1re matiere qui l'utilise)
  gameTextures() {
    if (this.gtex) return this.gtex;
    const info = this.v.info, seen = new Map();
    info.matieres.forEach((m, mi) => {
      if (m.t === undefined || m.t < 0 || seen.has(m.t) || /water|InvisibleShadow|Particles|Skidmarks/i.test(m.s || "")) return;
      const nom = info.texnoms && info.texnoms[m.t] ? info.texnoms[m.t] : m.n;
      seen.set(m.t, info.texnoms ? { k: "tex", t: m.t, nom, ref: "tex:" + nom } : { k: "mat", mi, nom, ref: "mat:" + m.n });
    });
    this.gtex = [...seen.values()].filter(h => h.nom).sort((a, b) => a.nom.localeCompare(b.nom));
    return this.gtex;
  }

  // apparence d'un modele : tenue entiere puis faces (geometrie regroupee par matiere, triOrig : triangle d'origine)
  applyLooks(rec, node) {
    node = node || this.nodes.get(rec.id);
    if (!node || rec.type !== "modele") return;
    const meshes = modelMeshes(node);
    meshes.forEach((mesh, mi) => {
      const o = mesh.userData.orig || (mesh.userData.orig = { geo: mesh.geometry, mat: mesh.material });
      if (!o.geo.attributes.uv && (rec.habit || (rec.faces || []).some(f => f.m === mi))) o.geo = boxUv(o.geo);   // (modele sans uv : texture projetee)
      const om = Array.isArray(o.mat) ? o.mat : [o.mat];
      const base = rec.habit ? om.map(m => this.habitMat(rec.habit, m)) : om;
      const fs = (rec.faces || []).filter(f => f.m === mi);
      if (!fs.length) { mesh.geometry = o.geo; mesh.material = Array.isArray(o.mat) ? base : base[0]; mesh.userData.triOrig = null; return; }
      const g0 = o.geo, idx = g0.index ? g0.index.array : null, nt = (idx ? idx.length : g0.attributes.position.count) / 3;
      const slot = new Int32Array(nt);
      for (const gr of (g0.groups.length ? g0.groups : [{ start: 0, count: nt * 3, materialIndex: 0 }]))
        for (let t = gr.start / 3; t < (gr.start + gr.count) / 3 && t < nt; t++) slot[t] = gr.materialIndex;
      fs.forEach((f, k) => { for (const t of f.tris) if (t < nt) slot[t] = base.length + k; });
      const order = [...Array(nt).keys()].sort((a, b) => slot[a] - slot[b] || a - b);
      const ni = new Uint32Array(nt * 3), triOrig = new Int32Array(nt);
      order.forEach((t, j) => { triOrig[j] = t; for (let c = 0; c < 3; c++) ni[j * 3 + c] = idx ? idx[t * 3 + c] : t * 3 + c; });
      const g = new THREE.BufferGeometry();
      for (const [n, a] of Object.entries(g0.attributes)) g.setAttribute(n, a);
      g.setIndex(new THREE.BufferAttribute(ni, 1));
      for (let j = 0; j < nt;) { let e = j; while (e < nt && slot[order[e]] === slot[order[j]]) e++; g.addGroup(j * 3, (e - j) * 3, slot[order[j]]); j = e; }
      mesh.geometry = g;
      mesh.material = base.concat(fs.map(f => this.habitMat(f.habit, base[0])));
      mesh.userData.triOrig = triOrig;
    });
  }

  // double-clic sur un modele : la face (triangles relies par une arete, du meme plan que celui clique)
  onDouble(e) {
    if (this.mode === "route") {   // (fin de la route ; le double-clic a pose deux fois le dernier point)
      const d = this.drawing;
      if (d && d.pts.length >= 2) { const a = d.pts[d.pts.length - 1].p, b = d.pts[d.pts.length - 2].p; if (Math.hypot(a[0] - b[0], a[2] - b[2]) < 0.5) d.pts.pop(); this.rebuildRoad(d, false); }
      this.endRoad();
      return;
    }
    const hit = this.v.hitAt(e.clientX, e.clientY);
    if (!hit || hit.faceIndex === undefined) return;
    let n = hit.object; while (n && !n.userData.rec) n = n.parent;
    const rec = n && n.userData.rec;
    if (!rec || rec.type !== "modele") return;
    const meshes = modelMeshes(n), mi = meshes.indexOf(hit.object);
    if (mi < 0) return;
    const mesh = hit.object, t0 = mesh.userData.triOrig ? mesh.userData.triOrig[hit.faceIndex] : hit.faceIndex;
    const tris = faceRegion((mesh.userData.orig || { geo: mesh.geometry }).geo, t0);
    this.select({ rec, face: { m: mi, tris } });
  }
  showFace(sel) {
    if (this.faceHl) { this.faceHl.parent && this.faceHl.parent.remove(this.faceHl); this.faceHl.geometry.dispose(); this.faceHl = null; }
    if (!sel || !sel.face) return;
    const node = this.nodes.get(sel.rec.id), mesh = node && modelMeshes(node)[sel.face.m];
    if (!mesh) return;
    const g0 = (mesh.userData.orig || { geo: mesh.geometry }).geo, p = g0.attributes.position, idx = g0.index ? g0.index.array : null;
    const out = new Float32Array(sel.face.tris.length * 9);
    sel.face.tris.forEach((t, j) => { for (let c = 0; c < 3; c++) { const v = idx ? idx[t * 3 + c] : t * 3 + c; out[j * 9 + c * 3] = p.getX(v); out[j * 9 + c * 3 + 1] = p.getY(v); out[j * 9 + c * 3 + 2] = p.getZ(v); } });
    const g = new THREE.BufferGeometry(); g.setAttribute("position", new THREE.BufferAttribute(out, 3));
    this.faceHl = new THREE.Mesh(g, new THREE.MeshBasicMaterial({ color: 0xffd23f, transparent: true, opacity: 0.45, side: THREE.DoubleSide, depthWrite: false, polygonOffset: true, polygonOffsetFactor: -2, polygonOffsetUnits: -2 }));
    this.faceHl.userData.noPick = true;
    this.faceHl.renderOrder = 5;
    mesh.add(this.faceHl);
  }

  // fenetre des textures pour une cible : { rec, sub } (copie du jeu), { rec } (modele entier), { rec, face }
  openTextures(target) {
    const dlg = this.ui.textures, grid = dlg.querySelector("#tx-grille"), q = dlg.querySelector("#tx-cherche");
    const rec = target.rec;
    const cur = () => target.surface ? rec.surface : target.face ? (rec.faces || []).find(f => sameFace(f, target.face))?.habit : target.sub !== undefined ? (rec.subs || {})[target.sub] : rec.habit;
    const what = target.surface ? T("texSurface") : target.face ? T("texFace", target.face.tris.length) : target.sub !== undefined ? (target.sub === "*" ? T("texToutes") : T("texMatiere", target.label || target.sub)) : T("texObjet");
    dlg.querySelector("#tx-sous").textContent = rec.nom + " · " + what;
    let tab = cur() && cur().k === "img" ? "perso" : "jeu";
    const h0 = cur();
    dlg.querySelector("#tx-u").value = h0 && h0.rep ? h0.rep[0] : 1;
    dlg.querySelector("#tx-v").value = h0 && h0.rep ? h0.rep[1] : 1;
    const rep = () => { const u = parseFloat(String(dlg.querySelector("#tx-u").value).replace(",", ".")), v = parseFloat(String(dlg.querySelector("#tx-v").value).replace(",", ".")); return isFinite(u) && isFinite(v) && u > 0 && v > 0 && (u !== 1 || v !== 1) ? [u, v] : undefined; };
    const apply = h => {
      if (h) { h = Object.assign({}, h); const r = rep(); if (r) h.rep = r; else delete h.rep; }
      if (target.surface) rec.surface = h || this.defaultSurface(rec.type);
      else if (target.face) {
        rec.faces = (rec.faces || []).filter(f => !sameFace(f, target.face));
        if (h) rec.faces.push({ m: target.face.m, tris: target.face.tris, habit: h });
        if (!rec.faces.length) delete rec.faces;
      } else if (target.sub !== undefined) {
        rec.subs = rec.subs || {};
        if (h) { if (target.sub === "*") rec.subs = {}; rec.subs[target.sub] = h; } else delete rec.subs[target.sub];
        if (!Object.keys(rec.subs).length) delete rec.subs;
      } else { if (h) rec.habit = h; else delete rec.habit; }
      this.rebuild(rec);
      this.commit();
      draw();
    };
    const draw = () => {
      dlg.querySelectorAll("#tx-onglets button").forEach(b => b.classList.toggle("on", b.dataset.o === tab));
      const c = cur(), qs = fold(q.value.trim());
      if (tab === "jeu") {
        const list = this.gameTextures().filter(h => !qs || fold(h.nom).split(/[^a-z0-9]+/).some(w => w.startsWith(qs)) || fold(h.nom).includes(qs)).slice(0, 400);
        grid.innerHTML = list.map((h, i) => `<button class="tcell${c && c.ref === h.ref ? " on" : ""}" data-j="${i}" title="${esc(h.nom)}"><img loading="lazy" src="${esc(this.habitUrl(h))}" alt=""><span>${esc(h.nom)}</span></button>`).join("") || `<p class="empty">${esc(T("rienTrouve"))}</p>`;
        grid.querySelectorAll("[data-j]").forEach(b => b.onclick = () => apply(list[+b.dataset.j]));
      } else {
        const imgs = (this.images || []).filter(f => !qs || fold(f).includes(qs));
        grid.innerHTML = imgs.map((f, i) => `<button class="tcell${c && c.k === "img" && c.f === f ? " on" : ""}" data-p="${i}" title="${esc(f)}"><img loading="lazy" src="${esc(this.habitUrl({ k: "img", f }))}" alt=""><span>${esc(f.split("/").pop())}</span></button>`).join("")
          || `<p class="empty">${esc(T("texAucuneImage"))}</p>`;
        grid.querySelectorAll("[data-p]").forEach(b => b.onclick = () => apply({ k: "img", f: imgs[+b.dataset.p] }));
      }
    };
    this.texTarget = { apply, draw };
    dlg.querySelectorAll("#tx-onglets button").forEach(b => b.onclick = () => { tab = b.dataset.o; draw(); });
    q.value = ""; q.oninput = draw;
    dlg.querySelector("#tx-u").onchange = dlg.querySelector("#tx-v").onchange = () => { const c = cur(); if (c) apply(c); };
    dlg.querySelector("#tx-import").onclick = () => this.send({ type: "importerImage", dossier: this.dossier });
    dlg.querySelector("#tx-origine").onclick = () => apply(null);
    dlg.querySelector("[data-close]").onclick = () => { dlg.hidden = true; this.texTarget = null; };
    dlg.hidden = false;
    this.send({ type: "images", dossier: this.dossier });
    draw();
  }
  onImages(m) { this.images = m.liste || []; if (this.texTarget) this.texTarget.draw(); }
  onImage(m) {
    if (m.erreur || !m.fichier) { if (m.erreur) this.ui.warn(T("importEchec", m.erreur)); return; }
    this.images = (this.images || []).filter(f => f !== m.fichier).concat([m.fichier]);
    if (this.texTarget) this.texTarget.apply({ k: "img", f: m.fichier });
  }

  // reconstruit l'affichage d'un ajout (tenues, routes, eau)
  rebuild(rec) {
    const old = this.nodes.get(rec.id);
    if (rec.type === "modele" && old) { this.applyLooks(rec, old); this.showFace(this.sel); return; }
    const wasSel = this.sel && this.sel.rec === rec;
    if (old) { if (this.tc.object === old) this.tc.detach(); this.root.remove(old); }
    this.build(rec);
    if (wasSel) this.select(this.sel);
  }

  // ---------------------------------------------------------------- palette des objets du jeu
  // recensement (catalogue.js) : categories aux noms generiques dans la langue de l'editeur, recherche par debut de mot ;
  // champ vide : toutes les categories, a deplier ; carte preparee avant la 1.3.0 (sans chemins) : refaire la preparation
  openPalette(onChoose) {
    const dlg = this.ui.palette;
    dlg.hidden = false;
    const input = dlg.querySelector("input"), list = dlg.querySelector(".plist");
    dlg.querySelector("[data-close]").onclick = () => dlg.hidden = true;
    if (!this.v.info.objets.some(o => o.p)) {
      input.disabled = true;
      list.innerHTML = `<p class="empty warnbox">${WARN}${esc(T("cacheAncien"))}</p>`;
      return;
    }
    input.disabled = false;
    if (!this.cat) this.cat = buildCatalogue(this.v.info.objets);
    const open = this.palOpen || (this.palOpen = new Set());
    const KC = ["#8fa86a", "#c9a46a", "#d07a5a", "#4f8a5b"];
    const draw = () => {
      const q = input.value.trim(), res = search(this.cat, q, window.LANG);
      const auto = q && res.length <= 3;
      let h = "";
      for (const r of res.slice(0, 120)) {
        const shown = auto || !r.all || open.has(r.cat.id);
        h += `<button class="row cat${shown ? " open" : ""}" data-c="${r.cat.id}"><span class="chev">${shown ? "▾" : "▸"}</span><span>${esc(catName(r.cat, window.LANG))}</span><span class="n">${r.all ? r.cat.n : r.vars.reduce((a, v) => a + v.c, 0)}</span></button>`;
        if (shown) h += r.vars.slice(0, 200).map(v => `<button class="row var" data-i="${v.i}"><span class="sw" style="background:${KC[v.k] || KC[2]}"></span><span>${esc(v.n)}</span><span class="n">×${v.c}</span></button>`).join("");
      }
      list.innerHTML = h || `<p class="empty">${esc(T("rienTrouve"))}</p>`;
      list.querySelectorAll("button[data-c]").forEach(b => b.onclick = () => { const id = b.dataset.c; open.has(id) ? open.delete(id) : open.add(id); draw(); });
      list.querySelectorAll("button[data-i]").forEach(b => b.onclick = () => { dlg.hidden = true; if (onChoose) onChoose(+b.dataset.i); else this.addGame(+b.dataset.i, false); });
    };
    input.value = "";
    input.oninput = draw;
    draw();
    setTimeout(() => input.focus(), 30);
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
  const habit = m.userData && m.userData.habit;
  return { c: habit ? [1, 1, 1, 1] : [c.r, c.g, c.b, m.opacity ?? 1], habit, image: !habit && m.map && m.map.image ? m.map.image : null, cut: !!(m.alphaTest > 0 || (m.transparent && m.map)) };
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
// maillages d'un modele, dans l'ordre (rang garde dans rec.faces)
function modelMeshes(node) { const l = []; node.traverse(o => { if (o.isMesh && !o.userData.noPick) l.push(o); }); return l; }
function sameFace(a, b) { return a.m === b.m && a.tris.length === b.tris.length && a.tris[0] === b.tris[0]; }
const fold = s => String(s || "").normalize("NFD").replace(/[̀-ͯ]/g, "").toLowerCase();
// uv projetees par face (axe dominant de la normale, 1 m par repetition) ; meme ordre des triangles (rec.faces reste juste)
function boxUv(g0) {
  const g = g0.index ? g0.toNonIndexed() : g0.clone();
  const p = g.attributes.position, uv = new Float32Array(p.count * 2);
  const a = new THREE.Vector3(), b = new THREE.Vector3(), c = new THREE.Vector3();
  for (let t = 0; t < p.count; t += 3) {
    a.fromBufferAttribute(p, t); b.fromBufferAttribute(p, t + 1); c.fromBufferAttribute(p, t + 2);
    const n = b.clone().sub(a).cross(c.clone().sub(a)), ax = Math.abs(n.x), ay = Math.abs(n.y), az = Math.abs(n.z);
    for (let k = 0; k < 3; k++) {
      const v = [a, b, c][k];
      const [u, w] = ax >= ay && ax >= az ? [v.z, v.y] : ay >= az ? [v.x, v.z] : [v.x, v.y];
      uv[(t + k) * 2] = u; uv[(t + k) * 2 + 1] = w;
    }
  }
  g.setAttribute("uv", new THREE.BufferAttribute(uv, 2));
  if (!g.attributes.normal) g.computeVertexNormals();
  return g;
}
// face cliquee : triangles relies par une arete (sommets a la meme place), de normale a moins de 10 degres de la sienne
function faceRegion(g, t0) {
  const p = g.attributes.position, idx = g.index ? g.index.array : null, nt = (idx ? idx.length : p.count) / 3;
  if (nt > 400000) return [t0];
  const ids = new Map(), vid = new Int32Array(p.count);
  for (let v = 0; v < p.count; v++) { const k = Math.round(p.getX(v) * 1e4) + "," + Math.round(p.getY(v) * 1e4) + "," + Math.round(p.getZ(v) * 1e4); let i = ids.get(k); if (i === undefined) ids.set(k, i = ids.size); vid[v] = i; }
  const tv = t => [0, 1, 2].map(c => idx ? idx[t * 3 + c] : t * 3 + c);
  const a = new THREE.Vector3(), b = new THREE.Vector3(), c = new THREE.Vector3();
  const normal = t => { const [i, j, k] = tv(t); a.fromBufferAttribute(p, i); b.fromBufferAttribute(p, j); c.fromBufferAttribute(p, k); return b.sub(a).cross(c.sub(a)).normalize().clone(); };
  const edges = new Map();
  for (let t = 0; t < nt; t++) {
    const v = tv(t).map(x => vid[x]);
    for (let e = 0; e < 3; e++) { const x = v[e], y = v[(e + 1) % 3]; const k = x < y ? x + "_" + y : y + "_" + x; const l = edges.get(k); if (l) l.push(t); else edges.set(k, [t]); }
  }
  const n0 = normal(t0), seen = new Set([t0]), stack = [t0];
  while (stack.length && seen.size < 200000) {
    const t = stack.pop(), v = tv(t).map(x => vid[x]);
    for (let e = 0; e < 3; e++) {
      const x = v[e], y = v[(e + 1) % 3];
      for (const u of edges.get(x < y ? x + "_" + y : y + "_" + x) || []) if (!seen.has(u) && normal(u).dot(n0) > 0.985) { seen.add(u); stack.push(u); }
    }
  }
  return [...seen].sort((x, y) => x - y);
}
// un trait de relief sur des sommets (repere du monde d'Unity) ; vrai si un sommet a bouge -- meme calcul que MapPacks.Stroke
function reliefStroke(xs, ys, zs, st) {
  const r2 = st.r * st.r, k = Math.min(1, st.f / 2);
  let mean = 0, ws = 0, any = false;
  if (st.m === "adoucir") for (let i = 0; i < xs.length; i++) { const d2 = (xs[i] - st.x) ** 2 + (zs[i] - st.z) ** 2; if (d2 < r2) { const w = (1 - d2 / r2) ** 2; mean += w * ys[i]; ws += w; } }
  if (ws) mean /= ws;
  for (let i = 0; i < xs.length; i++) {
    const d2 = (xs[i] - st.x) ** 2 + (zs[i] - st.z) ** 2;
    if (d2 >= r2) continue;
    const w = (1 - d2 / r2) ** 2;
    if (st.m === "monter") ys[i] += st.f * w;
    else if (st.m === "creuser") ys[i] -= st.f * w;
    else if (st.m === "aplanir") ys[i] += (st.y - ys[i]) * k * w;
    else if (st.m === "adoucir" && ws) ys[i] += (mean - ys[i]) * k * w;
    any = true;
  }
  return any;
}
// deplace un ajout (repere d'Unity) : sa position, ou tous les points d'une route
function shiftRec(r, dx, dz) {
  if (r.type === "route") { r.pos = [0, 0, 0]; for (const pt of r.pts) pt.p = [pt.p[0] + dx, pt.p[1], pt.p[2] + dz]; }
  else r.pos = [r.pos[0] + dx, r.pos[1], r.pos[2] + dz];
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
  gear: sv('<circle cx="12" cy="12" r="3"/><path d="M12 3v3M12 18v3M3 12h3M18 12h3M5.6 5.6l2.1 2.1M16.3 16.3l2.1 2.1M5.6 18.4l2.1-2.1M16.3 7.7l2.1-2.1"/>'),
  mountain: sv('<path d="M3 19l6-10 4 6 2-3 6 7z"/><path d="M8.2 10.5l1.8 1.5 1.5-1"/>'),
  tree: sv('<path d="M12 3l5 7h-3l4 6H6l4-6H7z"/><path d="M12 16v5"/>'),
  bulb: sv('<path d="M9 18h6M10 21h4"/><path d="M12 3a6 6 0 0 0-3.5 10.9c.6.5 1 1.2 1 2.1h5c0-.9.4-1.6 1-2.1A6 6 0 0 0 12 3z"/>'),
  magnet: sv('<path d="M6 4v7a6 6 0 0 0 12 0V4"/><path d="M6 8h4M14 8h4"/>'),
  water: sv('<path d="M3 15c2 0 2-1.5 4.5-1.5S10 15 12 15s2-1.5 4.5-1.5S19 15 21 15"/><path d="M3 19c2 0 2-1.5 4.5-1.5S10 19 12 19s2-1.5 4.5-1.5S19 19 21 19"/><path d="M12 3c2.5 3 4 5 4 6.5a4 4 0 0 1-8 0C8 8 9.5 6 12 3z"/>'),
  undo: sv('<path d="M9 14L4 9l5-5"/><path d="M4 9h10a6 6 0 0 1 0 12h-3"/>'),
  redo: sv('<path d="M15 14l5-5-5-5"/><path d="M20 9H10a6 6 0 0 0 0 12h3"/>'),
  eye: sv('<path d="M3 12s3.5-6 9-6 9 6 9 6-3.5 6-9 6-9-6-9-6z"/><circle cx="12" cy="12" r="2.5"/>'),
  eyeoff: sv('<path d="M3 12s3.5-6 9-6c1.6 0 3 .5 4.2 1.2M21 12s-3.5 6-9 6c-1.6 0-3-.5-4.2-1.2"/><path d="M4 20L20 4"/>'),
  down: sv('<path d="M12 4v12M7 11l5 5 5-5M4 20h16"/>'),
  copy: sv('<rect x="8" y="8" width="12" height="12" rx="2"/><path d="M16 8V6a2 2 0 0 0-2-2H6a2 2 0 0 0-2 2v8a2 2 0 0 0 2 2h2"/>'),
  trash: sv('<path d="M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3"/>'),
};
