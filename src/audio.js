// WebAudio ile sentetik sesler: koter, aspiratör, klik, alarm
export class Audio {
  constructor() { this.ctx = null; this.cau = [null, null]; }
  init() {
    if (this.ctx) { this.ctx.resume(); return; }
    try { this.ctx = new (window.AudioContext || window.webkitAudioContext)(); } catch { return; }
    const c = this.ctx;
    this.master = c.createGain(); this.master.gain.value = 0.5; this.master.connect(c.destination);
    const noiseBuf = c.createBuffer(1, c.sampleRate, c.sampleRate); const nd = noiseBuf.getChannelData(0);
    for (let i = 0; i < nd.length; i++) nd[i] = Math.random() * 2 - 1;
    this.noiseBuf = noiseBuf;
    for (let i = 0; i < 2; i++) {
      const o = c.createOscillator(); o.type = 'sawtooth'; o.frequency.value = 118 + i * 9;
      const bp = c.createBiquadFilter(); bp.type = 'bandpass'; bp.frequency.value = 900; bp.Q.value = 1.2;
      const g = c.createGain(); g.gain.value = 0;
      o.connect(bp).connect(g).connect(this.master); o.start();
      this.cau[i] = g;
    }
    const n = c.createBufferSource(); n.buffer = noiseBuf; n.loop = true;
    const lp = c.createBiquadFilter(); lp.type = 'lowpass'; lp.frequency.value = 700;
    this.suc = c.createGain(); this.suc.gain.value = 0;
    n.connect(lp).connect(this.suc).connect(this.master); n.start();
  }
  cautery(on, i) { if (!this.ctx) return; this.cau[i].gain.setTargetAtTime(on ? 0.12 : 0, this.ctx.currentTime, 0.02); }
  suction(on) { if (!this.ctx) return; this.suc.gain.setTargetAtTime(on ? 0.18 : 0, this.ctx.currentTime, 0.05); }
  beep(f, d, v = 0.15, type = 'sine') {
    if (!this.ctx) return; const c = this.ctx;
    const o = c.createOscillator(); o.type = type; o.frequency.value = f;
    const g = c.createGain(); g.gain.setValueAtTime(v, c.currentTime); g.gain.exponentialRampToValueAtTime(0.001, c.currentTime + d);
    o.connect(g).connect(this.master); o.start(); o.stop(c.currentTime + d);
  }
  click() { this.beep(1800, 0.05, 0.12, 'square'); }
  tick() { this.beep(1200, 0.06, 0.08); }
  snip() { this.beep(3200, 0.03, 0.05, 'triangle'); }
  alarm() { this.beep(880, 0.25, 0.15, 'square'); setTimeout(() => this.beep(660, 0.25, 0.15, 'square'), 260); }
}
