// Masaüstü giriş yolunu kullanan otomatik oynatma testi (fare simülasyonu).
// Tarayıcı konsolunda:  const t = await import('/tests/autoplay.js'); await t.autoplay({ shots: true })
export async function autoplay({ shots = false, from = 0 } = {}) {
  const app = window.app, { desk, lapCam, run, THREE, shot } = app.dbg;
  if (app.mode !== 'surgery') document.getElementById('btnSurgery').click();
  const P = app.proc, A = app.A;
  const V = (x, y, z) => new THREE.Vector3(x, y, z);
  const key = (k) => { dispatchEvent(new KeyboardEvent('keydown', { key: k })); dispatchEvent(new KeyboardEvent('keyup', { key: k })); };
  const ndc = (p) => { run(1); const v = A.root.localToWorld(p.clone()).project(lapCam); desk.ndc.set(v.x, v.y); };
  const aim = (p, n = 8, hold = true) => { ndc(p); run(2); desk.down = hold; run(n); desk.down = false; run(2); };
  const hit = (n = 6) => { for (const t of (P.step.tg || [])) if (!t.done) aim(t.p, n); };
  const settle = () => run(90);
  const snap = async (n) => { if (shots) await shot(n); };
  const log = [];
  const mark = (s) => log.push(`${s} → ${P.step?.id ?? 'bitti'}`);
  run(30);
  for (let i = 0; i < from; i++) { P.completeStep(true); settle(); }

  // 1 Toldt
  if (P.step.id === 'toldt') { key('e'); key('1'); await snap('p1'); hit(); hit(); settle(); mark('toldt'); }
  // 2 Üreter
  if (P.step.id === 'ureter') {
    key('q'); key('1');
    ndc(A.curves.gonadal.pts.find(p => p.y < -0.05)); run(10);
    ndc(A.curves.ureter.pts.find(p => p.y < -0.045)); run(3); desk.down = true; run(3);
    log.push('ureterGrab=' + !!P.state.ureterGrab + ' hit=' + app.lastHit);
    for (let i = 0; i < 30; i++) { desk.ndc.y += 0.006; run(1); }
    run(50); await snap('p2'); desk.down = false; run(5); settle(); mark('ureter');
  }
  // 3 Hilus
  if (P.step.id === 'hilum') { key('e'); key('1'); hit(); hit(); hit(); await snap('p3'); settle(); mark('hilum'); }
  // 4 Defat
  if (P.step.id === 'defat') { key('e'); key('1'); hit(); hit(); hit(); await snap('p4'); settle(); mark('defat'); }
  const tum = A.land.tumor;
  // 5 US
  if (P.step.id === 'us') { key('e'); key('8'); aim(P.toP(P.kToW(tum.C.clone().addScaledVector(tum.n, tum.R))), 80, false); await snap('p5'); settle(); mark('us'); }
  // 6 Klemp
  if (P.step.id === 'clamp') { key('e'); key('4'); for (const p of P.step.clampPts) { if (P.step.id === 'clamp') aim(p, 4); } settle(); await snap('p6'); mark('clamp'); }
  // 7 Eksizyon: işaretli hattın üzerinde 360° makasla
  if (P.step.id === 'excise') {
    key('e'); key('2');
    for (let a = 0; a < 2 * Math.PI + 0.2; a += Math.PI / 24) {
      const q = tum.S.clone().addScaledVector(tum.e1, Math.cos(a) * (tum.footprint + 0.0035)).addScaledVector(tum.e2, Math.sin(a) * (tum.footprint + 0.0035));
      const k = Math.sqrt((q.x / 0.03) ** 2 + (q.y / 0.055) ** 2 + (q.z / 0.0175) ** 2); q.divideScalar(k);
      aim(P.toP(P.kToW(q)), 3);
    }
    await snap('p7'); settle(); mark('excise');
  }
  // 8 İç kat
  if (P.step.id === 'inner') { key('e'); key('5'); for (let i = 0; i < 4; i++) { const t = P.step.tg?.find(x => !x.done); if (t) aim(t.p, 3); } await snap('p8'); settle(); mark('inner'); }
  // 9 Dış kat
  if (P.step.id === 'outer') { key('e'); key('5'); for (const pr of P.step.pairs) { aim(pr.a.p, 3); aim(pr.b.p, 3); } await snap('p9'); settle(); mark('outer'); }
  // 10 Klemp kaldır
  if (P.step.id === 'unclamp') { key('e'); key('4'); if (P.state.clamp) aim(P.state.clamp.p.clone().add(V(0, 0.0035, 0.002)), 3); settle(); await snap('p10'); mark('unclamp'); }
  // 11 Endobag
  if (P.step.id === 'extract') {
    key('q'); key('1');
    const sp = P.state.specimen;
    ndc(sp.position); run(2); desk.down = true; run(4);
    const v0 = desk.ndc.clone();
    const m = A.root.localToWorld(P.state.bagMouth.clone()).project(lapCam);
    for (let i = 1; i <= 30; i++) { desk.ndc.set(v0.x + (m.x - v0.x) * i / 30, v0.y + (m.y - v0.y) * i / 30); run(1); }
    run(5); await snap('p11'); desk.down = false; run(5); mark('extract');
  }
  return { log, finished: !!P.finished, report: P.finished ? P.report() : null, injuries: (P.injLog || []).map(i => `${i.k}@${i.step} (${i.hit})`), messages: P.messages.map(m => m.text) };
}
