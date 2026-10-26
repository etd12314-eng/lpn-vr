// Laparoskopik aletler ve trokar fulkrum kinematiği
// Kol (kontrolcü) trokarın dışında; uç = T + dir * (L - |H - T|), dir = (T - H)/|T - H|
// => klasik "fulcrum etkisi": el sağa giderse alet ucu sola gider.
import * as THREE from 'three';
import { V, clamp } from './util.js';

export const SHAFT = 0.36; // kol pivotundan uca (m) — 33 cm şaft + kol ofseti

export const TOOLS = {
  grasper: { tr: 'Fenestre grasper', energy: false },
  hook: { tr: 'Hook koter (monopolar)', energy: 'mono' },
  scissors: { tr: 'Soğuk makas', energy: false },
  bipolar: { tr: 'Maryland bipolar', energy: 'bi' },
  bulldog: { tr: 'Bulldog klemp aplikatörü', energy: false },
  needle: { tr: 'Portegü (V-Loc 3/0)', energy: false },
  clip: { tr: 'Hem-o-lok klip aplikatörü', energy: false },
  suction: { tr: 'Aspiratör-irrigatör', energy: false },
  us: { tr: 'Laparoskopik US probu', energy: false },
};
export const TOOLSETS = {
  left: ['grasper', 'suction', 'bipolar', 'us'],
  right: ['hook', 'scissors', 'bipolar', 'bulldog', 'needle', 'clip', 'suction', 'us'],
};

const black = new THREE.MeshStandardMaterial({ color: 0x151515, roughness: 0.45, metalness: 0.2 });
const steel = new THREE.MeshStandardMaterial({ color: 0xc8ccd2, roughness: 0.25, metalness: 0.9 });
const gold = new THREE.MeshStandardMaterial({ color: 0xd8b25a, roughness: 0.3, metalness: 0.9 });
const blueIns = new THREE.MeshStandardMaterial({ color: 0x2e5fb3, roughness: 0.35, metalness: 0.1 });
const handleM = new THREE.MeshStandardMaterial({ color: 0x3c3f46, roughness: 0.6, metalness: 0.1 });
const purple = new THREE.MeshStandardMaterial({ color: 0x7b3fc4, roughness: 0.4 });
const bulldogM = new THREE.MeshStandardMaterial({ color: 0xd0d4d8, roughness: 0.3, metalness: 0.8 });

function jawPair(len, w, mat, curve = 0) {
  const g = new THREE.Group();
  const a = new THREE.Group(), b = new THREE.Group();
  const geo = new THREE.BoxGeometry(w, w * 0.45, len); geo.translate(0, w * 0.22, len / 2);
  if (curve) { const p = geo.attributes.position; for (let i = 0; i < p.count; i++) p.setX(i, p.getX(i) + curve * (p.getZ(i) / len) ** 2); geo.computeVertexNormals(); }
  a.add(new THREE.Mesh(geo, mat));
  const gb = geo.clone(); gb.scale(1, -1, 1); { const idx = gb.index.array; for (let i = 0; i < idx.length; i += 3) { const t = idx[i + 1]; idx[i + 1] = idx[i + 2]; idx[i + 2] = t; } }
  b.add(new THREE.Mesh(gb, mat));
  g.add(a, b);
  g.userData.set = (open) => { a.rotation.x = -open; b.rotation.x = open; };
  return g;
}

export function makeBulldog() {
  const g = new THREE.Group();
  const j1 = new THREE.Mesh(new THREE.BoxGeometry(0.0022, 0.0012, 0.014), bulldogM); j1.position.set(0, 0.0012, 0.0);
  const j2 = j1.clone(); j2.position.y = -0.0012;
  const spring = new THREE.Mesh(new THREE.TorusGeometry(0.0025, 0.0008, 8, 16), bulldogM); spring.position.z = -0.009; spring.rotation.y = Math.PI / 2;
  g.add(j1, j2, spring);
  return g;
}
export function makeHemolok() {
  const g = new THREE.Group();
  const m = new THREE.Mesh(new THREE.TorusGeometry(0.0035, 0.0009, 6, 14, Math.PI * 1.2), purple);
  m.rotation.x = Math.PI / 2; g.add(m);
  return g;
}

export class Instrument {
  constructor(scene, hand) {
    this.hand = hand;
    this.root = new THREE.Group(); this.root.name = 'instr-' + hand;
    scene.add(this.root);
    this.tipWorld = new THREE.Vector3();
    this.dir = new THREE.Vector3(0, 0, -1);
    this.trigger = 0;
    this.insertion = 0.1;
    this.tools = {};
    // şaft (+Z yönü uca doğru)
    const shaft = new THREE.Mesh(new THREE.CylinderGeometry(0.0026, 0.0026, SHAFT - 0.05, 16), black);
    shaft.rotation.x = Math.PI / 2; shaft.position.z = 0.05 + (SHAFT - 0.05) / 2;
    this.root.add(shaft); this.shaft = shaft;
    // kol (tabanca kabza)
    const h = new THREE.Group();
    const body = new THREE.Mesh(new THREE.BoxGeometry(0.02, 0.03, 0.06), handleM); body.position.set(0, 0, 0.025); h.add(body);
    const grip = new THREE.Mesh(new THREE.BoxGeometry(0.018, 0.08, 0.026), handleM); grip.position.set(0, -0.05, 0.0); grip.rotation.x = -0.25; h.add(grip);
    const ring = new THREE.Mesh(new THREE.TorusGeometry(0.014, 0.004, 8, 20), handleM); ring.position.set(0, -0.05, 0.035); ring.rotation.y = Math.PI / 2; h.add(ring);
    this.lever = ring;
    const knob = new THREE.Mesh(new THREE.CylinderGeometry(0.008, 0.008, 0.006, 16), new THREE.MeshStandardMaterial({ color: 0x2277cc })); knob.rotation.x = Math.PI / 2; knob.position.z = 0.058; h.add(knob);
    this.root.add(h); this.handle = h;
    this.tipGroup = new THREE.Group(); this.tipGroup.position.z = SHAFT; this.root.add(this.tipGroup);
    this.buildTools();
    this.set(hand === 'left' ? 'grasper' : 'hook');
  }
  buildTools() {
    const T = this.tools;
    const mk = (name, obj, tipOff) => { obj.visible = false; obj.userData.tipOff = tipOff; this.tipGroup.add(obj); T[name] = obj; };
    { const g = new THREE.Group(); const j = jawPair(0.019, 0.0042, steel); g.add(j); g.userData.jaw = j; mk('grasper', g, 0.012); }
    { const g = new THREE.Group(); const j = jawPair(0.017, 0.0036, blueIns, 0.003); g.add(j); g.userData.jaw = j; mk('bipolar', g, 0.012); }
    { const g = new THREE.Group(); const j = jawPair(0.016, 0.0028, gold, 0.004); g.add(j); g.userData.jaw = j; mk('scissors', g, 0.012); }
    { const g = new THREE.Group();
      const ins = new THREE.Mesh(new THREE.CylinderGeometry(0.0026, 0.0018, 0.012, 12), blueIns); ins.rotation.x = Math.PI / 2; ins.position.z = 0.006; g.add(ins);
      const w = new THREE.Mesh(new THREE.CylinderGeometry(0.0006, 0.0006, 0.01, 8), steel); w.rotation.x = Math.PI / 2; w.position.z = 0.017; g.add(w);
      const hk = new THREE.Mesh(new THREE.CylinderGeometry(0.0006, 0.0006, 0.004, 8), steel); hk.position.set(0, 0.0018, 0.0215); g.add(hk);
      mk('hook', g, 0.021); }
    { const g = new THREE.Group(); const j = jawPair(0.014, 0.0035, steel); g.add(j); g.userData.jaw = j;
      const bd = makeBulldog(); bd.position.z = 0.016; g.add(bd); g.userData.load = bd; mk('bulldog', g, 0.017); }
    { const g = new THREE.Group(); const j = jawPair(0.012, 0.0034, steel); g.add(j); g.userData.jaw = j;
      const nd = new THREE.Mesh(new THREE.TorusGeometry(0.0055, 0.0005, 6, 20, Math.PI), steel); nd.position.set(0.0, 0, 0.012); nd.rotation.y = Math.PI / 2; g.add(nd);
      g.userData.load = nd; mk('needle', g, 0.014); }
    { const g = new THREE.Group(); const j = jawPair(0.013, 0.0038, steel); g.add(j); g.userData.jaw = j;
      const c = makeHemolok(); c.position.z = 0.01; g.add(c); g.userData.load = c; mk('clip', g, 0.012); }
    { const g = new THREE.Group(); const t = new THREE.Mesh(new THREE.CylinderGeometry(0.0026, 0.0026, 0.012, 14, 1, true), steel); t.rotation.x = Math.PI / 2; t.position.z = 0.006; g.add(t); mk('suction', g, 0.012); }
    { const g = new THREE.Group(); const t = new THREE.Mesh(new THREE.BoxGeometry(0.008, 0.005, 0.03), new THREE.MeshStandardMaterial({ color: 0x333a44, roughness: 0.35 })); t.position.z = 0.017; g.add(t);
      const face = new THREE.Mesh(new THREE.BoxGeometry(0.0075, 0.0008, 0.024), new THREE.MeshStandardMaterial({ color: 0x9aa4b0, roughness: 0.2 })); face.position.set(0, -0.0028, 0.018); g.add(face);
      mk('us', g, 0.018); }
  }
  set(tool) {
    for (const k in this.tools) this.tools[k].visible = k === tool;
    this.tool = tool;
    this.toolObj = this.tools[tool];
  }
  hasLoad(v) { const l = this.toolObj.userData.load; if (l) l.visible = v; }
  // H: kol (dünya), T: trokar (dünya), up: kontrolcü yukarı vektörü
  update(H, T, up, trigger) {
    this.trigger = trigger;
    const dir = this.dir.subVectors(T, H);
    let out = dir.length(); dir.normalize();
    out = clamp(out, 0.04, SHAFT - 0.012);
    this.insertion = SHAFT - out;
    const Hc = T.clone().addScaledVector(dir, -out);
    const z = dir, x = V().crossVectors(up, z); if (x.lengthSq() < 1e-6) x.set(1, 0, 0); x.normalize();
    const y = V().crossVectors(z, x);
    this.root.quaternion.setFromRotationMatrix(new THREE.Matrix4().makeBasis(x, y, z));
    this.root.position.copy(Hc);
    const jaw = this.toolObj.userData.jaw;
    if (jaw) jaw.userData.set((1 - trigger) * 0.42);
    this.lever.rotation.x = trigger * 0.3;
    this.tipWorld.copy(Hc).addScaledVector(dir, SHAFT + this.toolObj.userData.tipOff);
    this.handleWorld = Hc;
  }
}
