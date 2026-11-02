// Ameliyathane: masa, hasta (60° sağ lateral dekübit, sol taraf yukarıda), örtüler, trokarlar, monitör — room.js karşılığı.
using System.Collections.Generic;
using UnityEngine;
using static LPNVR.U;

namespace LPNVR
{
    public class Port { public Vector3 local, normal, dir; public Transform obj; }

    public class Room
    {
        // Transperitoneal sol LPN port yerleşimi (hasta-yerel):
        //  kamera: umbilikus lateral/kraniyali (12 mm), sol el: subkostal midklaviküler (12 mm),
        //  sağ el: umbilikus altı lateral rektus kenarı (12 mm), asistan: suprapubik/medial (5 mm)
        public static readonly Dictionary<string, (float x, float y, float d)> PORTS = new Dictionary<string, (float, float, float)>
        {
            ["camera"] = (0.032f, -0.03f, 12),
            ["left"] = (0.055f, 0.095f, 12),
            ["right"] = (0.092f, -0.115f, 12),
            ["assist"] = (-0.01f, -0.14f, 5),
        };

        public Transform root, env, patient, monitor, panelAnchor, torsoDrape;
        public readonly Dictionary<string, Port> ports = new Dictionary<string, Port>();

        // Negatif ölçekli FlipRoot altında, net dönüşümü "normal" Unity çerçevesi olan çocuk (UI, ekran, metin için)
        public static Transform CounterFlip(Transform parent, string name = "unflip")
        {
            var t = Group(name, parent).transform;
            t.localScale = new Vector3(1, 1, -1);
            return t;
        }

        public Room(Transform sceneRoot, Anatomy A)
        {
            root = Group("orRoot", sceneRoot).transform;

            // Oda (sabit; kalibrasyonda hareket etmez) — modern laminer akımlı ameliyathane
            env = Group("env", sceneRoot).transform;
            BuildEnvironment(env);

            // Hasta grubu: X ekseni kraniyal → +Y'ye; 30° yuvarlanma (60° lateral dekübit)
            patient = Group("patient", root).transform;
            var qBase = Quaternion.LookRotation(V(0, 0, 1), V(-1, 0, 0)); // makeBasis((0,1,0), (-1,0,0), (0,0,1))
            var qRoll = Quaternion.AngleAxis(-30, Vector3.right);
            patient.localRotation = qRoll * qBase;
            patient.localPosition = V(0, 1.0f, -0.55f);
            A.root.SetParent(patient, false);
            Vector3 PW(Vector3 p) => patient.localPosition + patient.localRotation * p; // hasta-yerel → oda (S) uzayı

            // Masa: hastanın en alt noktasının altına
            float minY = float.MaxValue;
            for (int i = 0; i < 64; i++)
            {
                float th = i / 64f * Mathf.PI * 2;
                minY = Mathf.Min(minY, PW(V(TORSO.a * Mathf.Sin(th), 0, TORSO.cz + TORSO.b * Mathf.Cos(th))).y);
            }
            var centerW = PW(V(-0.1f, 0, 0));
            var tableM = M("steel", 0xd0d5da, 0.35f, 0.85f, 3, 1);
            var top = Part.Make("tableTop", Geo.Box(2.0f, 0.08f, 0.55f), M("mattress", 0xffffff, 0.8f, 0, 3, 1), root);
            top.transform.localPosition = V(centerW.x - 0.25f, minY - 0.04f, centerW.z);
            // masa çerçevesi ve yan raylar (aksesuar rayı)
            var frame = Part.Make("tableFrame", Geo.Box(2.02f, 0.05f, 0.5f), tableM, root);
            frame.transform.localPosition = top.transform.localPosition + V(0, -0.065f, 0);
            foreach (float sz in new[] { -0.29f, 0.29f })
            {
                var rail = Part.Make("tableRail", Geo.Box(1.9f, 0.03f, 0.012f), tableM, root);
                rail.transform.localPosition = top.transform.localPosition + V(0, -0.05f, sz);
            }
            var col = Part.Make("tableColumn", Geo.Cylinder(0.12f, 0.16f, 2.0f, 24), tableM, root); // uzun: kalibrasyonda yere gömülür
            col.transform.localPosition = V(centerW.x - 0.25f, minY - 0.08f - 1.0f, centerW.z);

            // Örtüler (baş ve bacak tarafı) + gövde örtüsü pencereli
            var drapeM = M("drape", 0xffffff, 0.9f, 0, 2, 2, Side.Double);
            var head = Part.Make("drapeHead", Geo.Box(0.32f, 0.36f, 0.42f), drapeM, root);
            var hp = PW(V(-0.04f, 0.47f, 0.0f)); hp.y = minY + 0.18f; head.transform.localPosition = hp;
            var screenD = Part.Make("anesthesiaScreen", Geo.Box(0.02f, 0.55f, 0.6f), drapeM, root);
            var sp = PW(V(0, 0.33f, 0)); sp.y = minY + 0.3f; screenD.transform.localPosition = sp;
            var legs = Part.Make("drapeLegs", Geo.Box(0.85f, 0.26f, 0.5f), drapeM, root);
            var lp = PW(V(-0.04f, -0.74f, 0.0f)); lp.y = minY + 0.13f; legs.transform.localPosition = lp;

            // Gövde örtüsü: cilt geometrisinin 4 mm dışı; ameliyat penceresindeki üçgenler çıkarılır
            var dg = Anatomy.TorsoGeometry(0);
            for (int i = 0; i < dg.pos.Count; i++) dg.pos[i] += dg.nor[i] * 0.004f;
            var keep = new List<int>();
            for (int i = 0; i < dg.idx.Count; i += 3)
            {
                var c = (dg.pos[dg.idx[i]] + dg.pos[dg.idx[i + 1]] + dg.pos[dg.idx[i + 2]]) / 3;
                bool window = c.z > 0.02f && c.x > -0.06f && c.x < 0.15f && c.y > -0.19f && c.y < 0.15f;
                if (!window) keep.AddRange(new[] { dg.idx[i], dg.idx[i + 1], dg.idx[i + 2] });
            }
            dg.idx = keep;
            var td = Part.Make("torsoDrape", dg, M("drape", 0xffffff, 0.9f, 0, 5, 5), A.root);
            torsoDrape = td.transform;

            // Trokarlar
            var target = V(0.08f, -0.01f, 0.01f);
            var trocM = Mats.Std(0x1b1b1b, 0.4f, 0.2f);
            var capM = Mats.Std(0x3a8fd0, 0.5f);
            foreach (var kv in PORTS)
            {
                var P = kv.Value;
                Anatomy.AnteriorSurface(P.x, P.y, out var p, out var n);
                var dir = (target - p).N();
                if (kv.Key == "assist") dir = (V(0.06f, 0.06f, 0) - p).N();
                var g = Group("port-" + kv.Key, A.root).transform;
                g.localPosition = p;
                g.localRotation = Quaternion.FromToRotation(V(0, 0, 1), -dir);
                float r = P.d / 2000 + 0.0012f;
                var can = Part.Make("cannula", Geo.Cylinder(r, r, 0.11f, 20, 1, true), Mats.Std(new MatOpts { color = 0x1b1b1b, roughness = 0.4f, metallic = 0.2f, side = Side.Double }), g);
                can.transform.localRotation = EulerXYZ(Mathf.PI / 2, 0, 0); can.transform.localPosition = V(0, 0, -0.025f);
                var hub = Part.Make("hub", Geo.Cylinder(r * 2.6f, r * 2.2f, 0.035f, 24), capM, g);
                hub.transform.localRotation = EulerXYZ(Mathf.PI / 2, 0, 0); hub.transform.localPosition = V(0, 0, 0.035f);
                var valve = Part.Make("valve", Geo.Cylinder(0.003f, 0.003f, 0.02f, 8), trocM, g);
                valve.transform.localPosition = V(0, r * 2.6f + 0.006f, 0.035f);
                ports[kv.Key] = new Port { local = p, normal = n, dir = dir, obj = g };
            }

            // Monitör (laparoskopik görüntü) — hastanın sırt tarafında, cerrahın karşısında
            monitor = Group("monitor", root).transform;
            Part.Make("frame", Geo.Box(1.02f, 0.6f, 0.05f), Mats.Std(0x15181b, 0.3f), monitor);
            var back = Part.Make("monBack", Geo.Box(0.4f, 0.3f, 0.08f), Mats.Std(0x2a2e33, 0.5f), monitor); back.transform.localPosition = V(0, 0, -0.06f);
            var arm = Part.Make("arm", Geo.Cylinder(0.025f, 0.025f, 1.5f, 12), tableM, monitor); arm.transform.localPosition = V(0, 1.0f, -0.08f);
            monitor.localPosition = V(0.05f, 1.55f, -1.3f);

            // Bilgi paneli
            panelAnchor = Group("panelAnchor", root).transform;
            panelAnchor.localPosition = V(-0.9f, 1.45f, -1.12f); panelAnchor.localRotation = EulerXYZ(0, 0.45f, 0);
        }

        // ---------------------------------------------------------------- ameliyathane çevresi
        static Dictionary<string, Texture2D> _rtex;
        static Dictionary<string, Texture2D> RoomTex
        {
            get
            {
                if (_rtex != null) return _rtex;
                _rtex = new Dictionary<string, Texture2D>();
                ResetSeed(7351);
                foreach (var k in new[] { "floor", "panel", "ceiling", "drape", "steel", "mattress" }) _rtex[k] = ProcTextures.Make(k, 256);
                return _rtex;
            }
        }

        static Material M(string kind, uint color, float rough, float metal = 0, float sx = 1, float sy = 1, Side side = Side.Front, uint emissive = 0)
            => Mats.Std(new MatOpts { tex = kind != null ? RoomTex[kind] : null, color = color, roughness = rough, metallic = metal, mapScale = new Vector2(sx, sy), side = side, emissive = emissive });

        static Part Bx(string n, Vector3 size, Material m, Transform par, Vector3 pos)
        {
            var p = Part.Make(n, Geo.Box(size.x, size.y, size.z), m, par); p.transform.localPosition = pos; return p;
        }
        static Part Cy(string n, float r, float h, Material m, Transform par, Vector3 pos, int seg = 20)
        {
            var p = Part.Make(n, Geo.Cylinder(r, r, h, seg), m, par); p.transform.localPosition = pos; return p;
        }
        // a → b arası çubuk
        static Part Rod(string n, Vector3 a, Vector3 b, float r, Material m, Transform par)
        {
            var d = b - a;
            var p = Part.Make(n, Geo.Cylinder(r, r, d.magnitude, 12), m, par);
            p.transform.localPosition = (a + b) / 2; p.transform.localRotation = Quaternion.FromToRotation(Vector3.up, d.N());
            return p;
        }

        static void BuildEnvironment(Transform env)
        {
            const float H = 3.2f;
            // zemin, duvarlar, tavan
            var floor = Part.Make("floor", Geo.PlaneXY(7.0f, 6.7f), M("floor", 0xffffff, 0.35f, 0, 3.5f, 3.35f), env);
            floor.transform.localRotation = EulerXYZ(-Mathf.PI / 2, 0, 0); floor.transform.localPosition = V(0, 0, 0.15f);
            var wallM = M("panel", 0xffffff, 0.6f, 0, 4, 1.6f);
            foreach (var (x, z, ry, w) in new[] { (0f, -3.2f, 0f, 7f), (-3.5f, 0.15f, Mathf.PI / 2, 6.7f), (3.5f, 0.15f, -Mathf.PI / 2, 6.7f), (0f, 3.5f, Mathf.PI, 7f) })
            {
                var wall = Part.Make("wall", Geo.PlaneXY(w, H), wallM, env);
                wall.transform.localPosition = V(x, H / 2, z); wall.transform.localRotation = EulerXYZ(0, ry, 0);
                // süpürgelik ve tampon rayı (duvarın 2 cm önünde)
                var inward = Quaternion.Euler(0, ry * Mathf.Rad2Deg, 0) * Vector3.forward;
                var rotW = EulerXYZ(0, ry, 0);
                var bb = Bx("baseboard", V(w, 0.12f, 0.02f), Mats.Std(0x5b6468, 0.5f), env, V(x, 0.06f, z) + inward * 0.01f); bb.transform.localRotation = rotW;
                var rail = Bx("bumper", V(w, 0.1f, 0.03f), M("steel", 0xc8cdd2, 0.3f, 0.8f, 6, 1), env, V(x, 0.95f, z) + inward * 0.02f); rail.transform.localRotation = rotW;
            }
            var ceil = Part.Make("ceiling", Geo.PlaneXY(7.0f, 6.7f), M("ceiling", 0xffffff, 0.8f, 0, 3.5f, 3.35f), env);
            ceil.transform.localRotation = EulerXYZ(Mathf.PI / 2, 0, 0); ceil.transform.localPosition = V(0, H, 0.15f);
            // laminer akım tavanı (masanın üstünde) + çevre aydınlatma panelleri
            Bx("laminarFlow", V(2.6f, 0.05f, 2.0f), Mats.Std(new MatOpts { color = 0xf4f7f8, roughness = 0.6f, emissive = 0xc8d0d4 }), env, V(-0.1f, H - 0.03f, -0.55f));
            Bx("laminarFrame", V(2.75f, 0.06f, 2.15f), M("steel", 0xd8dde0, 0.4f, 0.6f, 4, 3), env, V(-0.1f, H - 0.01f, -0.55f));
            var panelL = Mats.Std(new MatOpts { color = 0xffffff, roughness = 0.5f, emissive = 0xb4babd });
            foreach (var (px, pz) in new[] { (-2.3f, -2.2f), (2.3f, -2.2f), (-2.3f, 2.2f), (2.3f, 2.2f), (0f, 2.4f) })
                Bx("ceilLight", V(1.2f, 0.03f, 0.6f), panelL, env, V(px, H - 0.02f, pz));

            // kapı (sağ duvar) ve pencere
            var steel = M("steel", 0xe0e4e8, 0.35f, 0.7f, 2, 4);
            Bx("door", V(0.06f, 2.2f, 1.6f), steel, env, V(3.46f, 1.1f, -1.4f));
            Bx("doorWindow", V(0.07f, 0.45f, 0.35f), Mats.Std(new MatOpts { color = 0x1b2b33, roughness = 0.05f, wet = 1, wetRough = 0.05f }), env, V(3.45f, 1.55f, -1.4f));
            Bx("doorFrame", V(0.08f, 0.06f, 1.7f), Mats.Std(0x5b6468, 0.5f), env, V(3.46f, 2.23f, -1.4f));

            // duvar dolapları (arka duvar), cam kapaklı
            var glass = Mats.Std(new MatOpts { color = 0x2a4048, roughness = 0.05f, wet = 1, wetRough = 0.04f });
            foreach (float cx in new[] { -2.1f, 1.9f })
            {
                Bx("cabinet", V(1.6f, 1.9f, 0.45f), M("steel", 0xeceff1, 0.4f, 0.5f, 2, 2), env, V(cx, 1.3f, -2.95f));
                Bx("cabinetGlassL", V(0.74f, 1.6f, 0.02f), glass, env, V(cx - 0.39f, 1.35f, -2.72f));
                Bx("cabinetGlassR", V(0.74f, 1.6f, 0.02f), glass, env, V(cx + 0.39f, 1.35f, -2.72f));
            }
            // dijital saat / zamanlayıcı
            Bx("clock", V(0.6f, 0.22f, 0.04f), Mats.Std(new MatOpts { color = 0x101214, roughness = 0.2f, emissive = 0x3a0a08 }), env, V(-0.2f, 2.45f, -3.17f));

            // cerrahi lambalar (tavan göbeği + iki kollu)
            var lampM = Mats.Std(new MatOpts { color = 0xeef1f3, roughness = 0.35f });
            var armM = M("steel", 0xdfe3e7, 0.3f, 0.6f, 1, 4);
            var hub = V(0.1f, H, -0.6f);
            Cy("lampHub", 0.08f, 0.3f, armM, env, hub - V(0, 0.15f, 0));
            var target = V(0.02f, 1.0f, -0.55f);
            foreach (var head in new[] { V(-0.45f, 2.15f, -0.85f), V(0.65f, 2.2f, -0.25f) })
            {
                var elbow = V(head.x, H - 0.45f, head.z);
                Rod("lampArm", hub - V(0, 0.3f, 0), elbow, 0.025f, armM, env);
                Rod("lampDrop", elbow, head + V(0, 0.12f, 0), 0.02f, armM, env);
                var g = Group("surgicalLight", env).transform; g.localPosition = head;
                g.localRotation = Quaternion.FromToRotation(Vector3.down, (target - head).N());
                Part.Make("dome", Geo.Sphere(0.33f, 32, 12, 0, Mathf.PI * 2, 0, Mathf.PI * 0.32f), lampM, g).transform.localPosition = V(0, -0.22f, 0);
                var face = Cy("lampFace", 0.29f, 0.012f, Mats.Std(new MatOpts { color = 0xffffff, roughness = 0.2f, emissive = 0xfff2dc }), g, V(0, -0.005f, 0), 32);
                // LED modülleri
                for (int i = 0; i < 7; i++)
                {
                    float a = i / 7f * Mathf.PI * 2;
                    Cy("led", 0.045f, 0.01f, Mats.Std(new MatOpts { color = 0xffffff, emissive = 0xffffff }), g, V(Mathf.Cos(a) * 0.18f, -0.013f, Mathf.Sin(a) * 0.18f), 16);
                }
                Cy("lampHandle", 0.022f, 0.12f, Mats.Std(0x2e7d6b, 0.4f), g, V(0, -0.07f, 0), 12);
            }

            // anestezi cihazı (baş tarafı)
            var anM = Mats.Std(new MatOpts { color = 0xe2e7ea, roughness = 0.45f });
            var an = Group("anesthesia", env).transform; an.localPosition = V(-1.45f, 0, -1.05f); an.localRotation = EulerXYZ(0, 0.5f, 0);
            Bx("anBody", V(0.75f, 0.95f, 0.6f), anM, an, V(0, 0.55f, 0));
            Bx("anDesk", V(0.8f, 0.03f, 0.35f), Mats.Std(0x3b4248, 0.5f), an, V(0, 1.04f, 0.15f));
            Bx("anTower", V(0.6f, 0.5f, 0.35f), anM, an, V(0, 1.3f, -0.1f));
            Bx("anScreen", V(0.5f, 0.34f, 0.03f), Mats.Std(new MatOpts { color = 0x0b0f12, roughness = 0.1f, emissive = 0x0d2a26, wet = 1, wetRough = 0.05f }), an, V(0, 1.75f, -0.02f));
            Bx("anScreenBack", V(0.54f, 0.38f, 0.06f), Mats.Std(0x30363c, 0.5f), an, V(0, 1.75f, -0.07f));
            foreach (var (vx, col) in new[] { (-0.18f, 0xd9b52bu), (0.0f, 0xa24fb0u) }) Bx("vaporizer", V(0.12f, 0.2f, 0.16f), Mats.Std(col, 0.4f), an, V(vx, 1.15f, 0.12f));
            Cy("bellows", 0.07f, 0.22f, Mats.Std(new MatOpts { color = 0xcfe6ee, roughness = 0.1f, wet = 0.8f }), an, V(0.25f, 1.16f, 0.12f));
            foreach (float wx in new[] { -0.32f, 0.32f }) foreach (float wz in new[] { -0.25f, 0.25f })
                    Cy("wheel", 0.05f, 0.04f, Mats.Std(0x1d1f22, 0.6f), an, V(wx, 0.05f, wz), 12).transform.localRotation = EulerXYZ(0, 0, Mathf.PI / 2);
            // solunum devresi (cihaz → baş örtüsü)
            Part.Make("circuit", Geo.TaperTube(new List<Vector3> { V(-1.2f, 1.15f, -0.85f), V(-1.0f, 1.25f, -0.7f), V(-0.75f, 1.2f, -0.6f), V(-0.58f, 1.1f, -0.55f) }, 0.014f, 32, 10),
                Mats.Std(new MatOpts { color = 0x8ec3dd, roughness = 0.2f, wet = 0.6f }), env);

            // laparoskopi kulesi (monitörün altında): kamera ünitesi, ışık kaynağı, insuflatör, koter
            var tw = Group("lapTower", env).transform; tw.localPosition = V(0.05f, 0, -1.3f);
            var towerM = Mats.Std(new MatOpts { color = 0x2a2e33, roughness = 0.45f });
            Bx("towerBody", V(0.64f, 1.1f, 0.6f), towerM, tw, V(0, 0.6f, -0.02f));
            var unitM = Mats.Std(new MatOpts { color = 0x3b4148, roughness = 0.35f });
            float[] uy = { 0.32f, 0.52f, 0.72f, 0.92f };
            uint[] led = { 0x2aff7a, 0x4ab8ff, 0xffd23a, 0x4ab8ff };
            for (int i = 0; i < uy.Length; i++)
            {
                Bx("unit", V(0.58f, 0.15f, 0.03f), unitM, tw, V(0, uy[i], 0.29f));
                Bx("display", V(0.16f, 0.07f, 0.01f), Mats.Std(new MatOpts { color = 0x05080a, emissive = led[i] & 0x7f7f7f }), tw, V(-0.14f, uy[i] + 0.01f, 0.305f));
                Bx("led", V(0.02f, 0.02f, 0.01f), Mats.Std(new MatOpts { color = 0x05080a, emissive = led[i] }), tw, V(0.2f, uy[i], 0.305f));
            }
            foreach (float wx in new[] { -0.27f, 0.27f }) foreach (float wz in new[] { -0.25f, 0.2f })
                    Cy("wheel", 0.05f, 0.04f, Mats.Std(0x1d1f22, 0.6f), tw, V(wx, 0.05f, wz), 12).transform.localRotation = EulerXYZ(0, 0, Mathf.PI / 2);

            // serum askısı
            var iv = Group("ivPole", env).transform; iv.localPosition = V(-1.0f, 0, -0.1f);
            Cy("pole", 0.012f, 2.0f, armM, iv, V(0, 1.0f, 0), 10);
            for (int i = 0; i < 5; i++) { float a = i / 5f * Mathf.PI * 2; Rod("leg", V(0, 0.08f, 0), V(Mathf.Cos(a) * 0.28f, 0.03f, Mathf.Sin(a) * 0.28f), 0.012f, armM, iv); }
            Rod("hook", V(-0.12f, 1.95f, 0), V(0.12f, 1.95f, 0), 0.008f, armM, iv);
            Bx("ivBag", V(0.13f, 0.22f, 0.04f), Mats.Std(new MatOpts { color = 0xe8f4f8, roughness = 0.1f, wet = 0.9f }), iv, V(0.1f, 1.8f, 0));
            Rod("ivLine", V(0.1f, 1.68f, 0), V(0.35f, 1.1f, -0.25f), 0.003f, Mats.Std(0xdfeaf0, 0.2f), iv);

            // Mayo masası (alet tepsisi) ve arka masa
            var mayo = Group("mayoStand", env).transform; mayo.localPosition = V(1.25f, 0, 0.1f);
            Cy("mayoLeg", 0.02f, 1.0f, armM, mayo, V(0, 0.5f, 0), 10);
            Bx("mayoBase", V(0.5f, 0.03f, 0.4f), armM, mayo, V(0, 0.03f, 0));
            Bx("mayoTray", V(0.55f, 0.025f, 0.38f), steel, mayo, V(0, 1.0f, 0));
            Bx("mayoTowel", V(0.5f, 0.01f, 0.33f), M("drape", 0x9fd8c0, 0.9f, 0, 1, 1), mayo, V(0, 1.018f, 0));
            var instM = M("steel", 0xf0f2f4, 0.2f, 0.95f, 1, 1);
            for (int i = 0; i < 5; i++) Bx("instrument", V(0.2f, 0.006f, 0.012f), instM, mayo, V(-0.05f + (i % 2) * 0.03f, 1.027f, -0.12f + i * 0.06f));
            var back = Group("backTable", env).transform; back.localPosition = V(2.0f, 0, 0.95f);
            Bx("backTableTop", V(1.3f, 0.85f, 0.65f), M("drape", 0xffffff, 0.9f, 0, 2, 2), back, V(0, 0.45f, 0));
            Cy("basin", 0.14f, 0.07f, instM, back, V(-0.35f, 0.91f, 0), 24);
            Cy("basin2", 0.09f, 0.06f, instM, back, V(0.05f, 0.9f, 0.12f), 24);
            Bx("tray", V(0.45f, 0.04f, 0.3f), instM, back, V(0.35f, 0.9f, -0.05f));

            // kick bucket, ayak pedalı (koter)
            Cy("bucket", 0.18f, 0.35f, steel, env, V(1.0f, 0.18f, 0.7f), 20);
            Bx("footPedal", V(0.3f, 0.06f, 0.2f), Mats.Std(0x2b6fb3, 0.5f), env, V(0.45f, 0.03f, 0.25f));
        }
    }
}
