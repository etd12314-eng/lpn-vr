// Cerrahi prosedür motoru: adımlar, diseksiyon, kanama, iskemi, renorafi, skor — sim.js'in birebir karşılığı.
// Konum kuralları: tipW = dünya; p = hasta-yerel (A.root); k = böbrek-yerel (K).
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static LPNVR.U;

namespace LPNVR
{
    public interface IProcHost
    {
        Camera LapCam { get; }
        bool IsVisible(Vector3 worldPos);
        void Toast(string text, string kind);
        void StepChanged(Procedure p);
        void ShowReport(Procedure.Report r);
        Sfx Audio { get; }
    }

    public class ToolState
    {
        public string hand, tool;
        public Vector3 tipW, dirW, p, k;
        public float trigger, prevTrigger;
        public bool active = true, on, pressed;
    }

    public class Target
    {
        public Vector3 p, k; public bool done; public string kind; public Part mk;
    }

    public class SuturePair { public Target a, b; public bool aDone, done; }

    public class Step
    {
        public string id, title, tools, text;
        public Vector3? focus; public float camDist = 0.11f;
        public Func<List<Target>> targets;
        public Func<Target, bool> check;
        public float t0; public List<Target> tg; public float? progress;
        public List<Vector3> clampPts; public List<SuturePair> pairs;
    }

    public class Bleed
    {
        public string key; public Vector3 pos; public float rate, vol, bip; public bool active = true, major, needClip; public Part pool;
    }

    public class Procedure
    {
        // Yaklaşık kanama hızları (ml/s) — simülasyon amaçlı
        static readonly Dictionary<string, float> BLEED = new Dictionary<string, float>
        { ["renalVein"] = 9, ["renalArtery"] = 14, ["aorta"] = 45, ["ivc"] = 35, ["gonadal"] = 1.6f, ["adrenalVein"] = 1.2f, ["lumbarVein"] = 2.8f, ["parenchyma"] = 5.5f };
        public static readonly Dictionary<string, string> TR_NAMES = new Dictionary<string, string>
        { ["renalVein"] = "renal ven", ["renalArtery"] = "renal arter", ["aorta"] = "aorta", ["ivc"] = "VCI", ["gonadal"] = "gonadal ven", ["adrenalVein"] = "adrenal ven", ["lumbarVein"] = "lomber ven", ["parenchyma"] = "parankim" };
        static readonly string[] MAJOR = { "renalVein", "renalArtery", "aorta", "ivc" };

        static Vector3 EllipProject(Vector3 p) // böbrek-yerel noktayı elipsoid yüzeyine (merkezden ışınla) taşır
        {
            float k = Mathf.Sqrt(Sq(p.x / KID.AX) + Sq(p.y / KID.AY) + Sq(p.z / KID.AZ));
            return p / k;
        }
        static float Sq(float x) => x * x;
        static float Ek(Vector3 k) => Mathf.Sqrt(Sq(k.x / KID.AX) + Sq(k.y / KID.AY) + Sq(k.z / KID.AZ));

        public class Metrics
        {
            public float bloodLoss, wit; public float? witStart, witEnd, margin, marginMin;
            public List<string> injuries = new List<string>(); public bool ureterInjury, bowelInjury, offClamp, venousClamp;
            public string marginStatus = "-"; public int energyOOV;
            public List<(int points, string reason)> penalties = new List<(int, string)>();
            public List<(string id, string title, float time, bool skipped)> stepTimes = new List<(string, string, float, bool)>();
            public List<string> skipped = new List<string>();
        }

        public class ClampRec { public Vector3 p; public GameObject obj; }
        public class Bowl { public Part mesh; public Vector3 Cb; public float Rb, rho, depth; }

        public class State
        {
            public float colonK; public bool colonAnim; public Vector3 ureterDelta; public (float s, Vector3 p0, string hand)? ureterGrab; public float ureterLiftT;
            public bool gonadalSeen, veinMsg; public ClampRec clamp, veinClamp; public float usT; public bool usShown;
            public float?[] bins = new float?[36]; public bool excised; public List<Vector3> inner = new List<Vector3>(); public List<int> outer = new List<int>();
            public bool outerDone; public Transform specimen; public string specGrab; public bool bagDone; public Vector3 bagMouth;
            public float ischemia, congestion; public Bleed offClampBleed; public Transform ring; public List<GameObject> cutMarks = new List<GameObject>();
            public Bowl bowl; public Part innerMesh, bolster;
        }

        public class Report
        {
            public int score; public float totalTime, wit, bloodLoss; public float? margin, marginMin; public string marginStatus; public bool offClamp;
            public List<string> injuries; public bool ureter, bowel, trifecta; public int oov;
            public List<(int points, string reason)> penalties; public List<(string id, string title, float time, bool skipped)> steps;
        }

        readonly IProcHost ctx;
        public readonly Anatomy A;
        readonly Part K;
        public float t;
        public int stepIdx;
        public bool guide = true, finished;
        public readonly Metrics m = new Metrics();
        public readonly State st = new State();
        public readonly List<Bleed> bleeds = new List<Bleed>();
        readonly Dictionary<string, float> cooldown = new Dictionary<string, float>();
        public List<Step> steps;
        public readonly Transform markers;
        ParticleSys drops, smoke;
        Material poolMat, charMat, cutMat, sutureMat;
        Transform bag;

        public Step Cur => stepIdx < steps.Count ? steps[stepIdx] : null;

        public Procedure(IProcHost ctx, Anatomy A)
        {
            this.ctx = ctx; this.A = A; K = A.K;
            BuildFx();
            BuildSteps();
            markers = Group("markers", A.root).transform;
            EnterStep(0);
        }

        // ---------------------------------------------------------------- yardımcılar
        public void Msg(string text, string kind = "info") { ctx.Toast(text, kind); if (kind == "bad") ctx.Audio?.Alarm(); }
        void Penalty(int points, string reason) => m.penalties.Add((points, reason));
        public Vector3 ToP(Vector3 w) => A.root.InverseTransformPoint(w);
        public Vector3 ToK(Vector3 w) => K.transform.InverseTransformPoint(w);
        public Vector3 PToW(Vector3 p) => A.root.TransformPoint(p);
        public Vector3 KToW(Vector3 p) => K.transform.TransformPoint(p);
        bool Cool(string key, float dt) { if (cooldown.TryGetValue(key, out var c) && t - c < dt) return false; cooldown[key] = t; return true; }
        bool Once(string key) { if (cooldown.ContainsKey(key)) return false; cooldown[key] = 1; return true; }

        Part Marker(Vector3 posP, uint color = 0x33ff99, float size = 0.0018f)
        {
            var mk = Part.Make("marker", Geo.Sphere(size, 10, 8), Mats.Unlit(U.Hex(color, 0.85f), true), markers);
            mk.transform.localPosition = posP;
            return mk;
        }
        void ClearMarkers() { foreach (Transform c in markers) UnityEngine.Object.Destroy(c.gameObject); }

        // Kamera → hedef doğrultusu üzerindeki tüm (diseke edilebilir) yüzey kesişimleri delikteyse hedef "açığa çıkmıştır"
        public bool Exposed(Vector3 targetW, params Part[] meshes)
        {
            var cam = ctx.LapCam.transform.position;
            var dir = targetW - cam; float dist = dir.magnitude; dir /= dist;
            var hits = new List<Part.Hit>();
            foreach (var mm in meshes)
            {
                if (!mm.Visible) continue;
                hits.Clear(); mm.Raycast(cam, dir, 0, dist - 0.0005f, hits);
                foreach (var h in hits) if (!mm.tissue.InHole(h.localPoint, 0.0006f)) return false;
            }
            return true;
        }

        // ---------------------------------------------------------------- efektler
        void BuildFx()
        {
            var dm = new Material(Mats.PointsShader); dm.SetFloat("_Size", 0.0026f); dm.SetFloat("_Mode", 0);
            drops = new ParticleSys("drops", A.root, 400, dm);
            var sm = new Material(Mats.PointsShader); sm.SetFloat("_Size", 0.012f); sm.SetFloat("_Mode", 1); sm.SetColor("_Color", new Color(0.72f, 0.72f, 0.72f, 0.45f));
            smoke = new ParticleSys("smoke", A.root, 120, sm);
            poolMat = Mats.Std(new MatOpts { color = 0x5a0303, roughness = 0.08f, metallic = 0.1f, transparent = true, opacity = 0.92f });
            charMat = Mats.Std(0x2a1a12, 0.9f);
            cutMat = Mats.Std(0x5c0b0b, 0.5f);
            sutureMat = Mats.Std(0x6b3bd1, 0.4f);
        }

        Bleed AddBleed(string key, Vector3 posP, float rate, bool major = false, bool needClip = false)
        {
            var pool = Part.Make("pool", Geo.Sphere(1, 20, 12), poolMat, A.root);
            pool.transform.localScale = Vector3.one * 0.0001f; pool.transform.localPosition = posP;
            var b = new Bleed { key = key, pos = posP, rate = rate, pool = pool, major = major, needClip = needClip };
            bleeds.Add(b); return b;
        }

        // ---------------------------------------------------------------- adımlar
        void BuildSteps()
        {
            var tum = A.tumor;
            var crv = A.curves;
            Vector3 PtOn(string name, float x, Vector3 off)
            { // eğri üzerinde verilen x'e en yakın nokta + ofset
                Vector3 best = crv[name].pts[0]; float bd = 1;
                foreach (var p in crv[name].pts) { float d = Mathf.Abs(p.x - x); if (d < bd) { bd = d; best = p; } }
                return best + off;
            }
            steps = new List<Step>
            {
                new Step { id = "toldt", title = "1. Toldt çizgisi insizyonu ve kolon medializasyonu", tools = "Sağ: Hook koter · Sol: Grasper (karşı traksiyon)",
                    text = "İnen kolonun lateralindeki Toldt beyaz çizgisini splenik fleksuradan pelvik girime doğru hook ile açın. Gerota fasyası ile mezokolon arasındaki avasküler planda (Toldt fasyası) kalın. Kolona termal temas = barsak hasarı.",
                    focus = V(0.135f, -0.02f, 0.03f), camDist = 0.13f,
                    targets = () => A.toldt.Select(p => new Target { p = p }).ToList(),
                    check = tg => A.meshes["latPer"].tissue.InHole(tg.p, 0.0025f) },
                new Step { id = "ureter", title = "2. Gonadal ven ve üreterin tanımlanması", tools = "Sol: Grasper · Sağ: Hook / Aspiratör",
                    text = "Psoas üzerinde medialdeki gonadal veni grasper ile gösterin (temas). Hemen lateral-posteriorundaki üreteri kavrayıp (tetik) anterolaterale ≥1 cm kaldırın ve 1 sn tutun. Üretere enerji uygulamayın.",
                    focus = V(0.05f, -0.075f, 0.03f) },
                new Step { id = "hilum", title = "3. Hiler diseksiyon (VAP: Ven–Arter–Pelvis)", tools = "Sağ: Hook · Sol: Grasper / Aspiratör",
                    text = "Gonadal veni renal vene kadar takip edin. Hiler yağı hook ile diseke ederek önce renal veni, sonra venin posterior-süperiorundaki renal arteri ortaya koyun. Dikkat: gonadal, adrenal ve lomber ven dalları.",
                    focus = V(0.05f, 0.002f, 0.028f),
                    targets = () => new[] { 0.043f, 0.05f, 0.057f }.Select(x => new Target { p = PtOn("renalVein", x, V(0, 0, 0.0062f)), kind = "vein" })
                        .Concat(new[] { 0.045f, 0.053f }.Select(x => new Target { p = PtOn("renalArtery", x, V(0, 0.0038f, 0.002f)), kind = "artery" })).ToList(),
                    check = tg => Exposed(PToW(tg.p), A.meshes["fatHilar"]) },
                new Step { id = "defat", title = "4. Gerota fasyasının açılması ve tümör çevresinin defatlanması", tools = "Sağ: Hook / Makas · Sol: Grasper",
                    text = "Gerota fasyasını açın, perirenal yağı tümörün etrafında ~1 cm'lik sağlıklı parankim görünene kadar temizleyin. Tümör üzerindeki yağı spesimen ile bırakmak onkolojik olarak tercih edilebilir; burada görünürlük için tümör yüzeyi de açılır.",
                    targets = () =>
                    {
                        var o = new List<Target>();
                        for (int i = 0; i < 8; i++)
                        {
                            float a = i / 8f * Mathf.PI * 2;
                            var q = EllipProject(tum.S + tum.e1 * (Mathf.Cos(a) * (tum.footprint + 0.008f)) + tum.e2 * (Mathf.Sin(a) * (tum.footprint + 0.008f)));
                            o.Add(new Target { k = q + tum.n * 0.002f });
                        }
                        o.Add(new Target { k = tum.C + tum.n * (tum.R + 0.001f) });
                        foreach (var x in o) x.p = ToP(KToW(x.k));
                        return o;
                    },
                    check = tg => A.meshes["fatPeri"].tissue.InHole(tg.k, 0.004f) || Exposed(PToW(tg.p), A.meshes["fatPeri"]) },
                new Step { id = "us", title = "5. İntraoperatif laparoskopik ultrasonografi", tools = "Sağ veya sol: US probu",
                    text = "US probunu tümör ve çevre parankim üzerine yerleştirip 2 sn temas halinde tutun. Tümör derinliği ve toplayıcı sisteme uzaklık değerlendirilir; 3–5 mm'lik rezeksiyon hattı koterle işaretlenmiş olarak gösterilecek." },
                new Step { id = "clamp", title = "6. Renal arterin bulldog ile klemplenmesi", tools = "Sağ: Bulldog aplikatörü",
                    text = "Bulldog klempi ana renal artere (dallanma öncesi) uygulayın. Sıcak iskemi süresi (WIT) başlar — hedef < 25 dk. Yalnız ven klemplemesi venöz konjesyona ve kanamaya yol açar. (Klempsiz \"off-clamp\" eksizyon da mümkündür; kanama artar.)" },
                new Step { id = "excise", title = "7. Tümör eksizyonu (soğuk makas)", tools = "Sağ: Soğuk makas · Sol: Aspiratör / Grasper",
                    text = "İşaretli hattan, 2–5 mm sağlıklı parankim sınırı bırakarak tümör çevresinde 360° kesin. Tümör kapsülüne girmek pozitif cerrahi sınır riskidir; gereksiz geniş eksizyon parankim kaybıdır." },
                new Step { id = "inner", title = "8. İç kat renorafi (medüller kat)", tools = "Sağ: Portegü (V-Loc) · Sol: Grasper",
                    text = "Defekt tabanında açılmış toplayıcı sistem ve ana intrarenal damarları sürekli dikişle kapatın: 4 noktayı sırayla portegü ile geçin." },
                new Step { id = "outer", title = "9. Dış kat renorafi (kayan Hem-o-lok tekniği)", tools = "Sağ: Portegü",
                    text = "Her dikiş için önce bir kortikal kenarı, sonra karşı kenarı geçin; dikiş Hem-o-lok kliplerle gerilip sabitlenir (sliding-clip). 3 dikiş gerekir." },
                new Step { id = "unclamp", title = "10. Klempin kaldırılması ve hemostaz", tools = "Sağ: Bulldog aplikatörü · Sol: Aspiratör",
                    text = "Bulldog aplikatörü ile klempi alın, WIT durur. Böbreğin reperfüzyonunu ve renorafi hattında aktif kanama olmadığını kontrol edin." },
                new Step { id = "extract", title = "11. Spesimenin endobag ile alınması", tools = "Sol: Grasper",
                    text = "Grasper ile spesimeni kavrayın (tetik basılı) ve parakolik oluktaki endobag ağzına getirip bırakın.",
                    focus = V(0.11f, -0.07f, 0.035f) },
            };
        }

        public void EnterStep(int i)
        {
            stepIdx = i; ClearMarkers();
            var s = Cur; if (s == null) return;
            s.t0 = t;
            if (s.targets != null) { s.tg = s.targets(); foreach (var tg in s.tg) tg.mk = Marker(tg.p); }
            var tum = A.tumor;
            if (s.focus == null) s.focus = ToP(KToW(tum.C));
            if (s.id == "clamp")
            {
                // hiler diseksiyonda açılan arter segmenti
                var a = A.curves["renalArtery"].pts;
                Vector3 Near(float x) => a.Aggregate(a[0], (b, p) => Mathf.Abs(p.x - x) < Mathf.Abs(b.x - x) ? p : b);
                s.clampPts = new[] { 0.045f, 0.049f, 0.053f }.Select(x => Near(x) + V(0, 0.0035f, 0.002f)).ToList();
                s.focus = Near(0.049f);
            }
            if (s.id == "unclamp" && st.clamp != null) { s.focus = st.clamp.p; Marker(st.clamp.p + V(0, 0.0035f, 0.002f), 0xffcc33); }
            if (s.id == "us") Marker(ToP(KToW(tum.C + tum.n * tum.R)), 0x55ccff, 0.0025f);
            if (s.id == "clamp") foreach (var p in s.clampPts) Marker(p, 0xffcc33);
            if (s.id == "excise") ShowMarginRing(true);
            if (s.id == "inner") MakeInnerTargets();
            if (s.id == "outer") MakeOuterTargets();
            if (s.id == "extract") MakeBag();
            markers.gameObject.SetActive(guide);
            ctx.StepChanged(this);
            ctx.Audio?.Click();
        }

        public void CompleteStep(bool skipped = false)
        {
            var s = Cur;
            m.stepTimes.Add((s.id, s.title, t - s.t0, skipped));
            if (skipped) { m.skipped.Add(s.title); Penalty(5, "Adım atlandı: " + s.title); ForceComplete(s.id); }
            if (stepIdx + 1 < steps.Count)
            {
                Msg("✓ " + System.Text.RegularExpressions.Regex.Replace(s.title, @"^\d+\. ", "") + " tamamlandı", "good");
                EnterStep(stepIdx + 1);
            }
            else Finish();
        }

        void ForceComplete(string id)
        {
            var s = Cur;
            if (id == "toldt") st.colonAnim = true;
            if (id == "ureter") { st.ureterDelta = V(0, 0, 0.012f); ApplyUreter(); }
            if (id == "hilum" || id == "defat")
            {
                var mesh = id == "hilum" ? A.meshes["fatHilar"] : A.meshes["fatPeri"];
                if (s.tg != null) foreach (var tg in s.tg) mesh.tissue.AddHole(mesh.transform.InverseTransformPoint(PToW(tg.p)), 0.009f);
            }
            if (id == "us") st.usShown = true;
            if (id == "clamp") PlaceClamp(s.clampPts[1] - V(0, 0.0035f, 0.002f), "artery");
            if (id == "excise") { float fp = A.tumor.footprint; for (int i = 0; i < 36; i++) st.bins[i] = fp + 0.003f; DoExcision(); }
            if (id == "inner") { if (s.tg != null) foreach (var tg in s.tg) tg.done = true; DrawInner(); }
            if (id == "outer") { if (s.pairs != null) for (int i = 0; i < s.pairs.Count; i++) DrawOuter(i); FillCrater(); st.outerDone = true; }
            if (id == "unclamp") RemoveClamp();
            if (id == "extract") { st.bagDone = true; if (st.specimen) st.specimen.gameObject.SetActive(false); }
        }

        void Finish()
        {
            finished = true;
            ctx.ShowReport(MakeReport());
        }

        // ---------------------------------------------------------------- ana güncelleme
        // Dönüş: [sol, sağ] haptik genlik
        public float[] Update(float dt, ToolState[] tools)
        {
            t += dt;
            var haptic = new float[2];
            // yerçekimi (hasta-yerel)
            var gP = A.root.InverseTransformVector(Vector3.down).N() * (9.81f * 0.02f);

            // deformasyon (alet uçları, nesne-uzayı)
            foreach (var key in new[] { "kidneyL", "fatPeri", "tumor", "fatHilar", "colon", "meso", "latPer" })
            {
                var mm = A.meshes[key]; if (mm.tissue == null) continue;
                for (int i = 0; i < tools.Length; i++) mm.tissue.SetTip(i, mm.transform.InverseTransformPoint(tools[i].tipW), tools[i].active);
            }

            for (int i = 0; i < tools.Length; i++)
            {
                var T = tools[i]; if (!T.active) continue;
                T.p = ToP(T.tipW); T.k = ToK(T.tipW);
                var info = Instrument.TOOLS[T.tool];
                bool on = T.trigger > 0.55f, pressed = on && T.prevTrigger <= 0.55f;
                // dokuya yakınlık → haptik
                float ek = Ek(T.k);
                if (ek < 1.02f) haptic[i] = Mathf.Max(haptic[i], 0.25f + (1.02f - ek) * 3);
                // enerji
                bool energy = info.energy != null && on;
                bool cutting = energy || (T.tool == "scissors" && on);
                if (energy)
                {
                    ctx.Audio?.Cautery(true, i);
                    haptic[i] = Mathf.Max(haptic[i], 0.35f);
                    if (UnityEngine.Random.value < 0.6f) smoke.Emit(T.p, gP * -0.15f, 0.01f);
                    if (!ctx.IsVisible(T.tipW) && Cool("oov" + i, 2)) { m.energyOOV++; Penalty(2, "Görüş alanı dışında enerji kullanımı"); Msg("⚠ Görüş alanı dışında enerji kullanımı!", "warn"); }
                }
                else ctx.Audio?.Cautery(false, i);
                if (cutting) { Dissect(T); InjuryCheck(T, energy); }
                if (T.tool == "suction" && on) Suction(T, dt);
                if (T.tool == "bipolar" && on) BipolarHemostasis(T, dt);
                if (T.tool == "clip" && pressed) ClipVessel(T);
                T.on = on; T.pressed = pressed;
            }
            ctx.Audio?.Suction(tools.Any(T => T.active && T.tool == "suction" && T.trigger > 0.55f));

            // adıma özel mantık
            var s = Cur;
            if (s != null && !finished) StepLogic(s, tools, dt);

            // kolon medializasyon animasyonu
            if (st.colonAnim && st.colonK < 1)
            {
                st.colonK = Mathf.Min(1, st.colonK + dt / 2.5f);
                float k = st.colonK * st.colonK * (3 - 2 * st.colonK);
                A.colonGroup.localPosition = V(-0.097f * k, 0, 0.006f * k);
                var meso = A.meshes["meso"].transform;
                meso.localScale = V(1 - 0.85f * k, 1, 1); meso.localPosition = V(0.096f * 0.85f * k, 0, 0);
                A.meshes["meso"].gameObject.SetActive(k < 0.7f); // mezokolon medialde toplanır
                A.panc.transform.localPosition = V(-0.05f * k, 0.008f * k, -0.012f * k); // pankreas kuyruğu kolonla birlikte medialize
                A.meshes["toldt"].gameObject.SetActive(false);
            }
            // iskemi rengi
            float tgtI = st.clamp != null ? 1 : 0, tgtC = st.veinClamp != null && st.clamp == null ? 1 : 0;
            st.ischemia += (tgtI - st.ischemia) * Mathf.Min(1, dt / 12);
            st.congestion += (tgtC - st.congestion) * Mathf.Min(1, dt / 8);
            K.tissue.SetTint(new Color(1 - 0.25f * st.ischemia - 0.35f * st.congestion, 1 - 0.3f * st.ischemia - 0.45f * st.congestion, 1 - 0.18f * st.ischemia - 0.1f * st.congestion));
            // WIT
            if (st.clamp != null && m.witStart != null) m.wit = t - m.witStart.Value;

            // kanamalar
            foreach (var b in bleeds)
            {
                float rate = b.active ? b.rate : 0;
                if (b.key == "parenchyma")
                {
                    if (st.clamp != null && st.veinClamp == null) rate *= 0.06f; else if (st.clamp != null && st.veinClamp != null) rate *= 0.02f; else if (st.veinClamp != null) rate *= 1.6f;
                    if (st.inner.Count >= 4) rate *= 0.25f;
                    if (st.outerDone) rate = 0;
                }
                if (b.key == "renalArtery" && st.clamp != null) rate *= 0.05f;
                if (rate > 0)
                {
                    m.bloodLoss += rate * dt;
                    b.vol += rate * dt;
                    int n = Mathf.Min(4, Mathf.CeilToInt(rate * dt * 30));
                    for (int k = 0; k < n; k++) drops.Emit(b.pos, (gP * -0.02f + V(0, 0, 0.02f)) * (rate > 5 ? 3 : 1), 0.06f);
                }
                float r = Mathf.Min(0.03f, 0.003f + 0.0011f * Mathf.Sqrt(b.vol));
                b.pool.transform.localScale = V(r, r, r * 0.35f);
                b.pool.transform.localRotation = Quaternion.FromToRotation(Vector3.forward, -gP);
                b.pool.gameObject.SetActive(b.vol > 0.5f);
            }
            drops.Step(dt, gP, 1.6f);
            smoke.Step(dt, gP * -0.05f, 0.8f);
            if (m.bloodLoss > 500 && Once("bl500")) Msg("⚠ Kan kaybı 500 ml'yi aştı — anestezi uyarıldı", "bad");
            return haptic;
        }

        void Dissect(ToolState T)
        {
            var list = new[] { "latPer", "meso", "fatPeri", "fatHilar", "fatRetro" };
            float r = T.tool == "hook" ? 0.0065f : 0.005f;
            if (!Cool("dis", 0.03f)) return;
            foreach (var key in list)
            {
                var mm = A.meshes[key];
                if (!mm.Visible) continue;
                var lp = mm.transform.InverseTransformPoint(T.tipW);
                mm.BoundingSphere(out var bc, out var br);
                if (Vector3.Distance(lp, bc) > br + 0.01f) continue;
                if (mm.NearSurface(lp, 0.0065f)) mm.tissue.AddHole(lp, r);
            }
        }

        void InjuryCheck(ToolState T, bool energy)
        {
            foreach (var k in new[] { "renalVein", "renalArtery", "aorta", "ivc", "gonadal", "adrenalVein", "lumbarVein" })
            {
                var c = A.curves[k];
                float d = DistToPolyline(T.p, c.pts, out var q, out _);
                if (d < c.r + 0.0006f)
                {
                    if (!Cool(k, 3)) continue;
                    bool major = MAJOR.Contains(k);
                    AddBleed(k, q + (T.p - q).N() * c.r, BLEED[k], major, major);
                    m.injuries.Add(TR_NAMES[k]);
                    Penalty(major ? 20 : 6, (major ? "Majör vasküler yaralanma: " : "Damar yaralanması: ") + TR_NAMES[k]);
                    Msg($"{(major ? "⛔ MAJÖR" : "⚠")} {TR_NAMES[k]} yaralandı! {(major ? "Aspiratörle alanı temizleyip Hem-o-lok klip uygulayın." : "Bipolar ile koagüle edin.")}", major ? "bad" : "warn");
                }
            }
            if (energy)
            {
                float du = DistToPolyline(T.p, A.curves["ureter"].pts);
                if (du < A.curves["ureter"].r + 0.0012f && !m.ureterInjury) { m.ureterInjury = true; Penalty(15, "Üreter termal hasarı"); Msg("⛔ Üretere termal hasar! (geç striktür/fistül riski)", "bad"); }
                float dc = DistToPolyline(T.p - A.colonGroup.localPosition, A.curves["colon"].pts);
                if (dc < A.curves["colon"].r + 0.0008f && !m.bowelInjury) { m.bowelInjury = true; Penalty(15, "Kolon termal hasarı"); Msg("⛔ Kolona termal temas! (gecikmiş perforasyon riski)", "bad"); }
            }
        }

        void Suction(ToolState T, float dt)
        {
            foreach (var b in bleeds) if (Vector3.Distance(b.pos, T.p) < 0.025f) b.vol = Mathf.Max(0, b.vol - dt * 40);
        }

        void BipolarHemostasis(ToolState T, float dt)
        {
            foreach (var b in bleeds)
            {
                if (!b.active || b.key == "parenchyma") continue;
                if (Vector3.Distance(b.pos, T.p) < 0.006f)
                {
                    if (b.needClip) { if (Once("bipmaj")) Msg("Majör damar: bipolar yetersiz — Hem-o-lok klip gerekli.", "warn"); continue; }
                    b.bip += dt;
                    if (b.bip > 1.5f) { b.active = false; Msg("✓ Kanama bipolar ile kontrol edildi", "good"); }
                }
            }
        }

        void ClipVessel(ToolState T)
        {
            foreach (var b in bleeds)
            {
                if (!b.active || b.key == "parenchyma") continue;
                if (Vector3.Distance(b.pos, T.p) < 0.007f)
                {
                    b.active = false;
                    var c = Instrument.MakeHemolok(A.root);
                    c.transform.localPosition = b.pos;
                    c.transform.localRotation = U.LookAt(b.pos, T.p, A.root.InverseTransformVector(Vector3.up));
                    ctx.Audio?.Click();
                    Msg("✓ Kanayan damar klipslendi", "good");
                    return;
                }
            }
        }

        // ---------------------------------------------------------------- adım mantığı
        void StepLogic(Step s, ToolState[] tools, float dt)
        {
            var tum = A.tumor;
            // genel hedef kontrolü (seyrek)
            if (s.tg != null && s.check != null && Cool("tg", 0.12f))
            {
                bool allDone = true;
                foreach (var tg in s.tg)
                {
                    if (!tg.done && s.check(tg)) { tg.done = true; if (tg.mk) tg.mk.gameObject.SetActive(false); ctx.Audio?.Tick(); }
                    if (!tg.done) allDone = false;
                }
                if (s.id == "toldt" && s.tg.Count(x => x.done) >= s.tg.Count - 1) { st.colonAnim = true; Msg("Kolon medialize edildi — Gerota fasyası görünür", "good"); CompleteStep(); return; }
                if (s.id == "hilum" && !st.veinMsg && s.tg.Count(x => x.kind == "vein" && x.done) == 3) { st.veinMsg = true; Msg("Renal ven iskeletize edildi. Arter venin posterior-süperiorunda.", "good"); }
                if (allDone && s.id != "toldt") { CompleteStep(); return; }
            }
            if (s.id == "ureter")
            {
                foreach (var T in tools)
                {
                    if (!T.active || T.tool != "grasper") continue;
                    if (!st.gonadalSeen && DistToPolyline(T.p, A.curves["gonadal"].pts) < 0.006f && T.p.y < -0.02f) { st.gonadalSeen = true; Msg("✓ Gonadal ven tanımlandı", "good"); ctx.Audio?.Tick(); }
                    if (T.on && st.ureterGrab == null)
                    {
                        float d = DistToPolyline(T.p, A.ureterBaseSamples, out _, out float sGrab);
                        if (d < 0.007f) { st.ureterGrab = (sGrab, T.p - st.ureterDelta, T.hand); ctx.Audio?.Tick(); }
                    }
                    if (st.ureterGrab != null && st.ureterGrab.Value.hand == T.hand)
                    {
                        if (!T.on) st.ureterGrab = null;
                        else
                        {
                            st.ureterDelta = T.p - st.ureterGrab.Value.p0;
                            if (st.ureterDelta.magnitude > 0.035f) st.ureterDelta = st.ureterDelta.N() * 0.035f;
                            ApplyUreter(st.ureterGrab.Value.s);
                            if (st.ureterDelta.magnitude > 0.01f) st.ureterLiftT += dt; else st.ureterLiftT = 0;
                        }
                    }
                }
                if (st.gonadalSeen && st.ureterLiftT > 1.0f) { Msg("✓ Üreter askıya alındı (asistan tutuyor)", "good"); st.ureterGrab = null; CompleteStep(); return; }
            }
            if (s.id == "us")
            {
                foreach (var T in tools)
                {
                    if (!T.active || T.tool != "us") continue;
                    float d = Vector3.Distance(T.k, tum.C);
                    if (d < tum.R + 0.007f || (Ek(T.k) < 1.15f && Vector3.Distance(T.k, tum.S) < 0.03f)) st.usT += dt;
                }
                if (st.usT > 2)
                {
                    st.usShown = true; ShowMarginRing(true);
                    Msg("US: 3.0 × 2.8 cm ekzofitik, heterojen solid kitle; toplayıcı sisteme ~5 mm. Rezeksiyon hattı işaretlendi.", "good");
                    CompleteStep(); return;
                }
            }
            if (s.id == "clamp")
            {
                foreach (var T in tools)
                {
                    if (!T.active || T.tool != "bulldog" || !T.pressed) continue;
                    float ra = DistToPolyline(T.p, A.curves["renalArtery"].pts, out var qa, out float sa);
                    float rv = DistToPolyline(T.p, A.curves["renalVein"].pts, out var qv, out _);
                    if (ra < A.curves["renalArtery"].r + 0.0045f && sa > 0.12f && sa < 0.9f)
                    {
                        if (!Exposed(PToW(qa + V(0, 0.003f, 0.002f)), A.meshes["fatHilar"])) { Msg("Arter yeterince diseke edilmemiş — klemp oturmuyor", "warn"); continue; }
                        PlaceClamp(qa, "artery"); Msg("✓ Renal arter klemplendi — WIT başladı", "good"); CompleteStep(); return;
                    }
                    else if (rv < A.curves["renalVein"].r + 0.004f && st.veinClamp == null)
                    {
                        PlaceClamp(qv, "vein"); m.venousClamp = true;
                        Msg("⚠ Renal ven klemplendi! Arter açıkken venöz konjesyon ve kanama artar. Arteri de klempleyin.", "warn"); Penalty(5, "Arterden önce ven klemplendi");
                    }
                }
            }
            if (s.id == "excise")
            {
                foreach (var T in tools)
                {
                    if (!T.active || T.tool != "scissors" || !T.on) continue;
                    var v = T.k - tum.S; float h = Vector3.Dot(v, tum.n);
                    var pl = v - tum.n * h; float rho = pl.magnitude;
                    if (h > -0.016f && h < 0.008f && rho > tum.footprint * 0.55f && rho < tum.footprint + 0.02f)
                    {
                        float ang = Mathf.Atan2(Vector3.Dot(pl, tum.e2), Vector3.Dot(pl, tum.e1));
                        int bin = ((Mathf.FloorToInt((ang + Mathf.PI) / (Mathf.PI * 2) * 36) % 36) + 36) % 36;
                        if (st.bins[bin] == null)
                        {
                            st.bins[bin] = rho;
                            var cm = Part.Make("cut", Geo.Sphere(0.0011f, 6, 5), cutMat, K.transform);
                            cm.transform.localPosition = EllipProject(tum.S + pl); st.cutMarks.Add(cm.gameObject);
                            ctx.Audio?.Snip();
                            if (rho < tum.footprint && Cool("tv", 2)) Msg("⚠ Tümör kapsülüne çok yakın / içinde kesi!", "warn");
                            if (st.clamp == null && st.offClampBleed == null) { st.offClampBleed = AddBleed("parenchyma", ToP(KToW(tum.S)), BLEED["parenchyma"]); m.offClamp = true; Msg("Klempsiz eksizyon: parankimal kanama — aspiratör kullanın", "warn"); }
                            else if (st.clamp != null && st.offClampBleed == null) st.offClampBleed = AddBleed("parenchyma", ToP(KToW(tum.S)), BLEED["parenchyma"]);
                        }
                    }
                }
                int done = st.bins.Count(b => b != null);
                s.progress = done / 36f;
                if (done >= 33) { DoExcision(); CompleteStep(); return; }
            }
            if (s.id == "inner")
            {
                foreach (var T in tools)
                {
                    if (!T.active || T.tool != "needle" || !T.pressed) continue;
                    var next = s.tg.FirstOrDefault(x => !x.done);
                    if (next != null && Vector3.Distance(T.k, next.k) < 0.0065f) { next.done = true; next.mk.gameObject.SetActive(false); st.inner.Add(next.k); DrawInner(); ctx.Audio?.Tick(); }
                    else if (next != null) Msg("Sıradaki dikiş noktasını hedefleyin (yeşil)", "info");
                }
                var nx = s.tg.FirstOrDefault(x => !x.done);
                foreach (var tg in s.tg) if (tg.mk) tg.mk.material.SetColor("_Color", U.Hex(tg == nx ? 0x33ff99u : 0x336644u, 0.85f));
                if (s.tg.All(x => x.done)) { Msg("✓ İç kat tamamlandı — toplayıcı sistem ve medüller damarlar kapatıldı", "good"); CompleteStep(); return; }
            }
            if (s.id == "outer")
            {
                foreach (var T in tools)
                {
                    if (!T.active || T.tool != "needle" || !T.pressed) continue;
                    int pi = s.pairs.FindIndex(p => !p.done);
                    if (pi < 0) break;
                    var P = s.pairs[pi];
                    var tgt = P.aDone ? P.b : P.a;
                    if (Vector3.Distance(T.k, tgt.k) < 0.0065f)
                    {
                        tgt.mk.gameObject.SetActive(false); ctx.Audio?.Tick();
                        if (!P.aDone) P.aDone = true; else { P.done = true; DrawOuter(pi); ctx.Audio?.Click(); }
                    }
                }
                if (s.pairs.All(p => p.done)) { FillCrater(); st.outerDone = true; Msg("✓ Dış kat renorafi tamamlandı", "good"); CompleteStep(); return; }
            }
            if (s.id == "unclamp")
            {
                foreach (var T in tools)
                {
                    if (!T.active || T.tool != "bulldog" || !T.pressed) continue;
                    if (st.clamp != null && Vector3.Distance(T.p, st.clamp.p) < 0.011f) { RemoveClamp(); Msg($"✓ Klemp alındı — WIT {Fmt(m.wit)}", "good"); }
                    else if (st.veinClamp != null && Vector3.Distance(T.p, st.veinClamp.p) < 0.009f) RemoveVeinClamp();
                }
                if (st.clamp == null && st.veinClamp == null)
                {
                    bool activeMajor = bleeds.Any(b => b.active && b.key != "parenchyma");
                    if (!activeMajor) { CompleteStep(); return; }
                    if (Once("hem")) Msg("Aktif kanama odağı var — önce hemostaz sağlayın", "warn");
                }
            }
            if (s.id == "extract")
            {
                var sp = st.specimen; if (!sp) return;
                foreach (var T in tools)
                {
                    if (!T.active || T.tool != "grasper") continue;
                    if (st.specGrab == null && T.pressed && Vector3.Distance(T.p, sp.localPosition) < tum.R + 0.008f) { st.specGrab = T.hand; ctx.Audio?.Tick(); }
                    if (st.specGrab == T.hand)
                    {
                        if (T.on) sp.localPosition = Vector3.Lerp(sp.localPosition, T.p + V(0, 0, -0.008f), 0.5f);
                        else
                        {
                            st.specGrab = null;
                            if (Vector3.Distance(sp.localPosition, st.bagMouth) < 0.025f)
                            {
                                sp.gameObject.SetActive(false); st.bagDone = true; bag.localScale = Vector3.one * 0.7f;
                                Msg("✓ Spesimen endobag içinde", "good"); CompleteStep(); return;
                            }
                        }
                    }
                }
            }
        }

        void ApplyUreter(float sGrab = 0.45f)
        {
            var bse = A.ureterBaseSamples;
            var ctrl = new List<Vector3>(bse.Count);
            for (int i = 0; i < bse.Count; i++)
            {
                float s = (float)i / (bse.Count - 1);
                float w = Mathf.Exp(-Sq((s - sGrab) / 0.16f)) * (s < 0.06f ? s / 0.06f : 1);
                ctrl.Add(bse[i] + st.ureterDelta * w);
            }
            A.RebuildUreter(ctrl);
        }

        void PlaceClamp(Vector3 pP, string kind)
        {
            var c = Instrument.MakeBulldog(A.root);
            var crv = kind == "artery" ? A.curves["renalArtery"] : A.curves["renalVein"];
            DistToPolyline(pP, crv.pts, out _, out float s);
            int i = Mathf.Min(crv.pts.Count - 2, Mathf.FloorToInt(s * (crv.pts.Count - 1)));
            var tan = (crv.pts[i + 1] - crv.pts[i]).N();
            c.transform.localPosition = pP;
            c.transform.localRotation = Quaternion.FromToRotation(V(1, 0, 0), tan);
            if (kind == "vein") c.transform.localScale = Vector3.one * 1.5f;
            var rec = new ClampRec { p = pP, obj = c };
            if (kind == "artery") { st.clamp = rec; m.witStart = t; } else st.veinClamp = rec;
            ctx.Audio?.Click();
        }
        void RemoveClamp()
        {
            if (st.clamp == null) return;
            UnityEngine.Object.Destroy(st.clamp.obj); m.witEnd = t; m.wit = t - (m.witStart ?? t); st.clamp = null;
            if (st.veinClamp != null) RemoveVeinClamp();
            ctx.Audio?.Click();
        }
        void RemoveVeinClamp() { UnityEngine.Object.Destroy(st.veinClamp.obj); st.veinClamp = null; }

        void ShowMarginRing(bool v)
        {
            var tum = A.tumor;
            if (st.ring) { st.ring.gameObject.SetActive(v); return; }
            var ring = Group("marginRing", K.transform).transform;
            for (int i = 0; i < 64; i += 2)
            {
                float a = i / 64f * Mathf.PI * 2;
                var p = EllipProject(tum.S + tum.e1 * (Mathf.Cos(a) * (tum.footprint + 0.0035f)) + tum.e2 * (Mathf.Sin(a) * (tum.footprint + 0.0035f))) + tum.n * 0.0004f;
                var mk = Part.Make("char", Geo.Sphere(0.0008f, 6, 5), charMat, ring); mk.transform.localPosition = p;
            }
            st.ring = ring;
        }

        void DoExcision()
        {
            var tum = A.tumor;
            if (st.excised) return; st.excised = true;
            var vals = st.bins.Where(b => b != null).Select(b => b.Value).ToList();
            float rho = vals.Average(), minR = vals.Min(), fp = tum.footprint;
            m.margin = (rho - fp) * 1000; m.marginMin = (minR - fp) * 1000;
            if (minR < fp - 0.0005f) { m.marginStatus = "POZİTİF (tümör kapsülü ihlali)"; Penalty(20, "Pozitif cerrahi sınır riski"); }
            else if (m.margin > 8) { m.marginStatus = "Negatif — gereksiz geniş eksizyon"; Penalty(Mathf.Min(12, Mathf.RoundToInt(m.margin.Value - 6)), "Fazla parankim kaybı"); }
            else m.marginStatus = "Negatif (ideal 2–5 mm)";
            // krater geometrisi
            float depth = 0.0105f + Mathf.Max(0, rho - fp) * 0.5f;
            float c = (rho * rho - depth * depth) / (2 * depth);
            float Rb = depth + c;
            var Cb = tum.S + tum.n * c;
            K.tissue.SetCrater(new Vector4(Cb.x, Cb.y, Cb.z, Rb));
            var bowlMat = Mats.Tissue(new MatOpts { color = 0xb33a2e, roughness = 0.55f, side = Side.Back, tex = Anatomy.Tex["kidney"] }, false, 0.009f, false);
            bowlMat.SetPlaneCut(new Vector4(0, 0, 0, 1), V(KID.AX * 0.998f, KID.AY * 0.998f, KID.AZ * 0.998f));
            var bowl = Part.Make("bowl", Geo.Sphere(Rb, 48, 32).Translate(Cb), bowlMat, K.transform);
            st.bowl = new Bowl { mesh = bowl, Cb = Cb, Rb = Rb, rho = rho, depth = depth };
            // yağ kalıntısını temizle
            A.meshes["fatPeri"].tissue.AddHole(tum.C, tum.R + 0.008f);
            foreach (var cm in st.cutMarks) cm.SetActive(false);
            if (st.ring) st.ring.gameObject.SetActive(false);
            // spesimen: tümör + parankim tabanı → hasta grubuna taşı
            var tumor = A.meshes["tumor"].transform;
            var spec = Group("specimen", A.root).transform;
            var wp = tumor.position;
            tumor.SetParent(spec, false); tumor.localPosition = Vector3.zero; tumor.localRotation = Quaternion.identity;
            var cap = Part.Make("cap", Geo.Sphere(rho, 24, 12, 0, Mathf.PI * 2, Mathf.PI / 2, Mathf.PI / 2), Mats.Std(0xa8402f, 0.5f), spec);
            cap.transform.localScale = V(1, depth / rho, 1);
            var capHolder = Group("capHolder", spec).transform;
            capHolder.localRotation = Quaternion.FromToRotation(V(0, 1, 0), tum.n); capHolder.localPosition = tum.n * -TUMOR.exo;
            cap.transform.SetParent(capHolder, false);
            spec.localPosition = ToP(wp);
            spec.localRotation = K.transform.localRotation;
            // spesimen yataktan kaldırılıp parakolik oluğa doğru bırakılır (defekt görünür kalsın)
            spec.localPosition = Vector3.Lerp(spec.localPosition, V(0.112f, -0.07f, 0.045f), 0.55f);
            st.specimen = spec;
            ctx.Audio?.Click();
        }

        void MakeInnerTargets()
        {
            var s = Cur; var tum = A.tumor;
            if (st.bowl == null) return;
            var Cb = st.bowl.Cb; float Rb = st.bowl.Rb;
            var pts = new[] { (-0.006f, -0.004f), (-0.002f, 0.004f), (0.003f, -0.004f), (0.007f, 0.003f) };
            s.tg = pts.Select(ab =>
            {
                var q = Cb - tum.n * Rb + tum.e1 * ab.Item1 + tum.e2 * ab.Item2;
                var k = Cb + (q - Cb).N() * (Rb * 0.96f);
                return new Target { k = k, p = ToP(KToW(k)) };
            }).ToList();
            foreach (var tg in s.tg) tg.mk = Marker(tg.p);
        }

        void DrawInner()
        {
            if (st.innerMesh) UnityEngine.Object.Destroy(st.innerMesh.gameObject);
            var s = Cur;
            var pts = s?.tg != null ? s.tg.Where(x => x.done).Select(x => x.k).ToList() : st.inner;
            if (pts.Count < 2) return;
            var zig = new List<Vector3>();
            for (int i = 0; i < pts.Count; i++)
            {
                zig.Add(pts[i]);
                if (i < pts.Count - 1) zig.Add(Vector3.Lerp(pts[i], pts[i + 1], 0.5f) + A.tumor.n * 0.0025f);
            }
            st.innerMesh = Part.Make("innerSuture", Geo.TaperTube(zig, 0.00045f, 60, 6), sutureMat, K.transform);
            st.inner = pts.ToList();
        }

        void MakeOuterTargets()
        {
            var s = Cur; var tum = A.tumor;
            if (st.bowl == null) return;
            float a = st.bowl.rho + 0.004f;
            s.pairs = new[] { -0.0085f, 0f, 0.0085f }.Select(k =>
            {
                Target Mk(float sgn)
                {
                    var kk = EllipProject(tum.S + tum.e1 * (sgn * Mathf.Sqrt(Mathf.Max(0.0001f, a * a - k * k * 0.3f))) + tum.e2 * k);
                    return new Target { k = kk, p = ToP(KToW(kk)) };
                }
                var P = new SuturePair { a = Mk(-1), b = Mk(1) };
                P.a.mk = Marker(P.a.p, 0x33ff99); P.b.mk = Marker(P.b.p, 0xffcc33);
                return P;
            }).ToList();
        }

        void DrawOuter(int i)
        {
            var P = Cur.pairs[i]; var tum = A.tumor;
            var mid = Vector3.Lerp(P.a.k, P.b.k, 0.5f) + tum.n * 0.003f;
            Part.Make("outerSuture", Geo.TaperTube(new List<Vector3> { P.a.k, mid, P.b.k }, 0.0005f, 30, 6), sutureMat, K.transform);
            foreach (var e in new[] { P.a.k, P.b.k })
            {
                var c = Instrument.MakeHemolok(K.transform);
                c.transform.localPosition = e + tum.n * 0.001f; c.transform.localRotation = Quaternion.FromToRotation(V(0, 1, 0), tum.n);
            }
            st.outer.Add(i);
        }

        void FillCrater()
        {
            var tum = A.tumor;
            if (st.bowl == null || st.bolster) return;
            var b = Part.Make("bolster", Geo.Sphere(st.bowl.rho * 0.92f, 24, 16), Mats.Std(0xe9e4cf, 0.8f), K.transform);
            b.transform.localScale = V(1, 1, 0.45f);
            b.transform.localPosition = tum.S - tum.n * 0.002f;
            b.transform.localRotation = Quaternion.FromToRotation(V(0, 0, 1), tum.n);
            st.bolster = b;
        }

        void MakeBag()
        {
            var g = Group("endobag", A.root).transform;
            var cone = Part.Make("bag", Geo.Cone(0.022f, 0.06f, 24, true), Mats.Std(new MatOpts { color = 0xd8e6f2, roughness = 0.3f, transparent = true, opacity = 0.8f, side = Side.Double }), g);
            cone.transform.localRotation = EulerXYZ(Mathf.PI, 0, 0); cone.transform.localPosition = V(0, -0.03f, 0);
            var ring = Part.Make("ring", Geo.Torus(0.022f, 0.0012f, 8, 32), Mats.Std(0x2f7fd6, 0.4f), g);
            ring.transform.localRotation = EulerXYZ(Mathf.PI / 2, 0, 0);
            var mouth = V(0.112f, -0.075f, 0.05f);
            g.localPosition = mouth;
            g.localRotation = Quaternion.FromToRotation(V(0, 1, 0), V(-0.3f, 0, 1).N());
            bag = g; st.bagMouth = mouth;
            Marker(mouth, 0x55ccff, 0.003f);
        }

        // ---------------------------------------------------------------- US görüntüsü (B-mod, prosedürel)
        public bool DrawUltrasound(Color32[] px, int W, int H, ToolState T)
        {
            var tum = A.tumor;
            var k = T.k; // prob ucu böbrek-yerel
            bool contact = Ek(k) < 1.25f;
            // görüntü düzlemi: derinlik = -normal, lateral = alet ekseninin teğet izdüşümü
            var sp = EllipProject(k);
            var nrm = V(sp.x / Sq(KID.AX), sp.y / Sq(KID.AY), sp.z / Sq(KID.AZ)).N();
            var shaftK = (ToK(T.tipW + T.dirW) - k).N();
            var lat = (shaftK - nrm * Vector3.Dot(shaftK, nrm)).N();
            var depthV = -nrm;
            const float widthM = 0.04f, depthM = 0.05f;
            var Cr = tum.C - sp;
            var planeN = Vector3.Cross(lat, depthV);
            float dist = Vector3.Dot(Cr, planeN);
            float rIm = Mathf.Abs(dist) < tum.R ? Mathf.Sqrt(tum.R * tum.R - dist * dist) : 0;
            float cx = Vector3.Dot(Cr, lat), cy = Vector3.Dot(Cr, depthV);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float ux = ((float)x / W - 0.5f) * widthM, uy = (float)y / H * depthM;
                    bool fan = Mathf.Abs(ux) < widthM * 0.5f * (0.55f + 0.45f * y / H); // sektör maskesi
                    float v = 0;
                    if (contact && fan)
                    {
                        var q = sp + lat * ux + depthV * uy;
                        float e = Ek(q);
                        float speck = UnityEngine.Random.value;
                        if (e < 1)
                        {
                            bool cortex = e > 0.72f;
                            v = cortex ? 95 + speck * 70 : (e < 0.4f ? 170 + speck * 80 : 55 + speck * 50); // korteks / sinüs yağı (hiperekoik) / medulla
                            if (rIm > 0) { float dd = Mathf.Sqrt(Sq(ux - cx) + Sq(uy - cy)); if (dd < rIm) v = 120 + speck * 90 * (0.6f + 0.4f * Mathf.Sin(ux * 900) * Mathf.Cos(uy * 800)); if (Mathf.Abs(dd - rIm) < 0.0008f) v = 40; }
                        }
                        else
                        {
                            v = (uy < 0.002f ? 200 : 20) + speck * 25;
                            if (rIm > 0) { float dd = Mathf.Sqrt(Sq(ux - cx) + Sq(uy - cy)); if (dd < rIm) v = 120 + speck * 90; if (Mathf.Abs(dd - rIm) < 0.0008f) v = 40; }
                        }
                        v *= 1 - 0.5f * ((float)y / H);
                    }
                    byte b = (byte)Mathf.Clamp(v, 0, 255);
                    px[(H - 1 - y) * W + x] = new Color32(b, b, b, 255);
                }
            return contact;
        }

        // ---------------------------------------------------------------- rapor
        public Report MakeReport()
        {
            int score = 100;
            foreach (var p in m.penalties) score -= p.points;
            if (m.wit > 25 * 60) { score -= 15; m.penalties.Add((15, "WIT > 25 dk")); }
            else if (m.wit > 20 * 60) { score -= 5; m.penalties.Add((5, "WIT 20–25 dk")); }
            if (m.bloodLoss > 500) { score -= 10; m.penalties.Add((10, "Kan kaybı > 500 ml")); }
            else if (m.bloodLoss > 250) { score -= 4; m.penalties.Add((4, "Kan kaybı > 250 ml")); }
            score = Mathf.Clamp(score, 0, 100);
            bool trifecta = m.marginStatus.StartsWith("Negatif") && m.wit <= 25 * 60 && !m.ureterInjury && !m.bowelInjury
                && !m.injuries.Any(x => x == "renal ven" || x == "renal arter" || x == "aorta" || x == "VCI");
            return new Report
            {
                score = score, totalTime = t, wit = m.wit, bloodLoss = m.bloodLoss, margin = m.margin, marginMin = m.marginMin, marginStatus = m.marginStatus, offClamp = m.offClamp,
                injuries = m.injuries, ureter = m.ureterInjury, bowel = m.bowelInjury, oov = m.energyOOV, penalties = m.penalties, steps = m.stepTimes, trifecta = trifecta
            };
        }
    }
}
