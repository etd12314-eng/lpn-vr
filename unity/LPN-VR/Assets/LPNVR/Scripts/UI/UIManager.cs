// Arayüz: VR içi paneller (menü, atlas, adım/rapor) + masaüstü HUD — ui.js karşılığı.
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace LPNVR
{
    public class UIManager
    {
        public static readonly string CaseTitle = "Vaka: Sol böbrek alt pol anterior kitle";
        public static readonly string[] CaseLines =
        {
            "58 yaşında erkek, insidental saptanan sol renal kitle.",
            "BT: 3.0 cm, %60 ekzofitik, kontrastlanan solid lezyon (cT1a N0 M0).",
            "R.E.N.A.L. nefrometri: R1 + E1 + N2 + L1 = 5a (düşük kompleksite).",
            "Tek renal arter / tek renal ven. eGFR 88 ml/dk/1.73m².",
            "Plan: Transperitoneal laparoskopik parsiyel nefrektomi,",
            "sağ lateral dekübit 60°, 4 port (kamera 12 mm, 2×12 mm, asistan 5 mm).",
        };

        public class Toast { public string text, kind; public float t; }
        public readonly List<Toast> toasts = new List<Toast>();
        public readonly VRPanel info, menu, atlas;
        public (Procedure.Report r, List<(string, string)> rows)? lastReport;
        public bool dirty = true;

        // masaüstü HUD
        readonly GameObject hud;
        readonly Text hudStep, hudMetrics, hudTools, hudToasts, hudHelp, hudReport;
        readonly Image hudProgBg, hudProgFill;
        readonly GameObject hudReportBox;
        public readonly RawImage hudUS;

        public UIManager(Transform worldParent, Transform infoAnchor)
        {
            info = new VRPanel("infoPanel", infoAnchor, 0.78f, 0.86f, 1024);
            menu = new VRPanel("menuPanel", worldParent, 0.9f, 0.62f, 1200);
            atlas = new VRPanel("atlasPanel", worldParent, 0.5f, 0.62f, 800);

            // Masaüstü HUD (ekran bindirmesi)
            hud = new GameObject("DesktopHUD", typeof(RectTransform));
            var c = hud.AddComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 100;
            var sc = hud.AddComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1600, 900); sc.matchWidthOrHeight = 0.5f;
            var r = (RectTransform)hud.transform;

            var stepBox = VRPanel.MakeImage("stepBox", r, new Color(0.05f, 0.09f, 0.13f, 0.82f));
            Corner(stepBox.rectTransform, 0, 1, 12, -12, 560, 300);
            hudStep = VRPanel.MakeText("step", stepBox.rectTransform, 17, Color.white, FontStyle.Normal);
            Inset(hudStep.rectTransform, 14, 12, 14, 30);
            hudProgBg = VRPanel.MakeImage("prog", stepBox.rectTransform, new Color32(0x22, 0x33, 0x44, 255));
            var pr = hudProgBg.rectTransform; pr.anchorMin = new Vector2(0, 0); pr.anchorMax = new Vector2(1, 0); pr.pivot = new Vector2(0.5f, 0); pr.offsetMin = new Vector2(14, 12); pr.offsetMax = new Vector2(-14, 22);
            hudProgFill = VRPanel.MakeImage("fill", pr, new Color32(0x3f, 0xd0, 0x8a, 255));

            var metBox = VRPanel.MakeImage("metrics", r, new Color(0.05f, 0.09f, 0.13f, 0.82f));
            Corner(metBox.rectTransform, 1, 1, -12, -12, 300, 110);
            hudMetrics = VRPanel.MakeText("m", metBox.rectTransform, 20, Color.white, FontStyle.Bold);
            Inset(hudMetrics.rectTransform, 14, 10, 14, 10);

            var toolBox = VRPanel.MakeImage("tools", r, new Color(0.05f, 0.09f, 0.13f, 0.82f));
            Corner(toolBox.rectTransform, 0, 0, 12, 12, 560, 70);
            hudTools = VRPanel.MakeText("t", toolBox.rectTransform, 17, Color.white, FontStyle.Normal);
            Inset(hudTools.rectTransform, 14, 8, 14, 8);

            hudToasts = VRPanel.MakeText("toasts", r, 18, Color.white, FontStyle.Bold);
            Corner(hudToasts.rectTransform, 1, 0, -12, 230, 560, 220);
            hudToasts.alignment = TextAnchor.LowerRight;

            hudHelp = VRPanel.MakeText("help", r, 14, new Color(0.6f, 0.75f, 0.8f), FontStyle.Normal);
            Corner(hudHelp.rectTransform, 0.5f, 0, 0, 90, 900, 40);
            hudHelp.alignment = TextAnchor.LowerCenter;

            var usGo = new GameObject("us", typeof(RectTransform)); usGo.transform.SetParent(r, false);
            hudUS = usGo.AddComponent<RawImage>(); hudUS.raycastTarget = false;
            Corner(hudUS.rectTransform, 1, 0, -12, 12, 256, 200);
            hudUS.gameObject.SetActive(false);

            hudReportBox = VRPanel.MakeImage("report", r, new Color(0.04f, 0.07f, 0.1f, 0.95f)).gameObject;
            var rr = (RectTransform)hudReportBox.transform; rr.anchorMin = rr.anchorMax = new Vector2(0.5f, 0.5f); rr.sizeDelta = new Vector2(820, 760);
            hudReport = VRPanel.MakeText("r", rr, 17, Color.white, FontStyle.Normal);
            Inset(hudReport.rectTransform, 24, 20, 24, 20);
            hudReportBox.SetActive(false);
            SetHud(false, "");
        }

        static void Corner(RectTransform r, float ax, float ay, float x, float y, float w, float h)
        {
            r.anchorMin = r.anchorMax = new Vector2(ax, ay); r.pivot = new Vector2(ax, ay);
            r.anchoredPosition = new Vector2(x, y); r.sizeDelta = new Vector2(w, h);
        }
        static void Inset(RectTransform r, float l, float t, float rr, float b) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = new Vector2(l, b); r.offsetMax = new Vector2(-rr, -t); }

        public void SetHud(bool surgery, string help)
        {
            foreach (Transform c in hud.transform) c.gameObject.SetActive(surgery && c.name != "us" && c.name != "report");
            hudHelp.gameObject.SetActive(!string.IsNullOrEmpty(help));
            hudHelp.text = help;
            hudReportBox.SetActive(surgery && lastReport != null);
        }
        public void HudVisible(bool v) => hud.SetActive(v);

        public void AddToast(string text, string kind)
        {
            toasts.Insert(0, new Toast { text = text, kind = kind, t = Time.time });
            if (toasts.Count > 4) toasts.RemoveRange(4, toasts.Count - 4);
            dirty = true;
        }

        static string Col(string kind) => kind == "bad" ? "#ff7a7a" : kind == "warn" ? "#ffd27a" : kind == "good" ? "#7affb0" : "#cccccc";

        public void ShowReport(Procedure.Report r)
        {
            var rows = new List<(string, string)>
            {
                ("Toplam süre", U.Fmt(r.totalTime)),
                ("Sıcak iskemi süresi (WIT)", r.offClamp && r.wit == 0 ? "Klempsiz (off-clamp)" : U.Fmt(r.wit)),
                ("Tahmini kan kaybı", Mathf.RoundToInt(r.bloodLoss) + " ml"),
                ("Cerrahi sınır", r.marginStatus + (r.margin != null ? $" · ort. {r.margin.Value:0.0} mm, min {r.marginMin.Value:0.0} mm" : "")),
                ("Damar yaralanmaları", r.injuries.Count > 0 ? string.Join(", ", r.injuries) : "Yok"),
                ("Üreter / kolon hasarı", $"{(r.ureter ? "Üreter: VAR" : "Üreter: yok")} · {(r.bowel ? "Kolon: VAR" : "Kolon: yok")}"),
                ("Görüş dışı enerji", r.oov + " kez"),
                ("\"Trifecta\" (negatif sınır + WIT<25 dk + majör komplikasyon yok)", r.trifecta ? "SAĞLANDI ✓" : "Sağlanamadı"),
            };
            lastReport = (r, rows);
            var sb = new StringBuilder();
            sb.Append($"<size=26><b><color=#6fd3ff>Performans Raporu — Skor {r.score}/100</color></b></size>\n\n");
            foreach (var (a, b) in rows) sb.Append($"<color=#99cccc>{a}</color>\n    {b}\n");
            sb.Append("\n<b>Kesintiler</b>\n");
            if (r.penalties.Count == 0) sb.Append("  Yok\n");
            foreach (var p in r.penalties) sb.Append($"  −{p.points}: {p.reason}\n");
            sb.Append("\n<b>Adım süreleri</b>\n");
            foreach (var s in r.steps) sb.Append($"  {s.title}: {U.Fmt(s.time)}{(s.skipped ? " (atlandı)" : "")}\n");
            sb.Append("\n<color=#88aabb>M / Esc: ana menü (yeniden başla)</color>");
            hudReport.text = sb.ToString();
            hudReportBox.SetActive(true);
            dirty = true;
        }

        // ---------------------------------------------------------------- masaüstü HUD
        public void Hud(LPNApp app)
        {
            var p = app.proc; if (p == null) return;
            var m = p.m; var s = p.Cur;
            if (s != null)
                hudStep.text = $"<color=#6fd3ff><b>Adım {p.stepIdx + 1}/{p.steps.Count}</b></color>\n<b><size=19>{s.title}</size></b>\n{s.text}\n<color=#ffd27a>Önerilen aletler: {s.tools}</color>";
            float prog = s == null ? 1 : s.progress ?? (s.tg != null ? (float)s.tg.Count(x => x.done) / s.tg.Count : 0);
            hudProgFill.rectTransform.anchorMin = Vector2.zero; hudProgFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(prog), 1);
            hudProgFill.rectTransform.offsetMin = hudProgFill.rectTransform.offsetMax = Vector2.zero;
            string witCol = m.wit > 25 * 60 ? "#ff7a7a" : m.wit > 20 * 60 ? "#ffd27a" : "#ffffff";
            string blCol = m.bloodLoss > 500 ? "#ff7a7a" : m.bloodLoss > 250 ? "#ffd27a" : "#ffffff";
            string wit = p.st.clamp != null ? U.Fmt(m.wit) : (m.witEnd != null ? U.Fmt(m.wit) + " ✓" : "—");
            hudMetrics.text = $"Süre  {U.Fmt(p.t)}\n<color={witCol}>WIT   {wit}</color>\n<color={blCol}>Kan   {Mathf.RoundToInt(m.bloodLoss)} ml</color>";
            var sb = new StringBuilder();
            foreach (var hand in new[] { "left", "right" })
            {
                var ins = app.instr[hand];
                bool act = app.activeHand == hand && !app.xrActive;
                sb.Append($"{(act ? "<color=#ffd27a>" : "")}<b>{(hand == "left" ? "Sol" : "Sağ")} el{(act ? " (aktif)" : "")}:</b> {Instrument.TOOLS[ins.tool].tr}{(act ? "</color>" : "")}\n");
            }
            hudTools.text = sb.ToString();
            var tb = new StringBuilder();
            foreach (var t in toasts.Where(t => Time.time - t.t < 6).Reverse()) tb.Append($"<color={Col(t.kind)}>{t.text}</color>\n");
            hudToasts.text = tb.ToString();
        }

        // ---------------------------------------------------------------- VR panelleri
        public void DrawInfo(LPNApp app)
        {
            var p = app.proc; var P = info; var s = p?.Cur;
            P.title.text = s != null && lastReport == null ? $"Adım {p.stepIdx + 1}/{p.steps.Count}" : "Rapor";
            var sb = new StringBuilder();
            float? prog = null;
            if (lastReport != null)
            {
                var (r, rows) = lastReport.Value;
                sb.Append($"<size=44><b>Skor: {r.score}/100</b></size>\n");
                foreach (var (a, b) in rows) sb.Append($"<size=24><color=#99cccc>{a}</color></size>\n  {b}\n");
            }
            else if (s != null)
            {
                sb.Append($"<size=34><b>{s.title}</b></size>\n{s.text}\n<color=#ffd27a><size=26>Aletler: {s.tools}</size></color>\n\n\n");
                prog = s.progress ?? (s.tg != null ? (float)s.tg.Count(x => x.done) / s.tg.Count : 0);
                sb.Append($"<size=30><b>Süre {U.Fmt(p.t)}   WIT {(p.st.clamp != null || p.m.witEnd != null ? U.Fmt(p.m.wit) : "—")}   Kan {Mathf.RoundToInt(p.m.bloodLoss)} ml</b></size>\n");
                sb.Append($"<color=#99eeee>Sol: {Instrument.TOOLS[app.instr["left"].tool].tr}\nSağ: {Instrument.TOOLS[app.instr["right"].tool].tr}</color>\n");
                foreach (var t in toasts.Take(3)) sb.Append($"<size=25><color={Col(t.kind)}>{t.text}</color></size>\n");
            }
            P.body.text = sb.ToString();
            // ilerleme çubuğu: metnin "Aletler" satırının altına yaklaşık konum
            float yProg = 96 + Mathf.Min(P.H - 400, LinesHeight(P.body, s));
            P.Progress(prog, yProg);
            float H = P.H, W = P.W;
            if (app.vrMenuOpen || lastReport != null)
            {
                float bw = (W - 64 - 30) / 3;
                P.Button("guide", "Rehber " + (p != null && p.guide ? "açık" : "kapalı"), 32, H - 190, bw, 70, p != null && p.guide);
                P.Button("labels", "Etiketler", 32 + bw + 15, H - 190, bw, 70, app.labelsOn);
                P.Button("skip", "Adımı atla", 32 + 2 * (bw + 15), H - 190, bw, 70);
                P.Button("calib", "Konumlandır", 32, H - 105, bw, 70);
                P.Button("resume", "Devam", 32 + bw + 15, H - 105, bw, 70);
                P.Button("home", "Ana menü", 32 + 2 * (bw + 15), H - 105, bw, 70);
            }
            else
            {
                P.ClearButtons();
                P.body.text += "\n<size=24><color=#88aabb>A/X: alet değiştir · Tetik: kullan · Sol çubuk: kamera · Sağ çubuk: zoom\nB: etiketler · Y: menü · İki çubuğa bas: konumlandır</color></size>";
            }
        }

        // Başlık + metin + aletler satırının kapladığı yükseklik (piksel) — ilerleme çubuğunu altına koymak için
        static float LinesHeight(Text t, Step s)
        {
            if (s == null) return 0;
            var probe = $"<size=34><b>{s.title}</b></size>\n{s.text}\n<color=#ffd27a><size=26>Aletler: {s.tools}</size></color>\n";
            var gen = new TextGenerator();
            var set = t.GetGenerationSettings(new Vector2(t.rectTransform.rect.width, 10000));
            return gen.GetPreferredHeight(probe, set) + 12;
        }

        public void DrawMenu()
        {
            var P = menu;
            P.title.text = "LPN-VR · Laparoskopik Parsiyel Nefrektomi Simülatörü";
            P.body.text = $"<color=#ffd27a><size=32><b>{CaseTitle}</b></size></color>\n\n{string.Join("\n", CaseLines)}";
            float W = P.W, y = 450;
            P.Button("atlas", "Anatomi Atlası", 40, y, (W - 120) / 2, 110);
            P.Button("surgery", "Ameliyata Başla", 80 + (W - 120) / 2, y, (W - 120) / 2, 110);
            var hint = P.go.transform.Find("hint")?.GetComponent<Text>();
            if (!hint)
            {
                hint = VRPanel.MakeText("hint", P.rt, 24, new Color(0.53f, 0.67f, 0.73f), FontStyle.Normal);
                hint.gameObject.name = "hint";
                VRPanel.Place(hint.rectTransform, 40, y + 130, W - 80, 90);
            }
            hint.text = "Işınla seçip tetiğe basın. Ameliyatta kontrolcüler alet kollarıdır (trokar fulkrumu).\nBaşlarken ellerinizi rahat çalışma pozisyonunda tutun: hasta buna göre konumlanır.";
        }

        public void DrawAtlas(Dictionary<string, bool> v)
        {
            var P = atlas;
            P.title.text = "Anatomi Atlası";
            P.body.text = "<size=24>Grip ile modeli tutup çevirin, sağ çubuk: ölçek\n(Masaüstü: sürükle = döndür, tekerlek = yakınlaştır)</size>";
            float W = P.W;
            var items = new[] { ("labels", "Etiketler"), ("fat", "Yağ / Gerota"), ("inner", "Şeffaf böbrek"), ("bone", "Kemikler"), ("gi", "GİS organları"), ("muscle", "Kaslar") };
            for (int i = 0; i < items.Length; i++)
                P.Button(items[i].Item1, items[i].Item2, 30 + (i % 2) * ((W - 75) / 2 + 15), 160 + (i / 2) * 100, (W - 75) / 2, 80, v[items[i].Item1]);
            P.Button("home", "Ana menü", 30, 480, W - 60, 80);
        }
    }
}
