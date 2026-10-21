// Visite libre de la carte preparee (cache ecrit par le mod : MapExport.cs). Unity est en repere main gauche : z inverse
// (positions, normales, matrices) et ordre des triangles retourne pour three.js. Couches : 0 sol, 1 routes, 2 batiments, 3 vegetation.
import * as THREE from "./vendor/three.module.min.js";

const KINDS = [
  { k: "kSol", color: "#8fa86a" }, { k: "kRoutes", color: "#c9a46a" },
  { k: "kBatiments", color: "#d07a5a" }, { k: "kVegetation", color: "#4f8a5b" },
];
const SKY = { msc: 0xa9cbe8, mwc: 0xbfcad8 };

export class Viewer {
  constructor(o) {
    this.o = o; this.ui = o.ui; this.base = o.base;
    this.keys = new Set();
    this.speed = 20; this.yaw = 0; this.pitch = -0.2;
    this.disposed = false;
    const r = this.renderer = new THREE.WebGLRenderer({ canvas: o.canvas, antialias: true, powerPreference: "high-performance" });
    r.setPixelRatio(Math.min(window.devicePixelRatio || 1, 1.5));
    r.outputColorSpace = THREE.SRGBColorSpace;
    this.scene = new THREE.Scene();
    const sky = SKY[o.game] || 0xbfcad8;
    this.scene.background = new THREE.Color(sky);
    this.scene.fog = new THREE.Fog(sky, 600, 4500);
    this.camera = new THREE.PerspectiveCamera(60, 1, 0.3, 9000);
    this.scene.add(new THREE.HemisphereLight(0xdfeaff, o.game === "msc" ? 0x5a6340 : 0x9aa4b0, 1.6));
    const sun = new THREE.DirectionalLight(o.game === "msc" ? 0xfff2d8 : 0xe8efff, 2.2);
    sun.position.set(0.4, 1, 0.3);
    this.scene.add(sun);
    this.groups = KINDS.map(() => { const g = new THREE.Group(); this.scene.add(g); return g; });
    this.counts = [0, 0, 0, 0];
    this.selBox = null;
    this.bind();
    this.resize();
    this.ro = new ResizeObserver(() => this.resize());
    this.ro.observe(o.canvas.parentElement);
    this.last = performance.now(); this.frames = 0; this.fpsT = this.last;
    this.loop = this.loop.bind(this);
    requestAnimationFrame(this.loop);
    this.hints();
  }

  hints() {
    const u = this.ui;
    u.hints.innerHTML = ["hVoler", "hOrbite", "hGlisser", "hMolette"].map(k => `<span class="chip">${u.esc(T(k))}</span>`).join("")
      + `<span class="chip aide" title="${u.esc(T("hAide"))}">?</span>`;
    u.speed.textContent = T("vitesse", Math.round(this.speed));
    u.persp.onclick = () => { this.pitch = -0.2; this.setView(false); };
    u.top.onclick = () => { this.camera.position.y = Math.max(this.camera.position.y, (this.maxY || 100) + 450); this.pitch = -Math.PI / 2 + 0.001; this.setView(true); };
    u.home.onclick = () => this.goHome();
    this.setView(false);
  }
  setView(top) { this.ui.persp.classList.toggle("on", !top); this.ui.top.classList.toggle("on", top); }

  resize() {
    const p = this.o.canvas.parentElement;
    const w = p.clientWidth, h = p.clientHeight;
    if (!w || !h) return;
    this.renderer.setSize(w, h, false);
    this.camera.aspect = w / h;
    this.camera.updateProjectionMatrix();
  }

  // ---------------------------------------------------------------- chargement
  async load() {
    const u = this.ui;
    u.loading.hidden = false;
    u.loadingText.textContent = T("chargement");
    const info = await (await fetch(this.base + "carte.json", { cache: "no-store" })).json();
    this.info = info;
    const resp = await fetch(this.base + "maillages.bin", { cache: "no-store" });
    if (!resp.ok) throw new Error("maillages.bin " + resp.status);
    const buf = await resp.arrayBuffer();
    if (this.disposed) return;
    this.makeMaterials(info);
    const geos = new Array(info.maillages.length);
    const geo = i => geos[i] || (geos[i] = this.parseMesh(buf, info.maillages[i]));
    this.geoOf = geo;                     // (edition : copies des objets du jeu)
    this.instMeshes = new Array(info.objets.length);
    this.byPath = new Map();              // chemin unique (cache format 2) -> rang dans info.objets
    let n = 0, t0 = performance.now(), tris = 0;
    this.maxY = -1e9;
    for (const ob of info.objets) {
      const g = geo(ob.m);
      const mats = this.matsFor(ob, g);
      const mesh = new THREE.Mesh(g, mats.length === 1 ? mats[0] : mats);
      const t = ob.t;   // matrice 3x4 d'Unity (lignes) -> z inverse : m' = S m S, S = diag(1, 1, -1)
      mesh.matrixAutoUpdate = false;
      mesh.matrix.set(t[0], t[1], -t[2], t[3], t[4], t[5], -t[6], t[7], -t[8], -t[9], t[10], -t[11], 0, 0, 0, 1);
      mesh.userData = { name: ob.n, kind: ob.k, mesh: ob.m, idx: n, path: ob.p };
      this.instMeshes[n] = mesh;
      if (ob.p && !this.byPath.has(ob.p)) this.byPath.set(ob.p, n);
      if (/^LakeWater(Under|Color)/.test(ob.n)) mesh.visible = false;   // (effets sous l'eau de MSC : plans superposes a la surface)
      const k = Math.max(0, Math.min(3, ob.k));
      this.groups[k].add(mesh);
      mesh.updateMatrixWorld(true);
      this.counts[k]++;
      tris += g.userData.tris;
      this.maxY = Math.max(this.maxY, t[7]);
      if (++n % 200 === 0 && performance.now() - t0 > 30) { u.loadingText.textContent = T("chargement") + " " + Math.round(100 * n / info.objets.length) + " %"; await frame(); t0 = performance.now(); if (this.disposed) return; }
    }
    this.tris = tris;
    u.loadingText.textContent = T("optimisation");
    await frame();
    await this.merge();
    if (this.disposed) return;
    this.renderLayers();
    u.obj.textContent = T("objets", info.objets.length.toLocaleString()) + " · " + (tris / 1e6).toFixed(1) + " M △";
    this.goHome();
    u.loading.hidden = true;
  }

  // matieres d'un objet du cache, une par sous-maillage
  matsFor(ob, g) {
    const mats = [];
    for (let s = 0; s < g.groups.length; s++) {
      const mi = ob.x.length ? ob.x[Math.min(s, ob.x.length - 1)] : -1;
      mats.push(mi >= 0 ? this.mats[mi] : this.grey);
    }
    return mats;
  }

  // matrice d'Unity (3x4, lignes) -> three (z inverse)
  static mat(t) { return new THREE.Matrix4().set(t[0], t[1], -t[2], t[3], t[4], t[5], -t[6], t[7], -t[8], -t[9], t[10], -t[11], 0, 0, 0, 1); }

  makeMaterials(info) {
    this.grey = new THREE.MeshLambertMaterial({ color: 0x9a9a9a });
    const images = new Map();   // une image par texture, partagee par les matieres (decalages et repetitions propres a chacune)
    const loadImage = i => {
      if (!images.has(i)) images.set(i, new Promise(res => { const im = new Image(); im.onload = () => res(im); im.onerror = () => res(null); im.src = this.base + "textures/" + i + ".png"; }));
      return images.get(i);
    };
    const hidden = new THREE.MeshBasicMaterial({ visible: false });
    this.mats = info.matieres.map(m => {
      const s = m.s || "", tex = m.t !== undefined && m.t >= 0;
      // eau (FX/Water4...) : shader sans texture lisible, une teinte d'eau transparente ; ombres seules, particules : rien
      if (/water/i.test(s)) return new THREE.MeshLambertMaterial({ color: this.o.game === "msc" ? 0x3f6f96 : 0x8fb0cc, transparent: true, opacity: 0.72, depthWrite: false });
      if (/InvisibleShadowCaster|Particles|Skidmarks/i.test(s)) return hidden;
      const p = { color: new THREE.Color().setRGB(m.c[0], m.c[1], m.c[2], THREE.SRGBColorSpace) };
      if (m.d !== undefined) { p.alphaTest = Math.min(0.9, Math.max(0.05, m.d)); p.side = THREE.DoubleSide; }
      if (m.a && tex) { p.alphaTest = 0.35; p.side = THREE.DoubleSide; }   // (transparence par la texture : decoupe, sans tri)
      else if (m.a) { p.transparent = true; p.opacity = Math.min(0.85, Math.max(0.2, m.c[3])); p.depthWrite = false; }
      const mat = s.startsWith("Unlit/") ? new THREE.MeshBasicMaterial(p) : new THREE.MeshLambertMaterial(p);
      if (tex) {
        const tex = new THREE.Texture();
        tex.colorSpace = THREE.SRGBColorSpace;
        tex.wrapS = tex.wrapT = THREE.RepeatWrapping;
        tex.anisotropy = 4;
        if (m.r) { tex.repeat.set(m.r[0], m.r[1]); tex.offset.set(m.r[2], m.r[3]); }
        mat.map = tex;
        loadImage(m.t).then(im => { if (im && !this.disposed) { tex.image = im; tex.needsUpdate = true; mat.needsUpdate = true; } });
        if (m.dt !== undefined && m.dt >= 0) this.detail(mat, m, loadImage);
      }
      return mat;
    });
  }

  // « Diffuse Detail » d'Unity (sol de MSC : une image pour tout le terrain + un grain repete, cache format 3) :
  // couleur x detail x 2, en espace gamma comme le jeu ; uv du detail = uv x (dr.xy) + dr.zw
  detail(mat, m, loadImage) {
    const dtex = new THREE.Texture();
    dtex.wrapS = dtex.wrapT = THREE.RepeatWrapping; dtex.anisotropy = 8; dtex.colorSpace = THREE.NoColorSpace;
    loadImage(m.dt).then(im => { if (im && !this.disposed) { dtex.image = im; dtex.needsUpdate = true; } });
    const dr = m.dr || [1, 1, 0, 0];
    mat.onBeforeCompile = sh => {
      sh.uniforms.detailMap = { value: dtex };
      sh.uniforms.detailST = { value: new THREE.Vector4(dr[0], dr[1], dr[2], dr[3]) };
      sh.vertexShader = "uniform vec4 detailST;\nvarying vec2 vDetUv;\n" + sh.vertexShader.replace("#include <uv_vertex>", "#include <uv_vertex>\n  vDetUv = uv * detailST.xy + detailST.zw;");
      sh.fragmentShader = "uniform sampler2D detailMap;\nvarying vec2 vDetUv;\n" + sh.fragmentShader.replace("#include <map_fragment>", "#include <map_fragment>\n  diffuseColor.rgb *= pow(clamp(texture2D(detailMap, vDetUv).rgb * 2.0, 0.0, 2.0), vec3(2.2));");
    };
    mat.customProgramCacheKey = () => "detail";
  }

  // ---------------------------------------------------------------- dessin regroupe (performances)
  // Un maillage par matiere et par couche, en coordonnees du monde : quelques centaines de dessins au lieu de milliers. Les
  // objets restent la, sur le calque 1 (pas dessines) : clic, selection, masques, raycasts de l'edition. Un objet masque :
  // ses sommets du maillage regroupe ramenes en un point (positions d'origine gardees pour le reafficher).
  async merge() {
    const buckets = new Map();   // "couche|matiere" -> [{ idx, mesh, start, count }]
    this.instMeshes.forEach((mesh, idx) => {
      if (!mesh || !mesh.visible) return;
      const g = mesh.geometry, mats = Array.isArray(mesh.material) ? mesh.material : [mesh.material];
      const groups = g.groups.length ? g.groups : [{ start: 0, count: g.index.count, materialIndex: 0 }];
      for (const gr of groups) {
        const mat = mats[gr.materialIndex] || mats[0];
        if (!mat || mat.visible === false) continue;
        const key = mesh.userData.kind + "|" + mat.uuid;
        let b = buckets.get(key); if (!b) buckets.set(key, b = { kind: Math.max(0, Math.min(3, mesh.userData.kind)), mat, parts: [] });
        b.parts.push({ idx, mesh, start: gr.start, count: gr.count });
      }
    });
    this.mergedGroups = this.groups.map((g, k) => { const m = new THREE.Group(); m.visible = g.visible; this.scene.add(m); return m; });
    this.ranges = new Map();   // rang de l'objet -> [{ attr, from, n }]
    let t0 = performance.now();
    const nm = new THREE.Matrix3(), v = new THREE.Vector3();
    for (const b of buckets.values()) {
      // sommets utilises par chaque morceau (renumerotes), indices 32 bits
      const maps = b.parts.map(p => {
        const I = p.mesh.geometry.index.array, seen = new Map(), loc = [];
        for (let i = p.start; i < p.start + p.count; i++) { const k = I[i]; let j = seen.get(k); if (j === undefined) { j = loc.length; seen.set(k, j); loc.push(k); } }
        return { seen, loc };
      });
      let nv = 0, ni = 0;
      b.parts.forEach((p, i) => { nv += maps[i].loc.length; ni += p.count; });
      const pos = new Float32Array(nv * 3), nor = new Float32Array(nv * 3), uv = new Float32Array(nv * 2), ind = new Uint32Array(ni);
      let o = 0, q = 0, hasUv = false;
      b.parts.forEach((p, pi) => {
        const g = p.mesh.geometry, P = g.attributes.position, N = g.attributes.normal, UV = g.attributes.uv, I = g.index.array, M = p.mesh.matrix;
        const { seen, loc } = maps[pi];
        nm.getNormalMatrix(M);
        if (UV) hasUv = true;
        const from = o;
        for (const k of loc) {
          v.fromBufferAttribute(P, k).applyMatrix4(M); pos[o * 3] = v.x; pos[o * 3 + 1] = v.y; pos[o * 3 + 2] = v.z;
          if (N) { v.fromBufferAttribute(N, k).applyMatrix3(nm).normalize(); nor[o * 3] = v.x; nor[o * 3 + 1] = v.y; nor[o * 3 + 2] = v.z; }
          if (UV) { uv[o * 2] = UV.getX(k); uv[o * 2 + 1] = UV.getY(k); }
          o++;
        }
        for (let i = p.start; i < p.start + p.count; i++) ind[q++] = from + seen.get(I[i]);
        let r = this.ranges.get(p.idx); if (!r) this.ranges.set(p.idx, r = []);
        r.push({ b, from, n: loc.length, loc: Uint32Array.from(loc) });
      });
      const g = new THREE.BufferGeometry();
      g.setAttribute("position", new THREE.BufferAttribute(pos, 3));
      g.setAttribute("normal", new THREE.BufferAttribute(nor, 3));
      if (hasUv) g.setAttribute("uv", new THREE.BufferAttribute(uv, 2));
      g.setIndex(new THREE.BufferAttribute(ind, 1));
      g.computeBoundingSphere();
      const m = new THREE.Mesh(g, b.mat);
      m.userData.noPick = true;
      b.geo = g;
      this.mergedGroups[b.kind].add(m);
      if (performance.now() - t0 > 40) { await frame(); t0 = performance.now(); if (this.disposed) return; }
    }
    for (const mesh of this.instMeshes) if (mesh) mesh.layers.set(1);   // (plus dessines : le regroupe les remplace)
    this.merged = true;
  }
  // objet dont les sommets ont change (relief sculpte) : ses morceaux du dessin regroupe recalcules
  refreshObject(idx) {
    const rs = this.ranges && this.ranges.get(idx), mesh = this.instMeshes[idx];
    if (!rs || !mesh) return;
    const g = mesh.geometry, P = g.attributes.position, N = g.attributes.normal, M = mesh.matrix, nm = new THREE.Matrix3().getNormalMatrix(M), v = new THREE.Vector3();
    for (const r of rs) {
      const pa = r.b.geo.attributes.position, na = r.b.geo.attributes.normal;
      for (let j = 0; j < r.loc.length; j++) {
        const k = r.loc[j], o = r.from + j;
        v.fromBufferAttribute(P, k).applyMatrix4(M); pa.setXYZ(o, v.x, v.y, v.z);
        if (N) { v.fromBufferAttribute(N, k).applyMatrix3(nm).normalize(); na.setXYZ(o, v.x, v.y, v.z); }
      }
      if (r.saved) r.saved = null;
      pa.needsUpdate = true; if (na) na.needsUpdate = true;
      r.b.geo.computeBoundingSphere();
    }
  }

  // objet masque / reaffiche dans le dessin regroupe
  setHidden(idx, hidden) {
    const rs = this.ranges && this.ranges.get(idx);
    if (!rs) return;
    for (const r of rs) {
      const a = r.b.geo.attributes.position, arr = a.array;
      if (hidden) {
        if (!r.saved) r.saved = arr.slice(r.from * 3, (r.from + r.n) * 3);
        for (let i = r.from; i < r.from + r.n; i++) { arr[i * 3] = r.saved[0]; arr[i * 3 + 1] = r.saved[1]; arr[i * 3 + 2] = r.saved[2]; }
      } else if (r.saved) { arr.set(r.saved, r.from * 3); r.saved = null; }
      a.needsUpdate = true;
    }
  }

  // maillages.bin : n, drapeaux, k, k tailles, positions 3n, normales 3n?, uv 2n?, indices
  parseMesh(buf, off) {
    const dv = new DataView(buf);
    const n = dv.getUint32(off, true), fl = dv.getUint32(off + 4, true), k = dv.getUint32(off + 8, true);
    let o = off + 12;
    const sizes = [];
    for (let i = 0; i < k; i++) { sizes.push(dv.getUint32(o, true)); o += 4; }
    const pos = new Float32Array(buf, o, n * 3); o += n * 12;
    for (let i = 2; i < pos.length; i += 3) pos[i] = -pos[i];
    const g = new THREE.BufferGeometry();
    g.setAttribute("position", new THREE.BufferAttribute(pos, 3));
    if (fl & 1) { const nr = new Float32Array(buf, o, n * 3); o += n * 12; for (let i = 2; i < nr.length; i += 3) nr[i] = -nr[i]; g.setAttribute("normal", new THREE.BufferAttribute(nr, 3)); }
    if (fl & 2) { g.setAttribute("uv", new THREE.BufferAttribute(new Float32Array(buf, o, n * 2), 2)); o += n * 8; }
    let total = 0;
    for (const s of sizes) total += s;
    const idx = new Uint32Array(buf, o, total);
    for (let i = 0; i + 2 < idx.length; i += 3) { const a = idx[i + 1]; idx[i + 1] = idx[i + 2]; idx[i + 2] = a; }
    g.setIndex(new THREE.BufferAttribute(idx, 1));
    let start = 0;
    sizes.forEach((s, i) => { g.addGroup(start, s, i); start += s; });
    if (!(fl & 1)) g.computeVertexNormals();
    g.userData.tris = total / 3;
    return g;
  }

  renderLayers() {
    const u = this.ui;
    u.layers.innerHTML = "";
    KINDS.forEach((kd, i) => {
      const b = document.createElement("button");
      b.className = "layer";
      b.innerHTML = `<span class="sw" style="background:${kd.color}"></span><span>${u.esc(T(kd.k))}</span><span class="n">${this.counts[i]}</span><span class="eye"><svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><path d="M3 12s3.5-6 9-6 9 6 9 6-3.5 6-9 6-9-6-9-6z"/><circle cx="12" cy="12" r="2.5"/></svg></span>`;
      b.onclick = () => { this.groups[i].visible = !this.groups[i].visible; if (this.mergedGroups) this.mergedGroups[i].visible = this.groups[i].visible; b.classList.toggle("off", !this.groups[i].visible); };
      u.layers.appendChild(b);
    });
  }

  goHome() {
    const p = this.info && this.info.joueur;
    // (le joueur d'une nouvelle partie est dans la maison : vue d'un peu haut, en retrait)
    if (p) this.camera.position.set(p[0], p[1] + 28, -p[2] + 45);
    else if (this.info) { const b = this.info.bornes; this.camera.position.set((b[0] + b[3]) / 2, b[4] + 50, -(b[2] + b[5]) / 2); }
    this.yaw = 0; this.pitch = -0.5;
    this.setView(false);
  }

  // ---------------------------------------------------------------- commandes
  bind() {
    const c = this.o.canvas;
    c.tabIndex = 0;
    this.on = [];
    const on = (t, e, f, o) => { t.addEventListener(e, f, o); this.on.push([t, e, f, o]); };
    let down = null;
    this.looking = false;
    this.drag = null;   // souris facon logiciel 3D (demande de JD, 20/10) : { mode: orbit | pan, pivot }
    on(c, "mousedown", e => { if (e.button === 1) e.preventDefault(); });   // (pas le defilement automatique du clic molette)
    on(c, "pointerdown", e => {
      c.focus();
      if (this.onPointer && this.onPointer(e, "down")) return;   // (outil de l'edition : routes...)
      if (e.button === 2) { this.looking = true; c.setPointerCapture(e.pointerId); }
      else if (e.button === 1 || (e.button === 0 && e.altKey)) {
        e.preventDefault();
        this.drag = { mode: e.shiftKey ? "pan" : "orbit", pivot: this.pivotAt(e.clientX, e.clientY) };
        c.setPointerCapture(e.pointerId);
        c.style.cursor = this.drag.mode === "pan" ? "move" : "grabbing";
      }
      else if (e.button === 0) down = { x: e.clientX, y: e.clientY };
    });
    on(c, "pointermove", e => {
      if (this.onPointer && this.onPointer(e, "move")) return;
      if (this.drag) { this.drag.mode === "pan" ? this.pan(e.movementX, e.movementY) : this.orbit(e.movementX, e.movementY); return; }
      if (!this.looking) return;
      this.yaw -= e.movementX * 0.0035;
      this.pitch = Math.max(-Math.PI / 2 + 0.001, Math.min(Math.PI / 2 - 0.001, this.pitch - e.movementY * 0.0035));
      if (this.pitch > -1.4) this.setView(false);
    });
    on(c, "pointerup", e => {
      if (this.onPointer && this.onPointer(e, "up")) { down = null; return; }
      if (this.drag && (e.button === 1 || e.button === 0)) { this.drag = null; c.style.cursor = ""; try { c.releasePointerCapture(e.pointerId); } catch { } return; }
      if (e.button === 2) { this.looking = false; this.keys.clear(); try { c.releasePointerCapture(e.pointerId); } catch { } }
      if (e.button === 0 && down && !this.blockPick && Math.hypot(e.clientX - down.x, e.clientY - down.y) < 4) this.pick(e);
      down = null;
    });
    on(c, "dblclick", e => { if (this.onDouble) this.onDouble(e); });
    on(c, "wheel", e => {
      e.preventDefault();
      if (this.looking) {   // (en volant, clic droit tenu : la vitesse)
        this.speed = Math.max(2, Math.min(600, this.speed * (e.deltaY < 0 ? 1.25 : 0.8)));
        this.ui.speed.textContent = T("vitesse", Math.round(this.speed));
        return;
      }
      this.zoomAt(e.clientX, e.clientY, e.deltaY < 0 ? 1 : -1);
    }, { passive: false });
    on(window, "keydown", e => { if (e.target.tagName === "INPUT" || e.target.tagName === "SELECT") return; if (!this.looking) return; this.keys.add(e.code); if (e.code === "Space") e.preventDefault(); });
    on(window, "keyup", e => this.keys.delete(e.code));
    on(window, "blur", () => this.keys.clear());
  }

  pick(e) {
    const r = this.o.canvas.getBoundingClientRect();
    const v = new THREE.Vector2(((e.clientX - r.left) / r.width) * 2 - 1, -((e.clientY - r.top) / r.height) * 2 + 1);
    const ray = new THREE.Raycaster();
    ray.layers.enableAll();   // (objets sur le calque 1 : dessin regroupe)
    ray.setFromCamera(v, this.camera);
    ray.far = 3000;
    const hits = ray.intersectObjects(this.groups.filter(g => g.visible).concat(this.extra ? [this.extra] : []), true).filter(h => h.object.visible && !h.object.userData.noPick);
    if (this.onPick) { this.onPick(hits[0] || null, e); return; }
    if (this.selBox) { this.scene.remove(this.selBox); this.selBox.geometry.dispose(); this.selBox = null; }
    const u = this.ui;
    if (!hits.length) { u.sel.innerHTML = `<p class="empty">${u.esc(T("selVide"))}</p>`; return; }
    const m = hits[0].object, d = m.userData, p = hits[0].point;
    this.selBox = new THREE.BoxHelper(m, 0xffffff);
    this.scene.add(this.selBox);
    const kd = KINDS[d.kind] || KINDS[2];
    u.sel.innerHTML = `<b>${u.esc(d.name)}</b><div class="kv"><span>${u.esc(T("sorte"))}</span><span>${u.esc(T(kd.k))}</span>`
      + `<span>${u.esc(T("position"))}</span><span>${p.x.toFixed(1)}, ${p.y.toFixed(1)}, ${(-p.z).toFixed(1)}</span>`
      + `<span>${u.esc(T("maillage"))}</span><span>#${d.mesh}</span><span>${u.esc(T("triangles"))}</span><span>${m.geometry.userData.tris.toLocaleString()}</span></div>`;
  }

  loop(now) {
    if (this.disposed) return;
    requestAnimationFrame(this.loop);
    const dt = Math.min(0.1, (now - this.last) / 1000);
    this.last = now;
    const k = this.keys, cam = this.camera;
    cam.rotation.set(this.pitch, this.yaw, 0, "YXZ");
    const f = new THREE.Vector3(0, 0, -1).applyEuler(cam.rotation), rgt = new THREE.Vector3(1, 0, 0).applyEuler(cam.rotation);
    const mv = new THREE.Vector3();
    if (k.has("KeyW") || k.has("ArrowUp")) mv.add(f);
    if (k.has("KeyS") || k.has("ArrowDown")) mv.sub(f);
    if (k.has("KeyD") || k.has("ArrowRight")) mv.add(rgt);
    if (k.has("KeyA") || k.has("ArrowLeft")) mv.sub(rgt);
    if (k.has("Space") || k.has("KeyE")) mv.y += 1;
    if (k.has("KeyC") || k.has("ControlLeft")) mv.y -= 1;
    if (mv.lengthSq() > 0) cam.position.addScaledVector(mv.normalize(), this.speed * (k.has("ShiftLeft") || k.has("ShiftRight") ? 4 : 1) * dt);
    this.renderer.render(this.scene, cam);
    this.frames++;
    if (now - this.fpsT > 500) {
      this.ui.fps.textContent = T("ips", Math.round(this.frames * 1000 / (now - this.fpsT)));
      this.frames = 0; this.fpsT = now;
      const p = cam.position;
      this.ui.pos.textContent = `X ${p.x.toFixed(1)} · Y ${p.y.toFixed(1)} · Z ${(-p.z).toFixed(1)}`;
    }
  }

  // ---------------------------------------------------------------- souris : tourner autour, glisser, zoom
  // point de la carte sous le curseur (sinon 40 m devant)
  hitAt(x, y) {
    const ray = this.rayAt(x, y);
    ray.far = 4000;
    const hits = ray.intersectObjects(this.groups.filter(g => g.visible).concat(this.extra ? [this.extra] : []), true).filter(h => h.object.visible && !h.object.userData.noPick);
    return hits[0] || null;
  }
  pivotAt(x, y) {
    const h = this.hitAt(x, y);
    if (h) return h.point.clone();
    return this.camera.position.clone().addScaledVector(this.rayAt(x, y).ray.direction, 40);
  }
  // la camera tourne autour du pivot : position et regard tournes ensemble (lacet autour de la verticale, tangage autour
  // de la droite de la camera)
  orbit(dx, dy) {
    const P = this.drag.pivot, cam = this.camera;
    const dyaw = -dx * 0.005;
    const np = Math.max(-Math.PI / 2 + 0.01, Math.min(Math.PI / 2 - 0.01, this.pitch - dy * 0.005)), dp = np - this.pitch;
    const off = cam.position.clone().sub(P).applyAxisAngle(new THREE.Vector3(0, 1, 0), dyaw);
    const right = new THREE.Vector3(1, 0, 0).applyAxisAngle(new THREE.Vector3(0, 1, 0), this.yaw + dyaw);
    off.applyAxisAngle(right, dp);
    cam.position.copy(P).add(off);
    this.yaw += dyaw; this.pitch = np;
    if (this.pitch > -1.4) this.setView(false);
  }
  pan(dx, dy) {
    const cam = this.camera, h = this.o.canvas.clientHeight || 600;
    const d = Math.max(2, cam.position.distanceTo(this.drag.pivot));
    const k = 2 * d * Math.tan(THREE.MathUtils.degToRad(cam.fov / 2)) / h;
    const right = new THREE.Vector3(1, 0, 0).applyQuaternion(cam.quaternion), up = new THREE.Vector3(0, 1, 0).applyQuaternion(cam.quaternion);
    cam.position.addScaledVector(right, -dx * k).addScaledVector(up, dy * k);
  }
  // vers le point sous le curseur, d'un cinquieme de la distance a chaque cran
  zoomAt(x, y, dir) {
    const h = this.hitAt(x, y), ray = this.rayAt(x, y).ray;
    const d = h ? h.distance : 60;
    const step = Math.max(0.5, d * 0.2) * dir;
    if (dir > 0 && h && d - step < 1) return;
    this.camera.position.addScaledVector(ray.direction, step);
  }

  // rayon depuis un point de l'ecran (clientX, clientY)
  rayAt(x, y) {
    const r = this.o.canvas.getBoundingClientRect();
    const ray = new THREE.Raycaster();
    ray.layers.enableAll();
    ray.setFromCamera(new THREE.Vector2(((x - r.left) / r.width) * 2 - 1, -((y - r.top) / r.height) * 2 + 1), this.camera);
    return ray;
  }

  dispose() {
    this.disposed = true;
    if (this.onDispose) this.onDispose();
    for (const [t, e, f, o] of this.on) t.removeEventListener(e, f, o);
    this.ro.disconnect();
    this.scene.traverse(o => { if (o.geometry) o.geometry.dispose(); });
    for (const m of this.mats || []) { if (m.map) m.map.dispose(); m.dispose(); }
    this.renderer.dispose();
    this.ui.sel.innerHTML = `<p class="empty">${this.ui.esc(T("selVide"))}</p>`;
  }
}
const frame = () => new Promise(r => requestAnimationFrame(r));
