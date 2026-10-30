// Başlıksız ekran görüntüleri (CI): uygulamayı Editor'da kurar ve laparoskop / ameliyathane görünümlerini
// Shots/*.png olarak kaydeder; shader derleme hatalarını "LPNSHOT" satırlarıyla yazar.
// Unity -batchmode -quit -buildTarget Android -executeMethod LPNVR.EditorTools.Shots.Run
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using static LPNVR.U;

namespace LPNVR.EditorTools
{
    public static class Shots
    {
        const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        const string Dir = "Shots";
        static void Log(string s) => Debug.Log("LPNSHOT " + s);
        static T Get<T>(object o, string f) => (T)o.GetType().GetField(f, F).GetValue(o);
        static void Set(object o, string f, object v) => o.GetType().GetField(f, F).SetValue(o, v);
        static void Call(object o, string m, params object[] a) => o.GetType().GetMethod(m, F).Invoke(o, a);

        public static void Run()
        {
            try { CheckShaders(); Capture(); }
            catch (System.Exception e) { Log("EXCEPTION " + e); }
        }

        static void CheckShaders()
        {
            foreach (var n in new[] { "LPN/Tissue", "LPN/Unlit", "LPN/Points", "LPN/Monitor" })
            {
                var s = Shader.Find(n);
                if (!s) { Log($"shader {n}: BULUNAMADI"); continue; }
                var msgs = ShaderUtil.GetShaderMessages(s);
                Log($"shader {n}: hata={ShaderUtil.ShaderHasError(s)} mesaj={msgs.Length}");
                foreach (var m in msgs) Log($"  {m.severity} {m.platform} {m.file}:{m.line} {m.message}");
            }
        }

        static void Capture()
        {
            Directory.CreateDirectory(Dir);
            var go = new GameObject("LPN-VR-shots");
            go.AddComponent<AudioSource>(); go.AddComponent<Sfx>();
            var app = go.AddComponent<LPNApp>();
            Call(app, "Start");
            Call(app, "SetMode", "surgery");
            var A = Get<Anatomy>(app, "A");
            var proc = app.proc;
            var lapCam = Get<Camera>(app, "lapCam");
            var mainCam = Get<Camera>(app, "mainCam");

            void Lap(string name, Vector3 focusP, float dist)
            {
                // UpdateLapCam hedefi adım odağı + camOffset'tir
                Set(app, "camTarget", focusP); Set(app, "camOffset", focusP - proc.Cur.focus.Value); Set(app, "camDist", dist);
                Call(app, "UpdateLapCam", 30f);
                Render(lapCam, name, 1280, 720);
            }

            // 1) başlangıç: Toldt çizgisi, inen kolon, lateral periton
            Lap("01_toldt", proc.Cur.focus.Value, proc.Cur.camDist);
            // 2) kolon medialize, Gerota / perirenal yağ
            proc.st.colonAnim = true;
            for (int i = 0; i < 200; i++) proc.Update(0.02f, new ToolState[0]);
            Lap("02_gerota", V(0.12f, -0.02f, 0.03f), 0.13f);
            // 3) hilus (yağ kaldırılmış)
            A.meshes["fatHilar"].gameObject.SetActive(false);
            Lap("03_hilum", V(0.05f, 0.002f, 0.028f), 0.09f);
            // 4) böbrek + tümör (perirenal yağ kaldırılmış)
            A.meshes["fatPeri"].gameObject.SetActive(false);
            var tumP = proc.ToP(proc.KToW(A.tumor.C));
            Lap("04_tumor", tumP, 0.08f);
            Lap("05_kidney", V(0.085f, -0.01f, 0.02f), 0.13f);
            // 6) ameliyathane (baş hizasından)
            // masaüstü "O" görünümüyle aynı yörünge kamerası
            var orT = A.root.TransformPoint(V(0.06f, -0.02f, 0.1f));
            mainCam.transform.position = orT + new Vector3(0, 0.5f, -0.9f);
            mainCam.transform.LookAt(orT);
            Render(mainCam, "06_room", 1280, 720);
            // VR başlangıç başı (orijinde ayakta, +Z'ye bakıyor)
            mainCam.transform.position = new Vector3(0, 1.6f, 0);
            mainCam.transform.LookAt(new Vector3(0, 1.0f, 0.55f));
            Render(mainCam, "07_head", 1280, 720);
            // uzaktan genel bakış (oda tanısı)
            mainCam.transform.position = new Vector3(1.8f, 2.6f, -1.6f);
            mainCam.transform.LookAt(A.root.position);
            Render(mainCam, "08_far", 1280, 720);
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.gameObject.layer == 0 && r.enabled && r.gameObject.activeInHierarchy).OrderByDescending(r => r.bounds.size.magnitude).Take(8))
                Log($"oda nesnesi {r.name} merkez={r.bounds.center} boyut={r.bounds.size} mat={r.sharedMaterial?.shader?.name}");
            Log($"mainCam near={mainCam.nearClipPlane} far={mainCam.farClipPlane} mask={mainCam.cullingMask} R.root={Get<Room>(app, "R").root.position} patient={A.root.position}");
            Object.DestroyImmediate(go);
            Log("tamam: " + string.Join(", ", Directory.GetFiles(Dir).Select(Path.GetFileName)));
        }

        static void Render(Camera cam, string name, int w, int h)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var prev = cam.targetTexture; var prevAspect = cam.aspect;
            cam.targetTexture = rt; cam.aspect = (float)w / h;
            cam.Render();
            var old = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = old;
            cam.targetTexture = prev; cam.aspect = prevAspect;
            File.WriteAllBytes(Path.Combine(Dir, name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt);
            Log("kaydedildi " + name);
        }
    }
}
