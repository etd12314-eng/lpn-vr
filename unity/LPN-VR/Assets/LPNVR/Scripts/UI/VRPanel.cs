// VR içi panel: dünya-uzayı uGUI Canvas, ışınla (VR kontrolcü veya fare) tıklanabilir düğmeler — ui.js Panel karşılığı.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LPNVR
{
    public class VRPanel
    {
        public readonly GameObject go;
        public readonly RectTransform rt;
        public readonly Text title, body;
        readonly Image progBg, progFill;
        readonly float pxW, pxH;
        readonly Dictionary<string, (Image img, Text txt, bool active)> buttons = new Dictionary<string, (Image, Text, bool)>();
        string hover;

        static Font _font;
        public static Font Font => _font ? _font : _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        static readonly Color BtnN = new Color32(0x1d, 0x33, 0x46, 255), BtnH = new Color32(0x2f, 0x7f, 0xb0, 255), BtnA = new Color32(0x1f, 0x6f, 0x4a, 255);
        public static readonly Color Accent = new Color32(0x6f, 0xd3, 0xff, 255);

        public VRPanel(string name, Transform parent, float w, float h, float px = 1024)
        {
            pxW = px; pxH = Mathf.Round(px * h / w);
            go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(pxW, pxH);
            rt.localScale = Vector3.one * (w / pxW);

            var bg = MakeImage("bg", rt, new Color(12 / 255f, 22 / 255f, 32 / 255f, 0.92f));
            Stretch(bg.rectTransform);
            var ol = bg.gameObject.AddComponent<Outline>(); ol.effectColor = new Color32(0x3f, 0xb6, 0xe8, 255); ol.effectDistance = new Vector2(4, -4);

            title = MakeText("title", rt, 40, Accent, FontStyle.Bold);
            Place(title.rectTransform, 32, 18, pxW - 64, 60);
            body = MakeText("body", rt, 27, new Color(0.87f, 0.87f, 0.93f), FontStyle.Normal);
            body.supportRichText = true;
            body.verticalOverflow = VerticalWrapMode.Truncate;
            Place(body.rectTransform, 32, 96, pxW - 64, pxH - 96 - 210);

            progBg = MakeImage("progBg", rt, new Color32(0x22, 0x33, 0x44, 255));
            progFill = MakeImage("progFill", progBg.rectTransform, new Color32(0x3f, 0xd0, 0x8a, 255));
            progBg.gameObject.SetActive(false);
        }

        public static Image MakeImage(string name, Transform parent, Color c)
        {
            var g = new GameObject(name, typeof(RectTransform));
            g.transform.SetParent(parent, false);
            var img = g.AddComponent<Image>(); img.color = c; img.raycastTarget = false;
            return img;
        }
        public static Text MakeText(string name, Transform parent, int size, Color c, FontStyle style)
        {
            var g = new GameObject(name, typeof(RectTransform));
            g.transform.SetParent(parent, false);
            var t = g.AddComponent<Text>();
            t.font = Font; t.fontSize = size; t.color = c; t.fontStyle = style;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.alignment = TextAnchor.UpperLeft; t.raycastTarget = false; t.supportRichText = true;
            return t;
        }
        public static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }
        // Sol-üst köşe pikseli koordinatlarıyla yerleştir (canvas'taki gibi)
        public static void Place(RectTransform r, float x, float y, float w, float h)
        {
            r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1);
            r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(w, h);
        }

        public float W => pxW;
        public float H => pxH;

        public void Progress(float? v, float y)
        {
            progBg.gameObject.SetActive(v != null);
            if (v == null) return;
            Place(progBg.rectTransform, 32, y, pxW - 64, 16);
            var f = progFill.rectTransform;
            f.anchorMin = Vector2.zero; f.anchorMax = new Vector2(Mathf.Clamp01(v.Value), 1); f.offsetMin = f.offsetMax = Vector2.zero;
        }

        public void ClearButtons()
        {
            foreach (var b in buttons.Values) Object.Destroy(b.img.gameObject);
            buttons.Clear();
        }

        public void Button(string id, string label, float x, float y, float w, float h, bool active = false)
        {
            if (buttons.TryGetValue(id, out var ex))
            {
                Place(ex.img.rectTransform, x, y, w, h);
                ex.txt.text = label;
                buttons[id] = (ex.img, ex.txt, active);
                Paint(id);
                return;
            }
            var img = MakeImage("btn-" + id, rt, BtnN);
            Place(img.rectTransform, x, y, w, h);
            var ol = img.gameObject.AddComponent<Outline>(); ol.effectColor = Accent; ol.effectDistance = new Vector2(2, -2);
            var t = MakeText("label", img.rectTransform, 30, Color.white, FontStyle.Bold);
            Stretch(t.rectTransform); t.alignment = TextAnchor.MiddleCenter;
            t.text = label;
            buttons[id] = (img, t, active);
            Paint(id);
        }

        void Paint(string id)
        {
            var b = buttons[id];
            b.img.color = hover == id ? BtnH : b.active ? BtnA : BtnN;
        }

        public void SetHover(string id)
        {
            if (hover == id) return;
            var old = hover; hover = id;
            if (old != null && buttons.ContainsKey(old)) Paint(old);
            if (id != null && buttons.ContainsKey(id)) Paint(id);
        }

        // Dünya ışını → (mesafe, düğme id). Panel düzlemi ile kesişmezse false.
        public bool Hit(Vector3 o, Vector3 d, out float dist, out string id)
        {
            dist = 0; id = null;
            if (!go.activeInHierarchy) return false;
            var n = rt.forward;
            float den = Vector3.Dot(n, d);
            if (Mathf.Abs(den) < 1e-6f) return false;
            float t = Vector3.Dot(rt.position - o, n) / den;
            if (t < 0) return false;
            var p = o + d * t;
            var lp = rt.InverseTransformPoint(p);
            if (!rt.rect.Contains(lp)) return false;
            dist = t;
            foreach (var kv in buttons)
            {
                var br = kv.Value.img.rectTransform;
                if (br.gameObject.activeSelf && br.rect.Contains(br.InverseTransformPoint(p))) { id = kv.Key; break; }
            }
            return true;
        }
    }
}
