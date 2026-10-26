// Cerrahi prosedür motoru: adımlar, diseksiyon, kanama, iskemi, renorafi, skor
import * as THREE from 'three';
import { V, clamp, distToPolyline, addHole, inHole, tissueMaterial, taperTube } from './util.js';
import { KID } from './anatomy.js';
import { TOOLS, makeBulldog, makeHemolok } from './instruments.js';

const LAYER_LAP = 5;
const tmp = new THREE.Vector3(), tmp2 = new THREE.Vector3();

// Yaklaşık kanama hızları (ml/s) — simülasyon amaçlı
const BLEED = { renalVein: 9, renalArtery: 14, aorta: 45, ivc: 35, gonadal: 1.6, adrenalVein: 1.2, lumbarVein: 2.8, parenchyma: 5.5 };
const TR_NAMES = { renalVein: 'renal ven', renalArtery: 'renal arter', aorta: 'aorta', ivc: 'VCI', gonadal: 'gonadal ven', adrenalVein: 'adrenal ven', lumbarVein: 'lomber ven', parenchyma: 'parankim' };

function ellipProject(p) { // böbrek-yerel noktayı elipsoid yüzeyine (merkezden ışınla) taşır
  const k = Math.sqrt((p.x / KID.AX) ** 2 + (p.y / KID.AY) ** 2 + (p.z / KID.AZ) ** 2);
  return p.clone().divideScalar(k);
}

export class Procedure {
  constructor(ctx) {
    this.ctx = ctx;
    const A = this.A = ctx.A;
    this.K = A.meshes.kidneyL;
    this.t = 0;
    this.stepIdx = 0;
    this.guide = true;
    this.messages = [];
    this.m = { start: 0, bloodLoss: 0, injuries: [], ureterInjury: false, bowelInjury: false, offClamp: false, wit: 0, witStart: null, witEnd: null,
      margin: null, marginMin: null, marginStatus: '-', venousClamp: false, energyOOV: 0, penalties: [], stepTimes: [], skipped: [] };
    this.bleeds = [];
    this.raycaster = new THREE.Raycaster();
    this.state = { colonK: 0, colonAnim: false, ureterDelta: V(), ureterGrab: null, ureterLiftT: 0, gonadalSeen: false,
      clamp: null, veinClamp: null, usT: 0, usShown: false, bins: new Array(36).fill(null), excised: false, inner: [], outer: [], outerPending: null,
      specimen: null, specGrab: null, bagDone: false, ischemia: 0, congestion: 0 };
    this.cooldown = {};
    this.buildFx();
    this.buildSteps();
    this.markers = new THREE.Group(); A.root.add(this.markers);
    this.enterStep(0);
  }

  // ---------------------------------------------------------------- yardımcılar
  msg(text, kind = 'info') { this.messages.push({ text, kind, t: this.t }); this.ctx.ui?.toast(text, kind); if (kind === 'bad') this.ctx.audio?.alarm(); }
  penalty(points, reason) { this.m.penalties.push({ points, reason }); }
  toP(w) { return this.A.root.worldToLocal(w.clone()); }
  toK(w) { return this.K.worldToLocal(w.clone()); }
  pToW(p) { return this.A.root.localToWorld(p.clone()); }
  kToW(p) { return this.K.localToWorld(p.clone()); }
  get step() { return this.steps[this.stepIdx]; }

  marker(posP, color = 0x33ff99, size = 0.0018) {
    const m = new THREE.Mesh(new THREE.SphereGeometry(size, 10, 8), new THREE.MeshBasicMaterial({ color, transparent: true, opacity: 0.85, depthTest: false }));
    m.position.copy(posP); m.renderOrder = 990; m.layers.set(LAYER_LAP);
    this.markers.add(m); return m;
  }
  clearMarkers() { this.markers.clear(); }

  // Kamera → hedef doğrultusu üzerindeki tüm (diseke edilebilir) yüzey kesişimleri delikteyse hedef "açığa çıkmıştır"
  exposed(targetW, meshes) {
    const cam = this.ctx.lapCam.getWorldPosition(tmp2);
    const dir = targetW.clone().sub(cam); const dist = dir.length(); dir.normalize();
    this.raycaster.set(cam, dir); this.raycaster.far = dist - 0.0005; this.raycaster.near = 0;
    for (const m of meshes) {
      if (!m.visible) continue;
      const hits = this.raycaster.intersectObject(m, false);
      for (const h of hits) { const lp = m.worldToLocal(h.point.clone()); if (!inHole(m.material, lp, 0.0006)) return false; }
    }
    return true;
  }

  nearSurface(mesh, pObj, maxD) {
    const p = mesh.geometry.attributes.position; const d2 = maxD * maxD;
    for (let i = 0; i < p.count; i += 2) {
      const dx = p.getX(i) - pObj.x, dy = p.getY(i) - pObj.y, dz = p.getZ(i) - pObj.z;
      if (dx * dx + dy * dy + dz * dz < d2) return true;
    }
    return false;
  }

  // ---------------------------------------------------------------- efektler
  buildFx() {
    const A = this.A;
    // kan damlaları
    const N = 400;
    const g = new THREE.BufferGeometry(); g.setAttribute('position', new THREE.Float32BufferAttribute(new Float32Array(N * 3), 3));
    const dropTex = (() => { const c = document.createElement('canvas'); c.width = c.height = 32; const x = c.getContext('2d'); const gr = x.createRadialGradient(13, 12, 1, 16, 16, 15); gr.addColorStop(0, '#d04040'); gr.addColorStop(0.5, '#7a0606'); gr.addColorStop(1, 'rgba(90,0,0,0)'); x.fillStyle = gr; x.fillRect(0, 0, 32, 32); const t = new THREE.CanvasTexture(c); t.colorSpace = THREE.SRGBColorSpace; return t; })();
    this.drops = { pts: new THREE.Points(g, new THREE.PointsMaterial({ map: dropTex, size: 0.0026, sizeAttenuation: true, transparent: true, alphaTest: 0.2, depthWrite: false })), v: Array.from({ length: N }, () => V()), life: new Float32Array(N), i: 0, N };
    this.drops.pts.frustumCulled = false; A.root.add(this.drops.pts);
    // duman
    const sg = new THREE.BufferGeometry(); sg.setAttribute('position', new THREE.Float32BufferAttribute(new Float32Array(120 * 3), 3));
    const st = (() => { const c = document.createElement('canvas'); c.width = c.height = 64; const x = c.getContext('2d'); const gr = x.createRadialGradient(32, 32, 0, 32, 32, 32); gr.addColorStop(0, 'rgba(255,255,255,0.8)'); gr.addColorStop(1, 'rgba(255,255,255,0)'); x.fillStyle = gr; x.fillRect(0, 0, 64, 64); return new THREE.CanvasTexture(c); })();
    this.smoke = { pts: new THREE.Points(sg, new THREE.PointsMaterial({ color: 0xdddddd, size: 0.012, map: st, transparent: true, opacity: 0.45, depthWrite: false })), v: Array.from({ length: 120 }, () => V()), life: new Float32Array(120), i: 0, N: 120 };
    this.smoke.pts.frustumCulled = false; A.root.add(this.smoke.pts);
    this.poolMat = new THREE.MeshStandardMaterial({ color: 0x5a0303, roughness: 0.08, metalness: 0.1, transparent: true, opacity: 0.92 });
    this.charMat = new THREE.MeshStandardMaterial({ color: 0x2a1a12, roughness: 0.9 });
    this.cutMat = new THREE.MeshStandardMaterial({ color: 0x5c0b0b, roughness: 0.5 });
    this.sutureMat = new THREE.MeshStandardMaterial({ color: 0x6b3bd1, roughness: 0.4 });
  }
  emit(sys, posP, vel, spread) {
    const i = sys.i = (sys.i + 1) % sys.N;
    sys.pts.geometry.attributes.position.setXYZ(i, posP.x, posP.y, posP.z);
    sys.v[i].copy(vel).add(V((Math.random() - .5) * spread, (Math.random() - .5) * spread, (Math.random() - .5) * spread));
    sys.life[i] = 1;
  }
  stepParticles(sys, dt, acc, decay) {
    const p = sys.pts.geometry.attributes.position;
    for (let i = 0; i < sys.N; i++) {
      if (sys.life[i] <= 0) { p.setXYZ(i, 0, -10, 0); continue; }
      sys.life[i] -= dt * decay;
      sys.v[i].addScaledVector(acc, dt);
      p.setXYZ(i, p.getX(i) + sys.v[i].x * dt, p.getY(i) + sys.v[i].y * dt, p.getZ(i) + sys.v[i].z * dt);
    }
    p.needsUpdate = true;
  }
  addBleed(key, posP, rate, opts = {}) {
    const pool = new THREE.Mesh(new THREE.SphereGeometry(1, 20, 12), this.poolMat);
    pool.scale.setScalar(0.0001); pool.position.copy(posP); this.A.root.add(pool);
    const b = { key, pos: posP.clone(), rate, vol: 0, active: true, pool, bip: 0, major: !!opts.major, needClip: !!opts.needClip };
    this.bleeds.push(b); return b;
  }

  // ---------------------------------------------------------------- adımlar
  buildSteps() {
    const A = this.A, tum = A.land.tumor;
    const crv = A.curves;
    const ptOn = (name, x, off) => { // eğri üzerinde verilen x'e en yakın nokta + ofset
      let best = null, bd = 1; for (const p of crv[name].pts) { const d = Math.abs(p.x - x); if (d < bd) { bd = d; best = p; } }
      return best.clone().add(off);
    };
    this.steps = [
      { id: 'toldt', title: '1. Toldt çizgisi insizyonu ve kolon medializasyonu', tools: 'Sağ: Hook koter · Sol: Grasper (karşı traksiyon)',
        text: 'İnen kolonun lateralindeki Toldt beyaz çizgisini splenik fleksuradan pelvik girime doğru hook ile açın. Gerota fasyası ile mezokolon arasındaki avasküler planda (Toldt fasyası) kalın. Kolona termal temas = barsak hasarı.',
        focus: V(0.135, -0.02, 0.03), camDist: 0.13,
        targets: () => A.land.toldt.map(p => ({ p: p.clone(), done: false })),
        check: (tg) => inHole(A.meshes.latPer.material, tg.p, 0.0025) },
      { id: 'ureter', title: '2. Gonadal ven ve üreterin tanımlanması', tools: 'Sol: Grasper · Sağ: Hook / Aspiratör',
        text: 'Psoas üzerinde medialdeki gonadal veni grasper ile gösterin (temas). Hemen lateral-posteriorundaki üreteri kavrayıp (tetik) anterolaterale ≥1 cm kaldırın ve 1 sn tutun. Üretere enerji uygulamayın.',
        focus: V(0.05, -0.075, 0.03) },
      { id: 'hilum', title: '3. Hiler diseksiyon (VAP: Ven–Arter–Pelvis)', tools: 'Sağ: Hook · Sol: Grasper / Aspiratör',
        text: 'Gonadal veni renal vene kadar takip edin. Hiler yağı hook ile diseke ederek önce renal veni, sonra venin posterior-süperiorundaki renal arteri ortaya koyun. Dikkat: gonadal, adrenal ve lomber ven dalları.',
        focus: V(0.05, 0.002, 0.028),
        targets: () => [
          ...[0.043, 0.05, 0.057].map(x => ({ p: ptOn('renalVein', x, V(0, 0, 0.0062)), done: false, kind: 'vein' })),
          ...[0.045, 0.053].map(x => ({ p: ptOn('renalArtery', x, V(0, 0.0038, 0.002)), done: false, kind: 'artery' })),
        ],
        check: (tg) => this.exposed(this.pToW(tg.p), [A.meshes.fatHilar]) },
      { id: 'defat', title: '4. Gerota fasyasının açılması ve tümör çevresinin defatlanması', tools: 'Sağ: Hook / Makas · Sol: Grasper',
        text: 'Gerota fasyasını açın, perirenal yağı tümörün etrafında ~1 cm\'lik sağlıklı parankim görünene kadar temizleyin. Tümör üzerindeki yağı spesimen ile bırakmak onkolojik olarak tercih edilebilir; burada görünürlük için tümör yüzeyi de açılır.',
        focus: null,
        targets: () => {
          const out = [];
          for (let i = 0; i < 8; i++) {
            const a = i / 8 * Math.PI * 2;
            const q = ellipProject(tum.S.clone().addScaledVector(tum.e1, Math.cos(a) * (tum.footprint + 0.008)).addScaledVector(tum.e2, Math.sin(a) * (tum.footprint + 0.008)));
            out.push({ k: q.addScaledVector(tum.n, 0.002), done: false });
          }
          out.push({ k: tum.C.clone().addScaledVector(tum.n, tum.R + 0.001), done: false });
          return out.map(o => ({ ...o, p: this.toP(this.kToW(o.k)) }));
        },
        check: (tg) => inHole(A.meshes.fatPeri.material, tg.k, 0.004) || this.exposed(this.pToW(tg.p), [A.meshes.fatPeri]) },
      { id: 'us', title: '5. İntraoperatif laparoskopik ultrasonografi', tools: 'Sağ veya sol: US probu',
        text: 'US probunu tümör ve çevre parankim üzerine yerleştirip 2 sn temas halinde tutun. Tümör derinliği ve toplayıcı sisteme uzaklık değerlendirilir; 3–5 mm\'lik rezeksiyon hattı koterle işaretlenmiş olarak gösterilecek.',
        focus: null },
      { id: 'clamp', title: '6. Renal arterin bulldog ile klemplenmesi', tools: 'Sağ: Bulldog aplikatörü',
        text: 'Bulldog klempi ana renal artere (dallanma öncesi) uygulayın. Sıcak iskemi süresi (WIT) başlar — hedef < 25 dk. Yalnız ven klemplemesi venöz konjesyona ve kanamaya yol açar. (Klempsiz "off-clamp" eksizyon da mümkündür; kanama artar.)',
        focus: null },
      { id: 'excise', title: '7. Tümör eksizyonu (soğuk makas)', tools: 'Sağ: Soğuk makas · Sol: Aspiratör / Grasper',
        text: 'İşaretli hattan, 2–5 mm sağlıklı parankim sınırı bırakarak tümör çevresinde 360° kesin. Tümör kapsülüne girmek pozitif cerrahi sınır riskidir; gereksiz geniş eksizyon parankim kaybıdır.',
        focus: null },
      { id: 'inner', title: '8. İç kat renorafi (medüller kat)', tools: 'Sağ: Portegü (V-Loc) · Sol: Grasper',
        text: 'Defekt tabanında açılmış toplayıcı sistem ve ana intrarenal damarları sürekli dikişle kapatın: 4 noktayı sırayla portegü ile geçin.',
        focus: null },
      { id: 'outer', title: '9. Dış kat renorafi (kayan Hem-o-lok tekniği)', tools: 'Sağ: Portegü',
        text: 'Her dikiş için önce bir kortikal kenarı, sonra karşı kenarı geçin; dikiş Hem-o-lok kliplerle gerilip sabitlenir (sliding-clip). 3 dikiş gerekir.',
        focus: null },
      { id: 'unclamp', title: '10. Klempin kaldırılması ve hemostaz', tools: 'Sağ: Bulldog aplikatörü · Sol: Aspiratör',
        text: 'Bulldog aplikatörü ile klempi alın, WIT durur. Böbreğin reperfüzyonunu ve renorafi hattında aktif kanama olmadığını kontrol edin.',
        focus: null },
      { id: 'extract', title: '11. Spesimenin endobag ile alınması', tools: 'Sol: Grasper',
        text: 'Grasper ile spesimeni kavrayın (tetik basılı) ve parakolik oluktaki endobag ağzına getirip bırakın.',
        focus: V(0.11, -0.07, 0.035) },
    ];
  }

  enterStep(i) {
    this.stepIdx = i; this.clearMarkers();
    const s = this.step; if (!s) return;
    s.t0 = this.t;
    if (s.targets) { s.tg = s.targets(); s.tg.forEach(t => { t.mk = this.marker(t.p); }); }
    const tum = this.A.land.tumor;
    if (!s.focus) s.focus = this.toP(this.kToW(tum.C));
    if (s.id === 'clamp') {
      // hiler diseksiyonda açılan arter segmenti
      const a = this.A.curves.renalArtery.pts;
      const near = (x) => a.reduce((b, p) => Math.abs(p.x - x) < Math.abs(b.x - x) ? p : b, a[0]);
      s.clampPts = [0.045, 0.049, 0.053].map(x => near(x).clone().add(V(0, 0.0035, 0.002)));
      s.focus = near(0.049).clone();
    }
    if (s.id === 'unclamp' && this.state.clamp) { s.focus = this.state.clamp.p.clone(); this.marker(this.state.clamp.p.clone().add(V(0, 0.0035, 0.002)), 0xffcc33); }
    if (s.id === 'us') this.marker(this.toP(this.kToW(tum.C.clone().addScaledVector(tum.n, tum.R))), 0x55ccff, 0.0025);
    if (s.id === 'clamp') s.clampPts.forEach(p => this.marker(p, 0xffcc33));
    if (s.id === 'excise') this.showMarginRing(true);
    if (s.id === 'inner') this.makeInnerTargets();
    if (s.id === 'outer') this.makeOuterTargets();
    if (s.id === 'extract') this.makeBag();
    this.ctx.ui?.stepChanged(this);
    this.ctx.audio?.click();
  }
  completeStep(skipped = false) {
    const s = this.step;
    this.m.stepTimes.push({ id: s.id, title: s.title, time: this.t - s.t0, skipped });
    if (skipped) { this.m.skipped.push(s.title); this.penalty(5, 'Adım atlandı: ' + s.title); this.forceComplete(s.id); }
    if (this.stepIdx + 1 < this.steps.length) { this.msg('✓ ' + s.title.replace(/^\d+\. /, '') + ' tamamlandı', 'good'); this.enterStep(this.stepIdx + 1); }
    else this.finish();
  }
  forceComplete(id) {
    const st = this.state, A = this.A;
    if (id === 'toldt') { st.colonAnim = true; }
    if (id === 'ureter') { st.ureterDelta.set(0, 0, 0.012); this.applyUreter(); }
    if (id === 'hilum' || id === 'defat') { const s = this.step; s.tg?.forEach(t => { const mesh = id === 'hilum' ? A.meshes.fatHilar : A.meshes.fatPeri; const lp = mesh.worldToLocal(this.pToW(t.p)); addHole(mesh.material, lp, 0.009); }); }
    if (id === 'us') { st.usShown = true; }
    if (id === 'clamp') { this.placeClamp(this.step.clampPts[1].clone().sub(V(0, 0.0035, 0.002)), 'artery'); }
    if (id === 'excise') { const fp = A.land.tumor.footprint; st.bins = st.bins.map(() => fp + 0.003); this.doExcision(); }
    if (id === 'inner') { this.step.tg?.forEach(t => t.done = true); this.drawInner(); }
    if (id === 'outer') { this.step.pairs?.forEach((p, i) => this.drawOuter(i)); this.fillCrater(); }
    if (id === 'unclamp') this.removeClamp();
    if (id === 'extract') { st.bagDone = true; if (st.specimen) st.specimen.visible = false; }
  }
  finish() {
    this.finished = true;
    this.ctx.ui?.report(this.report());
  }

  // ---------------------------------------------------------------- ana güncelleme
  // tools: [{hand, tool, tipW, trigger, prevTrigger}]
  update(dt, tools) {
    this.t += dt;
    const A = this.A, st = this.state, s = this.step;
    const haptic = [0, 0];
    // yerçekimi (hasta-yerel)
    const gW = V(0, -1, 0); const inv = new THREE.Matrix4().copy(A.root.matrixWorld).invert();
    const gP = gW.transformDirection(inv).multiplyScalar(9.81 * 0.02);

    // deformasyon uniformları
    const soft = [this.K, A.meshes.fatPeri, A.meshes.tumor, A.meshes.fatHilar, A.meshes.colon, A.meshes.meso, A.meshes.latPer];
    for (const m of soft) {
      if (!m.material.userData.u) continue;
      tools.forEach((T, i) => {
        const lp = m.worldToLocal(T.tipW.clone());
        m.material.userData.u.uTips.value[i].set(lp.x, lp.y, lp.z, T.active ? 1 : 0);
      });
    }

    for (let i = 0; i < tools.length; i++) {
      const T = tools[i]; if (!T.active) continue;
      T.p = this.toP(T.tipW); T.k = this.toK(T.tipW);
      const info = TOOLS[T.tool];
      const on = T.trigger > 0.55, pressed = on && T.prevTrigger <= 0.55;
      // dokuya yakınlık → haptik
      const ek = Math.sqrt((T.k.x / KID.AX) ** 2 + (T.k.y / KID.AY) ** 2 + (T.k.z / KID.AZ) ** 2);
      if (ek < 1.02) haptic[i] = Math.max(haptic[i], 0.25 + (1.02 - ek) * 3);
      // enerji
      const energy = info.energy && on;
      const cutting = energy || (T.tool === 'scissors' && on);
      if (energy) {
        this.ctx.audio?.cautery(true, i);
        haptic[i] = Math.max(haptic[i], 0.35);
        if (Math.random() < 0.6) this.emit(this.smoke, T.p, gP.clone().multiplyScalar(-0.15), 0.01);
        if (!this.ctx.isVisible(T.tipW)) { if (!this.cooldown['oov' + i] || this.t - this.cooldown['oov' + i] > 2) { this.cooldown['oov' + i] = this.t; this.m.energyOOV++; this.penalty(2, 'Görüş alanı dışında enerji kullanımı'); this.msg('⚠ Görüş alanı dışında enerji kullanımı!', 'warn'); } }
      } else this.ctx.audio?.cautery(false, i);
      if (cutting) {
        this.dissect(T);
        this.injuryCheck(T, energy);
      }
      if (T.tool === 'suction' && on) this.suction(T, dt);
      if (T.tool === 'bipolar' && on) this.bipolarHemostasis(T, dt);
      if (T.tool === 'clip' && pressed) this.clipVessel(T);
      T.on = on; T.pressed = pressed;
    }
    this.ctx.audio?.suction(tools.some(T => T.active && T.tool === 'suction' && T.trigger > 0.55));

    // adıma özel mantık
    if (s && !this.finished) this.stepLogic(s, tools, dt);

    // kolon medializasyon animasyonu
    if (st.colonAnim && st.colonK < 1) {
      st.colonK = Math.min(1, st.colonK + dt / 2.5);
      const k = st.colonK * st.colonK * (3 - 2 * st.colonK);
      const cg = A.root.getObjectByName('colonGroup');
      cg.position.set(-0.097 * k, 0, 0.006 * k);
      A.meshes.meso.scale.set(1 - 0.85 * k, 1, 1); A.meshes.meso.position.x = 0.096 * 0.85 * k;
      A.meshes.meso.visible = k < 0.7; // mezokolon medialde toplanır
      A.meshes.panc.position.set(-0.05 * k, 0.008 * k, -0.012 * k); // pankreas kuyruğu kolonla birlikte medialize
      A.meshes.toldt.visible = false;
    }
    // iskemi rengi
    const tgtI = st.clamp ? 1 : 0, tgtC = st.veinClamp && !st.clamp ? 1 : 0;
    st.ischemia += (tgtI - st.ischemia) * Math.min(1, dt / 12);
    st.congestion += (tgtC - st.congestion) * Math.min(1, dt / 8);
    const tint = this.K.material.userData.u.uTint.value;
    tint.setRGB(1 - 0.25 * st.ischemia - 0.35 * st.congestion, 1 - 0.3 * st.ischemia - 0.45 * st.congestion, 1 - 0.18 * st.ischemia - 0.1 * st.congestion);
    // WIT
    if (st.clamp && this.m.witStart !== null) this.m.wit = this.t - this.m.witStart;

    // kanamalar
    for (const b of this.bleeds) {
      let rate = b.active ? b.rate : 0;
      if (b.key === 'parenchyma') {
        if (st.clamp && !st.veinClamp) rate *= 0.06; else if (st.clamp && st.veinClamp) rate *= 0.02; else if (st.veinClamp) rate *= 1.6;
        if (st.inner.length >= 4) rate *= 0.25;
        if (st.outerDone) rate = 0;
      }
      if (b.key === 'renalArtery' && st.clamp) rate *= 0.05;
      if (rate > 0) {
        this.m.bloodLoss += rate * dt;
        b.vol += rate * dt;
        const n = Math.min(4, Math.ceil(rate * dt * 30));
        for (let k = 0; k < n; k++) this.emit(this.drops, b.pos, gP.clone().multiplyScalar(-0.02).add(V(0, 0, 0.02)).multiplyScalar(rate > 5 ? 3 : 1), 0.06);
      }
      const r = Math.min(0.03, 0.003 + 0.0011 * Math.sqrt(b.vol));
      b.pool.scale.set(r, r, r * 0.35);
      b.pool.lookAt(this.pToW(b.pos.clone().sub(gP)));
      b.pool.visible = b.vol > 0.5;
    }
    this.stepParticles(this.drops, dt, gP, 1.6);
    this.stepParticles(this.smoke, dt, gP.clone().multiplyScalar(-0.05), 0.8);
    if (this.m.bloodLoss > 500 && !this.cooldown.bl500) { this.cooldown.bl500 = 1; this.msg('⚠ Kan kaybı 500 ml\'yi aştı — anestezi uyarıldı', 'bad'); }
    return haptic;
  }

  dissect(T) {
    const A = this.A;
    const list = [A.meshes.latPer, A.meshes.meso, A.meshes.fatPeri, A.meshes.fatHilar, A.meshes.fatRetro];
    const r = T.tool === 'hook' ? 0.0065 : 0.005;
    if (!this.cooldown.dis || this.t - this.cooldown.dis > 0.03) {
      this.cooldown.dis = this.t;
      for (const m of list) {
        if (!m.visible) continue;
        const lp = m.worldToLocal(T.tipW.clone());
        if (!m.geometry.boundingSphere) m.geometry.computeBoundingSphere();
        const bs = m.geometry.boundingSphere;
        if (lp.distanceTo(bs.center) > bs.radius + 0.01) continue;
        if (this.nearSurface(m, lp, 0.0065)) addHole(m.material, lp, r);
      }
    }
  }

  injuryCheck(T, energy) {
    const A = this.A, st = this.state;
    const keys = ['renalVein', 'renalArtery', 'aorta', 'ivc', 'gonadal', 'adrenalVein', 'lumbarVein'];
    for (const k of keys) {
      const c = A.curves[k]; const q = V();
      const { d } = distToPolyline(T.p, c.pts, q);
      if (d < c.r + 0.0006) {
        if (this.cooldown[k] && this.t - this.cooldown[k] < 3) continue;
        this.cooldown[k] = this.t;
        (this.injLog ||= []).push({ k, d, tip: T.p.clone(), step: this.step?.id, hit: window.app?.lastHit });
        const major = ['renalVein', 'renalArtery', 'aorta', 'ivc'].includes(k);
        const rate = BLEED[k];
        this.addBleed(k, q.addScaledVector(T.p.clone().sub(q).normalize(), c.r), rate, { major, needClip: major });
        this.m.injuries.push(TR_NAMES[k]);
        this.penalty(major ? 20 : 6, (major ? 'Majör vasküler yaralanma: ' : 'Damar yaralanması: ') + TR_NAMES[k]);
        this.msg(`${major ? '⛔ MAJÖR' : '⚠'} ${TR_NAMES[k]} yaralandı! ${major ? 'Aspiratörle alanı temizleyip Hem-o-lok klip uygulayın.' : 'Bipolar ile koagüle edin.'}`, major ? 'bad' : 'warn');
      }
    }
    if (energy) {
      const du = distToPolyline(T.p, A.curves.ureter.pts).d;
      if (du < A.curves.ureter.r + 0.0012 && !this.m.ureterInjury) { this.m.ureterInjury = true; this.penalty(15, 'Üreter termal hasarı'); this.msg('⛔ Üretere termal hasar! (geç striktür/fistül riski)', 'bad'); }
      const dc = distToPolyline(this.toP(T.tipW).sub(A.root.getObjectByName('colonGroup').position), A.curves.colon.pts).d;
      if (dc < A.curves.colon.r + 0.0008 && !this.m.bowelInjury) { this.m.bowelInjury = true; this.penalty(15, 'Kolon termal hasarı'); this.msg('⛔ Kolona termal temas! (gecikmiş perforasyon riski)', 'bad'); }
    }
  }

  suction(T, dt) {
    for (const b of this.bleeds) {
      const d = b.pos.distanceTo(T.p);
      if (d < 0.025) b.vol = Math.max(0, b.vol - dt * 40);
    }
  }
  bipolarHemostasis(T, dt) {
    for (const b of this.bleeds) {
      if (!b.active || b.key === 'parenchyma') continue;
      if (b.pos.distanceTo(T.p) < 0.006) {
        if (b.needClip) { if (!this.cooldown['bipmaj']) { this.cooldown['bipmaj'] = 1; this.msg('Majör damar: bipolar yetersiz — Hem-o-lok klip gerekli.', 'warn'); } continue; }
        b.bip += dt;
        if (b.bip > 1.5) { b.active = false; this.msg('✓ Kanama bipolar ile kontrol edildi', 'good'); }
      }
    }
  }
  clipVessel(T) {
    for (const b of this.bleeds) {
      if (!b.active || b.key === 'parenchyma') continue;
      if (b.pos.distanceTo(T.p) < 0.007) {
        b.active = false;
        const c = makeHemolok(); c.position.copy(b.pos); c.lookAt(this.pToW(T.p)); this.A.root.add(c);
        this.ctx.audio?.click();
        this.msg('✓ Kanayan damar klipslendi', 'good');
        return;
      }
    }
  }

  // ---------------------------------------------------------------- adım mantığı
  stepLogic(s, tools, dt) {
    const A = this.A, st = this.state, tum = A.land.tumor;
    const grasp = tools.find(T => T.active && T.tool === 'grasper');
    // genel hedef kontrolü (seyrek)
    if (s.tg && s.check && (!this.cooldown.tg || this.t - this.cooldown.tg > 0.12)) {
      this.cooldown.tg = this.t;
      let allDone = true;
      for (const t of s.tg) {
        if (!t.done && s.check(t)) { t.done = true; t.mk.material.color.set(0x2266ff); t.mk.visible = false; this.ctx.audio?.tick(); }
        if (!t.done) allDone = false;
      }
      if (s.id === 'toldt' && s.tg.filter(t => t.done).length >= s.tg.length - 1) { st.colonAnim = true; this.msg('Kolon medialize edildi — Gerota fasyası görünür', 'good'); this.completeStep(); return; }
      if (s.id === 'hilum' && !st.veinMsg && s.tg.filter(t => t.kind === 'vein' && t.done).length === 3) { st.veinMsg = 1; this.msg('Renal ven iskeletize edildi. Arter venin posterior-süperiorunda.', 'good'); }
      if (allDone && s.id !== 'toldt') { this.completeStep(); return; }
    }
    if (s.id === 'ureter') {
      for (const T of tools) {
        if (!T.active || T.tool !== 'grasper') continue;
        if (!st.gonadalSeen && distToPolyline(T.p, A.curves.gonadal.pts).d < 0.006 && T.p.y < -0.02) { st.gonadalSeen = true; this.msg('✓ Gonadal ven tanımlandı', 'good'); this.ctx.audio?.tick(); }
        if (T.on && !st.ureterGrab) {
          const q = V(); const r = distToPolyline(T.p, A.ureterBaseSamples || (A.ureterBaseSamples = A.curves.ureter.pts.map(p => p.clone())), q);
          if (r.d < 0.007) { st.ureterGrab = { s: r.s, p0: T.p.clone().sub(st.ureterDelta), hand: T.hand }; this.ctx.audio?.tick(); }
        }
        if (st.ureterGrab && st.ureterGrab.hand === T.hand) {
          if (!T.on) { st.ureterGrab = null; }
          else {
            st.ureterDelta.copy(T.p).sub(st.ureterGrab.p0);
            if (st.ureterDelta.length() > 0.035) st.ureterDelta.setLength(0.035);
            this.applyUreter(st.ureterGrab.s);
            const lift = st.ureterDelta.length();
            if (lift > 0.01) st.ureterLiftT += dt; else st.ureterLiftT = 0;
          }
        }
      }
      if (st.gonadalSeen && st.ureterLiftT > 1.0) { this.msg('✓ Üreter askıya alındı (asistan tutuyor)', 'good'); st.ureterGrab = null; this.completeStep(); }
    }
    if (s.id === 'us') {
      for (const T of tools) {
        if (!T.active || T.tool !== 'us') continue;
        const d = T.k.distanceTo(tum.C);
        const ek = Math.sqrt((T.k.x / KID.AX) ** 2 + (T.k.y / KID.AY) ** 2 + (T.k.z / KID.AZ) ** 2);
        if (d < tum.R + 0.007 || (ek < 1.15 && T.k.distanceTo(tum.S) < 0.03)) st.usT += dt;
      }
      if (st.usT > 2) { st.usShown = true; this.showMarginRing(true); this.msg('US: 3.0 × 2.8 cm ekzofitik, heterojen solid kitle; toplayıcı sisteme ~5 mm. Rezeksiyon hattı işaretlendi.', 'good'); this.completeStep(); }
    }
    if (s.id === 'clamp') {
      for (const T of tools) {
        if (!T.active || T.tool !== 'bulldog' || !T.pressed) continue;
        const qa = V(), qv = V();
        const ra = distToPolyline(T.p, A.curves.renalArtery.pts, qa), rv = distToPolyline(T.p, A.curves.renalVein.pts, qv);
        if (ra.d < A.curves.renalArtery.r + 0.0045 && ra.s > 0.12 && ra.s < 0.9) {
          if (!this.exposed(this.pToW(qa.clone().add(V(0, 0.003, 0.002))), [A.meshes.fatHilar])) { this.msg('Arter yeterince diseke edilmemiş — klemp oturmuyor', 'warn'); continue; }
          this.placeClamp(qa, 'artery'); this.msg('✓ Renal arter klemplendi — WIT başladı', 'good'); this.completeStep(); return;
        } else if (rv.d < A.curves.renalVein.r + 0.004 && !st.veinClamp) {
          this.placeClamp(qv, 'vein'); this.m.venousClamp = true;
          this.msg('⚠ Renal ven klemplendi! Arter açıkken venöz konjesyon ve kanama artar. Arteri de klempleyin.', 'warn'); this.penalty(5, 'Arterden önce ven klemplendi');
        }
      }
    }
    if (s.id === 'excise') {
      for (const T of tools) {
        if (!T.active || T.tool !== 'scissors' || !T.on) continue;
        const v = T.k.clone().sub(tum.S); const h = v.dot(tum.n);
        const pl = v.clone().addScaledVector(tum.n, -h); const rho = pl.length();
        if (h > -0.016 && h < 0.008 && rho > tum.footprint * 0.55 && rho < tum.footprint + 0.02) {
          const ang = Math.atan2(pl.dot(tum.e2), pl.dot(tum.e1));
          const bin = ((Math.floor((ang + Math.PI) / (Math.PI * 2) * 36) % 36) + 36) % 36;
          if (st.bins[bin] === null) {
            st.bins[bin] = rho;
            const cm = new THREE.Mesh(new THREE.SphereGeometry(0.0011, 6, 5), this.cutMat);
            cm.position.copy(ellipProject(tum.S.clone().add(pl))); this.K.add(cm); (st.cutMarks ||= []).push(cm);
            this.ctx.audio?.snip();
            if (rho < tum.footprint) { if (!this.cooldown.tv || this.t - this.cooldown.tv > 2) { this.cooldown.tv = this.t; this.msg('⚠ Tümör kapsülüne çok yakın / içinde kesi!', 'warn'); } }
            if (!st.clamp && !st.offClampBleed) { st.offClampBleed = this.addBleed('parenchyma', this.toP(this.kToW(tum.S)), BLEED.parenchyma); this.m.offClamp = true; this.msg('Klempsiz eksizyon: parankimal kanama — aspiratör kullanın', 'warn'); }
            else if (st.clamp && !st.offClampBleed) { st.offClampBleed = this.addBleed('parenchyma', this.toP(this.kToW(tum.S)), BLEED.parenchyma); }
          }
        }
      }
      const done = st.bins.filter(b => b !== null).length;
      s.progress = done / 36;
      if (done >= 33) { this.doExcision(); this.completeStep(); }
    }
    if (s.id === 'inner') {
      for (const T of tools) {
        if (!T.active || T.tool !== 'needle' || !T.pressed) continue;
        const next = s.tg.find(t => !t.done);
        if (next && T.k.distanceTo(next.k) < 0.0065) { next.done = true; next.mk.visible = false; st.inner.push(next.k.clone()); this.drawInner(); this.ctx.audio?.tick(); }
        else if (next) this.msg('Sıradaki dikiş noktasını hedefleyin (yeşil)', 'info');
      }
      s.tg.forEach(t => { if (t.mk) t.mk.material.color.set(t === s.tg.find(x => !x.done) ? 0x33ff99 : 0x336644); });
      if (s.tg.every(t => t.done)) { this.msg('✓ İç kat tamamlandı — toplayıcı sistem ve medüller damarlar kapatıldı', 'good'); this.completeStep(); }
    }
    if (s.id === 'outer') {
      for (const T of tools) {
        if (!T.active || T.tool !== 'needle' || !T.pressed) continue;
        const pi = s.pairs.findIndex(p => !p.done);
        if (pi < 0) break;
        const P = s.pairs[pi];
        const tgt = P.aDone ? P.b : P.a;
        if (T.k.distanceTo(tgt.k) < 0.0065) {
          tgt.mk.visible = false; this.ctx.audio?.tick();
          if (!P.aDone) P.aDone = true; else { P.done = true; this.drawOuter(pi); this.ctx.audio?.click(); }
        }
      }
      if (s.pairs.every(p => p.done)) { this.fillCrater(); st.outerDone = true; this.msg('✓ Dış kat renorafi tamamlandı', 'good'); this.completeStep(); }
    }
    if (s.id === 'unclamp') {
      for (const T of tools) {
        if (!T.active || T.tool !== 'bulldog' || !T.pressed) continue;
        if (st.clamp && T.p.distanceTo(st.clamp.p) < 0.011) { this.removeClamp(); this.msg(`✓ Klemp alındı — WIT ${fmt(this.m.wit)}`, 'good'); }
        else if (st.veinClamp && T.p.distanceTo(st.veinClamp.p) < 0.009) { this.removeVeinClamp(); }
      }
      if (!st.clamp && !st.veinClamp) {
        const activeMajor = this.bleeds.some(b => b.active && b.key !== 'parenchyma');
        if (!activeMajor) this.completeStep(); else if (!this.cooldown.hem) { this.cooldown.hem = 1; this.msg('Aktif kanama odağı var — önce hemostaz sağlayın', 'warn'); }
      }
    }
    if (s.id === 'extract') {
      const sp = st.specimen; if (!sp) return;
      for (const T of tools) {
        if (!T.active || T.tool !== 'grasper') continue;
        if (!st.specGrab && T.pressed && T.p.distanceTo(sp.position) < tum.R + 0.008) { st.specGrab = T.hand; this.ctx.audio?.tick(); }
        if (st.specGrab === T.hand) {
          if (T.on) sp.position.lerp(T.p.clone().add(V(0, 0, -0.008)), 0.5);
          else {
            st.specGrab = null;
            if (sp.position.distanceTo(st.bagMouth) < 0.025) { sp.visible = false; st.bagDone = true; this.bag.scale.set(0.7, 0.7, 0.7); this.msg('✓ Spesimen endobag içinde', 'good'); this.completeStep(); }
          }
        }
      }
    }
  }

  applyUreter(sGrab = 0.45) {
    const A = this.A, st = this.state;
    const base = A.ureterBaseSamples || (A.ureterBaseSamples = A.curves.ureter.pts.map(p => p.clone()));
    const ctrl = base.map((p, i) => { const s = i / (base.length - 1); const w = Math.exp(-(((s - sGrab) / 0.16) ** 2)) * (s < 0.06 ? s / 0.06 : 1); return p.clone().addScaledVector(st.ureterDelta, w); });
    A.rebuildUreter(ctrl);
  }

  placeClamp(pP, kind) {
    const A = this.A, st = this.state;
    const c = makeBulldog(); c.scale.setScalar(1.0);
    const crv = kind === 'artery' ? A.curves.renalArtery : A.curves.renalVein;
    const { s } = distToPolyline(pP, crv.pts);
    const i = Math.min(crv.pts.length - 2, Math.floor(s * (crv.pts.length - 1)));
    const tan = crv.pts[i + 1].clone().sub(crv.pts[i]).normalize();
    c.position.copy(pP);
    c.quaternion.setFromUnitVectors(V(1, 0, 0), tan);
    if (kind === 'vein') c.scale.setScalar(1.5);
    A.root.add(c);
    const rec = { p: pP.clone(), obj: c };
    if (kind === 'artery') { st.clamp = rec; this.m.witStart = this.t; } else st.veinClamp = rec;
    this.ctx.audio?.click();
  }
  removeClamp() {
    const st = this.state; if (!st.clamp) return;
    st.clamp.obj.parent.remove(st.clamp.obj); this.m.witEnd = this.t; this.m.wit = this.t - this.m.witStart; st.clamp = null;
    if (st.veinClamp) this.removeVeinClamp();
    this.ctx.audio?.click();
  }
  removeVeinClamp() { const st = this.state; st.veinClamp.obj.parent.remove(st.veinClamp.obj); st.veinClamp = null; }

  showMarginRing(v) {
    const tum = this.A.land.tumor, st = this.state;
    if (st.ring) { st.ring.visible = v; return; }
    const pts = [];
    for (let i = 0; i <= 64; i++) {
      const a = i / 64 * Math.PI * 2;
      pts.push(ellipProject(tum.S.clone().addScaledVector(tum.e1, Math.cos(a) * (tum.footprint + 0.0035)).addScaledVector(tum.e2, Math.sin(a) * (tum.footprint + 0.0035))).addScaledVector(tum.n, 0.0004));
    }
    const ring = new THREE.Group();
    for (let i = 0; i < 64; i += 2) { const m = new THREE.Mesh(new THREE.SphereGeometry(0.0008, 6, 5), this.charMat); m.position.copy(pts[i]); ring.add(m); }
    this.K.add(ring); st.ring = ring;
  }

  doExcision() {
    const A = this.A, st = this.state, tum = A.land.tumor;
    if (st.excised) return; st.excised = true;
    const vals = st.bins.filter(b => b !== null);
    const rho = vals.reduce((a, b) => a + b, 0) / vals.length;
    const minR = Math.min(...vals);
    const fp = tum.footprint;
    this.m.margin = (rho - fp) * 1000; this.m.marginMin = (minR - fp) * 1000;
    if (minR < fp - 0.0005) { this.m.marginStatus = 'POZİTİF (tümör kapsülü ihlali)'; this.penalty(20, 'Pozitif cerrahi sınır riski'); }
    else if (this.m.margin > 8) { this.m.marginStatus = 'Negatif — gereksiz geniş eksizyon'; this.penalty(Math.min(12, Math.round(this.m.margin - 6)), 'Fazla parankim kaybı'); }
    else this.m.marginStatus = 'Negatif (ideal 2–5 mm)';
    // krater geometrisi
    const depth = 0.0105 + Math.max(0, rho - fp) * 0.5;
    const c = (rho * rho - depth * depth) / (2 * depth);
    const Rb = depth + c;
    const Cb = tum.S.clone().addScaledVector(tum.n, c);
    this.K.material.userData.u.uCrater.value.set(Cb.x, Cb.y, Cb.z, Rb);
    const bowlMat = tissueMaterial({ color: 0xb33a2e, roughness: 0.55, side: THREE.BackSide, map: A.tex.kidney }, { holes: false, deform: false });
    bowlMat.userData.u.uPlaneCut.value.set(0, 0, 0, 1);
    bowlMat.userData.u.uEllip.value.set(KID.AX * 0.998, KID.AY * 0.998, KID.AZ * 0.998);
    const bg = new THREE.SphereGeometry(Rb, 48, 32); bg.translate(Cb.x, Cb.y, Cb.z);
    const bowl = new THREE.Mesh(bg, bowlMat); this.K.add(bowl); st.bowl = { mesh: bowl, Cb, Rb, rho, depth };
    // yağ kalıntısını temizle
    addHole(A.meshes.fatPeri.material, tum.C, tum.R + 0.008);
    (st.cutMarks || []).forEach(m => m.visible = false);
    if (st.ring) st.ring.visible = false;
    // spesimen: tümör + parankim tabanı → hasta grubuna taşı
    const tumor = A.meshes.tumor;
    const spec = new THREE.Group();
    const wp = tumor.getWorldPosition(V()); const wq = tumor.getWorldQuaternion(new THREE.Quaternion());
    tumor.parent.remove(tumor); tumor.position.set(0, 0, 0); spec.add(tumor);
    const cap = new THREE.Mesh(new THREE.SphereGeometry(rho, 24, 12, 0, Math.PI * 2, Math.PI / 2, Math.PI / 2), new THREE.MeshStandardMaterial({ color: 0xa8402f, roughness: 0.5 }));
    cap.scale.set(1, depth / rho, 1);
    const capHolder = new THREE.Group(); capHolder.quaternion.setFromUnitVectors(V(0, 1, 0), tum.n); capHolder.position.copy(tum.n).multiplyScalar(-tum.exo); capHolder.add(cap);
    spec.add(capHolder);
    A.root.add(spec);
    spec.position.copy(this.toP(wp));
    spec.quaternion.copy(A.root.getWorldQuaternion(new THREE.Quaternion()).invert().multiply(wq));
    // spesimen yataktan kaldırılıp parakolik oluğa doğru bırakılır (defekt görünür kalsın)
    spec.position.lerp(V(0.112, -0.07, 0.045), 0.55);
    st.specimen = spec;
    this.ctx.audio?.click();
  }

  makeInnerTargets() {
    const st = this.state, s = this.step, tum = this.A.land.tumor;
    if (!st.bowl) return;
    const { Cb, Rb } = st.bowl;
    const pts = [[-0.006, -0.004], [-0.002, 0.004], [0.003, -0.004], [0.007, 0.003]];
    s.tg = pts.map(([a, b]) => {
      const q = Cb.clone().addScaledVector(tum.n, -Rb).addScaledVector(tum.e1, a).addScaledVector(tum.e2, b);
      const k = Cb.clone().add(q.sub(Cb).setLength(Rb * 0.96));
      return { k, p: this.toP(this.kToW(k)), done: false };
    });
    s.tg.forEach(t => { t.mk = this.marker(t.p); });
  }
  drawInner() {
    const st = this.state;
    if (st.innerMesh) { st.innerMesh.parent.remove(st.innerMesh); }
    const pts = this.step.tg ? this.step.tg.filter(t => t.done).map(t => t.k) : st.inner;
    if (pts.length < 2) return;
    const zig = [];
    pts.forEach((p, i) => { zig.push(p.clone()); if (i < pts.length - 1) zig.push(p.clone().lerp(pts[i + 1], 0.5).addScaledVector(this.A.land.tumor.n, 0.0025)); });
    st.innerMesh = new THREE.Mesh(taperTube(zig, 0.00045, 60, 6), this.sutureMat);
    this.K.add(st.innerMesh);
    st.inner = pts.map(p => p.clone());
  }
  makeOuterTargets() {
    const st = this.state, s = this.step, tum = this.A.land.tumor;
    if (!st.bowl) return;
    const a = st.bowl.rho + 0.004;
    s.pairs = [-0.0085, 0, 0.0085].map(k => {
      const mk = (sgn) => { const kk = ellipProject(tum.S.clone().addScaledVector(tum.e1, sgn * Math.sqrt(Math.max(0.0001, a * a - k * k * 0.3))).addScaledVector(tum.e2, k)); return { k: kk, p: this.toP(this.kToW(kk)) }; };
      const P = { a: mk(-1), b: mk(1), aDone: false, done: false };
      P.a.mk = this.marker(P.a.p, 0x33ff99); P.b.mk = this.marker(P.b.p, 0xffcc33);
      return P;
    });
  }
  drawOuter(i) {
    const P = this.step.pairs[i], tum = this.A.land.tumor;
    const mid = P.a.k.clone().lerp(P.b.k, 0.5).addScaledVector(tum.n, 0.003);
    const m = new THREE.Mesh(taperTube([P.a.k, mid, P.b.k], 0.0005, 30, 6), this.sutureMat); this.K.add(m);
    for (const e of [P.a.k, P.b.k]) { const c = makeHemolok(); c.position.copy(e).addScaledVector(tum.n, 0.001); c.quaternion.setFromUnitVectors(V(0, 1, 0), tum.n); this.K.add(c); }
    this.state.outer.push(i);
  }
  fillCrater() {
    const st = this.state, tum = this.A.land.tumor;
    if (!st.bowl || st.bolster) return;
    const b = new THREE.Mesh(new THREE.SphereGeometry(st.bowl.rho * 0.92, 24, 16), new THREE.MeshStandardMaterial({ color: 0xe9e4cf, roughness: 0.8 }));
    b.scale.set(1, 1, 0.45);
    b.position.copy(tum.S).addScaledVector(tum.n, -0.002);
    b.quaternion.setFromUnitVectors(V(0, 0, 1), tum.n);
    this.K.add(b); st.bolster = b;
  }
  makeBag() {
    const A = this.A, st = this.state;
    const g = new THREE.Group();
    const bag = new THREE.Mesh(new THREE.ConeGeometry(0.022, 0.06, 24, 1, true), new THREE.MeshStandardMaterial({ color: 0xd8e6f2, roughness: 0.3, transparent: true, opacity: 0.8, side: THREE.DoubleSide }));
    bag.rotation.x = Math.PI; bag.position.y = -0.03; g.add(bag);
    const ring = new THREE.Mesh(new THREE.TorusGeometry(0.022, 0.0012, 8, 32), new THREE.MeshStandardMaterial({ color: 0x2f7fd6, roughness: 0.4 })); ring.rotation.x = Math.PI / 2; g.add(ring);
    const mouth = V(0.112, -0.075, 0.05);
    g.position.copy(mouth); g.lookAt(this.pToW(mouth.clone().add(V(-0.3, 0.0, 1)))); g.rotateX(Math.PI / 2);
    A.root.add(g); this.bag = g; st.bagMouth = mouth;
    this.marker(mouth, 0x55ccff, 0.003);
  }

  // ---------------------------------------------------------------- US görüntüsü
  drawUltrasound(ctx2d, T) {
    const W = ctx2d.canvas.width, H = ctx2d.canvas.height, tum = this.A.land.tumor;
    const img = ctx2d.createImageData(W, H); const d = img.data;
    const k = T.k; // prob ucu böbrek-yerel
    const ek = Math.sqrt((k.x / KID.AX) ** 2 + (k.y / KID.AY) ** 2 + (k.z / KID.AZ) ** 2);
    const contact = ek < 1.25;
    // görüntü düzlemi: derinlik = -normal, lateral = alet ekseninin teğet izdüşümü
    const sp = ellipProject(k.clone());
    const nrm = V(sp.x / KID.AX ** 2, sp.y / KID.AY ** 2, sp.z / KID.AZ ** 2).normalize();
    const shaftK = this.K.worldToLocal(T.tipW.clone().add(T.dirW)).sub(k).normalize();
    const lat = shaftK.addScaledVector(nrm, -shaftK.dot(nrm)).normalize();
    const depthV = nrm.clone().negate();
    const widthM = 0.04, depthM = 0.05;
    const Cr = tum.C.clone().sub(sp);
    const planeN = lat.clone().cross(depthV);
    const dist = Cr.dot(planeN);
    const rIm = Math.abs(dist) < tum.R ? Math.sqrt(tum.R ** 2 - dist ** 2) : 0;
    const cx = Cr.dot(lat), cy = Cr.dot(depthV);
    const q = V();
    for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) {
      const i = (y * W + x) * 4;
      const ux = (x / W - 0.5) * widthM, uy = (y / H) * depthM;
      // sektör maskesi
      const fan = Math.abs(ux) < widthM * 0.5 * (0.55 + 0.45 * y / H);
      let v = 0;
      if (contact && fan) {
        q.copy(sp).addScaledVector(lat, ux).addScaledVector(depthV, uy);
        const e = Math.sqrt((q.x / KID.AX) ** 2 + (q.y / KID.AY) ** 2 + (q.z / KID.AZ) ** 2);
        const speck = Math.random();
        if (e < 1) {
          const cortex = e > 0.72 ? 1 : 0;
          v = cortex ? 95 + speck * 70 : (e < 0.4 ? 170 + speck * 80 : 55 + speck * 50); // korteks / sinüs yağı (hiperekoik) / medulla
          if (rIm > 0) { const dd = Math.hypot(ux - cx, uy - cy); if (dd < rIm) v = 120 + speck * 90 * (0.6 + 0.4 * Math.sin(ux * 900) * Math.cos(uy * 800)); if (Math.abs(dd - rIm) < 0.0008) v = 40; }
        } else {
          v = (uy < 0.002 ? 200 : 20) + speck * 25;
          if (rIm > 0) { const dd = Math.hypot(ux - cx, uy - cy); if (dd < rIm) v = 120 + speck * 90; if (Math.abs(dd - rIm) < 0.0008) v = 40; }
        }
        v *= 1 - 0.5 * (y / H);
      }
      d[i] = d[i + 1] = d[i + 2] = v; d[i + 3] = 255;
    }
    ctx2d.putImageData(img, 0, 0);
    ctx2d.fillStyle = '#9fe'; ctx2d.font = '14px monospace';
    ctx2d.fillText('L12-5 · 10 MHz · Derinlik 5 cm', 8, 18);
    if (!contact) ctx2d.fillText('Temas yok', 8, 36);
  }

  // ---------------------------------------------------------------- rapor
  report() {
    const m = this.m;
    let score = 100;
    for (const p of m.penalties) score -= p.points;
    if (m.wit > 25 * 60) { score -= 15; m.penalties.push({ points: 15, reason: 'WIT > 25 dk' }); }
    else if (m.wit > 20 * 60) { score -= 5; m.penalties.push({ points: 5, reason: 'WIT 20–25 dk' }); }
    if (m.bloodLoss > 500) { score -= 10; m.penalties.push({ points: 10, reason: 'Kan kaybı > 500 ml' }); }
    else if (m.bloodLoss > 250) { score -= 4; m.penalties.push({ points: 4, reason: 'Kan kaybı > 250 ml' }); }
    score = clamp(Math.round(score), 0, 100);
    const trifecta = m.marginStatus.startsWith('Negatif') && m.wit <= 25 * 60 && !m.ureterInjury && !m.bowelInjury && !m.injuries.some(x => ['renal ven', 'renal arter', 'aorta', 'VCI'].includes(x));
    return { score, totalTime: this.t, wit: m.wit, bloodLoss: m.bloodLoss, margin: m.margin, marginMin: m.marginMin, marginStatus: m.marginStatus, offClamp: m.offClamp,
      injuries: m.injuries, ureter: m.ureterInjury, bowel: m.bowelInjury, oov: m.energyOOV, penalties: m.penalties, steps: m.stepTimes, trifecta };
  }
}

export function fmt(sec) { sec = Math.max(0, sec | 0); return `${String(sec / 60 | 0).padStart(2, '0')}:${String(sec % 60).padStart(2, '0')}`; }
