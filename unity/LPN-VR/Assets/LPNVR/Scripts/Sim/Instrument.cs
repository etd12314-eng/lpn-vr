// Laparoskopik aletler ve trokar fulkrum kinematiği — instruments.js karşılığı.
// Kol (kontrolcü) trokarın dışında; uç = T + dir * (L - |H - T|), dir = (T - H)/|T - H|
// => klasik "fulkrum etkisi": el sağa giderse alet ucu sola gider.
// Aletler normal Unity dünyasında durur (FlipRoot dışında); konumlar dünya uzayındadır.
using System.Collections.Generic;
using UnityEngine;
using static LPNVR.U;

namespace LPNVR
{
    public class ToolInfo
    {
        public string tr; public string energy; // null, "mono", "bi"
        public ToolInfo(string tr, string energy = null) { this.tr = tr; this.energy = energy; }
    }

    public class Instrument
    {
        public const float SHAFT = 0.36f; // kol pivotundan uca (m) — 33 cm şaft + kol ofseti

        public static readonly Dictionary<string, ToolInfo> TOOLS = new Dictionary<string, ToolInfo>
        {
            ["grasper"] = new ToolInfo("Fenestre grasper"),
            ["hook"] = new ToolInfo("Hook koter (monopolar)", "mono"),
            ["scissors"] = new ToolInfo("Soğuk makas"),
            ["bipolar"] = new ToolInfo("Maryland bipolar", "bi"),
            ["bulldog"] = new ToolInfo("Bulldog klemp aplikatörü"),
            ["needle"] = new ToolInfo("Portegü (V-Loc 3/0)"),
            ["clip"] = new ToolInfo("Hem-o-lok klip aplikatörü"),
            ["suction"] = new ToolInfo("Aspiratör-irrigatör"),
            ["us"] = new ToolInfo("Laparoskopik US probu"),
        };
        public static readonly Dictionary<string, string[]> TOOLSETS = new Dictionary<string, string[]>
        {
            ["left"] = new[] { "grasper", "suction", "bipolar", "us" },
            ["right"] = new[] { "hook", "scissors", "bipolar", "bulldog", "needle", "clip", "suction", "us" },
        };

        static Material _black, _steel, _gold, _blueIns, _handle, _purple, _bulldogM;
        static void InitMats()
        {
            if (_black) return;
            _black = Mats.Std(0x151515, 0.45f, 0.2f);
            _steel = Mats.Std(0xc8ccd2, 0.25f, 0.9f);
            _gold = Mats.Std(0xd8b25a, 0.3f, 0.9f);
            _blueIns = Mats.Std(0x2e5fb3, 0.35f, 0.1f);
            _handle = Mats.Std(0x3c3f46, 0.6f, 0.1f);
            _purple = Mats.Std(0x7b3fc4, 0.4f);
            _bulldogM = Mats.Std(0xd0d4d8, 0.3f, 0.8f);
        }

        class Jaw { public Transform a, b; public void Set(float open) { a.localRotation = EulerXYZ(-open, 0, 0); b.localRotation = EulerXYZ(open, 0, 0); } }

        static Jaw JawPair(Transform parent, float len, float w, Material mat, float curve = 0)
        {
            var g = Group("jaws", parent).transform;
            var geo = Geo.Box(w, w * 0.45f, len).Translate(V(0, w * 0.22f, len / 2));
            if (curve != 0) geo.MapPositions((p, i) => V(p.x + curve * Mathf.Pow(p.z / len, 2), p.y, p.z)).ComputeNormals();
            var a = Group("a", g).transform; var b = Group("b", g).transform;
            Part.Make("jawA", geo, mat, a);
            Part.Make("jawB", geo.Clone().Scale(V(1, -1, 1)), mat, b);
            var j = new Jaw { a = a, b = b }; j.Set(0.42f);
            return j;
        }

        public static GameObject MakeBulldog(Transform parent)
        {
            InitMats();
            var g = Group("bulldog", parent);
            var j1 = Part.Make("j1", Geo.Box(0.0022f, 0.0012f, 0.014f), _bulldogM, g.transform); j1.transform.localPosition = V(0, 0.0012f, 0);
            var j2 = Part.Make("j2", Geo.Box(0.0022f, 0.0012f, 0.014f), _bulldogM, g.transform); j2.transform.localPosition = V(0, -0.0012f, 0);
            var spring = Part.Make("spring", Geo.Torus(0.0025f, 0.0008f, 8, 16), _bulldogM, g.transform);
            spring.transform.localPosition = V(0, 0, -0.009f); spring.transform.localRotation = EulerXYZ(0, Mathf.PI / 2, 0);
            return g;
        }
        public static GameObject MakeHemolok(Transform parent)
        {
            InitMats();
            var g = Group("hemolok", parent);
            var m = Part.Make("clip", Geo.Torus(0.0035f, 0.0009f, 6, 14, Mathf.PI * 1.2f), _purple, g.transform);
            m.transform.localRotation = EulerXYZ(Mathf.PI / 2, 0, 0);
            return g;
        }

        class Tool { public GameObject obj; public float tipOff; public Jaw jaw; public GameObject load; }

        public readonly string hand;
        public readonly Transform root;
        public Vector3 tipWorld, dir = V(0, 0, -1), handleWorld;
        public float trigger, insertion = 0.1f;
        public string tool;
        readonly Dictionary<string, Tool> tools = new Dictionary<string, Tool>();
        readonly Transform tipGroup, lever;
        Tool cur;

        public float TipOff => cur.tipOff;

        public Instrument(Transform parent, string hand)
        {
            InitMats();
            this.hand = hand;
            root = Group("instr-" + hand, parent).transform;
            // şaft (+Z yönü uca doğru)
            var shaft = Part.Make("shaft", Geo.Cylinder(0.0026f, 0.0026f, SHAFT - 0.05f, 16), _black, root);
            shaft.transform.localRotation = EulerXYZ(Mathf.PI / 2, 0, 0); shaft.transform.localPosition = V(0, 0, 0.05f + (SHAFT - 0.05f) / 2);
            // kol (tabanca kabza)
            var h = Group("handle", root).transform;
            var body = Part.Make("body", Geo.Box(0.02f, 0.03f, 0.06f), _handle, h); body.transform.localPosition = V(0, 0, 0.025f);
            var grip = Part.Make("grip", Geo.Box(0.018f, 0.08f, 0.026f), _handle, h); grip.transform.localPosition = V(0, -0.05f, 0); grip.transform.localRotation = EulerXYZ(-0.25f, 0, 0);
            var ring = Part.Make("ring", Geo.Torus(0.014f, 0.004f, 8, 20), _handle, h); ring.transform.localPosition = V(0, -0.05f, 0.035f); ring.transform.localRotation = EulerXYZ(0, Mathf.PI / 2, 0);
            lever = ring.transform;
            var knob = Part.Make("knob", Geo.Cylinder(0.008f, 0.008f, 0.006f, 16), Mats.Std(0x2277cc), h);
            knob.transform.localRotation = EulerXYZ(Mathf.PI / 2, 0, 0); knob.transform.localPosition = V(0, 0, 0.058f);
            tipGroup = Group("tip", root).transform; tipGroup.localPosition = V(0, 0, SHAFT);
            BuildTools();
            Set(hand == "left" ? "grasper" : "hook");
        }

        void BuildTools()
        {
            Tool Mk(string name, float tipOff)
            {
                var t = new Tool { obj = Group(name, tipGroup), tipOff = tipOff };
                tools[name] = t; t.obj.SetActive(false); return t;
            }
            { var t = Mk("grasper", 0.012f); t.jaw = JawPair(t.obj.transform, 0.019f, 0.0042f, _steel); }
            { var t = Mk("bipolar", 0.012f); t.jaw = JawPair(t.obj.transform, 0.017f, 0.0036f, _blueIns, 0.003f); }
            { var t = Mk("scissors", 0.012f); t.jaw = JawPair(t.obj.transform, 0.016f, 0.0028f, _gold, 0.004f); }
            {
                var t = Mk("hook", 0.021f); var g = t.obj.transform;
                var ins = Part.Make("ins", Geo.Cylinder(0.0026f, 0.0018f, 0.012f, 12), _blueIns, g); ins.transform.localRotation = EulerXYZ(Mathf.PI / 2, 0, 0); ins.transform.localPosition = V(0, 0, 0.006f);
                var w = Part.Make("wire", Geo.Cylinder(0.0006f, 0.0006f, 0.01f, 8), _steel, g); w.transform.localRotation = EulerXYZ(Mathf.PI / 2, 0, 0); w.transform.localPosition = V(0, 0, 0.017f);
                var hk = Part.Make("hook", Geo.Cylinder(0.0006f, 0.0006f, 0.004f, 8), _steel, g); hk.transform.localPosition = V(0, 0.0018f, 0.0215f);
            }
            {
                var t = Mk("bulldog", 0.017f); t.jaw = JawPair(t.obj.transform, 0.014f, 0.0035f, _steel);
                t.load = MakeBulldog(t.obj.transform); t.load.transform.localPosition = V(0, 0, 0.016f);
            }
            {
                var t = Mk("needle", 0.014f); t.jaw = JawPair(t.obj.transform, 0.012f, 0.0034f, _steel);
                var nd = Part.Make("needle", Geo.Torus(0.0055f, 0.0005f, 6, 20, Mathf.PI), _steel, t.obj.transform);
                nd.transform.localPosition = V(0, 0, 0.012f); nd.transform.localRotation = EulerXYZ(0, Mathf.PI / 2, 0);
                t.load = nd.gameObject;
            }
            {
                var t = Mk("clip", 0.012f); t.jaw = JawPair(t.obj.transform, 0.013f, 0.0038f, _steel);
                t.load = MakeHemolok(t.obj.transform); t.load.transform.localPosition = V(0, 0, 0.01f);
            }
            {
                var t = Mk("suction", 0.012f);
                var tube = Part.Make("tube", Geo.Cylinder(0.0026f, 0.0026f, 0.012f, 14, 1, true), Mats.Std(new MatOpts { color = 0xc8ccd2, roughness = 0.25f, metallic = 0.9f, side = Side.Double }), t.obj.transform);
                tube.transform.localRotation = EulerXYZ(Mathf.PI / 2, 0, 0); tube.transform.localPosition = V(0, 0, 0.006f);
            }
            {
                var t = Mk("us", 0.018f); var g = t.obj.transform;
                var b = Part.Make("probe", Geo.Box(0.008f, 0.005f, 0.03f), Mats.Std(0x333a44, 0.35f), g); b.transform.localPosition = V(0, 0, 0.017f);
                var face = Part.Make("face", Geo.Box(0.0075f, 0.0008f, 0.024f), Mats.Std(0x9aa4b0, 0.2f), g); face.transform.localPosition = V(0, -0.0028f, 0.018f);
            }
        }

        public void Set(string name)
        {
            foreach (var kv in tools) kv.Value.obj.SetActive(kv.Key == name);
            tool = name; cur = tools[name];
        }

        public void HasLoad(bool v) { if (cur.load) cur.load.SetActive(v); }

        // H: kol (dünya), T: trokar (dünya), up: kontrolcü yukarı vektörü
        public void Update(Vector3 H, Vector3 T, Vector3 up, float trig)
        {
            trigger = trig;
            var d = T - H;
            float o = d.magnitude; d = d.N(); dir = d;
            o = Clamp(o, 0.04f, SHAFT - 0.012f);
            insertion = SHAFT - o;
            var Hc = T - d * o;
            var x = Vector3.Cross(up, d); if (x.sqrMagnitude < 1e-6f) x = V(1, 0, 0); x = x.N();
            var y = Vector3.Cross(d, x);
            root.SetPositionAndRotation(Hc, Quaternion.LookRotation(d, y));
            cur.jaw?.Set((1 - trig) * 0.42f);
            lever.localRotation = EulerXYZ(trig * 0.3f, Mathf.PI / 2, 0);
            tipWorld = Hc + d * (SHAFT + cur.tipOff);
            handleWorld = Hc;
        }
    }
}
