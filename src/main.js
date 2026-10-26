// LPN-VR — Laparoskopik Parsiyel Nefrektomi simülatörü (Meta Quest 3S / WebXR + masaüstü)
import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { buildAnatomy, buildLabels } from './anatomy.js';
import { buildRoom } from './room.js';
import { Instrument, TOOLSETS, TOOLS, SHAFT } from './instruments.js';
import { Procedure } from './sim.js';
import { UI } from './ui.js';
import { Audio } from './audio.js';
import { V, inHole, clamp } from './util.js';

const LAYER_LAP = 5, LAYER_OR = 6;
const app = window.app = {
  mode: 'menu', activeHand: 'right', labelsOn: false, xrActive: false, xrMode: null, vrMenuOpen: false, orView: false,
  atlasVis: { labels: true, fat: true, inner: false, bone: true, gi: true, muscle: true },
};

// ------------------------------------------------------------------ renderer / sahne
const renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true });
renderer.setPixelRatio(Math.min(2, window.devicePixelRatio));
renderer.setSize(innerWidth, innerHeight);
renderer.toneMapping = THREE.ACESFilmicToneMapping;
renderer.toneMappingExposure = 1.0;
renderer.xr.enabled = true;
renderer.xr.setReferenceSpaceType('local-floor');
renderer.xr.setFoveation(1);
document.getElementById('app').appendChild(renderer.domElement);

const scene = new THREE.Scene();
const BG = new THREE.Color(0x1d2a31);
scene.background = BG;
const camera = new THREE.PerspectiveCamera(60, innerWidth / innerHeight, 0.01, 50);
camera.position.set(0.0, 1.55, 0.35);
camera.layers.enable(LAYER_OR);
scene.add(camera);

// Laparoskop kamerası + ışık kaynağı (yalnız karın içi: katman 5)
const lapCam = new THREE.PerspectiveCamera(70, 16 / 9, 0.003, 1.2);
lapCam.layers.enable(LAYER_LAP);
scene.add(lapCam);
const scopeLight = new THREE.SpotLight(0xfff1e0, 2.6, 0.8, 1.0, 0.75, 1);
scopeLight.layers.set(LAYER_LAP);
lapCam.add(scopeLight); scopeLight.target.position.set(0, 0, -1); lapCam.add(scopeLight.target);
const lapAmb = new THREE.HemisphereLight(0xffe8e0, 0x502020, 0.9); lapAmb.layers.set(LAYER_LAP); scene.add(lapAmb);
const rt = new THREE.WebGLRenderTarget(1280, 720, { samples: 4, type: THREE.HalfFloatType });

// ------------------------------------------------------------------ içerik
const A = buildAnatomy();
const R = buildRoom(scene, A);
const audio = new Audio();
const ui = new UI(app);
app.ui = ui; app.A = A; app.R = R;
const instr = app.instr = { left: new Instrument(scene, 'left'), right: new Instrument(scene, 'right') };
const surgLabels = buildLabels(A, A.root, 0.00016, LAYER_LAP); surgLabels.visible = false;

// Monitör ekranı (laparoskopik görüntü; hafif fıçı distorsiyonu + vinyet)
const screenMat = new THREE.ShaderMaterial({
  uniforms: { tMap: { value: rt.texture } },
  vertexShader: 'varying vec2 vUv; void main(){ vUv = uv; gl_Position = projectionMatrix * modelViewMatrix * vec4(position,1.); }',
  fragmentShader: `uniform sampler2D tMap; varying vec2 vUv;
    void main(){ vec2 c = (vUv - 0.5) * vec2(1.7778, 1.0); float r2 = dot(c, c);
      vec2 uv = 0.5 + (vUv - 0.5) * (1.0 - 0.05 * r2);
      vec3 col = texture2D(tMap, uv).rgb * 1.9;
      col *= smoothstep(1.15, 0.55, length(c));
      gl_FragColor = vec4(col, 1.0);
      #include <tonemapping_fragment>
      #include <colorspace_fragment>
    }`,
});
const screen = new THREE.Mesh(new THREE.PlaneGeometry(0.96, 0.54), screenMat);
screen.position.z = 0.026; R.monitor.add(screen);
// US resim-içinde-resim
const usCanvas = document.getElementById('usCanvas');
const usCtx = usCanvas.getContext('2d');
const usTex = new THREE.CanvasTexture(usCanvas); usTex.colorSpace = THREE.SRGBColorSpace;
const usPlane = new THREE.Mesh(new THREE.PlaneGeometry(0.26, 0.2), new THREE.MeshBasicMaterial({ map: usTex, toneMapped: false }));
usPlane.position.set(0.33, -0.15, 0.03); usPlane.visible = false; R.monitor.add(usPlane);

// VR panelleri
ui.info.mesh.visible = true; R.panelAnchor.add(ui.info.mesh);
scene.add(ui.menu.mesh); ui.menu.mesh.position.set(0, 1.45, -0.9);
scene.add(ui.atlas.mesh); ui.atlas.mesh.visible = false;

// ------------------------------------------------------------------ atlas (ayrı, büyütülmüş kopya)
const A2 = buildAnatomy();
const atlasRoot = new THREE.Group(); atlasRoot.visible = false; scene.add(atlasRoot);
const atlasPivot = new THREE.Group(); atlasRoot.add(atlasPivot);
atlasPivot.add(A2.root); A2.root.position.set(-0.04, 0.0, -0.01);
atlasRoot.scale.setScalar(2.6);
A2.meshes.skin.visible = false; A2.meshes.wall.visible = false;
A2.meshes.fatRetro.visible = false; A2.meshes.latPer.visible = false;
A2.meshes.meso.material.transparent = true; A2.meshes.meso.material.opacity = 0.35;
const atlasLabels = buildLabels(A2, A2.root, 0.00014);
function applyAtlasVis() {
  const v = app.atlasVis;
  for (const k of ['fat', 'bone', 'gi', 'muscle']) (A2.systems[k] || []).forEach(o => o.visible = v[k]);
  A2.meshes.midFat.visible = false; A2.meshes.fatRetro.visible = false; A2.meshes.latPer.visible = false; // atlasta büyük damarları örtmesin
  const km = A2.meshes.kidneyL.material;
  km.transparent = v.inner; km.opacity = v.inner ? 0.28 : 1; km.depthWrite = !v.inner; km.needsUpdate = true;
  A2.meshes.tumor.material.transparent = v.inner; A2.meshes.tumor.material.opacity = v.inner ? 0.5 : 1; A2.meshes.tumor.material.needsUpdate = true;
  atlasLabels.visible = v.labels;
  atlasLabels.children.forEach(g => { g.visible = v[g.userData.system] !== false; });
  syncAtlasButtons();
}

// ------------------------------------------------------------------ prosedür
let proc = null;
app.lapCam = lapCam;
const camState = { target: V(0.1, -0.02, 0.02), offset: V(), dist: 0.11 };
function newProcedure() {
  proc = app.proc = new Procedure({ A, R, ui, audio, lapCam, isVisible });
  camState.target.copy(proc.step.focus); camState.offset.set(0, 0, 0);
  const origStep = proc.enterStep.bind(proc);
  proc.enterStep = (i) => { origStep(i); camState.offset.set(0, 0, 0); camState.dist = proc.step?.camDist || 0.11; updateSurgLabels(); };
  camState.dist = proc.step.camDist || 0.11;
  updateSurgLabels();
}
const _pv = new THREE.Vector3();
function isVisible(pW) { _pv.copy(pW).project(lapCam); return Math.abs(_pv.x) < 1 && Math.abs(_pv.y) < 1 && _pv.z < 1 && _pv.z > -1; }
function updateSurgLabels() {
  surgLabels.visible = app.labelsOn;
  if (!proc?.step) return;
  const f = proc.step.focus;
  surgLabels.children.forEach((g, i) => { g.visible = A.labels[i].pos.distanceTo(f) < 0.075 && !['bone', 'muscle'].includes(A.labels[i].system) || A.labels[i].system === 'muscle' && A.labels[i].pos.distanceTo(f) < 0.05; });
}

function updateLapCam(dt) {
  if (!proc?.step) return;
  const goal = proc.step.focus.clone().add(camState.offset);
  camState.target.lerp(goal, 1 - Math.exp(-dt * 2.5));
  const portW = R.ports.camera.obj.getWorldPosition(V());
  const tW = A.root.localToWorld(camState.target.clone());
  const dir = tW.clone().sub(portW); const dist = dir.length(); dir.normalize();
  const ins = clamp(dist - camState.dist, 0.02, 0.22);
  lapCam.position.copy(portW).addScaledVector(dir, ins);
  lapCam.up.set(0, 1, 0);
  lapCam.lookAt(tW);
}
function panCamera(dx, dy) { // ekran düzleminde (metre)
  const right = V(1, 0, 0).applyQuaternion(lapCam.quaternion), up = V(0, 1, 0).applyQuaternion(lapCam.quaternion);
  const inv = new THREE.Matrix4().copy(A.root.matrixWorld).invert();
  camState.offset.add(right.multiplyScalar(dx).add(up.multiplyScalar(dy)).transformDirection(inv).multiplyScalar(Math.hypot(dx, dy)));
  if (camState.offset.length() > 0.08) camState.offset.setLength(0.08);
}

// ------------------------------------------------------------------ mod geçişleri
function setMode(m) {
  app.mode = m;
  const xr = app.xrActive;
  R.root.visible = m !== 'atlas';
  atlasRoot.visible = m === 'atlas';
  ui.menu.mesh.visible = xr && m === 'menu';
  ui.atlas.mesh.visible = xr && m === 'atlas';
  ui.info.mesh.visible = m === 'surgery';
  for (const h of ['left', 'right']) instr[h].root.visible = m === 'surgery';
  document.body.dataset.mode = m;
  if (m === 'atlas') { applyAtlasVis(); placeAtlas(); }
  if (m === 'surgery') { if (!proc) newProcedure(); if (xr) setTimeout(calibrate, 400); else initDeskTips(); }
  if (m === 'menu' && xr) placeInFront(ui.menu.mesh, 0.9, -0.05);
  orbit.enabled = !xr && (m === 'atlas' || m === 'menu' || app.orView);
  if (m === 'atlas' && !xr) { orbit.target.set(0, 1.3, -0.6); camera.position.set(0, 1.4, 0.45); }
  if (m === 'menu' && !xr) { orbit.target.set(0, 1.0, -0.5); camera.position.set(0.6, 1.7, 0.6); }
  renderer.domElement.style.cursor = m === 'surgery' && !app.orView ? 'crosshair' : 'default';
}
function headPose() { const c = app.xrActive ? renderer.xr.getCamera() : camera; return { p: c.getWorldPosition(V()), q: c.getWorldQuaternion(new THREE.Quaternion()) }; }
function placeInFront(obj, dist, dy = 0) {
  const { p, q } = headPose(); const f = V(0, 0, -1).applyQuaternion(q); f.y = 0; f.normalize();
  obj.position.copy(p).addScaledVector(f, dist); obj.position.y = p.y + dy;
  obj.lookAt(p.x, obj.position.y, p.z);
}
function placeAtlas() {
  if (app.xrActive) {
    placeInFront(atlasRoot, 0.65, -0.2);
    atlasRoot.rotateY(Math.PI); // anterior yüz kullanıcıya
    const { p, q } = headPose(); const f = V(0, 0, -1).applyQuaternion(q); f.y = 0; f.normalize();
    const r = V(-f.z, 0, f.x);
    ui.atlas.mesh.position.copy(p).addScaledVector(f, 0.7).addScaledVector(r, -0.5); ui.atlas.mesh.position.y = p.y - 0.1;
    ui.atlas.mesh.lookAt(p.x, ui.atlas.mesh.position.y, p.z);
  } else { atlasRoot.position.set(0, 1.3, -0.6); atlasRoot.rotation.set(0, 0, 0); }
  atlasPivot.rotation.set(0, 0, 0);
}

// Hasta/rig'i kullanıcının ellerine göre konumlandır (VR)
function calibrate() {
  const c = controllerPoses();
  if (!c.left || !c.right) { ui.toast('Konumlandırma için iki kontrolcü gerekli', 'warn'); return; }
  if (c.left.p.distanceTo(c.right.p) < 0.15) { ui.toast('Ellerinizi omuz genişliğinde açıp tekrar deneyin (iki çubuğa basın)', 'warn'); return; }
  const rig = R.root; rig.position.set(0, 0, 0); rig.rotation.set(0, 0, 0); rig.updateMatrixWorld(true);
  const focusW = A.root.localToWorld(V(0.075, -0.01, 0.015));
  const neutral = (k) => { const T = R.ports[k].obj.getWorldPosition(V()); const d = focusW.clone().sub(T); const inside = Math.min(d.length(), SHAFT - 0.13); return T.addScaledVector(d.normalize(), -(SHAFT - inside)); };
  const NL = neutral('left'), NR = neutral('right');
  const CL = c.left.p, CR = c.right.p;
  const ang = (v) => Math.atan2(v.x, v.z);
  const psi = ang(V().subVectors(CR, CL)) - ang(V().subVectors(NR, NL));
  rig.rotation.y = psi;
  const midN = NL.clone().add(NR).multiplyScalar(0.5).applyAxisAngle(V(0, 1, 0), psi);
  const midC = CL.clone().add(CR).multiplyScalar(0.5);
  rig.position.copy(midC.sub(midN));
  rig.updateMatrixWorld(true);
  ui.toast('Hasta ellerinize göre konumlandırıldı', 'good');
}

// ------------------------------------------------------------------ XR kontrolcüler
const ctrls = [0, 1].map(i => {
  const ray = renderer.xr.getController(i), grip = renderer.xr.getControllerGrip(i);
  scene.add(ray, grip);
  const line = new THREE.Line(new THREE.BufferGeometry().setFromPoints([V(), V(0, 0, -1)]), new THREE.LineBasicMaterial({ color: 0x6fd3ff }));
  line.scale.z = 2; ray.add(line);
  // basit kontrolcü gövdesi (menü/atlas modunda görünür)
  const body = new THREE.Mesh(new THREE.BoxGeometry(0.03, 0.025, 0.09), new THREE.MeshStandardMaterial({ color: 0x2a2f36, roughness: 0.5 }));
  body.position.z = 0.02; grip.add(body);
  const C = { ray, grip, line, body, src: null, prev: {} };
  ray.addEventListener('connected', (e) => { C.src = e.data; });
  ray.addEventListener('disconnected', () => { C.src = null; });
  return C;
});
function controllerPoses() {
  const out = {};
  for (const C of ctrls) {
    if (!C.src || !C.src.gamepad) continue;
    out[C.src.handedness] = { C, p: C.grip.getWorldPosition(V()), q: C.grip.getWorldQuaternion(new THREE.Quaternion()), gp: C.src.gamepad };
  }
  return out;
}
const btn = (gp, i) => !!gp.buttons[i]?.pressed;
function edge(C, key, v) { const was = C.prev[key]; C.prev[key] = v; return v && !was; }

const rayc = new THREE.Raycaster();
function panelRay(C) {
  const panels = [ui.menu.mesh, ui.atlas.mesh, ui.info.mesh].filter(m => m.visible && (m !== ui.info.mesh || app.vrMenuOpen || ui.lastReport));
  const m = new THREE.Matrix4().extractRotation(C.ray.matrixWorld);
  rayc.ray.origin.setFromMatrixPosition(C.ray.matrixWorld); rayc.ray.direction.set(0, 0, -1).applyMatrix4(m);
  const hits = rayc.intersectObjects(panels, false);
  return hits[0] || null;
}

const grab = { active: null };
function xrUpdate(dt) {
  const poses = controllerPoses();
  const pointerMode = app.mode !== 'surgery' || app.vrMenuOpen || ui.lastReport;
  for (const C of ctrls) {
    C.line.visible = !!(pointerMode && C.src);
    C.body.visible = app.mode !== 'surgery';
    C.ray.updateMatrixWorld(true);
    if (!C.src?.gamepad) continue;
    const gp = C.src.gamepad;
    if (pointerMode) {
      const h = panelRay(C);
      const P = h?.object.userData.panel;
      [ui.menu, ui.atlas, ui.info].forEach(p => { const nh = p === P ? p.hit(h.uv) : null; if (p.hover !== nh && (p === P || p.hover)) { p.hover = nh; p._dirty = true; } });
      C.line.scale.z = h ? h.distance : 2;
      if (edge(C, 'trig', btn(gp, 0)) && P) { const id = P.hit(h.uv); if (id) onPanelClick(P, id); }
    } else C.prev.trig = btn(gp, 0);
  }
  if (app.mode === 'atlas') {
    // grip ile tut-çevir
    for (const hand of ['left', 'right']) {
      const c = poses[hand]; if (!c) continue;
      const g = btn(c.gp, 1);
      if (g && !grab.active) { grab.active = hand; grab.q0 = c.q.clone(); grab.p0 = c.p.clone(); grab.mq = atlasRoot.quaternion.clone(); grab.mp = atlasRoot.position.clone(); }
      if (grab.active === hand) {
        if (!g) grab.active = null;
        else {
          const dq = c.q.clone().multiply(grab.q0.clone().invert());
          atlasRoot.quaternion.copy(dq).multiply(grab.mq);
          atlasRoot.position.copy(grab.mp.clone().sub(grab.p0).applyQuaternion(dq).add(c.p));
        }
      }
      const sy = c.gp.axes[3] || 0;
      if (hand === 'right' && Math.abs(sy) > 0.2) atlasRoot.scale.multiplyScalar(1 - sy * dt * 0.8).clampScalar(0.8, 6);
    }
  }
  if (app.mode === 'surgery') {
    const L = poses.left, Rr = poses.right;
    if (L && edge(L.C, 'y', btn(L.gp, 5))) { app.vrMenuOpen = !app.vrMenuOpen; ui.dirty = true; }
    if (Rr && edge(Rr.C, 'b', btn(Rr.gp, 5))) toggleLabels();
    if (!app.vrMenuOpen) {
      if (Rr && edge(Rr.C, 'a', btn(Rr.gp, 4))) cycleTool('right');
      if (L && edge(L.C, 'x', btn(L.gp, 4))) cycleTool('left');
    }
    if (L && Rr && btn(L.gp, 3) && btn(Rr.gp, 3)) { if (!app._calibLatch) { app._calibLatch = true; calibrate(); } } else app._calibLatch = false;
    if (L) { const ax = L.gp.axes[2] || 0, ay = L.gp.axes[3] || 0; if (Math.hypot(ax, ay) > 0.2) panCamera(ax * dt * 0.05, -ay * dt * 0.05); }
    if (Rr) { const ay = Rr.gp.axes[3] || 0; if (Math.abs(ay) > 0.2) camState.dist = clamp(camState.dist + ay * dt * 0.06, 0.035, 0.16); }
    // aletler
    for (const hand of ['left', 'right']) {
      const c = poses[hand]; const ins = instr[hand];
      if (!c) continue;
      const T = R.ports[hand].obj.getWorldPosition(V());
      const up = V(0, 1, 0).applyQuaternion(c.q);
      ins.update(c.p, T, up, app.vrMenuOpen ? 0 : (c.gp.buttons[0]?.value || 0));
    }
  }
}
function hapticPulse(hand, v) {
  if (!app.xrActive || v <= 0.01) return;
  for (const C of ctrls) if (C.src?.handedness === hand) C.src.gamepad?.hapticActuators?.[0]?.pulse?.(clamp(v, 0, 1), 30);
}
function onPanelClick(P, id) {
  audio.click();
  if (P === ui.menu) { if (id === 'atlas') setMode('atlas'); if (id === 'surgery') setMode('surgery'); }
  else if (P === ui.atlas) { if (id === 'home') setMode('menu'); else { app.atlasVis[id] = !app.atlasVis[id]; applyAtlasVis(); } }
  else if (P === ui.info) {
    if (id === 'guide') toggleGuide();
    if (id === 'labels') toggleLabels();
    if (id === 'skip' && proc && !proc.finished) proc.completeStep(true);
    if (id === 'calib') calibrate();
    if (id === 'resume') app.vrMenuOpen = false;
    if (id === 'home') location.reload();
    ui.dirty = true;
  }
}
function toggleLabels() { app.labelsOn = !app.labelsOn; updateSurgLabels(); ui.dirty = true; }
function toggleGuide() { if (!proc) return; proc.guide = !proc.guide; proc.markers.visible = proc.guide; ui.dirty = true; }
function cycleTool(hand, idx) {
  const set = TOOLSETS[hand], ins = instr[hand];
  const i = idx ?? (set.indexOf(ins.tool) + 1) % set.length;
  if (set[i]) { ins.set(set[i]); audio.tick(); ui.dirty = true; }
}

// ------------------------------------------------------------------ masaüstü girişi
const orbit = new OrbitControls(camera, renderer.domElement);
orbit.target.set(0, 1.0, -0.5); orbit.enableDamping = true;
const desk = { ndc: new THREE.Vector2(), down: false, depth: 0.0012, hold: null, tips: { left: null, right: null }, trig: { left: 0, right: 0 }, keys: {} };
function initDeskTips() {
  for (const h of ['left', 'right']) {
    const T = R.ports[h].obj.getWorldPosition(V());
    const f = A.root.localToWorld(proc.step.focus.clone());
    desk.tips[h] = T.clone().lerp(f, 0.75);
  }
}
renderer.domElement.addEventListener('pointermove', (e) => { desk.ndc.set(e.clientX / innerWidth * 2 - 1, -(e.clientY / innerHeight) * 2 + 1); });
renderer.domElement.addEventListener('pointerdown', (e) => { audio.init(); if (app.mode === 'surgery' && !app.orView && e.button === 0) { desk.down = true; desk.hold = null; } if (e.button === 2) { app.activeHand = app.activeHand === 'left' ? 'right' : 'left'; ui.dirty = true; } });
addEventListener('pointerup', () => { desk.down = false; desk.hold = null; });
renderer.domElement.addEventListener('contextmenu', e => e.preventDefault());
renderer.domElement.addEventListener('wheel', (e) => { if (app.mode === 'surgery' && !app.orView) { if (e.ctrlKey || e.shiftKey) camState.dist = clamp(camState.dist + e.deltaY * 0.00004, 0.035, 0.16); else desk.depth = clamp(desk.depth + e.deltaY * 0.00001, -0.008, 0.03); e.preventDefault(); } }, { passive: false });
addEventListener('keydown', (e) => {
  audio.init();
  const k = e.key.toLowerCase(); desk.keys[k] = true;
  if (app.mode !== 'surgery') return;
  if (k === 'q') app.activeHand = 'left';
  if (k === 'e') app.activeHand = 'right';
  if (k === 'tab') { e.preventDefault(); cycleTool(app.activeHand); }
  if (/^[1-8]$/.test(k)) cycleTool(app.activeHand, +k - 1);
  if (k === 'l') toggleLabels();
  if (k === 'g') toggleGuide();
  if (k === 'n' && proc && !proc.finished) proc.completeStep(true);
  if (k === 'o') { app.orView = !app.orView; orbit.enabled = app.orView; if (app.orView) { orbit.target.copy(A.root.localToWorld(V(0.06, -0.02, 0.1))); camera.position.copy(orbit.target).add(V(0.0, 0.5, 0.9)); } setMode('surgery'); }
  if (k === 'm' || k === 'escape') location.reload();
  if (k === ' ') desk.space = true;
  ui.dirty = true;
});
addEventListener('keyup', (e) => { const k = e.key.toLowerCase(); desk.keys[k] = false; if (k === ' ') desk.space = false; });

const pickRay = new THREE.Raycaster();
function pickList() {
  const l = A.pickables.filter(m => m.visible && m.parent);
  const st = proc?.state;
  if (st?.bowl) l.push(st.bowl.mesh);
  if (st?.bolster) l.push(st.bolster);
  if (st?.specimen?.visible) st.specimen.traverse(o => o.isMesh && l.push(o));
  for (const c of [st?.clamp, st?.veinClamp]) if (c) c.obj.traverse(o => o.isMesh && l.push(o));
  return l;
}
function validHit(h) {
  const o = h.object; let p = o.worldToLocal(h.point.clone());
  if (!o.visible) return false;
  const mat = o.material; const u = mat.userData?.u;
  if (mat.userData?.holes?.length && inHole(mat, p, 0)) return false;
  if (u && u.uCrater.value.w > 0 && p.distanceTo(V(u.uCrater.value.x, u.uCrater.value.y, u.uCrater.value.z)) < u.uCrater.value.w + 0.0008) return false;
  if (u && u.uPlaneCut.value.w > 0) { const e = u.uEllip.value; if ((p.x / e.x) ** 2 + (p.y / e.y) ** 2 + (p.z / e.z) ** 2 > 1) return false; }
  return true;
}
function deskUpdate(dt) {
  if (app.orView) return;
  const k = desk.keys, s = dt * 0.04;
  if (k.w || k.arrowup) panCamera(0, s); if (k.s || k.arrowdown) panCamera(0, -s);
  if (k.a || k.arrowleft) panCamera(-s, 0); if (k.d || k.arrowright) panCamera(s, 0);
  if (k.r) camState.dist = clamp(camState.dist - dt * 0.04, 0.035, 0.16);
  if (k.f) camState.dist = clamp(camState.dist + dt * 0.04, 0.035, 0.16);
  const hand = app.activeHand, ins = instr[hand];
  const trig = (desk.down || desk.space) ? 1 : 0;
  if (!trig) desk.hold = null;
  pickRay.setFromCamera(desk.ndc, lapCam);
  const planeTools = ['grasper', 'bulldog', 'needle', 'clip', 'bipolar', 'suction'];
  let tip;
  if (trig && planeTools.includes(ins.tool) && desk.hold != null) tip = pickRay.ray.at(desk.hold, V());
  else {
    // makasla eksizyonda tümör sol el ile ekarte ediliyormuş gibi arka kenar da seçilebilir
    const list = pickList().filter(m => !(ins.tool === 'scissors' && proc?.step?.id === 'excise' && m === A.meshes.tumor));
    const hits = pickRay.intersectObjects(list, false);
    const h = hits.find(validHit);
    app.lastHit = h ? h.object.name || h.object.type : null;
    if (h) {
      // yüzey normali boyunca hafif yukarıda dur (dokuya dik mesafe ≈ 1.5 mm + tekerlek derinliği)
      const n = h.face ? h.face.normal.clone().transformDirection(h.object.matrixWorld) : pickRay.ray.direction.clone().negate();
      if (n.dot(pickRay.ray.direction) > 0) n.negate();
      tip = h.point.clone().addScaledVector(n, desk.depth + 0.0012);
    }
    else tip = pickRay.ray.at(0.09, V());
    if (trig && desk.hold == null) desk.hold = tip.distanceTo(pickRay.ray.origin);
  }
  desk.tips[hand] = tip;
  desk.trig[hand] = trig;
  desk.trig[hand === 'left' ? 'right' : 'left'] = 0;
  for (const h of ['left', 'right']) {
    const I = instr[h]; const T = R.ports[h].obj.getWorldPosition(V());
    const P = desk.tips[h] || T.clone();
    const dir = P.clone().sub(T).normalize();
    const H = P.clone().addScaledVector(dir, -(SHAFT + I.toolObj.userData.tipOff));
    I.update(H, T, V(0, 1, 0), desk.trig[h]);
  }
}

// ------------------------------------------------------------------ döngü
const clock = new THREE.Clock();
let uiTimer = 0, usTimer = 0;
const prevTrig = { left: 0, right: 0 };
renderer.setAnimationLoop(frame);
function frame(fixedDt) {
  const dt = typeof fixedDt === 'number' && fixedDt < 1 ? fixedDt : Math.min(0.05, clock.getDelta());
  if (app.xrActive) xrUpdate(dt);
  if (app.mode === 'surgery' && proc) {
    updateLapCam(dt);
    if (!app.xrActive) deskUpdate(dt);
    const tools = ['left', 'right'].map(h => {
      const I = instr[h];
      const o = { hand: h, tool: I.tool, tipW: I.tipWorld.clone(), dirW: I.dir.clone(), trigger: I.trigger, prevTrigger: prevTrig[h], active: true };
      prevTrig[h] = I.trigger; return o;
    });
    const hap = proc.update(dt, tools);
    hapticPulse('left', hap[0]); hapticPulse('right', hap[1]);
    for (const h of ['left', 'right']) instr[h].hasLoad(['bulldog', 'needle', 'clip'].includes(instr[h].tool));
    // US
    usTimer += dt;
    const usT = tools.find(t => t.tool === 'us');
    const showUS = !!usT && proc.toK(usT.tipW).length() < 0.07;
    usPlane.visible = showUS; usCanvas.style.display = showUS && !app.xrActive ? 'block' : 'none';
    if (showUS && usTimer > 0.12) { usTimer = 0; usT.k = proc.toK(usT.tipW); proc.drawUltrasound(usCtx, usT); usTex.needsUpdate = true; }
  }
  if (orbit.enabled) orbit.update();
  uiTimer += dt;
  if (uiTimer > 0.25 || ui.dirty) {
    uiTimer = 0; ui.dirty = false;
    if (app.mode === 'surgery') { ui.hud(app); if (app.xrActive) ui.drawInfo(app); }
  }
  for (const P of [ui.menu, ui.atlas, ui.info]) if (P._dirty) { P._dirty = false; if (P === ui.menu) ui.drawMenu(app); if (P === ui.atlas) ui.drawAtlas(app); if (P === ui.info) ui.drawInfo(app); }
  render();
}

const LAP_EXPOSURE = 1.9; // laparoskop kamerasının otomatik pozlaması
function renderLap() {
  const cur = renderer.getRenderTarget(), xrOn = renderer.xr.enabled;
  renderer.xr.enabled = false;
  const asp = lapCam.aspect; lapCam.aspect = 16 / 9; lapCam.updateProjectionMatrix();
  renderer.setRenderTarget(rt); renderer.clear(); renderer.render(scene, lapCam);
  lapCam.aspect = asp; lapCam.updateProjectionMatrix();
  renderer.xr.enabled = xrOn; renderer.setRenderTarget(cur);
}
// Dış (ameliyathane) görüşte karın içi yapılar zaten cilt arkasında: çizmeyerek GPU yükünü azalt
const keepOutside = new Set([A.meshes.skin, R.torsoDrape, ...Object.values(R.ports).map(p => p.obj)]);
const hidden = [];
function renderOutside(cam) {
  for (const o of A.root.children) if (o.visible && !keepOutside.has(o)) { o.visible = false; hidden.push(o); }
  renderer.render(scene, cam);
  while (hidden.length) hidden.pop().visible = true;
}
function render() {
  const surg = app.mode === 'surgery' && proc;
  if (app.xrActive) { if (surg) renderLap(); renderOutside(camera); return; }
  // masaüstünde laparoskop pencere oranında (fare seçimi aynı projeksiyonu kullanır)
  const asp = innerWidth / innerHeight; if (lapCam.aspect !== asp) { lapCam.aspect = asp; lapCam.updateProjectionMatrix(); }
  if (surg && !app.orView) {
    renderer.toneMappingExposure = LAP_EXPOSURE; renderer.render(scene, lapCam); renderer.toneMappingExposure = 1;
    return;
  }
  if (surg) renderLap();
  renderOutside(camera);
}
addEventListener('resize', () => { camera.aspect = innerWidth / innerHeight; camera.updateProjectionMatrix(); renderer.setSize(innerWidth, innerHeight); });

// ------------------------------------------------------------------ XR oturumları
renderer.xr.addEventListener('sessionstart', () => {
  app.xrActive = true; audio.init();
  const ar = app.xrMode === 'ar';
  scene.background = ar ? null : BG; R.env.visible = !ar;
  ui.drawMenu(app); ui.drawAtlas(app); ui.drawInfo(app);
  document.body.dataset.xr = '1';
  setTimeout(() => setMode(app.mode === 'surgery' ? 'surgery' : 'menu'), 300);
});
renderer.xr.addEventListener('sessionend', () => { app.xrActive = false; scene.background = BG; R.env.visible = true; delete document.body.dataset.xr; setMode(app.mode); });
async function startXR(kind) {
  if (!navigator.xr) return alert('Bu tarayıcıda WebXR yok. Quest Browser kullanın (HTTPS veya localhost gerekli).');
  app.xrMode = kind;
  const mode = kind === 'ar' ? 'immersive-ar' : 'immersive-vr';
  try {
    const s = await navigator.xr.requestSession(mode, { optionalFeatures: ['local-floor', 'bounded-floor', 'hand-tracking'] });
    await renderer.xr.setSession(s);
  } catch (err) { alert('XR oturumu başlatılamadı: ' + err.message); }
}
(async () => {
  const vrOK = navigator.xr && await navigator.xr.isSessionSupported('immersive-vr').catch(() => false);
  const arOK = navigator.xr && await navigator.xr.isSessionSupported('immersive-ar').catch(() => false);
  const bv = document.getElementById('btnVR'), ba = document.getElementById('btnMR');
  bv.disabled = !vrOK; ba.disabled = !arOK;
  if (!vrOK) bv.title = 'WebXR immersive-vr desteklenmiyor (Quest Browser + HTTPS gerekli)';
  bv.onclick = () => startXR('vr'); ba.onclick = () => startXR('ar');
})();

// HTML menü düğmeleri
document.getElementById('btnAtlas').onclick = () => { audio.init(); setMode('atlas'); };
document.getElementById('btnSurgery').onclick = () => { audio.init(); setMode('surgery'); };
document.getElementById('homeBtn').onclick = () => location.reload();
const atlasBox = document.getElementById('atlasToggles');
function syncAtlasButtons() { atlasBox.querySelectorAll('button[data-k]').forEach(b => b.classList.toggle('on', !!app.atlasVis[b.dataset.k])); ui.drawAtlas(app); }
atlasBox.querySelectorAll('button[data-k]').forEach(b => b.onclick = () => { app.atlasVis[b.dataset.k] = !app.atlasVis[b.dataset.k]; applyAtlasVis(); });
document.getElementById('atlasHome').onclick = () => setMode('menu');

app.dbg = { THREE, scene, camera, orbit, lapCam, renderer, A2, atlasRoot, camState, setMode, calibrate, frame, desk, xrUpdate, ctrls, onPanelClick,
  // test yardımcıları: n kare ilerlet, ekran görüntüsü al
  run: (n = 10, dt = 1 / 30) => { for (let i = 0; i < n; i++) frame(dt); },
  shot: (n = 'shot') => { frame(1 / 30); return fetch('/__shot?n=' + n, { method: 'POST', body: renderer.domElement.toDataURL('image/jpeg', 0.85) }); } };
setMode('menu');
ui.drawMenu(app); ui.drawAtlas(app);
