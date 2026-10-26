// Prosedürel retroperiton / sol böbrek anatomisi
// Hasta-yerel koordinat sistemi (metre):  +X = hastanın solu, +Y = kraniyal, +Z = anterior
// Orijin: L1 vertebra korpusu merkezi.
//
// Ölçüler erişkin ortalama değerlere göre (Gray's Anatomy 42. baskı; Netter Atlas; BT morfometri serileri):
//  - Böbrek: 11 × 6 × 3.5 cm, sol böbrek T12–L3, hilus L1 seviyesi; üst pol mediale ve posteriora eğik,
//    hilus anteromediale bakar (uzun eksen etrafında ~30° rotasyon).
//  - Hilus yapıları anteriordan posteriora: Ven – Arter – Pelvis (VAP).
//  - Sol renal ven (~8 cm, Ø ~10 mm) aortanın önünden SMA'nın altından geçerek VCI'ya döner;
//    sol gonadal ven (inferior), sol adrenal ven (süperior) ve lomber ven (posterior) sol renal vene drene olur.
//  - Renal arter L1–L2 seviyesinde aortanın lateralinden çıkar (Ø ~5-6 mm), venin posterior-süperiorunda seyreder,
//    hilusta anterior (apikal, üst, orta, alt segment) ve posterior dallara ayrılır.
//  - Üreter psoas major önünde iner; sol gonadal ven proksimalde üreterin medialinde, aşağıda önünden çaprazlar.
//  - İnen kolon böbreğin lateral kenarının önünde; lateral peritoneal yansıma = Toldt beyaz çizgisi.
import * as THREE from 'three';
import { V, rand, resetSeed, taperTube, samplePoints, makeTexture, tissueMaterial, textSprite } from './util.js';

const D2R = Math.PI / 180;

// Böbrek yarı eksenleri (yerel: +x lateral, +y kraniyal, +z anterior)
export const KID = { AX: 0.030, AY: 0.055, AZ: 0.0175 };
export const TUMOR = { R: 0.015, exo: 0.004 }; // 3 cm (cT1a), merkez yüzeyden 4 mm dışarıda (>%50 ekzofitik)

function kidneyShape(u) {
  let { x, y, z } = u;
  const ind = Math.pow(Math.max(0, -x), 1.6) * Math.exp(-(y * y) / (0.38 * 0.38));
  x = x + 0.62 * ind;          // hiler konkavite
  z = z * (1 - 0.28 * ind);    // sinüs dudakları
  const sx = y > 0 ? 1 + 0.06 * y : 1; // üst pol biraz daha geniş
  return V(x * KID.AX * sx, y * KID.AY, z * KID.AZ);
}

function kidneyGeometry() {
  const g = new THREE.SphereGeometry(1, 72, 56);
  const p = g.attributes.position, u = new THREE.Vector3();
  for (let i = 0; i < p.count; i++) {
    u.fromBufferAttribute(p, i).normalize();
    const q = kidneyShape(u);
    p.setXYZ(i, q.x, q.y, q.z);
  }
  g.computeVertexNormals();
  return g;
}

function mirrorX(g) {
  const p = g.attributes.position, n = g.attributes.normal;
  for (let i = 0; i < p.count; i++) { p.setX(i, -p.getX(i)); if (n) n.setX(i, -n.getX(i)); }
  const idx = g.index.array;
  for (let i = 0; i < idx.length; i += 3) { const t = idx[i + 1]; idx[i + 1] = idx[i + 2]; idx[i + 2] = t; }
  g.index.needsUpdate = true;
  return g;
}

function ellipsoid(rx, ry, rz, seg = 40, noise = 0) {
  const g = new THREE.SphereGeometry(1, seg, Math.round(seg * 0.75));
  const p = g.attributes.position;
  for (let i = 0; i < p.count; i++) {
    const k = 1 + (noise ? (Math.sin(p.getX(i) * 9 + p.getY(i) * 7) * Math.cos(p.getZ(i) * 8)) * noise : 0);
    p.setXYZ(i, p.getX(i) * rx * k, p.getY(i) * ry * k, p.getZ(i) * rz * k);
  }
  g.computeVertexNormals();
  return g;
}

// Gövde kesiti (elips) + pnömoperiton kubbesi
export const TORSO = { a: 0.17, b: 0.12, cz: 0.02, y0: -0.28, y1: 0.30, wall: 0.014 };
function torsoScale(y) {
  if (y > 0.18) return Math.sqrt(Math.max(0.05, 1 - ((y - 0.18) / (TORSO.y1 - 0.18)) ** 2));
  if (y < -0.16) return Math.sqrt(Math.max(0.05, 1 - ((y + 0.16) / (TORSO.y0 + 0.16)) ** 2));
  return 1;
}
function dome(x, y, c) { return c > 0 ? 0.03 * c * Math.exp(-(x * x) / 0.012) * Math.exp(-((y + 0.02) ** 2) / 0.025) : 0; }
export function torsoPoint(theta, y, inset = 0) {
  const s = torsoScale(y), c = Math.cos(theta);
  const x = (TORSO.a - inset) * Math.sin(theta) * s;
  const z = TORSO.cz + (TORSO.b - inset) * c * s + dome(x, y, c) * s;
  return V(x, y, z);
}
export function anteriorSurface(x, y) {
  const s = torsoScale(y), th = Math.asin(Math.max(-1, Math.min(1, x / (TORSO.a * s))));
  const p = torsoPoint(th, y);
  const n = new THREE.Vector3().subVectors(torsoPoint(th, y), torsoPoint(th, y, 0.01)).normalize();
  return { p, n };
}
function torsoGeometry(inset) {
  const seg = 96, rows = 60, pos = [], uv = [], idx = [];
  for (let j = 0; j <= rows; j++) {
    const y = TORSO.y0 + (TORSO.y1 - TORSO.y0) * j / rows;
    for (let i = 0; i <= seg; i++) {
      const p = torsoPoint((i / seg) * Math.PI * 2, y, inset);
      pos.push(p.x, p.y, p.z); uv.push(i / seg * 6, j / rows * 4);
    }
  }
  for (let j = 0; j < rows; j++) for (let i = 0; i < seg; i++) {
    const a = j * (seg + 1) + i, b = a + seg + 1;
    idx.push(a, a + 1, b, b, a + 1, b + 1);
  }
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
  g.setAttribute('uv', new THREE.Float32BufferAttribute(uv, 2));
  g.setIndex(idx); g.computeVertexNormals();
  return g;
}

export function buildAnatomy(opts = {}) {
  resetSeed(opts.seed || 20241);
  const root = new THREE.Group(); root.name = 'anatomy';
  const A = { root, meshes: {}, curves: {}, labels: [], systems: {}, land: {}, pickables: [] };
  const sys = (name, obj) => { (A.systems[name] ||= []).push(obj); return obj; };
  const add = (obj, system, parent = root) => { parent.add(obj); if (system) sys(system, obj); return obj; };
  const label = (text, pos, system) => A.labels.push({ text, pos: pos.clone(), system });

  const tex = {
    fat: makeTexture('fat'), fatBump: makeTexture('fatBump'), kidney: makeTexture('kidney'), tumor: makeTexture('tumor'),
    colon: makeTexture('colon'), perit: makeTexture('peritoneum'), muscle: makeTexture('muscle'), vein: makeTexture('vein'),
    artery: makeTexture('artery'), organ: makeTexture('organ'), wall: makeTexture('wall'),
  };
  A.tex = tex;
  const std = (p) => new THREE.MeshStandardMaterial(Object.assign({ roughness: 0.4, metalness: 0 }, p));

  // ------------------------------------------------------------------ Kemik
  const boneM = std({ color: 0xe8dcc0, roughness: 0.7 });
  const vertNames = ['T10', 'T11', 'T12', 'L1', 'L2', 'L3', 'L4', 'L5'];
  vertNames.forEach((nm, i) => {
    const y = (3 - i) * 0.036;
    const body = new THREE.Mesh(new THREE.CylinderGeometry(0.021, 0.022, 0.027, 24), boneM);
    body.position.set(0, y, 0);
    add(body, 'bone');
    const tp = new THREE.Mesh(new THREE.BoxGeometry(nm.startsWith('L') ? 0.085 : 0.06, 0.008, 0.01), boneM);
    tp.position.set(0, y, -0.03); add(tp, 'bone');
    const sp = new THREE.Mesh(new THREE.BoxGeometry(0.008, 0.016, 0.04), boneM);
    sp.position.set(0, y - 0.004, -0.05); add(sp, 'bone');
    if (nm === 'L1') label('L1 vertebra', V(-0.022, y, 0.02), 'bone');
  });
  const disc = std({ color: 0xbfd4d8, roughness: 0.6 });
  for (let i = 0; i < 7; i++) {
    const d = new THREE.Mesh(new THREE.CylinderGeometry(0.0215, 0.0215, 0.008, 24), disc);
    d.position.set(0, (3 - i) * 0.036 - 0.018, 0); add(d, 'bone');
  }
  // Kostalar 10–12 (her iki taraf, posterior arklar)
  const ribs = {
    10: [V(0.03, 0.10, -0.04), V(0.09, 0.09, -0.068), V(0.14, 0.06, -0.045), V(0.158, 0.03, 0.01), V(0.148, 0.0, 0.06)],
    11: [V(0.03, 0.068, -0.04), V(0.08, 0.052, -0.066), V(0.125, 0.022, -0.056), V(0.15, -0.01, -0.03)],
    12: [V(0.03, 0.034, -0.04), V(0.07, 0.014, -0.064), V(0.10, -0.012, -0.062), V(0.12, -0.034, -0.05)],
  };
  for (const side of [1, -1]) for (const k in ribs) {
    const pts = ribs[k].map(p => V(p.x * side, p.y, p.z));
    const m = new THREE.Mesh(taperTube(pts, 0.0048, 40, 8), boneM);
    add(m, 'bone');
    if (side === 1 && k === '12') label('12. kosta', pts[2], 'bone');
  }

  // ------------------------------------------------------------------ Kaslar
  const muscM = std({ map: tex.muscle, color: 0xffffff, roughness: 0.55 });
  for (const side of [1, -1]) {
    const pts = [V(0.026, 0.05, -0.004), V(0.034, 0.0, 0.0), V(0.042, -0.06, 0.004), V(0.05, -0.12, 0.008), V(0.058, -0.18, 0.012), V(0.065, -0.24, 0.02)]
      .map(p => V(p.x * side, p.y, p.z));
    const psoas = new THREE.Mesh(taperTube(pts, t => 0.007 + 0.018 * Math.sin(Math.min(1, t * 1.3) * Math.PI * 0.5), 48, 16), muscM);
    add(psoas, 'muscle');
    if (side === 1) { label('M. psoas major', pts[3], 'muscle'); A.curves.psoas = { pts: samplePoints(pts, 30), r: 0.02 }; }
    const ql = new THREE.Mesh(ellipsoid(0.024, 0.075, 0.01, 32), muscM);
    ql.position.set(0.085 * side, -0.045, -0.042); ql.rotation.z = -0.15 * side;
    add(ql, 'muscle');
    if (side === 1) label('M. quadratus lumborum', V(0.1, -0.09, -0.035), 'muscle');
  }

  // ------------------------------------------------------------------ Büyük damarlar
  const artM = std({ map: tex.artery, color: 0xffd0c8, roughness: 0.35 });
  const veinM = std({ map: tex.vein, color: 0xffffff, roughness: 0.28 });
  const vessel = (name, pts, r, mat, system = 'vascular', seg = 64) => {
    const g = taperTube(pts, r, seg, 14);
    const m = new THREE.Mesh(g, mat); m.name = name; add(m, system);
    A.curves[name] = { pts: samplePoints(pts, 50), r: typeof r === 'function' ? r(0.5) : r, mesh: m };
    return m;
  };
  vessel('aorta', [V(0.012, 0.2, 0.033), V(0.012, 0.1, 0.034), V(0.012, 0.0, 0.034), V(0.012, -0.12, 0.035)], 0.0095, artM);
  vessel('iliacaL', [V(0.012, -0.115, 0.035), V(0.035, -0.17, 0.032), V(0.055, -0.24, 0.026)], 0.0055, artM);
  vessel('iliacaR', [V(0.012, -0.115, 0.035), V(-0.012, -0.17, 0.032), V(-0.035, -0.24, 0.026)], 0.0055, artM);
  vessel('ivc', [V(-0.024, -0.12, 0.033), V(-0.024, 0.0, 0.034), V(-0.026, 0.1, 0.03), V(-0.03, 0.2, 0.025)], 0.0105, veinM);
  vessel('iliacVL', [V(-0.024, -0.118, 0.032), V(0.022, -0.175, 0.027), V(0.045, -0.24, 0.02)], 0.0065, veinM);
  vessel('iliacVR', [V(-0.024, -0.118, 0.032), V(-0.04, -0.175, 0.027), V(-0.055, -0.24, 0.02)], 0.0065, veinM);
  vessel('celiac', [V(0.012, 0.075, 0.044), V(0.012, 0.078, 0.062)], 0.0035, artM);
  vessel('sma', [V(0.012, 0.042, 0.044), V(0.012, 0.03, 0.068), V(0.009, 0.0, 0.08), V(0.004, -0.05, 0.088), V(-0.004, -0.11, 0.09)], 0.004, artM);
  label('Aorta abdominalis', V(0.012, -0.07, 0.046), 'vascular');
  label('V. cava inferior', V(-0.024, -0.06, 0.046), 'vascular');
  label('A. mesenterica superior', V(0.009, 0.0, 0.085), 'vascular');
  label('Truncus coeliacus', V(0.012, 0.078, 0.064), 'vascular');

  // ------------------------------------------------------------------ Böbrekler
  const kidneyMat = tissueMaterial({ map: tex.kidney, color: 0xffffff, roughness: 0.28, bumpMap: tex.fatBump, bumpScale: 0.15 }, { holes: false, sigma: 0.009 });
  const kidneyL = new THREE.Mesh(kidneyGeometry(), kidneyMat);
  kidneyL.name = 'kidneyL';
  kidneyL.position.set(0.078, -0.008, -0.008);
  kidneyL.rotation.set(-12 * D2R, 30 * D2R, 15 * D2R, 'XZY');
  add(kidneyL, 'urinary'); kidneyL.updateMatrix();
  A.meshes.kidneyL = kidneyL;
  const KL = (x, y, z) => V(x, y, z).applyMatrix4(kidneyL.matrix);

  const kidneyR = new THREE.Mesh(mirrorX(kidneyGeometry()), std({ map: tex.kidney, roughness: 0.3 }));
  kidneyR.position.set(-0.08, -0.026, -0.008);
  kidneyR.rotation.set(-12 * D2R, -30 * D2R, -15 * D2R, 'XZY');
  add(kidneyR, 'urinary'); kidneyR.updateMatrix();
  const KR = (x, y, z) => V(-x, y, z).applyMatrix4(kidneyR.matrix);
  label('Sol böbrek (Ren sinister)', KL(0.03, 0.03, 0.0), 'urinary');
  label('Sağ böbrek', KR(0.03, 0.02, 0.0), 'urinary');

  // Tümör (kidney-yerel). Alt pol, anterior yüz, >%50 ekzofitik, 3 cm. RENAL: 1+1+2+1 = 5a
  const u = V(-0.22, -0.62, 0.76).normalize(); // alt pol, anterior yüz (orta-medial)
  const S = V(u.x * KID.AX, u.y * KID.AY, u.z * KID.AZ);
  const n = V(S.x / KID.AX ** 2, S.y / KID.AY ** 2, S.z / KID.AZ ** 2).normalize();
  const C = S.clone().addScaledVector(n, TUMOR.exo);
  const e1 = V(0, 1, 0).cross(n).normalize(), e2 = n.clone().cross(e1).normalize();
  A.land.tumor = { S, n, C, R: TUMOR.R, e1, e2, footprint: Math.sqrt(TUMOR.R ** 2 - TUMOR.exo ** 2) };
  const tg = new THREE.IcosahedronGeometry(TUMOR.R, 5);
  { const p = tg.attributes.position; for (let i = 0; i < p.count; i++) { const v = V().fromBufferAttribute(p, i); const k = 1 + 0.06 * Math.sin(v.x * 400) * Math.sin(v.y * 330 + 1) * Math.sin(v.z * 360); v.multiplyScalar(k); p.setXYZ(i, v.x, v.y, v.z); } tg.computeVertexNormals(); }
  const tumorMat = tissueMaterial({ map: tex.tumor, color: 0xffffff, roughness: 0.42 }, { holes: false, sigma: 0.008 });
  const tumor = new THREE.Mesh(tg, tumorMat); tumor.name = 'tumor';
  tumor.position.copy(C); kidneyL.add(tumor); sys('urinary', tumor);
  A.meshes.tumor = tumor;
  label('Tümör (RCC, 3 cm)', KL(C.x, C.y, C.z).addScaledVector(V(0, 0, 1), 0.012), 'urinary');

  // Kollektör sistem + segmental arterler (kidney-yerel, atlas modunda şeffaf böbrekle görünür)
  const collM = std({ color: 0xf2e6a0, roughness: 0.35 });
  const inner = new THREE.Group(); kidneyL.add(inner); sys('inner', inner);
  const pelvisPts = [V(-0.0175, -0.006, -0.006), V(-0.011, -0.003, -0.004), V(-0.004, -0.001, -0.002)];
  inner.add(new THREE.Mesh(taperTube(pelvisPts, t => 0.0035 + 0.004 * t, 20, 12), collM));
  const calyces = [[V(-0.004, 0, -0.002), V(0.0, 0.015, 0), V(0.006, 0.032, 0)], [V(-0.004, 0, -0.002), V(0.006, 0.0, 0), V(0.013, 0.002, 0)], [V(-0.004, -0.002, -0.002), V(0.0, -0.016, 0), V(0.006, -0.031, 0)]];
  calyces.forEach(c => {
    inner.add(new THREE.Mesh(taperTube(c, 0.0026, 16, 10), collM));
    for (const d of [-1, 1]) {
      const tip = c[2].clone().add(V(0.004, d * 0.006, d * 0.003));
      inner.add(new THREE.Mesh(taperTube([c[2], tip], 0.0018, 6, 8), collM));
      const cup = new THREE.Mesh(new THREE.SphereGeometry(0.0028, 12, 8), collM); cup.position.copy(tip); inner.add(cup);
    }
  });
  const segA = std({ color: 0xd4483c, roughness: 0.35 });
  const aHil = V(-0.017, 0.005, 0.0);
  const segs = { apikal: V(0.0, 0.048, 0.003), 'üst': V(0.008, 0.026, 0.006), orta: V(0.012, -0.004, 0.006), alt: V(0.006, -0.042, 0.004), posterior: V(0.008, 0.004, -0.007) };
  for (const k in segs) {
    const mid = aHil.clone().lerp(segs[k], 0.35).add(V(0, 0, k === 'posterior' ? -0.004 : 0.003));
    inner.add(new THREE.Mesh(taperTube([aHil, mid, segs[k]], t => 0.0016 * (1 - 0.5 * t), 24, 8), segA));
  }
  A.land.collectingLower = KL(0.006, -0.031, 0);

  // ------------------------------------------------------------------ Hiler damarlar ve üreter
  const hV = KL(-0.0125, 0.002, 0.0065), hA = KL(-0.0128, 0.005, 0.0005), hP = KL(-0.0118, -0.005, -0.0055);
  A.land.hilum = KL(-0.012, 0, 0);
  const rvPts = [V(-0.013, -0.002, 0.036), V(0.0, -0.001, 0.046), V(0.012, 0.0, 0.0505), V(0.028, 0.0, 0.046), V(0.045, -0.001, 0.034), hV.clone().lerp(V(0.045, -0.001, 0.034), 0.45), hV];
  vessel('renalVein', rvPts, 0.0052, veinM);
  const raPts = [V(0.021, 0.008, 0.033), V(0.034, 0.009, 0.027), V(0.047, 0.008, 0.02), hA.clone().lerp(V(0.047, 0.008, 0.02), 0.4), hA];
  vessel('renalArtery', raPts, 0.0029, artM);
  label('V. renalis sinistra', V(0.04, -0.003, 0.042), 'vascular');
  label('A. renalis sinistra', V(0.04, 0.012, 0.024), 'vascular');
  // Sağ renal damarlar (arter VCI arkasından)
  const hRV = KR(-0.0125, 0.002, 0.0065), hRA = KR(-0.0128, 0.005, 0.0005);
  vessel('renalVeinR', [V(-0.03, -0.004, 0.03), hRV.clone().lerp(V(-0.03, -0.004, 0.03), 0.5).add(V(0, 0, 0.004)), hRV], 0.0055, veinM);
  vessel('renalArteryR', [V(0.003, 0.006, 0.03), V(-0.022, 0.002, 0.014), hRA.clone().lerp(V(-0.022, 0.002, 0.014), 0.4), hRA], 0.0027, artM);

  // Renal ven dalları
  const gonPts = [V(0.044, -0.004, 0.032), V(0.044, -0.025, 0.035), V(0.043, -0.06, 0.034), V(0.047, -0.11, 0.035), V(0.057, -0.16, 0.034), V(0.064, -0.215, 0.031)];
  vessel('gonadal', gonPts, 0.0021, veinM);
  label('V. gonadalis sinistra', gonPts[2], 'vascular');
  const adrPts = [V(0.042, 0.004, 0.034), V(0.044, 0.018, 0.03), V(0.048, 0.032, 0.022)];
  vessel('adrenalVein', adrPts, 0.0017, veinM);
  label('V. suprarenalis sinistra', adrPts[1], 'vascular');
  const lumPts = [V(0.03, -0.002, 0.042), V(0.032, -0.008, 0.026), V(0.034, -0.014, 0.008), V(0.034, -0.016, -0.004)];
  vessel('lumbarVein', lumPts, 0.0016, veinM);
  label('V. lumbalis', lumPts[2], 'vascular');

  // Adrenal bez (üst pol medialinde hilal)
  const adr = new THREE.Mesh(ellipsoid(0.011, 0.022, 0.0045, 32, 0.08), std({ color: 0xe0a040, roughness: 0.5 }));
  adr.position.set(0.054, 0.044, 0.006); adr.rotation.set(0.2, 0.5, 0.35);
  add(adr, 'adrenal');
  label('Gl. suprarenalis sinistra', V(0.054, 0.05, 0.016), 'adrenal');

  // Üreter (dinamik: kavranıp kaldırılabilir)
  const urM = std({ color: 0xf0e2c8, roughness: 0.35 });
  const ureterBase = [hP.clone(), KL(-0.02, -0.014, -0.004), V(0.058, -0.045, 0.012), V(0.052, -0.08, 0.024), V(0.046, -0.12, 0.027), V(0.04, -0.17, 0.03), V(0.035, -0.215, 0.03)];
  const ureter = new THREE.Mesh(taperTube(ureterBase, 0.0026, 64, 10), urM); ureter.name = 'ureter';
  add(ureter, 'urinary');
  A.meshes.ureter = ureter;
  A.ureterBase = ureterBase;
  A.curves.ureter = { pts: samplePoints(ureterBase, 50), r: 0.0026, mesh: ureter };
  A.rebuildUreter = (ctrl) => {
    ureter.geometry.dispose();
    ureter.geometry = taperTube(ctrl, 0.0026, 64, 10);
    A.curves.ureter.pts = samplePoints(ctrl, 50);
  };
  label('Üreter', V(0.052, -0.09, 0.03), 'urinary');
  label('Pelvis renalis', KL(-0.02, -0.008, -0.008), 'urinary');

  // ------------------------------------------------------------------ Yağ dokuları (diseke edilebilir)
  // Perirenal yağ + Gerota fasyası (böbrek yüzeyinin 9 mm şişirilmiş kopyası, tümör üstünde kubbe)
  const fg = kidneyGeometry();
  {
    const p = fg.attributes.position, nn = fg.attributes.normal, v = V(), nv = V();
    for (let i = 0; i < p.count; i++) {
      v.fromBufferAttribute(p, i); nv.fromBufferAttribute(nn, i);
      v.addScaledVector(nv, 0.009 + 0.0018 * (rand() - 0.5));
      const dc = v.distanceTo(C);
      if (dc < TUMOR.R + 0.006) v.sub(C).setLength(TUMOR.R + 0.006).add(C);
      p.setXYZ(i, v.x, v.y, v.z);
    }
    fg.computeVertexNormals();
  }
  const fatMat = tissueMaterial({ map: tex.fat, color: 0xffffff, roughness: 0.32, bumpMap: tex.fatBump, bumpScale: 0.6, side: THREE.DoubleSide }, { holes: true, sigma: 0.01 });
  const fatPeri = new THREE.Mesh(fg, fatMat); fatPeri.name = 'fatPeri';
  kidneyL.add(fatPeri); sys('fat', fatPeri);
  A.meshes.fatPeri = fatPeri;

  // Hiler yağ dokusu
  const hilarFat = new THREE.Mesh(ellipsoid(0.034, 0.025, 0.021, 48, 0.05),
    tissueMaterial({ map: tex.fat, color: 0xfff2d8, roughness: 0.32, bumpMap: tex.fatBump, bumpScale: 0.6, side: THREE.DoubleSide }, { holes: true, sigma: 0.01 }));
  hilarFat.name = 'fatHilar';
  hilarFat.position.set(0.045, 0.0, 0.027); hilarFat.rotation.set(0, 0.55, 0);
  add(hilarFat, 'fat'); A.meshes.fatHilar = hilarFat;

  // Retroperitoneal yağ (üreter/gonadal ven çevresi, ince)
  const rpFat = new THREE.Mesh(ellipsoid(0.022, 0.06, 0.006, 40, 0.1),
    tissueMaterial({ map: tex.fat, color: 0xfff4ea, roughness: 0.3, transparent: true, opacity: 0.3, side: THREE.DoubleSide, depthWrite: false }, { holes: true }));
  rpFat.position.set(0.05, -0.085, 0.032); add(rpFat, 'fat'); A.meshes.fatRetro = rpFat;

  // Orta hat retroperitoneal yağ (aorta/VCI/vertebra önü; sol LPN'de diseke edilmez)
  const midFat = new THREE.Mesh(ellipsoid(0.036, 0.13, 0.022, 40, 0.06), std({ map: tex.fat, color: 0xfff0dc, roughness: 0.35, bumpMap: tex.fatBump, bumpScale: 0.5 }));
  midFat.position.set(-0.006, -0.04, 0.034); add(midFat, 'fat'); A.meshes.midFat = midFat;

  // ------------------------------------------------------------------ GİS organları
  const spleen = new THREE.Mesh(ellipsoid(0.035, 0.06, 0.02, 40, 0.04), std({ map: tex.organ, color: 0x6a2238, roughness: 0.3 }));
  spleen.position.set(0.103, 0.1, -0.012); spleen.rotation.set(0.5, 0.6, -0.35);
  add(spleen, 'gi'); label('Dalak (Splen)', V(0.11, 0.11, 0.012), 'gi');
  const panc = new THREE.Mesh(taperTube([V(-0.035, -0.005, 0.075), V(0.0, 0.02, 0.085), V(0.04, 0.04, 0.066), V(0.075, 0.065, 0.036), V(0.09, 0.08, 0.018)], t => 0.014 - 0.005 * t, 48, 14), std({ map: tex.fat, color: 0xf6d6a8, roughness: 0.55 }));
  A.meshes.panc = panc; add(panc, 'gi'); label('Pankreas kuyruğu', V(0.075, 0.065, 0.045), 'gi');
  const stomach = new THREE.Mesh(ellipsoid(0.05, 0.07, 0.035, 40), std({ map: tex.organ, color: 0xd99a8e, roughness: 0.35 }));
  stomach.position.set(0.045, 0.13, 0.075); stomach.rotation.set(0.3, 0, 0.5); add(stomach, 'gi');
  label('Mide', V(0.04, 0.14, 0.11), 'gi');
  const liver = new THREE.Mesh(ellipsoid(0.1, 0.07, 0.075, 48, 0.02), std({ map: tex.organ, color: 0x7a2e22, roughness: 0.3 }));
  liver.position.set(-0.075, 0.14, 0.035); liver.rotation.set(0, 0, 0.25); add(liver, 'gi');
  label('Karaciğer', V(-0.08, 0.15, 0.1), 'gi');

  // İnce barsak ansları (sol üst pozisyonda yerçekimiyle mediale düşmüş)
  const sbM = std({ map: tex.colon, color: 0xf0b8a8, roughness: 0.3 });
  for (let k = 0; k < 3; k++) {
    const pts = []; let p = V(-0.05 + k * 0.012, -0.03 - k * 0.04, 0.08);
    const dir = V(1, 0, 0);
    for (let i = 0; i < 14; i++) {
      pts.push(p.clone());
      dir.applyAxisAngle(V(0, 0, 1), (rand() - 0.5) * 2.2).applyAxisAngle(V(1, 0, 0), (rand() - 0.5) * 0.6).normalize();
      p.addScaledVector(dir, 0.025);
      p.x = Math.min(-0.008, Math.max(-0.1, p.x)); p.y = Math.min(0.0, Math.max(-0.17, p.y)); p.z = Math.min(0.11, Math.max(0.055, p.z));
    }
    add(new THREE.Mesh(taperTube(pts, 0.011, 120, 12), sbM), 'gi');
  }
  label('İnce barsak', V(-0.05, -0.08, 0.1), 'gi');

  // İnen kolon + mezokolon (hareketli grup: medializasyon)
  const colonGroup = new THREE.Group(); colonGroup.name = 'colonGroup'; add(colonGroup, 'gi');
  const colonPts = [V(0.05, 0.065, 0.085), V(0.085, 0.085, 0.06), V(0.103, 0.088, 0.038), V(0.108, 0.05, 0.031), V(0.108, 0.0, 0.03), V(0.107, -0.06, 0.031), V(0.103, -0.12, 0.036), V(0.092, -0.17, 0.043), V(0.065, -0.2, 0.055)];
  const colonMat = tissueMaterial({ map: tex.colon, color: 0xffffff, roughness: 0.3, bumpMap: tex.fatBump, bumpScale: 0.3 }, { holes: false, sigma: 0.012 });
  const colon = new THREE.Mesh(taperTube(colonPts, t => 0.0165 * (1 + 0.12 * Math.sin(t * 95)), 200, 18), colonMat);
  colon.name = 'colon'; colonGroup.add(colon); A.meshes.colon = colon;
  A.curves.colon = { pts: samplePoints(colonPts, 60), r: 0.0165, mesh: colon };
  label('Kolon descendens', V(0.118, -0.03, 0.055), 'gi');
  label('Fleksura splenika', V(0.11, 0.088, 0.06), 'gi');
  // Mezokolon (Gerota fasyası önünde peritoneal örtü)
  const mg = new THREE.PlaneGeometry(1, 1, 30, 40); {
    const p = mg.attributes.position;
    for (let i = 0; i < p.count; i++) {
      const uu = p.getX(i) + 0.5, vv = p.getY(i) + 0.5;
      const y = -0.13 + vv * 0.2;
      const xl = 0.108 - 0.012, xm = 0.02;
      const x = xm + (xl - xm) * uu;
      const z = 0.044 + 0.008 * Math.sin(Math.PI * uu) + 0.003 * Math.sin(y * 80) * Math.sin(x * 60);
      p.setXYZ(i, x, y, z);
    }
    mg.computeVertexNormals();
  }
  const mesoMat = tissueMaterial({ map: tex.perit, color: 0xffffff, roughness: 0.25, side: THREE.DoubleSide }, { holes: true, sigma: 0.012 });
  const meso = new THREE.Mesh(mg, mesoMat); meso.name = 'mesocolon';
  colonGroup.add(meso); A.meshes.meso = meso;
  label('Mezokolon', V(0.06, -0.06, 0.05), 'gi');

  // Lateral parietal periton (kolon ile yan duvar arası) + Toldt beyaz çizgisi
  const lg = new THREE.PlaneGeometry(1, 1, 8, 40); {
    const p = lg.attributes.position;
    for (let i = 0; i < p.count; i++) {
      const uu = p.getX(i) + 0.5, vv = p.getY(i) + 0.5;
      // parakolik oluk: kolon lateral kenarı → oluk tabanı → yan karın duvarı (kuadratik Bezier)
      const y = -0.135 + vv * 0.215;
      const w0 = (1 - uu) ** 2, w1 = 2 * uu * (1 - uu), w2 = uu * uu;
      p.setXYZ(i, w0 * 0.124 + w1 * 0.15 + w2 * 0.153, y, w0 * 0.03 + w1 * 0.01 + w2 * 0.05 + 0.0015 * Math.sin(y * 90));
    }
    lg.computeVertexNormals();
  }
  const latPer = new THREE.Mesh(lg, tissueMaterial({ map: tex.perit, color: 0xffeae0, roughness: 0.25, side: THREE.DoubleSide }, { holes: true, sigma: 0.01 }));
  latPer.name = 'latPeritoneum'; add(latPer, 'gi'); A.meshes.latPer = latPer;
  const toldtPts = []; for (let i = 0; i <= 10; i++) { const y = 0.065 - i * 0.019; toldtPts.push(V(0.1411, y, 0.0244 + 0.0015 * Math.sin(y * 90))); } // oluk üzerinde u=0.4
  const toldt = new THREE.Mesh(taperTube(toldtPts, 0.0016, 60, 6), std({ color: 0xfffaf0, roughness: 0.3, emissive: 0x777066 }));
  toldt.name = 'toldt'; add(toldt, 'gi'); A.meshes.toldt = toldt;
  A.land.toldt = toldtPts;
  label('Toldt beyaz çizgisi', V(0.137, 0.0, 0.03), 'gi');

  // Perirenal yağ etiketi
  label('Gerota fasyası / perirenal yağ', KL(0.035, -0.0, 0.012), 'fat');

  // ------------------------------------------------------------------ Karın duvarı
  const skinMat = std({ color: 0xc8916a, roughness: 0.6 }); // povidon-iyot boyalı cilt
  const skin = new THREE.Mesh(torsoGeometry(0), skinMat); skin.name = 'skin';
  add(skin, 'wall'); A.meshes.skin = skin;
  const wallMat = std({ map: tex.wall, color: 0xffffff, roughness: 0.32, side: THREE.BackSide });
  const wall = new THREE.Mesh(torsoGeometry(TORSO.wall), wallMat); wall.name = 'innerWall';
  add(wall, 'wall'); A.meshes.wall = wall;

  // Etkileşim/ışın testi için yüzeyler
  A.pickables = [kidneyL, tumor, fatPeri, hilarFat, midFat, colon, meso, latPer, ureter, wall, A.curves.renalVein.mesh, A.curves.renalArtery.mesh, A.curves.gonadal.mesh, A.curves.aorta.mesh, A.curves.adrenalVein.mesh, A.curves.lumbarVein.mesh];
  return A;
}

export function buildLabels(A, parent, scale = 0.00028, layer = 0) {
  const group = new THREE.Group();
  const lineM = new THREE.LineBasicMaterial({ color: 0x6fd3ff, depthTest: false, transparent: true });
  for (const L of A.labels) {
    const s = textSprite(L.text, { size: 34, scale });
    const off = V(0, 0.012, 0.012);
    s.position.copy(L.pos).add(off);
    s.center.set(0.5, 0);
    const lg = new THREE.BufferGeometry().setFromPoints([L.pos, s.position]);
    const line = new THREE.Line(lg, lineM); line.renderOrder = 998;
    const dot = new THREE.Mesh(new THREE.SphereGeometry(0.0012, 8, 6), new THREE.MeshBasicMaterial({ color: 0x6fd3ff, depthTest: false }));
    dot.position.copy(L.pos); dot.renderOrder = 998;
    const g = new THREE.Group(); g.add(s, line, dot); g.userData.system = L.system;
    if (layer) g.traverse(o => o.layers.set(layer));
    group.add(g);
  }
  parent.add(group);
  return group;
}
