// Ortak yardımcılar: geometri, prosedürel dokular, doku materyali (deformasyon + diseksiyon delikleri)
import * as THREE from 'three';

export const V = (x = 0, y = 0, z = 0) => new THREE.Vector3(x, y, z);
export const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
export const lerp = (a, b, t) => a + (b - a) * t;

// Deterministik rastgele (her açılışta aynı anatomi)
let _seed = 1234567;
export function rand() {
  _seed = (_seed * 16807) % 2147483647;
  return (_seed - 1) / 2147483646;
}
export function resetSeed(s = 1234567) { _seed = s; }

// ---------------------------------------------------------------------------
// Değişken yarıçaplı tüp (damar, üreter, psoas, kolon)
// radius: sayı veya (t)=>yarıçap fonksiyonu
// ---------------------------------------------------------------------------
export function taperTube(points, radius, tubular = 64, radial = 12, closed = false) {
  const curve = new THREE.CatmullRomCurve3(points, closed, 'centripetal');
  const frames = curve.computeFrenetFrames(tubular, closed);
  const pos = [], nor = [], uv = [], idx = [];
  const P = new THREE.Vector3(), N = new THREE.Vector3();
  for (let i = 0; i <= tubular; i++) {
    const t = i / tubular;
    curve.getPointAt(t, P);
    const r = typeof radius === 'function' ? radius(t) : radius;
    const n = frames.normals[Math.min(i, frames.normals.length - 1)];
    const b = frames.binormals[Math.min(i, frames.binormals.length - 1)];
    for (let j = 0; j <= radial; j++) {
      const a = (j / radial) * Math.PI * 2;
      N.set(0, 0, 0).addScaledVector(n, Math.cos(a)).addScaledVector(b, Math.sin(a)).normalize();
      pos.push(P.x + N.x * r, P.y + N.y * r, P.z + N.z * r);
      nor.push(N.x, N.y, N.z);
      uv.push(t * 4, j / radial);
    }
  }
  for (let i = 0; i < tubular; i++) for (let j = 0; j < radial; j++) {
    const a = i * (radial + 1) + j, b = (i + 1) * (radial + 1) + j;
    idx.push(a, a + 1, b, b, a + 1, b + 1); // dış yüz ön yüz (CCW, normal dışa)
  }
  // uç kapakları
  if (!closed) {
    for (const end of [0, tubular]) {
      const c = pos.length / 3;
      curve.getPointAt(end / tubular, P);
      const tg = curve.getTangentAt(end / tubular).multiplyScalar(end === 0 ? -1 : 1);
      pos.push(P.x, P.y, P.z); nor.push(tg.x, tg.y, tg.z); uv.push(0, 0);
      for (let j = 0; j < radial; j++) {
        const a = end * (radial + 1) + j;
        if (end === 0) idx.push(c, a + 1, a); else idx.push(c, a, a + 1);
      }
    }
  }
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
  g.setAttribute('normal', new THREE.Float32BufferAttribute(nor, 3));
  g.setAttribute('uv', new THREE.Float32BufferAttribute(uv, 2));
  g.setIndex(idx);
  g.userData.curve = curve;
  return g;
}

// Eğri boyunca örnek noktalar (etkileşim mesafesi hesapları için)
export function samplePoints(points, n = 40) {
  const c = new THREE.CatmullRomCurve3(points, false, 'centripetal');
  return c.getSpacedPoints(n);
}

const _ab = new THREE.Vector3(), _ap = new THREE.Vector3(), _q = new THREE.Vector3();
export function distToPolyline(p, pts, out) {
  let best = Infinity, bi = 0;
  for (let i = 0; i < pts.length - 1; i++) {
    _ab.subVectors(pts[i + 1], pts[i]);
    _ap.subVectors(p, pts[i]);
    const t = clamp(_ap.dot(_ab) / Math.max(1e-9, _ab.lengthSq()), 0, 1);
    _q.copy(pts[i]).addScaledVector(_ab, t);
    const d = _q.distanceTo(p);
    if (d < best) { best = d; bi = i + t; if (out) out.copy(_q); }
  }
  return { d: best, s: bi / (pts.length - 1) };
}

// ---------------------------------------------------------------------------
// Prosedürel canvas dokuları
// ---------------------------------------------------------------------------
function canvas(w, h) { const c = document.createElement('canvas'); c.width = w; c.height = h; return c; }

export function makeTexture(kind, size = 512) {
  const c = canvas(size, size), g = c.getContext('2d');
  const blob = (x, y, r, col) => {
    const gr = g.createRadialGradient(x, y, 0, x, y, r);
    gr.addColorStop(0, col); gr.addColorStop(1, 'rgba(0,0,0,0)');
    g.fillStyle = gr; g.beginPath(); g.arc(x, y, r, 0, 7); g.fill();
  };
  const wrapBlob = (x, y, r, col) => {
    for (const dx of [-size, 0, size]) for (const dy of [-size, 0, size]) if (x + dx > -r && x + dx < size + r && y + dy > -r && y + dy < size + r) blob(x + dx, y + dy, r, col);
  };
  if (kind === 'fat') {
    g.fillStyle = '#e3b552'; g.fillRect(0, 0, size, size);
    for (let i = 0; i < 260; i++) wrapBlob(rand() * size, rand() * size, 12 + rand() * 26, `rgba(${250},${215 + rand() * 30 | 0},${120 + rand() * 50 | 0},0.55)`);
    g.strokeStyle = 'rgba(170,70,40,0.35)'; g.lineWidth = 1.2;
    for (let i = 0; i < 18; i++) { g.beginPath(); let x = rand() * size, y = rand() * size; g.moveTo(x, y); for (let k = 0; k < 8; k++) { x += (rand() - .5) * 60; y += (rand() - .5) * 60; g.lineTo(x, y); } g.stroke(); }
  } else if (kind === 'fatBump') {
    g.fillStyle = '#555'; g.fillRect(0, 0, size, size);
    for (let i = 0; i < 300; i++) wrapBlob(rand() * size, rand() * size, 10 + rand() * 24, 'rgba(255,255,255,0.45)');
  } else if (kind === 'kidney') {
    g.fillStyle = '#7a2b22'; g.fillRect(0, 0, size, size);
    for (let i = 0; i < 400; i++) wrapBlob(rand() * size, rand() * size, 6 + rand() * 20, `rgba(${140 + rand() * 40 | 0},${50 + rand() * 25 | 0},${40},0.35)`);
    g.strokeStyle = 'rgba(200,90,90,0.25)'; g.lineWidth = 0.8;
    for (let i = 0; i < 40; i++) { g.beginPath(); let x = rand() * size, y = rand() * size; g.moveTo(x, y); for (let k = 0; k < 6; k++) { x += (rand() - .5) * 40; y += (rand() - .5) * 40; g.lineTo(x, y); } g.stroke(); }
  } else if (kind === 'tumor') {
    g.fillStyle = '#d9a23a'; g.fillRect(0, 0, size, size);
    for (let i = 0; i < 160; i++) wrapBlob(rand() * size, rand() * size, 10 + rand() * 40, `rgba(${230 + rand() * 25 | 0},${170 + rand() * 60 | 0},${60},0.5)`);
    for (let i = 0; i < 25; i++) wrapBlob(rand() * size, rand() * size, 8 + rand() * 22, 'rgba(140,30,25,0.55)'); // hemorajik odaklar
  } else if (kind === 'colon') {
    g.fillStyle = '#c98a78'; g.fillRect(0, 0, size, size);
    for (let i = 0; i < 200; i++) wrapBlob(rand() * size, rand() * size, 10 + rand() * 30, 'rgba(225,165,140,0.4)');
    g.strokeStyle = 'rgba(150,40,60,0.45)'; g.lineWidth = 1.5;
    for (let i = 0; i < 30; i++) { g.beginPath(); let x = rand() * size, y = rand() * size; g.moveTo(x, y); for (let k = 0; k < 5; k++) { x += (rand() - .5) * 30; y += 25; g.lineTo(x, y); } g.stroke(); }
    // epiploik apendiksler için sarı lekeler
    for (let i = 0; i < 20; i++) wrapBlob(rand() * size, rand() * size, 14, 'rgba(240,200,90,0.6)');
  } else if (kind === 'peritoneum') {
    g.fillStyle = '#d9a59a'; g.fillRect(0, 0, size, size);
    for (let i = 0; i < 150; i++) wrapBlob(rand() * size, rand() * size, 15 + rand() * 40, 'rgba(240,200,120,0.35)');
    g.strokeStyle = 'rgba(160,40,50,0.4)'; g.lineWidth = 1;
    for (let i = 0; i < 25; i++) { g.beginPath(); let x = rand() * size, y = rand() * size; g.moveTo(x, y); for (let k = 0; k < 10; k++) { x += (rand() - .5) * 50; y += (rand() - .5) * 50; g.lineTo(x, y); } g.stroke(); }
  } else if (kind === 'muscle') {
    g.fillStyle = '#9c3a33'; g.fillRect(0, 0, size, size);
    for (let i = 0; i < size; i += 3) { g.fillStyle = `rgba(${120 + rand() * 60 | 0},40,40,0.4)`; g.fillRect(0, i, size, 1 + rand() * 2); }
  } else if (kind === 'vein') {
    g.fillStyle = '#4c3a78'; g.fillRect(0, 0, size, size);
    for (let i = 0; i < 120; i++) wrapBlob(rand() * size, rand() * size, 10 + rand() * 30, 'rgba(110,80,150,0.4)');
  } else if (kind === 'artery') {
    g.fillStyle = '#c9867a'; g.fillRect(0, 0, size, size);
    for (let i = 0; i < 120; i++) wrapBlob(rand() * size, rand() * size, 10 + rand() * 30, 'rgba(230,160,150,0.4)');
    g.strokeStyle = 'rgba(170,40,40,0.5)'; g.lineWidth = 1;
    for (let i = 0; i < 12; i++) { g.beginPath(); let x = rand() * size, y = rand() * size; g.moveTo(x, y); for (let k = 0; k < 6; k++) { x += 30; y += (rand() - .5) * 20; g.lineTo(x, y); } g.stroke(); }
  } else if (kind === 'organ') {
    g.fillStyle = '#888'; g.fillRect(0, 0, size, size);
    for (let i = 0; i < 300; i++) wrapBlob(rand() * size, rand() * size, 5 + rand() * 20, `rgba(255,255,255,${0.1 + rand() * 0.15})`);
  } else if (kind === 'wall') {
    g.fillStyle = '#c47b72'; g.fillRect(0, 0, size, size);
    for (let i = 0; i < 300; i++) wrapBlob(rand() * size, rand() * size, 10 + rand() * 30, 'rgba(220,150,140,0.35)');
    g.strokeStyle = 'rgba(140,30,40,0.45)'; g.lineWidth = 1.2;
    for (let i = 0; i < 40; i++) { g.beginPath(); let x = rand() * size, y = rand() * size; g.moveTo(x, y); for (let k = 0; k < 10; k++) { x += (rand() - .5) * 40; y += (rand() - .5) * 40; g.lineTo(x, y); } g.stroke(); }
  }
  const t = new THREE.CanvasTexture(c);
  t.wrapS = t.wrapT = THREE.RepeatWrapping;
  if (!kind.endsWith('Bump')) t.colorSpace = THREE.SRGBColorSpace;
  t.anisotropy = 4;
  return t;
}

// ---------------------------------------------------------------------------
// Doku materyali: alet ucu ile yumuşak doku çökmesi (vertex shader),
// koter/diseksiyon delikleri ve eksizyon krateri (fragment discard)
// ---------------------------------------------------------------------------
export const MAX_HOLES = 128;

export function tissueMaterial(params = {}, opts = {}) {
  const mat = new THREE.MeshStandardMaterial(Object.assign({ roughness: 0.38, metalness: 0.0 }, params));
  const u = {
    uTips: { value: [new THREE.Vector4(), new THREE.Vector4()] },
    uHoles: { value: Array.from({ length: opts.holes ? MAX_HOLES : 1 }, () => new THREE.Vector4()) },
    uHoleCount: { value: 0 },
    uCrater: { value: new THREE.Vector4(0, 0, 0, 0) },
    uTint: { value: new THREE.Color(1, 1, 1) },
    uSigma: { value: opts.sigma || 0.009 },
    uPlaneCut: { value: new THREE.Vector4(0, 0, 0, 0) }, // ellipsoid dışını kes (krater kasesi)
    uEllip: { value: new THREE.Vector3(1, 1, 1) },
  };
  mat.userData.u = u;
  mat.userData.holes = [];
  const deform = opts.deform !== false;
  mat.onBeforeCompile = (sh) => {
    Object.assign(sh.uniforms, u);
    sh.vertexShader = sh.vertexShader
      .replace('#include <common>', `#include <common>
        uniform vec4 uTips[2]; uniform float uSigma; varying vec3 vObjPos;`)
      .replace('#include <begin_vertex>', `#include <begin_vertex>
        vObjPos = position;
        ${deform ? `for (int i = 0; i < 2; i++) {
          vec4 t = uTips[i];
          if (t.w > 0.5) {
            vec3 d = t.xyz - transformed;
            float along = dot(d, objectNormal);
            float lat = length(d - objectNormal * along);
            if (along < 0.0 && along > -0.022) {
              transformed += objectNormal * along * exp(-(lat*lat)/(uSigma*uSigma));
            } else if (along >= 0.0 && along < 0.004) {
              transformed += objectNormal * (along - 0.004) * 0.25 * exp(-(lat*lat)/(uSigma*uSigma));
            }
          }
        }` : ''}`);
    sh.fragmentShader = sh.fragmentShader
      .replace('#include <common>', `#include <common>
        uniform vec4 uHoles[${opts.holes ? MAX_HOLES : 1}]; uniform int uHoleCount; uniform vec4 uCrater; uniform vec3 uTint;
        uniform vec4 uPlaneCut; uniform vec3 uEllip; varying vec3 vObjPos;`)
      .replace('#include <color_fragment>', `#include <color_fragment>
        float edge = 1.0;
        ${opts.holes ? `for (int i = 0; i < ${MAX_HOLES}; i++) {
          if (i >= uHoleCount) break;
          float dd = distance(vObjPos, uHoles[i].xyz) - uHoles[i].w;
          if (dd < 0.0) discard;
          edge = min(edge, dd);
        }` : ''}
        if (uCrater.w > 0.0) {
          float dc = distance(vObjPos, uCrater.xyz) - uCrater.w;
          if (dc < 0.0) discard;
          edge = min(edge, dc * 0.5);
        }
        if (uPlaneCut.w > 0.0) {
          vec3 q = vObjPos / uEllip;
          if (dot(q, q) > 1.0) discard;
        }
        diffuseColor.rgb *= uTint;
        diffuseColor.rgb *= mix(0.25, 1.0, smoothstep(0.0, 0.0018, edge));`);
  };
  mat.customProgramCacheKey = () => `tissue-${opts.holes ? 1 : 0}-${deform ? 1 : 0}`;
  return mat;
}

// Delik ekle (nesne-uzayı). Çok yakın delik varsa eklemez.
export function addHole(mat, p, r) {
  const holes = mat.userData.holes;
  for (const h of holes) if (_v3.set(h.x, h.y, h.z).distanceTo(p) < r * 0.45 && h.w >= r * 0.9) return false;
  if (holes.length >= MAX_HOLES) holes.shift();
  holes.push(new THREE.Vector4(p.x, p.y, p.z, r));
  const arr = mat.userData.u.uHoles.value;
  holes.forEach((h, i) => arr[i].set(h.x, h.y, h.z, h.w));
  mat.userData.u.uHoleCount.value = holes.length;
  return true;
}
export function inHole(mat, p, extra = 0) {
  for (const h of mat.userData.holes) if (p.distanceTo(_v3.set(h.x, h.y, h.z)) < h.w + extra) return true;
  return false;
}
const _v3 = new THREE.Vector3();

// Metin sprite'ı (etiketler)
export function textSprite(text, opts = {}) {
  const fs = opts.size || 40;
  const c = canvas(10, 10), g = c.getContext('2d');
  g.font = `600 ${fs}px Segoe UI, Arial`;
  const lines = text.split('\n');
  const w = Math.max(...lines.map(l => g.measureText(l).width)) + 24, h = lines.length * fs * 1.2 + 14;
  c.width = w; c.height = h;
  g.font = `600 ${fs}px Segoe UI, Arial`;
  g.fillStyle = opts.bg || 'rgba(10,20,30,0.78)';
  g.beginPath(); g.roundRect(0, 0, w, h, 12); g.fill();
  g.strokeStyle = opts.border || '#6fd3ff'; g.lineWidth = 3; g.stroke();
  g.fillStyle = opts.color || '#fff'; g.textBaseline = 'top';
  lines.forEach((l, i) => g.fillText(l, 12, 7 + i * fs * 1.2));
  const t = new THREE.CanvasTexture(c); t.colorSpace = THREE.SRGBColorSpace;
  const m = new THREE.SpriteMaterial({ map: t, depthTest: false, transparent: true });
  const s = new THREE.Sprite(m);
  const k = opts.scale || 0.00028;
  s.scale.set(w * k, h * k, 1);
  s.renderOrder = 999;
  return s;
}
