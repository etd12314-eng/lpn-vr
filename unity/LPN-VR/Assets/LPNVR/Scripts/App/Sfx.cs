// Sentetik sesler: koter (testere dişi + bant geçiren), aspiratör (alçak geçiren gürültü), klik/tik/makas/alarm — audio.js karşılığı.
using System.Collections.Generic;
using UnityEngine;

namespace LPNVR
{
    [RequireComponent(typeof(AudioSource))]
    public class Sfx : MonoBehaviour
    {
        class Beep { public float f, dur, vol, t; public int type; public float delay; }

        readonly float[] cauTarget = new float[2], cauGain = new float[2], cauPhase = new float[2];
        readonly float[] bpLow = new float[2], bpBand = new float[2];
        float sucTarget, sucGain, sucLp;
        readonly List<Beep> beeps = new List<Beep>();
        readonly object lk = new object();
        float sr = 48000;
        System.Random rng = new System.Random(7);
        const float Master = 0.5f;

        void Awake()
        {
            sr = AudioSettings.outputSampleRate;
            var src = GetComponent<AudioSource>();
            src.clip = AudioClip.Create("silence", 1024, 1, (int)sr, false);
            src.loop = true; src.spatialBlend = 0; src.volume = 1;
            src.Play();
        }

        public void Cautery(bool on, int i) => cauTarget[i] = on ? 0.12f : 0;
        public void Suction(bool on) => sucTarget = on ? 0.18f : 0;
        void Add(float f, float d, float v, int type, float delay = 0) { lock (lk) beeps.Add(new Beep { f = f, dur = d, vol = v, type = type, delay = delay }); }
        public void Click() => Add(1800, 0.05f, 0.12f, 1);
        public void Tick() => Add(1200, 0.06f, 0.08f, 0);
        public void Snip() => Add(3200, 0.03f, 0.05f, 2);
        public void Alarm() { Add(880, 0.25f, 0.15f, 1); Add(660, 0.25f, 0.15f, 1, 0.26f); }

        void OnAudioFilterRead(float[] data, int channels)
        {
            float dt = 1f / sr;
            // durum değişkenleri yumuşak geçiş (setTargetAtTime ~ 20/50 ms)
            float kc = 1 - Mathf.Exp(-dt / 0.02f), ks = 1 - Mathf.Exp(-dt / 0.05f);
            // bant geçiren (900 Hz, Q 1.2) — durum değişkenli filtre
            float fbp = 2 * Mathf.Sin(Mathf.PI * 900 / sr), qd = 1 / 1.2f;
            float alp = 1 - Mathf.Exp(-2 * Mathf.PI * 700 / sr);
            Beep[] bs; lock (lk) bs = beeps.ToArray();
            for (int n = 0; n < data.Length; n += channels)
            {
                float s = 0;
                for (int i = 0; i < 2; i++)
                {
                    cauGain[i] += (cauTarget[i] - cauGain[i]) * kc;
                    if (cauGain[i] < 1e-4f) continue;
                    cauPhase[i] += (118 + i * 9) * dt; if (cauPhase[i] > 1) cauPhase[i] -= 1;
                    float saw = 2 * cauPhase[i] - 1;
                    float hp = saw - bpLow[i] - qd * bpBand[i];
                    bpBand[i] += fbp * hp; bpLow[i] += fbp * bpBand[i];
                    s += bpBand[i] * cauGain[i];
                }
                sucGain += (sucTarget - sucGain) * ks;
                if (sucGain > 1e-4f)
                {
                    float noise = (float)rng.NextDouble() * 2 - 1;
                    sucLp += (noise - sucLp) * alp;
                    s += sucLp * sucGain;
                }
                foreach (var b in bs)
                {
                    if (b.delay > 0) { b.delay -= dt; continue; }
                    if (b.t >= b.dur) continue;
                    float ph = b.f * b.t;
                    float w = b.type == 0 ? Mathf.Sin(2 * Mathf.PI * ph) : b.type == 1 ? (ph % 1 < 0.5f ? 1 : -1) : 1 - 4 * Mathf.Abs((ph % 1) - 0.5f);
                    float env = b.vol * Mathf.Exp(Mathf.Log(0.001f / b.vol) * b.t / b.dur);
                    s += w * env;
                    b.t += dt;
                }
                s *= Master;
                for (int c = 0; c < channels; c++) data[n + c] += s;
            }
            lock (lk) beeps.RemoveAll(b => b.t >= b.dur);
        }
    }
}
