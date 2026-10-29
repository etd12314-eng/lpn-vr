// Başlıksız mantık testi (CI): uygulamayı Editor'da kurar, cerrahi moda geçer ve sağ eldeki hook ile
// 1. adımın (Toldt çizgisi) hedeflerine dokunup tetik basar; delik/hedef/adım ilerlemesini "LPNTEST" satırlarıyla yazar.
// Unity -batchmode -quit -executeMethod LPNVR.EditorTools.SelfTest.Run
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using static LPNVR.U;

namespace LPNVR.EditorTools
{
    public static class SelfTest
    {
        const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        static void Log(string s) => Debug.Log("LPNTEST " + s);
        static T Get<T>(object o, string f) => (T)o.GetType().GetField(f, F).GetValue(o);
        static void Call(object o, string m, params object[] a) => o.GetType().GetMethod(m, F).Invoke(o, a);

        public static void Run()
        {
            try { Scenario(); }
            catch (System.Exception e) { Log("EXCEPTION " + e); }
        }

        static void Scenario()
        {
            var go = new GameObject("LPN-VR-test");
            go.AddComponent<AudioSource>(); go.AddComponent<Sfx>();
            var app = go.AddComponent<LPNApp>();
            Call(app, "Start");
            Call(app, "SetMode", "surgery");
            var A = Get<Anatomy>(app, "A");
            var R = Get<Room>(app, "R");
            var proc = app.proc;
            Log($"step={proc.Cur.id} targets={proc.Cur.tg?.Count}");

            var lat = A.meshes["latPer"];
            var I = app.instr["right"]; var IL = app.instr["left"];
            Log($"rightTool={I.tool} latPer visible={lat.Visible} holesEnabled={lat.tissue != null}");
            var T = R.ports["right"].obj.position;
            var TL = R.ports["left"].obj.position;

            ToolState[] Tools(float trig, float prev) => new[]
            {
                new ToolState { hand = "left", tool = IL.tool, tipW = IL.tipWorld, dirW = IL.dir, trigger = 0, prevTrigger = 0, active = true },
                new ToolState { hand = "right", tool = I.tool, tipW = I.tipWorld, dirW = I.dir, trigger = trig, prevTrigger = prev, active = true },
            };
            // sol alet nötr
            IL.Update(TL - (A.root.TransformPoint(V(0.1f, 0, 0.02f)) - TL).N() * 0.25f, TL, Vector3.up, 0);

            var sb = new StringBuilder();
            int idx = 0;
            foreach (var tg in proc.Cur.tg.ToList())
            {
                var P = proc.PToW(tg.p);
                float dist = Vector3.Distance(P, T);
                // kol konumu: uç tam hedefe gelecek şekilde (fulkrum)
                var d = (P - T).N();
                float o = Instrument.SHAFT + I.TipOff - dist;
                var H = T - d * o;
                I.Update(H, T, Vector3.up, 1);
                float err = Vector3.Distance(I.tipWorld, P);
                int before = HoleCount(lat);
                float prev = 0;
                for (int f = 0; f < 20 && proc.stepIdx == 0; f++) { proc.Update(0.02f, Tools(1, prev)); prev = 1; }
                proc.Update(0.02f, Tools(0, 1));
                int after = HoleCount(lat);
                var lp = lat.transform.InverseTransformPoint(I.tipWorld);
                lat.BoundingSphere(out var bc, out var br);
                sb.Append($"\n  tg{idx}: portDist={dist:F3} reach={(o >= 0.04f && o <= Instrument.SHAFT - 0.012f ? "ok" : "OUT(o=" + o.ToString("F3") + ")")} tipErr={err * 1000:F1}mm near={lat.NearSurface(lp, 0.0065f)} inSphere={Vector3.Distance(lp, bc) <= br + 0.01f} holes {before}->{after} done={tg.done}");
                idx++;
                if (proc.stepIdx != 0) break;
            }
            Log("targets:" + sb);
            Log($"after: stepIdx={proc.stepIdx} step={proc.Cur?.id} colonAnim={proc.st.colonAnim} holes={HoleCount(lat)}");

            // gerçekçi el konumu: kalibrasyondaki nötr kol noktaları (uç odak noktasına yakın olmalı)
            var focusW = A.root.TransformPoint(V(0.075f, -0.01f, 0.015f));
            foreach (var k in new[] { "left", "right" })
            {
                var Tk = R.ports[k].obj.position; var dk = focusW - Tk;
                Log($"port {k}: world={Tk} focusDist={dk.magnitude:F3}");
            }
            Object.DestroyImmediate(go);
        }

        static int HoleCount(Part p)
        {
            var f = p.tissue.GetType().GetField("holes", F);
            return f != null ? ((System.Collections.ICollection)f.GetValue(p.tissue)).Count : -1;
        }
    }
}
