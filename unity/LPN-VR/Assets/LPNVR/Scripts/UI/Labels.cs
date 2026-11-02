// Anatomik etiketler (Latince/Türkçe) — anatomy.js buildLabels karşılığı.
// Bağlantı noktası hasta-yerel uzayda (FlipRoot altında), metin ise aynalanmasın diye normal dünyada billboard.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LPNVR
{
    public class LabelSet
    {
        public class Item { public AnatLabel L; public Transform anchor, text; public LineRenderer line; public GameObject dot; public bool visible = true; }
        public readonly List<Item> items = new List<Item>();
        readonly Transform worldRoot;
        readonly float scale;
        bool on = true;

        public LabelSet(Anatomy A, Transform worldParent, float scale, int layer)
        {
            this.scale = scale;
            worldRoot = U.Group("labels-" + A.root.name, worldParent).transform;
            U.SetLayer(worldRoot.gameObject, layer);
            var anchors = U.Group("labelAnchors", A.root).transform;
            var dotMat = Mats.Unlit(VRPanel.Accent, true);
            var lineMat = Mats.Unlit(VRPanel.Accent, true);
            foreach (var L in A.labels)
            {
                var it = new Item { L = L };
                it.anchor = U.Group("lbl", anchors).transform; it.anchor.localPosition = L.pos;
                var dot = Part.Make("dot", Geo.Sphere(0.0012f, 8, 6), dotMat, it.anchor); it.dot = dot.gameObject;
                U.SetLayer(it.anchor.gameObject, layer);

                var go = new GameObject("label", typeof(RectTransform));
                go.transform.SetParent(worldRoot, false); go.layer = layer;
                var cv = go.AddComponent<Canvas>(); cv.renderMode = RenderMode.WorldSpace; cv.sortingOrder = 10;
                var rt = (RectTransform)go.transform;
                var t = VRPanel.MakeText("t", rt, 34, Color.white, FontStyle.Bold);
                t.text = L.text; t.horizontalOverflow = HorizontalWrapMode.Overflow; t.alignment = TextAnchor.MiddleCenter;
                float w = t.preferredWidth + 24, h = 34 * 1.2f + 14;
                rt.sizeDelta = new Vector2(w, h); rt.pivot = new Vector2(0.5f, 0);
                var bg = VRPanel.MakeImage("bg", rt, new Color(10 / 255f, 20 / 255f, 30 / 255f, 0.78f));
                VRPanel.Stretch(bg.rectTransform); bg.transform.SetAsFirstSibling();
                var ol = bg.gameObject.AddComponent<Outline>(); ol.effectColor = VRPanel.Accent; ol.effectDistance = new Vector2(3, -3);
                VRPanel.Stretch(t.rectTransform);
                U.SetLayer(go, layer);
                rt.localScale = Vector3.one * scale; // Update çalışmadan önce de dev (piksel = metre) tuval olmasın
                it.text = rt;

                var lgo = U.Group("line", worldRoot); lgo.layer = layer;
                it.line = lgo.AddComponent<LineRenderer>();
                it.line.sharedMaterial = lineMat; it.line.useWorldSpace = true; it.line.positionCount = 2;
                it.line.widthMultiplier = 0.0004f; it.line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                items.Add(it);
            }
        }

        public void SetAll(bool v) { on = v; Apply(); }
        // Etiket kümesi bu modda kullanılmıyorsa tümünü (metin, çizgi) gizle
        public void SetActive(bool v) => worldRoot.gameObject.SetActive(v);
        public void SetVisible(Item it, bool v) { it.visible = v; }
        public void Apply()
        {
            foreach (var it in items)
            {
                bool v = on && it.visible;
                it.text.gameObject.SetActive(v); it.line.gameObject.SetActive(v); it.dot.SetActive(v);
            }
        }

        // Her kare: metni bağlantının üstüne yerleştir, kameraya döndür
        public void Update(Camera cam, float worldScale)
        {
            if (!on || !cam) return;
            var cp = cam.transform.position;
            var up = cam.transform.up;
            foreach (var it in items)
            {
                if (!it.visible) continue;
                var a = it.anchor.position;
                var root = it.anchor.parent.parent; // A.root
                var off = root.TransformVector(new Vector3(0, 0.012f, 0.012f));
                var p = a + off;
                it.text.position = p;
                it.text.rotation = Quaternion.LookRotation(p - cp, up);
                it.text.localScale = Vector3.one * scale * worldScale;
                it.line.SetPosition(0, a); it.line.SetPosition(1, p);
                it.line.widthMultiplier = 0.0004f * worldScale;
            }
        }
    }
}
