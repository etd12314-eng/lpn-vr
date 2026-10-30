// Prosedürel dokular — util.js makeTexture() karşılığı (canvas yerine CPU tamponu, kenarlarda sarmalı/tileable).
using UnityEngine;

namespace LPNVR
{
    public static class ProcTextures
    {
        class Canvas
        {
            public readonly int S;
            public readonly Color[] px;
            public Canvas(int s) { S = s; px = new Color[s * s]; }

            public void Fill(Color c) { for (int i = 0; i < px.Length; i++) px[i] = c; }

            void Blend(int x, int y, Color c, float a)
            {
                x = ((x % S) + S) % S; y = ((y % S) + S) % S;
                int i = y * S + x;
                var d = px[i];
                px[i] = new Color(d.r + (c.r - d.r) * a, d.g + (c.g - d.g) * a, d.b + (c.b - d.b) * a, 1);
            }

            // Radyal gradyan leke: merkezde c (alfa c.a), kenarda saydam
            public void Blob(float cx, float cy, float r, Color c)
            {
                int x0 = Mathf.FloorToInt(cx - r), x1 = Mathf.CeilToInt(cx + r);
                int y0 = Mathf.FloorToInt(cy - r), y1 = Mathf.CeilToInt(cy + r);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                        if (d >= r) continue;
                        Blend(x, y, c, c.a * (1 - d / r));
                    }
            }

            public void Line(float x0, float y0, float x1, float y1, float w, Color c)
            {
                float len = Mathf.Max(1, Mathf.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)));
                int n = Mathf.CeilToInt(len);
                float hw = Mathf.Max(0.5f, w * 0.5f);
                for (int k = 0; k <= n; k++)
                {
                    float t = (float)k / n, x = Mathf.Lerp(x0, x1, t), y = Mathf.Lerp(y0, y1, t);
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            float d = Mathf.Sqrt(dx * dx + dy * dy);
                            float a = Mathf.Clamp01(hw + 0.5f - d);
                            if (a > 0) Blend(Mathf.RoundToInt(x) + dx, Mathf.RoundToInt(y) + dy, c, c.a * a * 0.5f);
                        }
                }
            }

            public void Walk(int steps, System.Func<Vector2> step, float w, Color c)
            {
                float x = U.Rand() * S, y = U.Rand() * S;
                for (int k = 0; k < steps; k++)
                {
                    var d = step();
                    Line(x, y, x + d.x, y + d.y, w, c);
                    x += d.x; y += d.y;
                }
            }

            // Kıvrımlı, incelen ve dallanan damar (sabit adım, açısı yavaşça sapar) — Walk'un kırık çizgileri yerine
            public void Vessel(float x, float y, float ang, float len, float w, Color c, int depth = 0)
            {
                float step = 2.5f, curv = 0;
                int n = Mathf.CeilToInt(len / step);
                for (int k = 0; k < n; k++)
                {
                    curv = curv * 0.9f + (U.Rand() - 0.5f) * 0.12f;
                    ang += curv;
                    float t = (float)k / n, ww = Mathf.Max(0.6f, w * (1 - 0.7f * t));
                    float nx = x + Mathf.Cos(ang) * step, ny = y + Mathf.Sin(ang) * step;
                    Line(x, y, nx, ny, ww, c);
                    x = nx; y = ny;
                    if (depth < 2 && U.Rand() < 0.035f)
                        Vessel(x, y, ang + (U.Rand() < 0.5f ? 1 : -1) * (0.5f + U.Rand() * 0.6f), len * (1 - t) * 0.6f, ww * 0.7f, c, depth + 1);
                }
            }
            public void Vessels(int count, float len, float w, Color c)
            {
                for (int i = 0; i < count; i++) Vessel(U.Rand() * S, U.Rand() * S, U.Rand() * Mathf.PI * 2, len * (0.6f + U.Rand() * 0.8f), w, c);
            }

            public void Row(int y, int h, Color c)
            {
                for (int yy = y; yy < Mathf.Min(S, y + h); yy++)
                    for (int x = 0; x < S; x++) Blend(x, yy, c, c.a);
            }
        }

        static Color C(int r, int g, int b, float a = 1) => new Color(r / 255f, g / 255f, b / 255f, a);
        static Color H(uint hex) => U.Hex(hex);

        public static Texture2D Make(string kind, int size = 512)
        {
            var c = new Canvas(size);
            float R() => U.Rand();
            Vector2 J(float jx, float jy) => new Vector2((R() - .5f) * jx, (R() - .5f) * jy);
            switch (kind)
            {
                case "fat":
                    c.Fill(H(0xe3b552));
                    for (int i = 0; i < 260; i++) c.Blob(R() * size, R() * size, 12 + R() * 26, C(250, 215 + (int)(R() * 30), 120 + (int)(R() * 50), 0.55f));
                    c.Vessels(7, 260, 2.2f, C(185, 60, 40, 0.55f));
                    for (int i = 0; i < 60; i++) c.Blob(R() * size, R() * size, 3 + R() * 6, C(255, 240, 190, 0.5f)); // ışık yansıtan lobül tepeleri
                    break;
                case "kidney":
                    c.Fill(H(0x7a2b22));
                    for (int i = 0; i < 400; i++) c.Blob(R() * size, R() * size, 6 + R() * 20, C(140 + (int)(R() * 40), 50 + (int)(R() * 25), 40, 0.35f));
                    c.Vessels(10, 160, 1.4f, C(95, 22, 22, 0.45f));
                    break;
                case "tumor":
                    c.Fill(H(0xd9a23a));
                    for (int i = 0; i < 160; i++) c.Blob(R() * size, R() * size, 10 + R() * 40, C(230 + (int)(R() * 25), 170 + (int)(R() * 60), 60, 0.5f));
                    for (int i = 0; i < 25; i++) c.Blob(R() * size, R() * size, 8 + R() * 22, C(140, 30, 25, 0.55f)); // hemorajik odaklar
                    break;
                case "colon":
                    c.Fill(H(0xc98a78));
                    for (int i = 0; i < 200; i++) c.Blob(R() * size, R() * size, 10 + R() * 30, C(225, 165, 140, 0.4f));
                    for (int i = 0; i < 14; i++) c.Vessel(R() * size, R() * size, Mathf.PI / 2 + (R() - .5f) * 0.6f, 140, 1.8f, C(150, 40, 60, 0.5f)); // vasa recta
                    for (int i = 0; i < 20; i++) c.Blob(R() * size, R() * size, 14, C(240, 200, 90, 0.6f)); // epiploik apendiksler
                    break;
                case "peritoneum":
                    c.Fill(H(0xd9a59a));
                    for (int i = 0; i < 150; i++) c.Blob(R() * size, R() * size, 15 + R() * 40, C(240, 200, 120, 0.35f));
                    c.Vessels(9, 320, 2.0f, C(170, 35, 40, 0.6f));
                    c.Vessels(25, 90, 0.9f, C(190, 60, 60, 0.4f)); // kapiller ağ
                    break;
                case "muscle":
                    c.Fill(H(0x9c3a33));
                    for (int i = 0; i < size; i += 3)
                    {
                        var col = C(120 + (int)(R() * 60), 40, 40, 0.4f);
                        c.Row(i, 1 + (int)(R() * 2), col);
                    }
                    break;
                case "vein":
                    c.Fill(H(0x4c3a78));
                    for (int i = 0; i < 120; i++) c.Blob(R() * size, R() * size, 10 + R() * 30, C(110, 80, 150, 0.4f));
                    break;
                case "artery":
                    c.Fill(H(0xc9867a));
                    for (int i = 0; i < 120; i++) c.Blob(R() * size, R() * size, 10 + R() * 30, C(230, 160, 150, 0.4f));
                    for (int i = 0; i < 12; i++) c.Walk(6, () => new Vector2(30, (R() - .5f) * 20), 1, C(170, 40, 40, 0.5f));
                    break;
                case "organ":
                    c.Fill(H(0x888888));
                    for (int i = 0; i < 300; i++) c.Blob(R() * size, R() * size, 5 + R() * 20, new Color(1, 1, 1, 0.1f + R() * 0.15f));
                    break;
                case "wall":
                    c.Fill(H(0xc47b72));
                    for (int i = 0; i < 300; i++) c.Blob(R() * size, R() * size, 10 + R() * 30, C(220, 150, 140, 0.35f));
                    c.Vessels(8, 300, 2.2f, C(150, 30, 40, 0.55f));
                    c.Vessels(20, 100, 1.0f, C(170, 50, 55, 0.35f));
                    break;
                default:
                    c.Fill(Color.white);
                    break;
            }
            var t = new Texture2D(size, size, TextureFormat.RGBA32, true, false) { name = "tex-" + kind, wrapMode = TextureWrapMode.Repeat, anisoLevel = 4, filterMode = FilterMode.Trilinear };
            t.SetPixels(c.px);
            t.Apply(true, true);
            Heights[kind] = MakeHeight(kind, c);
            return t;
        }

        // Yükseklik haritası (kabartma için): renk dokusunun parlaklığından türetilir (doku türüne göre işaret/
        // yumuşatma) + ince mikro-pürüz. Tissue shader'ı bunu türevlerle normal sapmasına çevirir.
        public static readonly System.Collections.Generic.Dictionary<string, Texture2D> Heights = new System.Collections.Generic.Dictionary<string, Texture2D>();

        static Texture2D MakeHeight(string kind, Canvas c)
        {
            int S = c.S;
            var h = new float[S * S];
            float sign = 1, micro = 0.08f; int blur = 2;
            switch (kind)
            {
                case "fat": blur = 3; micro = 0.05f; break;          // parlak lobüller kabarık, septalar çukur
                case "kidney": sign = 1; blur = 2; micro = 0.12f; break;
                case "tumor": blur = 3; micro = 0.1f; break;
                case "colon": blur = 3; micro = 0.06f; break;
                case "peritoneum": sign = -1; blur = 1; micro = 0.05f; break; // koyu damarlar hafif kabarık
                case "muscle": blur = 1; micro = 0.1f; break;
                case "vein": case "artery": blur = 2; micro = 0.04f; break;
                case "wall": blur = 2; micro = 0.08f; break;
            }
            for (int i = 0; i < h.Length; i++) { var p = c.px[i]; h[i] = (0.3f * p.r + 0.59f * p.g + 0.11f * p.b) * sign; }
            for (int pass = 0; pass < blur; pass++) h = BoxBlur(h, S);
            float mn = float.MaxValue, mx = float.MinValue;
            foreach (var v in h) { mn = Mathf.Min(mn, v); mx = Mathf.Max(mx, v); }
            float inv = 1f / Mathf.Max(1e-5f, mx - mn);
            var px = new Color32[S * S];
            var rnd = new System.Random(7919 + kind.Length * 31 + kind[0]); // U.Rand dizisini (anatomi) bozmamak için ayrı üreteç
            for (int i = 0; i < h.Length; i++)
            {
                float v = (h[i] - mn) * inv * (1 - micro) + (float)rnd.NextDouble() * micro;
                byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255), 0, 255);
                px[i] = new Color32(b, b, b, 255);
            }
            var t = new Texture2D(S, S, TextureFormat.R8, true, true) { name = "height-" + kind, wrapMode = TextureWrapMode.Repeat, anisoLevel = 4, filterMode = FilterMode.Trilinear };
            t.SetPixels32(px);
            t.Apply(true, true);
            return t;
        }

        static float[] BoxBlur(float[] a, int S)
        {
            var o = new float[a.Length];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float sum = 0;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                            sum += a[((y + dy + S) % S) * S + (x + dx + S) % S];
                    o[y * S + x] = sum / 9f;
                }
            return o;
        }
    }
}
