// Arayüz: VR içi canvas paneller + masaüstü HTML HUD
import * as THREE from 'three';
import { TOOLS } from './instruments.js';
import { fmt } from './sim.js';

export const CASE = {
  title: 'Vaka: Sol böbrek alt pol anterior kitle',
  lines: [
    '58 yaşında erkek, insidental saptanan sol renal kitle.',
    'BT: 3.0 cm, %60 ekzofitik, kontrastlanan solid lezyon (cT1a N0 M0).',
    'R.E.N.A.L. nefrometri: R1 + E1 + N2 + L1 = 5a (düşük kompleksite).',
    'Tek renal arter / tek renal ven. eGFR 88 ml/dk/1.73m².',
    'Plan: Transperitoneal laparoskopik parsiyel nefrektomi,',
    'sağ lateral dekübit 60°, 4 port (kamera 12 mm, 2×12 mm, asistan 5 mm).',
  ],
};

function wrap(g, text, x, y, maxW, lh) {
  const words = text.split(' '); let line = '';
  for (const w of words) {
    const t = line ? line + ' ' + w : w;
    if (g.measureText(t).width > maxW && line) { g.fillText(line, x, y); y += lh; line = w; } else line = t;
  }
  if (line) { g.fillText(line, x, y); y += lh; }
  return y;
}

export class Panel {
  constructor(w = 0.7, h = 0.7, px = 1024) {
    this.c = document.createElement('canvas'); this.c.width = px; this.c.height = Math.round(px * h / w);
    this.g = this.c.getContext('2d');
    this.tex = new THREE.CanvasTexture(this.c); this.tex.colorSpace = THREE.SRGBColorSpace;
    this.mesh = new THREE.Mesh(new THREE.PlaneGeometry(w, h), new THREE.MeshBasicMaterial({ map: this.tex, transparent: true }));
    this.mesh.userData.panel = this;
    this.buttons = []; this.hover = null;
  }
  begin(title) {
    const g = this.g, W = this.c.width, H = this.c.height;
    g.clearRect(0, 0, W, H);
    g.fillStyle = 'rgba(12,22,32,0.92)'; g.beginPath(); g.roundRect(0, 0, W, H, 28); g.fill();
    g.strokeStyle = '#3fb6e8'; g.lineWidth = 4; g.stroke();
    g.fillStyle = '#6fd3ff'; g.font = '700 40px Segoe UI, Arial'; g.fillText(title, 32, 62);
    this.buttons = [];
    return g;
  }
  button(id, label, x, y, w, h, active = false) {
    const g = this.g;
    g.fillStyle = this.hover === id ? '#2f7fb0' : active ? '#1f6f4a' : '#1d3346';
    g.beginPath(); g.roundRect(x, y, w, h, 14); g.fill();
    g.strokeStyle = '#6fd3ff'; g.lineWidth = 2; g.stroke();
    g.fillStyle = '#fff'; g.font = '600 30px Segoe UI, Arial'; g.textAlign = 'center'; g.textBaseline = 'middle';
    g.fillText(label, x + w / 2, y + h / 2); g.textAlign = 'left'; g.textBaseline = 'alphabetic';
    this.buttons.push({ id, x, y, w, h });
  }
  end() { this.tex.needsUpdate = true; }
  hit(uv) {
    const x = uv.x * this.c.width, y = (1 - uv.y) * this.c.height;
    return this.buttons.find(b => x >= b.x && x <= b.x + b.w && y >= b.y && y <= b.y + b.h)?.id || null;
  }
}

export class UI {
  constructor(app) {
    this.app = app;
    this.toasts = [];
    this.$ = (id) => document.getElementById(id);
    this.info = new Panel(0.78, 0.86, 1024);
    this.menu = new Panel(0.9, 0.62, 1200);
    this.atlas = new Panel(0.5, 0.62, 800);
  }
  toast(text, kind = 'info') {
    this.toasts.unshift({ text, kind, t: performance.now() });
    this.toasts = this.toasts.slice(0, 4);
    const el = document.createElement('div'); el.className = 'toast ' + kind; el.textContent = text;
    this.$('toasts').prepend(el); setTimeout(() => el.remove(), 6000);
    this.dirty = true;
  }
  stepChanged(proc) {
    const s = proc.step; if (!s) return;
    this.$('stepTitle').textContent = s.title;
    this.$('stepText').textContent = s.text;
    this.$('stepTools').textContent = 'Önerilen aletler: ' + s.tools;
    this.$('stepNum').textContent = `Adım ${proc.stepIdx + 1}/${proc.steps.length}`;
    this.dirty = true;
  }
  report(r) {
    const rows = [
      ['Toplam süre', fmt(r.totalTime)],
      ['Sıcak iskemi süresi (WIT)', r.offClamp && !r.wit ? 'Klempsiz (off-clamp)' : fmt(r.wit)],
      ['Tahmini kan kaybı', Math.round(r.bloodLoss) + ' ml'],
      ['Cerrahi sınır', r.marginStatus + (r.margin != null ? ` · ort. ${r.margin.toFixed(1)} mm, min ${r.marginMin.toFixed(1)} mm` : '')],
      ['Damar yaralanmaları', r.injuries.length ? r.injuries.join(', ') : 'Yok'],
      ['Üreter / kolon hasarı', `${r.ureter ? 'Üreter: VAR' : 'Üreter: yok'} · ${r.bowel ? 'Kolon: VAR' : 'Kolon: yok'}`],
      ['Görüş dışı enerji', r.oov + ' kez'],
      ['"Trifecta" (negatif sınır + WIT<25 dk + major komplikasyon yok)', r.trifecta ? 'SAĞLANDI ✓' : 'Sağlanamadı'],
    ];
    this.lastReport = { r, rows };
    const el = this.$('report');
    el.innerHTML = `<h2>Performans Raporu — Skor ${r.score}/100</h2><table>${rows.map(([a, b]) => `<tr><td>${a}</td><td>${b}</td></tr>`).join('')}</table>
      <h3>Kesintiler</h3><ul>${r.penalties.map(p => `<li>−${p.points}: ${p.reason}</li>`).join('') || '<li>Yok</li>'}</ul>
      <h3>Adım süreleri</h3><ul>${r.steps.map(s => `<li>${s.title}: ${fmt(s.time)}${s.skipped ? ' (atlandı)' : ''}</li>`).join('')}</ul>
      <button onclick="location.reload()">Yeniden başla</button>`;
    el.style.display = 'block';
    this.dirty = true;
  }

  // Masaüstü HUD güncellemesi
  hud(app) {
    const p = app.proc; if (!p) return;
    const m = p.m;
    this.$('mTime').textContent = fmt(p.t);
    this.$('mWit').textContent = p.state.clamp ? fmt(m.wit) : (m.witEnd ? fmt(m.wit) + ' ✓' : '—');
    this.$('mWit').className = m.wit > 25 * 60 ? 'bad' : m.wit > 20 * 60 ? 'warn' : '';
    this.$('mBlood').textContent = Math.round(m.bloodLoss) + ' ml';
    this.$('mBlood').className = m.bloodLoss > 500 ? 'bad' : m.bloodLoss > 250 ? 'warn' : '';
    const s = p.step;
    this.$('stepProg').style.width = s ? Math.round(100 * (s.progress ?? (s.tg ? s.tg.filter(t => t.done).length / s.tg.length : 0))) + '%' : '100%';
    for (const hand of ['left', 'right']) {
      const ins = app.instr[hand];
      this.$('tool-' + hand).innerHTML = `<b>${hand === 'left' ? 'Sol' : 'Sağ'} el${app.activeHand === hand && !app.xrActive ? ' (aktif)' : ''}:</b> ${TOOLS[ins.tool].tr}`;
      this.$('tool-' + hand).classList.toggle('active', app.activeHand === hand);
    }
  }

  // ---------------------------------------------------------------- VR panelleri
  drawInfo(app) {
    const p = app.proc, P = this.info, s = p?.step;
    const g = P.begin(s ? `Adım ${p.stepIdx + 1}/${p.steps.length}` : 'Rapor');
    const W = P.c.width;
    let y = 110;
    if (this.lastReport) {
      const { r, rows } = this.lastReport;
      g.fillStyle = '#fff'; g.font = '700 44px Segoe UI'; g.fillText(`Skor: ${r.score}/100`, 32, y); y += 60;
      g.font = '26px Segoe UI';
      for (const [a, b] of rows) { g.fillStyle = '#9cc'; y = wrap(g, a, 32, y, W - 64, 32); g.fillStyle = '#fff'; y = wrap(g, b, 52, y, W - 84, 32) + 6; }
    } else if (s) {
      g.fillStyle = '#fff'; g.font = '700 34px Segoe UI'; y = wrap(g, s.title, 32, y, W - 64, 42) + 8;
      g.font = '27px Segoe UI'; g.fillStyle = '#dde'; y = wrap(g, s.text, 32, y, W - 64, 35) + 8;
      g.fillStyle = '#ffd27a'; g.font = '600 26px Segoe UI'; y = wrap(g, 'Aletler: ' + s.tools, 32, y, W - 64, 32) + 10;
      const prog = s.progress ?? (s.tg ? s.tg.filter(t => t.done).length / s.tg.length : 0);
      g.fillStyle = '#234'; g.fillRect(32, y, W - 64, 16); g.fillStyle = '#3fd08a'; g.fillRect(32, y, (W - 64) * prog, 16); y += 50;
      g.font = '600 30px Segoe UI'; g.fillStyle = '#fff';
      g.fillText(`Süre ${fmt(p.t)}   WIT ${p.state.clamp || p.m.witEnd ? fmt(p.m.wit) : '—'}   Kan ${Math.round(p.m.bloodLoss)} ml`, 32, y); y += 46;
      g.font = '27px Segoe UI'; g.fillStyle = '#9fe';
      g.fillText(`Sol: ${TOOLS[app.instr.left.tool].tr}`, 32, y); y += 36;
      g.fillText(`Sağ: ${TOOLS[app.instr.right.tool].tr}`, 32, y); y += 46;
      g.font = '25px Segoe UI';
      for (const t of this.toasts.slice(0, 3)) { g.fillStyle = t.kind === 'bad' ? '#ff7a7a' : t.kind === 'warn' ? '#ffd27a' : t.kind === 'good' ? '#7affb0' : '#ccc'; y = wrap(g, t.text, 32, y, W - 64, 31) + 4; }
    }
    const H = P.c.height;
    if (app.vrMenuOpen || this.lastReport) {
      const bw = (W - 64 - 30) / 3;
      P.button('guide', 'Rehber ' + (p?.guide ? 'açık' : 'kapalı'), 32, H - 190, bw, 70, p?.guide);
      P.button('labels', 'Etiketler', 32 + bw + 15, H - 190, bw, 70, app.labelsOn);
      P.button('skip', 'Adımı atla', 32 + 2 * (bw + 15), H - 190, bw, 70);
      P.button('calib', 'Konumlandır', 32, H - 105, bw, 70);
      P.button('resume', 'Devam', 32 + bw + 15, H - 105, bw, 70);
      P.button('home', 'Ana menü', 32 + 2 * (bw + 15), H - 105, bw, 70);
    } else {
      g.fillStyle = '#8ab'; g.font = '24px Segoe UI';
      g.fillText('A/X: alet değiştir · Tetik: kullan · Sol çubuk: kamera · Sağ çubuk: zoom', 32, H - 70);
      g.fillText('B: etiketler · Y: menü · İki çubuğa bas: konumlandır', 32, H - 36);
    }
    P.end();
  }
  drawMenu(app) {
    const P = this.menu, g = P.begin('LPN-VR · Laparoskopik Parsiyel Nefrektomi Simülatörü');
    const W = P.c.width;
    g.fillStyle = '#ffd27a'; g.font = '600 32px Segoe UI'; g.fillText(CASE.title, 40, 130);
    g.fillStyle = '#dde'; g.font = '27px Segoe UI';
    CASE.lines.forEach((l, i) => g.fillText(l, 40, 180 + i * 38));
    const y = 450;
    P.button('atlas', 'Anatomi Atlası', 40, y, (W - 120) / 2, 110);
    P.button('surgery', 'Ameliyata Başla', 80 + (W - 120) / 2, y, (W - 120) / 2, 110);
    g.fillStyle = '#8ab'; g.font = '24px Segoe UI';
    g.fillText('Işınla seçip tetiğe basın. Ameliyatta kontrolcüler alet kollarıdır (trokar fulkrumu).', 40, y + 160);
    g.fillText('Başlarken ellerinizi rahat çalışma pozisyonunda tutun: hasta buna göre konumlanır.', 40, y + 196);
    P.end();
  }
  drawAtlas(app) {
    const P = this.atlas, g = P.begin('Anatomi Atlası');
    const W = P.c.width; const v = app.atlasVis;
    g.fillStyle = '#dde'; g.font = '24px Segoe UI';
    g.fillText('Grip ile modeli tutup çevirin, sağ çubuk: ölçek', 30, 100);
    const items = [['labels', 'Etiketler'], ['fat', 'Yağ / Gerota'], ['inner', 'Şeffaf böbrek'], ['bone', 'Kemikler'], ['gi', 'GİS organları'], ['muscle', 'Kaslar']];
    items.forEach(([id, lb], i) => P.button(id, lb, 30 + (i % 2) * ((W - 75) / 2 + 15), 130 + Math.floor(i / 2) * 100, (W - 75) / 2, 80, v[id]));
    P.button('home', 'Ana menü', 30, 450, W - 60, 80);
    P.end();
  }
}
