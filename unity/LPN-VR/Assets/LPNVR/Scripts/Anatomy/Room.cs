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

            // Oda (sabit; kalibrasyonda hareket etmez)
            env = Group("env", sceneRoot).transform;
            var floor = Part.Make("floor", Geo.PlaneXY(8, 8), Mats.Std(0x5f7f86, 0.8f), env);
            floor.transform.localRotation = EulerXYZ(-Mathf.PI / 2, 0, 0);
            var wallM = Mats.Std(0x9fbfc2, 0.9f);
            foreach (var (x, z, ry) in new[] { (0f, -3.2f, 0f), (-3.5f, 0f, Mathf.PI / 2), (3.5f, 0f, -Mathf.PI / 2), (0f, 3.5f, Mathf.PI) })
            {
                var w = Part.Make("wall", Geo.PlaneXY(8, 3.2f), wallM, env);
                w.transform.localPosition = V(x, 1.6f, z); w.transform.localRotation = EulerXYZ(0, ry, 0);
            }
            var lampM = Mats.Std(new MatOpts { color = 0xffffff, emissive = 0xcccccc });
            foreach (float x in new[] { -1.2f, 1.2f })
            {
                var l = Part.Make("lamp", Geo.Cylinder(0.28f, 0.32f, 0.08f, 32), lampM, env);
                l.transform.localPosition = V(x, 2.4f, -0.6f);
            }

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
            var tableM = Mats.Std(0x2b3238, 0.5f, 0.3f);
            var top = Part.Make("tableTop", Geo.Box(2.0f, 0.08f, 0.55f), Mats.Std(0x1d4f73, 0.7f), root);
            top.transform.localPosition = V(centerW.x - 0.25f, minY - 0.04f, centerW.z);
            var col = Part.Make("tableColumn", Geo.Cylinder(0.12f, 0.16f, 2.0f, 24), tableM, root); // uzun: kalibrasyonda yere gömülür
            col.transform.localPosition = V(centerW.x - 0.25f, minY - 0.08f - 1.0f, centerW.z);

            // Örtüler (baş ve bacak tarafı) + gövde örtüsü pencereli
            var drapeM = Mats.Std(0x2f6fa8, 0.85f, 0, null, Side.Double);
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
            var td = Part.Make("torsoDrape", dg, Mats.Std(0x2f6fa8, 0.85f), A.root);
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
            Part.Make("frame", Geo.Box(1.02f, 0.6f, 0.05f), Mats.Std(0x111111, 0.4f), monitor);
            var arm = Part.Make("arm", Geo.Cylinder(0.02f, 0.02f, 1.0f, 12), tableM, monitor); arm.transform.localPosition = V(0, 0.75f, -0.05f);
            monitor.localPosition = V(0.05f, 1.55f, -1.3f);

            // Bilgi paneli
            panelAnchor = Group("panelAnchor", root).transform;
            panelAnchor.localPosition = V(-0.9f, 1.45f, -1.12f); panelAnchor.localRotation = EulerXYZ(0, 0.45f, 0);
        }
    }
}
