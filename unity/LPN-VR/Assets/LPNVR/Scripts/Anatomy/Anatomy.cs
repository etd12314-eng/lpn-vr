// Prosedürel retroperiton / sol böbrek anatomisi — web sürümü anatomy.js'in birebir karşılığı.
// Hasta-yerel koordinat sistemi (metre):  +X = hastanın solu, +Y = kraniyal, +Z = anterior. Orijin: L1 vertebra korpusu.
//
// Ölçüler erişkin ortalama değerlere göre (Gray's Anatomy 42. baskı; Netter Atlas; BT morfometri serileri):
//  - Böbrek: 11 × 6 × 3.5 cm, sol böbrek T12–L3, hilus L1 seviyesi; üst pol mediale ve posteriora eğik,
//    hilus anteromediale bakar (uzun eksen etrafında ~30° rotasyon).
//  - Hilus yapıları anteriordan posteriora: Ven – Arter – Pelvis (VAP).
//  - Sol renal ven (~8 cm, Ø ~10 mm) aortanın önünden SMA'nın altından geçerek VCI'ya döner;
//    sol gonadal ven (inferior), sol adrenal ven (süperior) ve lomber ven (posterior) sol renal vene drene olur.
//  - Renal arter L1–L2 seviyesinde aortanın lateralinden çıkar (Ø ~5-6 mm), venin posterior-süperiorunda seyreder,
//    hilusta anterior (apikal, üst, orta, alt segment) ve posterior dallara ayrılır.
//  - Üreter psoas major önünde iner; sol gonadal ven proksimalde üreterin medialinde, aşağıda önünden çaprazlar.
//  - İnen kolon böbreğin lateral kenarının önünde; lateral peritoneal yansıma = Toldt beyaz çizgisi.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static LPNVR.U;

namespace LPNVR
{
    public class VesselCurve { public List<Vector3> pts; public float r; public Part mesh; }
    public class AnatLabel { public string text; public Vector3 pos; public string system; }
    public class TumorLand { public Vector3 S, n, C, e1, e2; public float R, footprint; }

    public static class KID { public const float AX = 0.030f, AY = 0.055f, AZ = 0.0175f; }
    public static class TUMOR { public const float R = 0.015f, exo = 0.004f; } // 3 cm (cT1a), merkez yüzeyden 4 mm dışarıda (>%50 ekzofitik)
    public static class TORSO { public const float a = 0.17f, b = 0.12f, cz = 0.02f, y0 = -0.28f, y1 = 0.30f, wall = 0.014f; }

    public class Anatomy
    {
        public Transform root;
        public readonly Dictionary<string, Part> meshes = new Dictionary<string, Part>();
        public readonly Dictionary<string, VesselCurve> curves = new Dictionary<string, VesselCurve>();
        public readonly List<AnatLabel> labels = new List<AnatLabel>();
        public readonly Dictionary<string, List<GameObject>> systems = new Dictionary<string, List<GameObject>>();
        public readonly List<Part> pickables = new List<Part>();
        public TumorLand tumor;
        public Vector3 collectingLower, hilum;
        public List<Vector3> toldt;
        public List<Vector3> ureterBase, ureterBaseSamples;
        public Transform colonGroup, inner;
        public Part panc;

        static Dictionary<string, Texture2D> _tex;
        public static Dictionary<string, Texture2D> Tex
        {
            get
            {
                if (_tex != null) return _tex;
                ResetSeed(20241);
                _tex = new Dictionary<string, Texture2D>();
                foreach (var k in new[] { "fat", "kidney", "tumor", "colon", "peritoneum", "muscle", "vein", "artery", "organ", "wall" })
                    _tex[k] = ProcTextures.Make(k);
                return _tex;
            }
        }

        // ------------------------------------------------------------------ geometri yardımcıları
        static Vector3 KidneyShape(Vector3 u)
        {
            float x = u.x, y = u.y, z = u.z;
            float ind = Mathf.Pow(Mathf.Max(0, -x), 1.6f) * Mathf.Exp(-(y * y) / (0.38f * 0.38f));
            x = x + 0.62f * ind;          // hiler konkavite
            z = z * (1 - 0.28f * ind);    // sinüs dudakları
            float sx = y > 0 ? 1 + 0.06f * y : 1; // üst pol biraz daha geniş
            return V(x * KID.AX * sx, y * KID.AY, z * KID.AZ);
        }

        public static MeshData KidneyGeometry()
            => Geo.Sphere(1, 72, 56).MapPositions((p, i) => KidneyShape(p.N())).ComputeNormals();

        static MeshData MirrorX(MeshData g)
        {
            for (int i = 0; i < g.pos.Count; i++) { var p = g.pos[i]; p.x = -p.x; g.pos[i] = p; }
            for (int i = 0; i < g.nor.Count; i++) { var n = g.nor[i]; n.x = -n.x; g.nor[i] = n; }
            return g.FlipWinding();
        }

        static float TorsoScale(float y)
        {
            if (y > 0.18f) return Mathf.Sqrt(Mathf.Max(0.05f, 1 - Mathf.Pow((y - 0.18f) / (TORSO.y1 - 0.18f), 2)));
            if (y < -0.16f) return Mathf.Sqrt(Mathf.Max(0.05f, 1 - Mathf.Pow((y + 0.16f) / (TORSO.y0 + 0.16f), 2)));
            return 1;
        }
        static float Dome(float x, float y, float c) => c > 0 ? 0.03f * c * Mathf.Exp(-(x * x) / 0.012f) * Mathf.Exp(-((y + 0.02f) * (y + 0.02f)) / 0.025f) : 0;

        public static Vector3 TorsoPoint(float theta, float y, float inset = 0)
        {
            float s = TorsoScale(y), c = Mathf.Cos(theta);
            float x = (TORSO.a - inset) * Mathf.Sin(theta) * s;
            float z = TORSO.cz + (TORSO.b - inset) * c * s + Dome(x, y, c) * s;
            return V(x, y, z);
        }
        public static void AnteriorSurface(float x, float y, out Vector3 p, out Vector3 n)
        {
            float s = TorsoScale(y), th = Mathf.Asin(Clamp(x / (TORSO.a * s), -1, 1));
            p = TorsoPoint(th, y);
            n = (TorsoPoint(th, y) - TorsoPoint(th, y, 0.01f)).N();
        }
        public static MeshData TorsoGeometry(float inset)
        {
            const int seg = 96, rows = 60;
            var g = new MeshData();
            for (int j = 0; j <= rows; j++)
            {
                float y = TORSO.y0 + (TORSO.y1 - TORSO.y0) * j / rows;
                for (int i = 0; i <= seg; i++)
                {
                    g.pos.Add(TorsoPoint((float)i / seg * Mathf.PI * 2, y, inset));
                    g.uv.Add(new Vector2((float)i / seg * 6, (float)j / rows * 4));
                }
            }
            for (int j = 0; j < rows; j++)
                for (int i = 0; i < seg; i++)
                {
                    int a = j * (seg + 1) + i, b = a + seg + 1;
                    g.idx.AddRange(new[] { a, a + 1, b, b, a + 1, b + 1 });
                }
            return g.ComputeNormals();
        }

        // ------------------------------------------------------------------ inşa
        GameObject Sys(string name, GameObject o)
        {
            if (!systems.TryGetValue(name, out var l)) systems[name] = l = new List<GameObject>();
            l.Add(o); return o;
        }
        Part Add(Part p, string system) { if (system != null) Sys(system, p.gameObject); return p; }
        void Label(string text, Vector3 pos, string system) => labels.Add(new AnatLabel { text = text, pos = pos, system = system });

        Material Std(uint color, float rough = 0.4f, Texture tex = null, Side side = Side.Front, float metal = 0)
            => Mats.Std(new MatOpts { color = color, roughness = rough, tex = tex, side = side, metallic = metal });

        Part Mesh(string name, MeshData g, Material m, Transform parent = null, Side side = Side.Front)
            => Part.Make(name, g, m, parent ? parent : root, side);

        Part Vessel(string name, List<Vector3> pts, Func<float, float> r, Material mat, string system = "vascular", int seg = 64)
        {
            var m = Mesh(name, Geo.TaperTube(pts, r, seg, 14), mat);
            Add(m, system);
            curves[name] = new VesselCurve { pts = Geo.SamplePoints(pts, 50), r = r(0.5f), mesh = m };
            return m;
        }
        Part Vessel(string name, List<Vector3> pts, float r, Material mat) => Vessel(name, pts, _ => r, mat);

        static List<Vector3> L(params Vector3[] v) => new List<Vector3>(v);

        public Anatomy(Transform parent, string name = "anatomy")
        {
            var tex = Tex;
            ResetSeed(20241);
            root = Group(name, parent).transform;

            // ------------------------------------------------------------------ Kemik
            var boneM = Std(0xe8dcc0, 0.7f);
            string[] vertNames = { "T10", "T11", "T12", "L1", "L2", "L3", "L4", "L5" };
            for (int i = 0; i < vertNames.Length; i++)
            {
                string nm = vertNames[i];
                float y = (3 - i) * 0.036f;
                var body = Mesh("vert-" + nm, Geo.Cylinder(0.021f, 0.022f, 0.027f, 24), boneM);
                body.transform.localPosition = V(0, y, 0); Add(body, "bone");
                var tp = Mesh("tp-" + nm, Geo.Box(nm.StartsWith("L") ? 0.085f : 0.06f, 0.008f, 0.01f), boneM);
                tp.transform.localPosition = V(0, y, -0.03f); Add(tp, "bone");
                var sp = Mesh("sp-" + nm, Geo.Box(0.008f, 0.016f, 0.04f), boneM);
                sp.transform.localPosition = V(0, y - 0.004f, -0.05f); Add(sp, "bone");
                if (nm == "L1") Label("L1 vertebra", V(-0.022f, y, 0.02f), "bone");
            }
            var disc = Std(0xbfd4d8, 0.6f);
            for (int i = 0; i < 7; i++)
            {
                var d = Mesh("disc", Geo.Cylinder(0.0215f, 0.0215f, 0.008f, 24), disc);
                d.transform.localPosition = V(0, (3 - i) * 0.036f - 0.018f, 0); Add(d, "bone");
            }
            // Kostalar 10–12 (her iki taraf, posterior arklar)
            var ribs = new Dictionary<int, Vector3[]>
            {
                [10] = new[] { V(0.03f, 0.10f, -0.04f), V(0.09f, 0.09f, -0.068f), V(0.14f, 0.06f, -0.045f), V(0.158f, 0.03f, 0.01f), V(0.148f, 0.0f, 0.06f) },
                [11] = new[] { V(0.03f, 0.068f, -0.04f), V(0.08f, 0.052f, -0.066f), V(0.125f, 0.022f, -0.056f), V(0.15f, -0.01f, -0.03f) },
                [12] = new[] { V(0.03f, 0.034f, -0.04f), V(0.07f, 0.014f, -0.064f), V(0.10f, -0.012f, -0.062f), V(0.12f, -0.034f, -0.05f) },
            };
            foreach (int side in new[] { 1, -1 })
                foreach (var kv in ribs)
                {
                    var pts = kv.Value.Select(p => V(p.x * side, p.y, p.z)).ToList();
                    Add(Mesh("rib" + kv.Key, Geo.TaperTube(pts, 0.0048f, 40, 8), boneM), "bone");
                    if (side == 1 && kv.Key == 12) Label("12. kosta", pts[2], "bone");
                }

            // ------------------------------------------------------------------ Kaslar
            var muscM = Std(0xffffff, 0.55f, tex["muscle"]);
            foreach (int side in new[] { 1, -1 })
            {
                var pts = new[] { V(0.026f, 0.05f, -0.004f), V(0.034f, 0.0f, 0.0f), V(0.042f, -0.06f, 0.004f), V(0.05f, -0.12f, 0.008f), V(0.058f, -0.18f, 0.012f), V(0.065f, -0.24f, 0.02f) }
                    .Select(p => V(p.x * side, p.y, p.z)).ToList();
                Add(Mesh("psoas", Geo.TaperTube(pts, t => 0.007f + 0.018f * Mathf.Sin(Mathf.Min(1, t * 1.3f) * Mathf.PI * 0.5f), 48, 16), muscM), "muscle");
                if (side == 1) { Label("M. psoas major", pts[3], "muscle"); curves["psoas"] = new VesselCurve { pts = Geo.SamplePoints(pts, 30), r = 0.02f }; }
                var ql = Mesh("quadratus", Geo.Ellipsoid(0.024f, 0.075f, 0.01f, 32), muscM);
                ql.transform.localPosition = V(0.085f * side, -0.045f, -0.042f);
                ql.transform.localRotation = EulerXYZ(0, 0, -0.15f * side);
                Add(ql, "muscle");
                if (side == 1) Label("M. quadratus lumborum", V(0.1f, -0.09f, -0.035f), "muscle");
            }

            // ------------------------------------------------------------------ Büyük damarlar
            var artM = Std(0xffd0c8, 0.35f, tex["artery"]);
            var veinM = Std(0xffffff, 0.28f, tex["vein"]);
            Vessel("aorta", L(V(0.012f, 0.2f, 0.033f), V(0.012f, 0.1f, 0.034f), V(0.012f, 0.0f, 0.034f), V(0.012f, -0.12f, 0.035f)), 0.0095f, artM);
            Vessel("iliacaL", L(V(0.012f, -0.115f, 0.035f), V(0.035f, -0.17f, 0.032f), V(0.055f, -0.24f, 0.026f)), 0.0055f, artM);
            Vessel("iliacaR", L(V(0.012f, -0.115f, 0.035f), V(-0.012f, -0.17f, 0.032f), V(-0.035f, -0.24f, 0.026f)), 0.0055f, artM);
            Vessel("ivc", L(V(-0.024f, -0.12f, 0.033f), V(-0.024f, 0.0f, 0.034f), V(-0.026f, 0.1f, 0.03f), V(-0.03f, 0.2f, 0.025f)), 0.0105f, veinM);
            Vessel("iliacVL", L(V(-0.024f, -0.118f, 0.032f), V(0.022f, -0.175f, 0.027f), V(0.045f, -0.24f, 0.02f)), 0.0065f, veinM);
            Vessel("iliacVR", L(V(-0.024f, -0.118f, 0.032f), V(-0.04f, -0.175f, 0.027f), V(-0.055f, -0.24f, 0.02f)), 0.0065f, veinM);
            Vessel("celiac", L(V(0.012f, 0.075f, 0.044f), V(0.012f, 0.078f, 0.062f)), 0.0035f, artM);
            Vessel("sma", L(V(0.012f, 0.042f, 0.044f), V(0.012f, 0.03f, 0.068f), V(0.009f, 0.0f, 0.08f), V(0.004f, -0.05f, 0.088f), V(-0.004f, -0.11f, 0.09f)), 0.004f, artM);
            Label("Aorta abdominalis", V(0.012f, -0.07f, 0.046f), "vascular");
            Label("V. cava inferior", V(-0.024f, -0.06f, 0.046f), "vascular");
            Label("A. mesenterica superior", V(0.009f, 0.0f, 0.085f), "vascular");
            Label("Truncus coeliacus", V(0.012f, 0.078f, 0.064f), "vascular");

            // ------------------------------------------------------------------ Böbrekler
            var kidneyMat = Mats.Tissue(new MatOpts { tex = tex["kidney"], roughness = 0.28f }, false, 0.009f);
            var kidneyL = Part.Make("kidneyL", KidneyGeometry(), kidneyMat, root);
            kidneyL.transform.localPosition = V(0.078f, -0.008f, -0.008f);
            kidneyL.transform.localRotation = EulerXZY(-12 * D2R, 30 * D2R, 15 * D2R);
            Add(kidneyL, "urinary");
            meshes["kidneyL"] = kidneyL;
            var KLt = kidneyL.transform;
            Vector3 KL(float x, float y, float z) => KLt.localPosition + KLt.localRotation * V(x, y, z);

            var kidneyR = Mesh("kidneyR", MirrorX(KidneyGeometry()), Std(0xffffff, 0.3f, tex["kidney"]));
            kidneyR.transform.localPosition = V(-0.08f, -0.026f, -0.008f);
            kidneyR.transform.localRotation = EulerXZY(-12 * D2R, -30 * D2R, -15 * D2R);
            Add(kidneyR, "urinary");
            var KRt = kidneyR.transform;
            Vector3 KR(float x, float y, float z) => KRt.localPosition + KRt.localRotation * V(-x, y, z);
            Label("Sol böbrek (Ren sinister)", KL(0.03f, 0.03f, 0.0f), "urinary");
            Label("Sağ böbrek", KR(0.03f, 0.02f, 0.0f), "urinary");

            // Tümör (böbrek-yerel). Alt pol, anterior yüz, >%50 ekzofitik, 3 cm. RENAL: 1+1+2+1 = 5a
            var u = V(-0.22f, -0.62f, 0.76f).N(); // alt pol, anterior yüz (orta-medial)
            var S = V(u.x * KID.AX, u.y * KID.AY, u.z * KID.AZ);
            var n = V(S.x / (KID.AX * KID.AX), S.y / (KID.AY * KID.AY), S.z / (KID.AZ * KID.AZ)).N();
            var C = S + n * TUMOR.exo;
            var e1 = Vector3.Cross(V(0, 1, 0), n).N();
            var e2 = Vector3.Cross(n, e1).N();
            tumor = new TumorLand { S = S, n = n, C = C, R = TUMOR.R, e1 = e1, e2 = e2, footprint = Mathf.Sqrt(TUMOR.R * TUMOR.R - TUMOR.exo * TUMOR.exo) };
            var tg = Geo.Sphere(TUMOR.R, 48, 36).MapPositions((v, i) =>
            {
                float k = 1 + 0.06f * Mathf.Sin(v.x * 400) * Mathf.Sin(v.y * 330 + 1) * Mathf.Sin(v.z * 360);
                return v * k;
            }).ComputeNormals();
            var tumorMat = Mats.Tissue(new MatOpts { tex = tex["tumor"], roughness = 0.42f }, false, 0.008f);
            var tumorP = Part.Make("tumor", tg, tumorMat, KLt);
            tumorP.transform.localPosition = C; Sys("urinary", tumorP.gameObject);
            meshes["tumor"] = tumorP;
            Label("Tümör (RCC, 3 cm)", KL(C.x, C.y, C.z) + V(0, 0, 1) * 0.012f, "urinary");

            // Kollektör sistem + segmental arterler (böbrek-yerel, atlas modunda şeffaf böbrekle görünür)
            var collM = Std(0xf2e6a0, 0.35f);
            inner = Group("inner", KLt).transform; Sys("inner", inner.gameObject);
            var pelvisPts = L(V(-0.0175f, -0.006f, -0.006f), V(-0.011f, -0.003f, -0.004f), V(-0.004f, -0.001f, -0.002f));
            Mesh("pelvis", Geo.TaperTube(pelvisPts, t => 0.0035f + 0.004f * t, 20, 12), collM, inner);
            var calyces = new[] {
                new[] { V(-0.004f, 0, -0.002f), V(0.0f, 0.015f, 0), V(0.006f, 0.032f, 0) },
                new[] { V(-0.004f, 0, -0.002f), V(0.006f, 0.0f, 0), V(0.013f, 0.002f, 0) },
                new[] { V(-0.004f, -0.002f, -0.002f), V(0.0f, -0.016f, 0), V(0.006f, -0.031f, 0) } };
            foreach (var c in calyces)
            {
                Mesh("calyx", Geo.TaperTube(c, 0.0026f, 16, 10), collM, inner);
                foreach (int d in new[] { -1, 1 })
                {
                    var tip = c[2] + V(0.004f, d * 0.006f, d * 0.003f);
                    Mesh("calyxMinor", Geo.TaperTube(L(c[2], tip), 0.0018f, 6, 8), collM, inner);
                    var cup = Mesh("cup", Geo.Sphere(0.0028f, 12, 8), collM, inner); cup.transform.localPosition = tip;
                }
            }
            var segA = Std(0xd4483c, 0.35f);
            var aHil = V(-0.017f, 0.005f, 0.0f);
            var segs = new (string, Vector3)[] { ("apikal", V(0.0f, 0.048f, 0.003f)), ("üst", V(0.008f, 0.026f, 0.006f)), ("orta", V(0.012f, -0.004f, 0.006f)), ("alt", V(0.006f, -0.042f, 0.004f)), ("posterior", V(0.008f, 0.004f, -0.007f)) };
            foreach (var (k, sp) in segs)
            {
                var mid = Vector3.Lerp(aHil, sp, 0.35f) + V(0, 0, k == "posterior" ? -0.004f : 0.003f);
                Mesh("segA-" + k, Geo.TaperTube(L(aHil, mid, sp), t => 0.0016f * (1 - 0.5f * t), 24, 8), segA, inner);
            }
            collectingLower = KL(0.006f, -0.031f, 0);

            // ------------------------------------------------------------------ Hiler damarlar ve üreter
            Vector3 hV = KL(-0.0125f, 0.002f, 0.0065f), hA = KL(-0.0128f, 0.005f, 0.0005f), hP = KL(-0.0118f, -0.005f, -0.0055f);
            hilum = KL(-0.012f, 0, 0);
            Vessel("renalVein", L(V(-0.013f, -0.002f, 0.036f), V(0.0f, -0.001f, 0.046f), V(0.012f, 0.0f, 0.0505f), V(0.028f, 0.0f, 0.046f), V(0.045f, -0.001f, 0.034f), Vector3.Lerp(hV, V(0.045f, -0.001f, 0.034f), 0.45f), hV), 0.0052f, veinM);
            Vessel("renalArtery", L(V(0.021f, 0.008f, 0.033f), V(0.034f, 0.009f, 0.027f), V(0.047f, 0.008f, 0.02f), Vector3.Lerp(hA, V(0.047f, 0.008f, 0.02f), 0.4f), hA), 0.0029f, artM);
            Label("V. renalis sinistra", V(0.04f, -0.003f, 0.042f), "vascular");
            Label("A. renalis sinistra", V(0.04f, 0.012f, 0.024f), "vascular");
            // Sağ renal damarlar (arter VCI arkasından)
            Vector3 hRV = KR(-0.0125f, 0.002f, 0.0065f), hRA = KR(-0.0128f, 0.005f, 0.0005f);
            Vessel("renalVeinR", L(V(-0.03f, -0.004f, 0.03f), Vector3.Lerp(hRV, V(-0.03f, -0.004f, 0.03f), 0.5f) + V(0, 0, 0.004f), hRV), 0.0055f, veinM);
            Vessel("renalArteryR", L(V(0.003f, 0.006f, 0.03f), V(-0.022f, 0.002f, 0.014f), Vector3.Lerp(hRA, V(-0.022f, 0.002f, 0.014f), 0.4f), hRA), 0.0027f, artM);

            // Renal ven dalları
            var gonPts = L(V(0.044f, -0.004f, 0.032f), V(0.044f, -0.025f, 0.035f), V(0.043f, -0.06f, 0.034f), V(0.047f, -0.11f, 0.035f), V(0.057f, -0.16f, 0.034f), V(0.064f, -0.215f, 0.031f));
            Vessel("gonadal", gonPts, 0.0021f, veinM);
            Label("V. gonadalis sinistra", gonPts[2], "vascular");
            var adrPts = L(V(0.042f, 0.004f, 0.034f), V(0.044f, 0.018f, 0.03f), V(0.048f, 0.032f, 0.022f));
            Vessel("adrenalVein", adrPts, 0.0017f, veinM);
            Label("V. suprarenalis sinistra", adrPts[1], "vascular");
            var lumPts = L(V(0.03f, -0.002f, 0.042f), V(0.032f, -0.008f, 0.026f), V(0.034f, -0.014f, 0.008f), V(0.034f, -0.016f, -0.004f));
            Vessel("lumbarVein", lumPts, 0.0016f, veinM);
            Label("V. lumbalis", lumPts[2], "vascular");

            // Adrenal bez (üst pol medialinde hilal)
            var adr = Mesh("adrenal", Geo.Ellipsoid(0.011f, 0.022f, 0.0045f, 32, 0.08f), Std(0xe0a040, 0.5f));
            adr.transform.localPosition = V(0.054f, 0.044f, 0.006f); adr.transform.localRotation = EulerXYZ(0.2f, 0.5f, 0.35f);
            Add(adr, "adrenal");
            Label("Gl. suprarenalis sinistra", V(0.054f, 0.05f, 0.016f), "adrenal");

            // Üreter (dinamik: kavranıp kaldırılabilir)
            var urM = Std(0xf0e2c8, 0.35f);
            ureterBase = L(hP, KL(-0.02f, -0.014f, -0.004f), V(0.058f, -0.045f, 0.012f), V(0.052f, -0.08f, 0.024f), V(0.046f, -0.12f, 0.027f), V(0.04f, -0.17f, 0.03f), V(0.035f, -0.215f, 0.03f));
            var ureter = Mesh("ureter", Geo.TaperTube(ureterBase, 0.0026f, 64, 10), urM);
            Add(ureter, "urinary");
            meshes["ureter"] = ureter;
            curves["ureter"] = new VesselCurve { pts = Geo.SamplePoints(ureterBase, 50), r = 0.0026f, mesh = ureter };
            ureterBaseSamples = curves["ureter"].pts.ToList();
            Label("Üreter", V(0.052f, -0.09f, 0.03f), "urinary");
            Label("Pelvis renalis", KL(-0.02f, -0.008f, -0.008f), "urinary");

            // ------------------------------------------------------------------ Yağ dokuları (diseke edilebilir)
            // Perirenal yağ + Gerota fasyası (böbrek yüzeyinin 9 mm şişirilmiş kopyası, tümör üstünde kubbe)
            var fg = KidneyGeometry();
            for (int i = 0; i < fg.pos.Count; i++)
            {
                var v = fg.pos[i] + fg.nor[i] * (0.009f + 0.0018f * (Rand() - 0.5f));
                if (Vector3.Distance(v, C) < TUMOR.R + 0.006f) v = C + (v - C).N() * (TUMOR.R + 0.006f);
                fg.pos[i] = v;
            }
            fg.ComputeNormals();
            var fatMat = Mats.Tissue(new MatOpts { tex = tex["fat"], roughness = 0.32f, side = Side.Double }, true, 0.01f);
            var fatPeri = Part.Make("fatPeri", fg, fatMat, KLt);
            Sys("fat", fatPeri.gameObject);
            meshes["fatPeri"] = fatPeri;

            // Hiler yağ dokusu
            var hilarFat = Part.Make("fatHilar", Geo.Ellipsoid(0.034f, 0.025f, 0.021f, 48, 0.05f),
                Mats.Tissue(new MatOpts { tex = tex["fat"], color = 0xfff2d8, roughness = 0.32f, side = Side.Double }, true, 0.01f), root);
            hilarFat.transform.localPosition = V(0.045f, 0.0f, 0.027f); hilarFat.transform.localRotation = EulerXYZ(0, 0.55f, 0);
            Add(hilarFat, "fat"); meshes["fatHilar"] = hilarFat;

            // Retroperitoneal yağ (üreter/gonadal ven çevresi, ince)
            var rpFat = Part.Make("fatRetro", Geo.Ellipsoid(0.022f, 0.06f, 0.006f, 40, 0.1f),
                Mats.Tissue(new MatOpts { tex = tex["fat"], color = 0xfff4ea, roughness = 0.3f, transparent = true, opacity = 0.3f, side = Side.Double, depthWrite = false }, true), root);
            rpFat.transform.localPosition = V(0.05f, -0.085f, 0.032f); Add(rpFat, "fat"); meshes["fatRetro"] = rpFat;

            // Orta hat retroperitoneal yağ (aorta/VCI/vertebra önü; sol LPN'de diseke edilmez)
            var midFat = Mesh("midFat", Geo.Ellipsoid(0.036f, 0.13f, 0.022f, 40, 0.06f), Std(0xfff0dc, 0.35f, tex["fat"]));
            midFat.transform.localPosition = V(-0.006f, -0.04f, 0.034f); Add(midFat, "fat"); meshes["midFat"] = midFat;

            // ------------------------------------------------------------------ GİS organları
            var spleen = Mesh("spleen", Geo.Ellipsoid(0.035f, 0.06f, 0.02f, 40, 0.04f), Std(0xd8607e, 0.3f, tex["organ"]));
            spleen.transform.localPosition = V(0.103f, 0.1f, -0.012f); spleen.transform.localRotation = EulerXYZ(0.5f, 0.6f, -0.35f);
            Add(spleen, "gi"); Label("Dalak (Splen)", V(0.11f, 0.11f, 0.012f), "gi");
            panc = Mesh("panc", Geo.TaperTube(L(V(-0.035f, -0.005f, 0.075f), V(0.0f, 0.02f, 0.085f), V(0.04f, 0.04f, 0.066f), V(0.075f, 0.065f, 0.036f), V(0.09f, 0.08f, 0.018f)), t => 0.014f - 0.005f * t, 48, 14), Std(0xf6d6a8, 0.55f, tex["fat"]));
            meshes["panc"] = panc; Add(panc, "gi"); Label("Pankreas kuyruğu", V(0.075f, 0.065f, 0.045f), "gi");
            var stomach = Mesh("stomach", Geo.Ellipsoid(0.05f, 0.07f, 0.035f, 40), Std(0xd99a8e, 0.35f, tex["organ"]));
            stomach.transform.localPosition = V(0.045f, 0.13f, 0.075f); stomach.transform.localRotation = EulerXYZ(0.3f, 0, 0.5f); Add(stomach, "gi");
            Label("Mide", V(0.04f, 0.14f, 0.11f), "gi");
            var liver = Mesh("liver", Geo.Ellipsoid(0.1f, 0.07f, 0.075f, 48, 0.02f), Std(0x7a2e22, 0.3f, tex["organ"]));
            liver.transform.localPosition = V(-0.075f, 0.14f, 0.035f); liver.transform.localRotation = EulerXYZ(0, 0, 0.25f); Add(liver, "gi");
            Label("Karaciğer", V(-0.08f, 0.15f, 0.1f), "gi");

            // İnce barsak ansları (sağ lateral dekübitte yerçekimiyle mediale düşmüş)
            var sbM = Std(0xf0b8a8, 0.3f, tex["colon"]);
            for (int k = 0; k < 3; k++)
            {
                var pts = new List<Vector3>(); var p = V(-0.05f + k * 0.012f, -0.03f - k * 0.04f, 0.08f);
                var dir = V(1, 0, 0);
                for (int i = 0; i < 14; i++)
                {
                    pts.Add(p);
                    dir = CatmullRom.RotateAxis(dir, V(0, 0, 1), (Rand() - 0.5f) * 2.2f);
                    dir = CatmullRom.RotateAxis(dir, V(1, 0, 0), (Rand() - 0.5f) * 0.6f).N();
                    p += dir * 0.025f;
                    p.x = Mathf.Min(-0.008f, Mathf.Max(-0.1f, p.x)); p.y = Mathf.Min(0.0f, Mathf.Max(-0.17f, p.y)); p.z = Mathf.Min(0.11f, Mathf.Max(0.055f, p.z));
                }
                Add(Mesh("smallBowel", Geo.TaperTube(pts, 0.011f, 120, 12), sbM), "gi");
            }
            Label("İnce barsak", V(-0.05f, -0.08f, 0.1f), "gi");

            // İnen kolon + mezokolon (hareketli grup: medializasyon)
            colonGroup = Group("colonGroup", root).transform; Sys("gi", colonGroup.gameObject);
            var colonPts = L(V(0.05f, 0.065f, 0.085f), V(0.085f, 0.085f, 0.06f), V(0.103f, 0.088f, 0.038f), V(0.108f, 0.05f, 0.031f), V(0.108f, 0.0f, 0.03f), V(0.107f, -0.06f, 0.031f), V(0.103f, -0.12f, 0.036f), V(0.092f, -0.17f, 0.043f), V(0.065f, -0.2f, 0.055f));
            var colonMat = Mats.Tissue(new MatOpts { tex = tex["colon"], roughness = 0.3f }, false, 0.012f);
            var colon = Part.Make("colon", Geo.TaperTube(colonPts, t => 0.0165f * (1 + 0.12f * Mathf.Sin(t * 95)), 200, 18), colonMat, colonGroup);
            meshes["colon"] = colon;
            curves["colon"] = new VesselCurve { pts = Geo.SamplePoints(colonPts, 60), r = 0.0165f, mesh = colon };
            Label("Kolon descendens", V(0.118f, -0.03f, 0.055f), "gi");
            Label("Fleksura splenika", V(0.11f, 0.088f, 0.06f), "gi");
            // Mezokolon (Gerota fasyası önünde peritoneal örtü)
            var mg = Geo.PlaneXY(1, 1, 30, 40).MapPositions((v, i) =>
            {
                float uu = v.x + 0.5f, vv = v.y + 0.5f;
                float y = -0.13f + vv * 0.2f;
                float xl = 0.108f - 0.012f, xm = 0.02f;
                float x = xm + (xl - xm) * uu;
                float z = 0.044f + 0.008f * Mathf.Sin(Mathf.PI * uu) + 0.003f * Mathf.Sin(y * 80) * Mathf.Sin(x * 60);
                return V(x, y, z);
            }).ComputeNormals();
            var meso = Part.Make("mesocolon", mg, Mats.Tissue(new MatOpts { tex = tex["peritoneum"], roughness = 0.25f, side = Side.Double }, true, 0.012f), colonGroup);
            meshes["meso"] = meso;
            Label("Mezokolon", V(0.06f, -0.06f, 0.05f), "gi");

            // Lateral parietal periton (kolon ile yan duvar arası) + Toldt beyaz çizgisi
            var lg = Geo.PlaneXY(1, 1, 8, 40).MapPositions((v, i) =>
            {
                float uu = v.x + 0.5f, vv = v.y + 0.5f;
                // parakolik oluk: kolon lateral kenarı → oluk tabanı → yan karın duvarı (kuadratik Bezier)
                float y = -0.135f + vv * 0.215f;
                float w0 = (1 - uu) * (1 - uu), w1 = 2 * uu * (1 - uu), w2 = uu * uu;
                return V(w0 * 0.124f + w1 * 0.15f + w2 * 0.153f, y, w0 * 0.03f + w1 * 0.01f + w2 * 0.05f + 0.0015f * Mathf.Sin(y * 90));
            }).ComputeNormals();
            var latPer = Part.Make("latPeritoneum", lg, Mats.Tissue(new MatOpts { tex = tex["peritoneum"], color = 0xfbe4da, roughness = 0.25f, side = Side.Double }, true, 0.01f), root);
            Add(latPer, "gi"); meshes["latPer"] = latPer;
            toldt = new List<Vector3>();
            for (int i = 0; i <= 10; i++) { float y = 0.065f - i * 0.019f; toldt.Add(V(0.1411f, y, 0.0244f + 0.0015f * Mathf.Sin(y * 90))); } // oluk üzerinde u=0.4
            var toldtP = Mesh("toldt", Geo.TaperTube(toldt, 0.0016f, 60, 6), Mats.Std(new MatOpts { color = 0xfffaf0, roughness = 0.3f, emissive = 0x777066 }));
            Add(toldtP, "gi"); meshes["toldt"] = toldtP;
            Label("Toldt beyaz çizgisi", V(0.137f, 0.0f, 0.03f), "gi");

            // Perirenal yağ etiketi
            Label("Gerota fasyası / perirenal yağ", KL(0.035f, -0.0f, 0.012f), "fat");

            // ------------------------------------------------------------------ Karın duvarı
            var skin = Mesh("skin", TorsoGeometry(0), Std(0xc8916a, 0.6f)); // povidon-iyot boyalı cilt
            Add(skin, "wall"); meshes["skin"] = skin;
            var wall = Mesh("innerWall", TorsoGeometry(TORSO.wall), Std(0xffffff, 0.32f, tex["wall"], Side.Back), null, Side.Back);
            Add(wall, "wall"); meshes["wall"] = wall;

            // Etkileşim/ışın testi için yüzeyler
            pickables.AddRange(new[] { kidneyL, tumorP, fatPeri, hilarFat, midFat, colon, meso, latPer, ureter, wall,
                curves["renalVein"].mesh, curves["renalArtery"].mesh, curves["gonadal"].mesh, curves["aorta"].mesh, curves["adrenalVein"].mesh, curves["lumbarVein"].mesh });
        }

        public void RebuildUreter(List<Vector3> ctrl)
        {
            meshes["ureter"].SetMesh(Geo.TaperTube(ctrl, 0.0026f, 64, 10));
            curves["ureter"].pts = Geo.SamplePoints(ctrl, 50);
        }

        public Part K => meshes["kidneyL"];

        // Sistem görünürlüğü (atlas)
        public void SetSystem(string name, bool v)
        {
            if (systems.TryGetValue(name, out var l)) foreach (var o in l) o.SetActive(v);
        }
    }
}
