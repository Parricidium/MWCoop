// Apercu 3D d'une destination convertie : lit circuit.vmesh et textures\N.vtex (formats du mod Voyage, voir Modele.cs)
// depuis https://dest.local/<id>/. Les fichiers sont faits pour Unity (repere gauche) : le groupe est retourne en X
// (three.js est en repere droit ; il garde les faces dans le bon sens). Rendu a la demande ; textures DXT telles quelles.
import * as THREE from "three";

const DXT = { 1: [THREE.RGBA_S3TC_DXT1_Format, 8], 5: [THREE.RGBA_S3TC_DXT5_Format, 16] };

async function fetchProgress(url, cb) {
  const res = await fetch(url);
  if (!res.ok) throw new Error(res.status + " · " + decodeURIComponent(url.split("/").pop().split("?")[0]));
  const total = +res.headers.get("content-length") || 0;
  if (!total || !res.body) return await res.arrayBuffer();
  const parts = []; let got = 0;
  const rd = res.body.getReader();
  for (;;) {
    const { done, value } = await rd.read();
    if (done) break;
    parts.push(value); got += value.length; cb(Math.min(1, got / total));
  }
  const out = new Uint8Array(got); let off = 0;
  for (const p of parts) { out.set(p, off); off += p.length; }
  return out.buffer;
}

// « VOYM », version, nombre ; par sous-maillage : nom, texture, drapeaux, metal, lisse, pivot, sommets, normales (sauf
// drapeau 8), uv, indices
function parse(buf) {
  const dv = new DataView(buf), u8 = new Uint8Array(buf), td = new TextDecoder();
  let p = 0;
  const i32 = () => { const v = dv.getInt32(p, true); p += 4; return v; };
  const str = () => { const n = i32(); const s = td.decode(u8.subarray(p, p + n)); p += n; return s; };
  if (td.decode(u8.subarray(0, 4)) !== "VOYM") throw new Error("circuit.vmesh illisible");
  p = 4;
  const version = i32(), n = i32(), subs = [];
  for (let k = 0; k < n; k++) {
    const name = str(), tex = str();
    let flags = u8[p++];
    if (version < 2) flags = (flags ? 1 : 0) | 2;
    p += 20;   // (metal, lisse, pivot : toujours 0 pour un circuit)
    const nv = i32();
    const pos = new Float32Array(buf.slice(p, p + nv * 12)); p += nv * 12;
    let nor = null;
    if (!(flags & 8)) { nor = new Float32Array(buf.slice(p, p + nv * 12)); p += nv * 12; }
    const uv = new Float32Array(buf.slice(p, p + nv * 8)); p += nv * 8;
    const ni = i32();
    const idx = new Uint32Array(buf.slice(p, p + ni * 4)); p += ni * 4;
    subs.push({ name, tex, flags, pos, nor, uv, idx });
  }
  return subs;
}

export class Viewer {
  constructor(canvas, { onLoad, onTex }) {
    this.canvas = canvas; this.onLoad = onLoad || (() => { }); this.onTex = onTex || (() => { });
    this.r = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: true, logarithmicDepthBuffer: true });
    this.r.setPixelRatio(Math.min(2, window.devicePixelRatio || 1));
    this.s3tc = this.r.extensions.has("WEBGL_compressed_texture_s3tc");
    this.scene = new THREE.Scene();
    this.scene.add(new THREE.HemisphereLight(0xe4f1ff, 0x5a5044, 1.5));
    const sun = new THREE.DirectionalLight(0xfff2dc, 1.9); sun.position.set(0.45, 1, 0.3); this.scene.add(sun);
    this.cam = new THREE.PerspectiveCamera(55, 1, 0.5, 120000);
    this.root = new THREE.Group(); this.root.scale.x = -1; this.scene.add(this.root);
    this.orbit = { target: new THREE.Vector3(), yaw: 0.7, pitch: 0.6, dist: 900 };
    this.gen = 0; this.dirty = true; this.key = null;
    this.box = new THREE.Box3(); this.trackBox = new THREE.Box3();
    this.input();
    new ResizeObserver(() => this.resize()).observe(canvas.parentElement);
    const loop = () => { requestAnimationFrame(loop); if (this.dirty && this.canvas.clientWidth > 0) { this.dirty = false; this.render(); } };
    loop();
  }

  resize() {
    const w = this.canvas.parentElement.clientWidth, h = this.canvas.parentElement.clientHeight;
    if (!w || !h) return;
    this.r.setSize(w, h, false);
    this.cam.aspect = w / h; this.cam.updateProjectionMatrix();
    this.dirty = true;
  }

  // ------------------------------------------------------------ chargement
  clear() {
    this.gen++; this.key = null;
    for (const o of [...this.root.children]) this.root.remove(o);
    for (const g of this.geos || []) g.dispose();
    for (const m of Object.values(this.mats || {})) { if (m.map) m.map.dispose(); m.dispose(); }
    if (this.colMat) this.colMat.dispose();
    this.geos = []; this.mats = {}; this.col = null; this.marker = null;
    this.box.makeEmpty(); this.trackBox.makeEmpty();
    this.dirty = true;
  }

  async load(base, ver, d) {
    this.clear();
    const gen = this.gen;
    this.key = d.id + "|" + ver;
    const v = "?v=" + encodeURIComponent(ver);
    this.onLoad("maillages", 0);
    let subs;
    try {
      const buf = await fetchProgress(base + "circuit.vmesh" + v, f => gen === this.gen && this.onLoad("maillages", f));
      if (gen !== this.gen) return;
      subs = parse(buf);
    } catch (e) { if (gen === this.gen) this.onLoad("erreur", 0, e.message); return; }
    this.col = new THREE.Group(); this.col.visible = false; this.root.add(this.col);
    this.colMat = new THREE.MeshBasicMaterial({ color: 0xff6e6e, transparent: true, opacity: 0.38, depthWrite: false, side: THREE.DoubleSide, polygonOffset: true, polygonOffsetFactor: -2 });
    const texNames = [];
    for (const s of subs) {
      const g = new THREE.BufferGeometry();
      g.setAttribute("position", new THREE.BufferAttribute(s.pos, 3));
      g.setIndex(new THREE.BufferAttribute(s.idx, 1));
      g.computeBoundingBox(); g.computeBoundingSphere();
      this.geos.push(g);
      if (s.flags & 16) {   // (collision seule, invisible dans le jeu)
        this.col.add(new THREE.Mesh(g, this.colMat));
        this.trackBox.union(g.boundingBox);
        continue;
      }
      if (s.nor) g.setAttribute("normal", new THREE.BufferAttribute(s.nor, 3)); else g.computeVertexNormals();
      g.setAttribute("uv", new THREE.BufferAttribute(s.uv, 2));
      const mk = s.tex + "|" + (s.flags & 35);
      let m = this.mats[mk];
      if (!m) {
        const cut = (s.flags & 32) !== 0, transp = (s.flags & 1) !== 0 && !cut;
        m = this.mats[mk] = new THREE.MeshLambertMaterial({ color: 0x8c8c8c, side: s.flags & 2 ? THREE.DoubleSide : THREE.FrontSide, alphaTest: cut ? 0.4 : 0, transparent: transp, opacity: transp ? 0.6 : 1 });
        m.userData.tex = s.tex;
        if (s.tex && !texNames.includes(s.tex)) texNames.push(s.tex);
      }
      this.root.add(new THREE.Mesh(g, m));
      this.box.union(g.boundingBox);
    }
    if (this.trackBox.isEmpty()) this.trackBox.copy(this.box);
    this.setDest(d);
    this.frameAll(true);
    this.onLoad("fini", 1);
    // textures : quatre a la fois, affichees au fur et a mesure
    let done = 0, next = 0;
    this.onTex(0, texNames.length);
    const worker = async () => {
      while (next < texNames.length && gen === this.gen) {
        const name = texNames[next++];
        let tex = null;
        try { tex = await this.loadTex(base + name + v); } catch { }
        if (gen !== this.gen) { if (tex) tex.dispose(); return; }
        if (tex) for (const m of Object.values(this.mats)) if (m.userData.tex === name) { m.map = tex; m.color.set(0xffffff); m.needsUpdate = true; }
        this.onTex(++done, texNames.length);
        this.dirty = true;
      }
    };
    await Promise.all([worker(), worker(), worker(), worker()]);
  }

  // « VOYT », format (1 DXT1, 5 DXT5, 32 BGRA32), largeur, hauteur, niveaux, octets, donnees
  async loadTex(url) {
    const buf = await (await fetch(url)).arrayBuffer();
    const dv = new DataView(buf);
    if (buf.byteLength < 24 || dv.getUint32(0, true) !== 0x54594f56) return null;
    const fmt = dv.getInt32(4, true), w = dv.getInt32(8, true), h = dv.getInt32(12, true), mips = dv.getInt32(16, true), n = dv.getInt32(20, true);
    const data = new Uint8Array(buf, 24, Math.min(n, buf.byteLength - 24));
    let tex;
    if (DXT[fmt] && this.s3tc) {
      const [format, bs] = DXT[fmt];
      const levels = []; let off = 0, lw = w, lh = h;
      for (let i = 0; i < Math.max(1, mips); i++) {
        const sz = Math.max(1, (lw + 3) >> 2) * Math.max(1, (lh + 3) >> 2) * bs;
        if (off + sz > data.length) break;
        levels.push({ data: data.subarray(off, off + sz), width: lw, height: lh });
        off += sz;
        if (lw === 1 && lh === 1) break;
        lw = Math.max(1, lw >> 1); lh = Math.max(1, lh >> 1);
      }
      if (!levels.length) return null;
      const full = levels.length > 1 && levels[levels.length - 1].width === 1 && levels[levels.length - 1].height === 1;
      tex = new THREE.CompressedTexture(full ? levels : [levels[0]], w, h, format);
      tex.minFilter = full ? THREE.LinearMipmapLinearFilter : THREE.LinearFilter;
    } else if (fmt === 32 && data.length >= w * h * 4) {
      const px = new Uint8Array(w * h * 4);
      for (let i = 0; i < px.length; i += 4) { px[i] = data[i + 2]; px[i + 1] = data[i + 1]; px[i + 2] = data[i]; px[i + 3] = data[i + 3]; }
      tex = new THREE.DataTexture(px, w, h, THREE.RGBAFormat);
      tex.generateMipmaps = true; tex.minFilter = THREE.LinearMipmapLinearFilter;
    } else return null;
    tex.colorSpace = THREE.SRGBColorSpace;
    tex.wrapS = tex.wrapT = THREE.RepeatWrapping;
    tex.magFilter = THREE.LinearFilter;
    tex.anisotropy = Math.min(8, this.r.capabilities.getMaxAnisotropy());
    tex.flipY = false;
    tex.needsUpdate = true;
    return tex;
  }

  // ------------------------------------------------------------ point d'arrivee
  setDest(d) {
    this.dest = d;
    const a = String(d.arrivee || "").split(",").map(Number);
    this.showArrival(a.length === 3 && a.every(isFinite) ? [a[0], a[1], a[2], +d.cap || 0] : null);
  }

  showArrival(p) {
    if (this.marker) { this.root.remove(this.marker); this.marker.traverse(o => { if (o.geometry) o.geometry.dispose(); if (o.material) o.material.dispose(); }); this.marker = null; }
    this.arrival = p;
    this.dirty = true;
    if (!p) return;
    const m = this.marker = new THREE.Group();
    const acc = 0xa6e1ff;
    const mat = () => new THREE.MeshBasicMaterial({ color: acc, depthTest: false, transparent: true });
    const pole = new THREE.Mesh(new THREE.CylinderGeometry(0.12, 0.12, 6, 8).translate(0, 3, 0), mat());
    const head = new THREE.Mesh(new THREE.SphereGeometry(0.7, 16, 12).translate(0, 6.4, 0), mat());
    const ring = new THREE.Mesh(new THREE.RingGeometry(1.6, 2.1, 40).rotateX(-Math.PI / 2).translate(0, 0.15, 0), mat());
    // fleche a plat au sol, dans le sens du cap
    const ag = new THREE.BufferGeometry();
    ag.setAttribute("position", new THREE.BufferAttribute(new Float32Array([-0.35, 0, 2.3, 0.35, 0, 2.3, 0.35, 0, 3.4, -0.35, 0, 3.4, -1.1, 0, 3.4, 1.1, 0, 3.4, 0, 0, 5.2]).map((v, i) => i % 3 === 1 ? 0.2 : v), 3));
    ag.setIndex([0, 1, 2, 0, 2, 3, 4, 5, 6]);
    const arrow = new THREE.Mesh(ag, mat()); arrow.material.side = THREE.DoubleSide;
    for (const o of [pole, head, ring, arrow]) { o.renderOrder = 10; m.add(o); }
    m.position.set(p[0], p[1], p[2]);
    m.rotation.y = (p[3] || 0) * Math.PI / 180;   // (cap du jeu : +Z tourne vers (sin, 0, cos))
    this.root.add(m);
  }

  arrivalWorld() {
    if (!this.arrival) return null;
    return new THREE.Vector3(this.arrival[0], this.arrival[1], this.arrival[2]).applyMatrix4(this.root.matrixWorld);
  }

  // ------------------------------------------------------------ vues
  frameAll(keepAngle) {
    this.root.updateMatrixWorld(true);
    const b = (this.trackBox.isEmpty() ? this.box : this.trackBox).clone().applyMatrix4(this.root.matrixWorld);
    if (b.isEmpty()) return;
    const c = b.getCenter(new THREE.Vector3()), s = b.getSize(new THREE.Vector3());
    this.orbit.target.copy(c);
    this.orbit.dist = Math.max(60, Math.max(s.x, s.z) * 0.95);
    if (!keepAngle) { this.orbit.pitch = 0.75; }
    this.dirty = true;
  }

  frameArrival() {
    this.root.updateMatrixWorld(true);
    const p = this.arrivalWorld();
    if (!p) return this.frameAll();
    const h = (this.arrival[3] || 0) * Math.PI / 180;
    const fw = new THREE.Vector3(Math.sin(h), 0, Math.cos(h)); fw.x *= -1;   // (retourne comme le groupe)
    this.orbit.target.copy(p).add(new THREE.Vector3(0, 1.5, 0)).addScaledVector(fw, 8);
    this.orbit.yaw = Math.atan2(-fw.x, -fw.z);
    this.orbit.pitch = 0.32; this.orbit.dist = 42;
    this.dirty = true;
  }

  toggleCollision() {
    if (!this.col) return false;
    this.col.visible = !this.col.visible;
    this.dirty = true;
    return this.col.visible;
  }

  // ------------------------------------------------------------ souris : tourner (gauche), deplacer (droit / milieu), zoom (molette)
  input() {
    const c = this.canvas;
    let drag = null;
    c.addEventListener("pointerdown", e => { drag = { b: e.button, x: e.clientX, y: e.clientY }; c.setPointerCapture(e.pointerId); c.focus(); });
    c.addEventListener("pointerup", e => { drag = null; try { c.releasePointerCapture(e.pointerId); } catch { } });
    c.addEventListener("pointermove", e => {
      if (!drag) return;
      const dx = e.clientX - drag.x, dy = e.clientY - drag.y;
      drag.x = e.clientX; drag.y = e.clientY;
      const o = this.orbit;
      if (drag.b === 0 && !e.shiftKey) {
        o.yaw -= dx * 0.005;
        o.pitch = Math.max(0.03, Math.min(1.55, o.pitch + dy * 0.005));
      } else {
        const k = o.dist * 0.0016;
        const right = new THREE.Vector3(Math.cos(o.yaw), 0, -Math.sin(o.yaw));
        const fwd = new THREE.Vector3(-Math.sin(o.yaw), 0, -Math.cos(o.yaw));
        o.target.addScaledVector(right, -dx * k).addScaledVector(fwd, dy * k);
      }
      this.dirty = true;
    });
    c.addEventListener("wheel", e => {
      e.preventDefault();
      this.orbit.dist = Math.max(4, Math.min(40000, this.orbit.dist * Math.exp(e.deltaY * 0.0012)));
      this.dirty = true;
    }, { passive: false });
    c.addEventListener("dblclick", e => {   // (double-clic : tourner autour de ce point)
      const r = c.getBoundingClientRect();
      const ray = new THREE.Raycaster();
      ray.setFromCamera(new THREE.Vector2((e.clientX - r.left) / r.width * 2 - 1, -((e.clientY - r.top) / r.height) * 2 + 1), this.cam);
      const hits = ray.intersectObjects(this.root.children.filter(o => o.isMesh), false);
      if (!hits.length) return;
      const o = this.orbit, cam = this.cam.position.clone();
      o.target.copy(hits[0].point);
      o.dist = Math.max(4, cam.distanceTo(o.target));
      this.dirty = true;
    });
  }

  render() {
    const o = this.orbit, cp = Math.cos(o.pitch);
    this.cam.position.set(o.target.x + o.dist * cp * Math.sin(o.yaw), o.target.y + o.dist * Math.sin(o.pitch), o.target.z + o.dist * cp * Math.cos(o.yaw));
    this.cam.lookAt(o.target);
    if (this.marker) this.marker.scale.setScalar(Math.max(1, o.dist / 90));
    this.r.render(this.scene, this.cam);
  }
}
