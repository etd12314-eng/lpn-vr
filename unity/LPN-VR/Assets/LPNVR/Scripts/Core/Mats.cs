// Materyal fabrikası ve doku etkileşim durumu (util.js tissueMaterial / addHole / inHole karşılığı).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LPNVR
{
    public enum Side { Front, Back, Double }

    public class MatOpts
    {
        public uint color = 0xffffff;
        public Texture tex;
        public float roughness = 0.4f, metallic = 0, opacity = 1;
        public uint emissive = 0;
        public Side side = Side.Front;
        public bool transparent;
        public bool depthWrite = true;
        // Gerçekçi doku görünümü (boşsa doku türünden otomatik — bkz. Mats.Look)
        public float? wet, wetRough, bump, cavity;
        public Color? sss;
        public Vector2 mapScale = Vector2.one;
    }

    // Doku türüne göre ıslak film / yüzey altı saçılma / kabartma varsayılanları
    public struct TissueLook
    {
        public float wet, wetRough, bump, cavity; public Color sss;
        public TissueLook(float wet, float wetRough, float bump, float cavity, Color sss) { this.wet = wet; this.wetRough = wetRough; this.bump = bump; this.cavity = cavity; this.sss = sss; }
    }

    // Doku materyali + nesne-uzayı durum (delikler, alet uçları, krater, renk tonu)
    public class TissueMat
    {
        public const int MaxHoles = 128;
        public readonly Material mat;
        public readonly bool holesEnabled, deform;
        public readonly List<Vector4> holes = new List<Vector4>();
        public Side side;
        readonly Vector4[] holeArr = new Vector4[MaxHoles];
        readonly Vector4[] tips = new Vector4[2];
        public Vector4 crater, planeCut, ellip = Vector4.one;
        public Color tint = Color.white;

        public TissueMat(Material m, bool holes, bool deform, float sigma, Side side)
        {
            mat = m; holesEnabled = holes; this.deform = deform; this.side = side;
            m.SetFloat("_Deform", deform ? 1 : 0);
            m.SetFloat("_Sigma", sigma);
            m.SetVectorArray("_Holes", holeArr);
            m.SetVectorArray("_Tips", tips);
            m.SetFloat("_HoleCount", 0);
            SetTint(Color.white);
            m.SetVector("_Crater", Vector4.zero);
            m.SetVector("_PlaneCut", Vector4.zero);
            m.SetVector("_Ellip", Vector4.one);
        }

        public void SetTip(int i, Vector3 lp, bool active)
        {
            tips[i] = new Vector4(lp.x, lp.y, lp.z, active ? 1 : 0);
            mat.SetVectorArray("_Tips", tips);
        }
        public void SetTint(Color c) { tint = c; mat.SetVector("_Tint", new Vector4(c.r, c.g, c.b, 1)); }
        public void SetCrater(Vector4 c) { crater = c; mat.SetVector("_Crater", c); }
        public void SetPlaneCut(Vector4 p, Vector3 e) { planeCut = p; ellip = e; mat.SetVector("_PlaneCut", p); mat.SetVector("_Ellip", e); }

        // Delik ekle (nesne-uzayı). Aynı yerde zaten yeterli delik varsa eklemez.
        public bool AddHole(Vector3 p, float r)
        {
            if (!holesEnabled) return false;
            foreach (var h in holes) if (Vector3.Distance(h, p) < r * 0.45f && h.w >= r * 0.9f) return false;
            if (holes.Count >= MaxHoles) holes.RemoveAt(0);
            holes.Add(new Vector4(p.x, p.y, p.z, r));
            for (int i = 0; i < holes.Count; i++) holeArr[i] = holes[i];
            mat.SetVectorArray("_Holes", holeArr);
            mat.SetFloat("_HoleCount", holes.Count);
            return true;
        }
        public bool InHole(Vector3 p, float extra = 0)
        {
            foreach (var h in holes) if (Vector3.Distance(p, h) < h.w + extra) return true;
            return false;
        }
    }

    public static class Mats
    {
        static Shader _tissue, _unlit, _points, _monitor;
        public static Shader TissueShader => _tissue ? _tissue : _tissue = Load("LPN/Tissue");
        public static Shader UnlitShader => _unlit ? _unlit : _unlit = Load("LPN/Unlit");
        public static Shader PointsShader => _points ? _points : _points = Load("LPN/Points");
        public static Shader MonitorShader => _monitor ? _monitor : _monitor = Load("LPN/Monitor");

        // Shader'lar Resources/LPNShaders.asset (ShaderRefs) üzerinden derlemeye dahil edilir
        static Shader Load(string name)
        {
            var refs = Resources.Load<ShaderRefs>("LPNShaders");
            if (refs) foreach (var s in refs.shaders) if (s && s.name == name) return s;
            var sh = Shader.Find(name);
            if (!sh) Debug.LogError("LPN: shader bulunamadı: " + name + " (LPN-VR ▸ Projeyi Kur menüsünü çalıştırın)");
            return sh;
        }

        static Texture2D _white;
        public static Texture2D White
        {
            get
            {
                if (_white) return _white;
                _white = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                _white.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
                _white.Apply();
                return _white;
            }
        }

        public static Vector4 Lin(uint hex, float a = 1)
        {
            var c = U.Hex(hex).linear;
            return new Vector4(c.r, c.g, c.b, a);
        }

        // Islak film (wet, wetRough), kabartma gücü, girinti kararması, kanal başına saçılma sarması (R > G > B)
        public static readonly Dictionary<string, TissueLook> Looks = new Dictionary<string, TissueLook>
        {
            ["fat"] = new TissueLook(0.9f, 0.10f, 2.2f, 0.35f, new Color(0.60f, 0.42f, 0.18f)),
            ["kidney"] = new TissueLook(0.85f, 0.09f, 0.5f, 0.15f, new Color(0.55f, 0.22f, 0.16f)),
            ["tumor"] = new TissueLook(0.6f, 0.14f, 2.0f, 0.30f, new Color(0.50f, 0.32f, 0.14f)),
            ["colon"] = new TissueLook(0.95f, 0.08f, 1.4f, 0.25f, new Color(0.50f, 0.28f, 0.22f)),
            ["peritoneum"] = new TissueLook(1.0f, 0.06f, 0.45f, 0.15f, new Color(0.45f, 0.30f, 0.22f)),
            ["muscle"] = new TissueLook(0.6f, 0.14f, 1.6f, 0.30f, new Color(0.45f, 0.18f, 0.14f)),
            ["vein"] = new TissueLook(0.9f, 0.07f, 0.35f, 0.10f, new Color(0.30f, 0.18f, 0.32f)),
            ["artery"] = new TissueLook(0.8f, 0.09f, 0.5f, 0.10f, new Color(0.50f, 0.28f, 0.22f)),
            ["organ"] = new TissueLook(0.85f, 0.09f, 0.7f, 0.15f, new Color(0.50f, 0.24f, 0.20f)),
            ["wall"] = new TissueLook(0.8f, 0.10f, 0.4f, 0.25f, new Color(0.50f, 0.28f, 0.22f)),
            // ameliyathane yüzeyleri (saçılma yok; "ıslak" = cila/vernik parlaklığı)
            ["floor"] = new TissueLook(0.55f, 0.16f, 0.35f, 0.25f, Color.black),
            ["panel"] = new TissueLook(0.25f, 0.25f, 0.5f, 0.3f, Color.black),
            ["ceiling"] = new TissueLook(0f, 0.3f, 0.4f, 0.2f, Color.black),
            ["drape"] = new TissueLook(0f, 0.3f, 0.8f, 0.3f, new Color(0.15f, 0.12f, 0.1f)),
            ["steel"] = new TissueLook(0.3f, 0.12f, 0.25f, 0.1f, Color.black),
            ["mattress"] = new TissueLook(0.35f, 0.2f, 0.6f, 0.2f, Color.black),
        };

        static Texture2D _black;
        static Texture2D Black
        {
            get
            {
                if (_black) return _black;
                _black = new Texture2D(2, 2, TextureFormat.R8, false, true);
                _black.SetPixels32(new Color32[] { default, default, default, default }); _black.Apply();
                return _black;
            }
        }

        static void ApplyLook(Material m, MatOpts o)
        {
            string kind = o.tex && o.tex.name.StartsWith("tex-") ? o.tex.name.Substring(4) : null;
            var look = kind != null && Looks.TryGetValue(kind, out var l) ? l : new TissueLook(0, 0.1f, 0, 0, Color.black);
            Texture2D hm = null;
            if (kind != null) ProcTextures.Heights.TryGetValue(kind, out hm);
            float bump = o.bump ?? look.bump;
            m.SetTexture("_HeightMap", hm ? hm : Black);
            m.SetFloat("_BumpScale", hm ? bump : 0);
            m.SetFloat("_Wet", o.wet ?? look.wet);
            m.SetFloat("_WetRough", o.wetRough ?? look.wetRough);
            m.SetFloat("_Cavity", hm ? (o.cavity ?? look.cavity) : 0);
            var sss = o.sss ?? look.sss;
            m.SetVector("_SSS", new Vector4(sss.r, sss.g, sss.b, 0));
        }

        public static Material Std(MatOpts o)
        {
            var m = new Material(TissueShader);
            m.SetTexture("_BaseMap", o.tex ? o.tex : White);
            ApplyLook(m, o);
            m.SetVector("_Color", Lin(o.color, o.opacity));
            m.SetVector("_Emission", Lin(o.emissive));
            m.SetFloat("_Roughness", o.roughness);
            m.SetFloat("_Metallic", o.metallic);
            m.SetFloat("_Cull", (float)(o.side == Side.Front ? CullMode.Back : o.side == Side.Back ? CullMode.Front : CullMode.Off));
            if (o.transparent)
            {
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", o.depthWrite ? 1 : 0);
                m.renderQueue = (int)RenderQueue.Transparent;
            }
            else
            {
                m.SetFloat("_SrcBlend", (float)BlendMode.One);
                m.SetFloat("_DstBlend", (float)BlendMode.Zero);
                m.SetFloat("_ZWrite", 1);
                m.renderQueue = (int)RenderQueue.Geometry;
            }
            // _Deform kapalı, delik yok — düz standart materyal
            m.SetFloat("_Deform", 0);
            m.SetFloat("_HoleCount", 0);
            m.SetVector("_Tint", Vector4.one);
            m.SetVector("_Crater", Vector4.zero);
            m.SetVector("_PlaneCut", Vector4.zero);
            m.SetVector("_Ellip", Vector4.one);
            m.SetVector("_MapScale", new Vector4(o.mapScale.x, o.mapScale.y, 0, 0));
            return m;
        }
        public static Material Std(uint color, float roughness = 0.4f, float metallic = 0, Texture tex = null, Side side = Side.Front)
            => Std(new MatOpts { color = color, roughness = roughness, metallic = metallic, tex = tex, side = side });

        public static void SetTransparent(Material m, bool on, float opacity)
        {
            var c = m.GetVector("_Color"); c.w = opacity; m.SetVector("_Color", c);
            m.SetFloat("_SrcBlend", (float)(on ? BlendMode.SrcAlpha : BlendMode.One));
            m.SetFloat("_DstBlend", (float)(on ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
            m.SetFloat("_ZWrite", on ? 0 : 1);
            m.renderQueue = (int)(on ? RenderQueue.Transparent : RenderQueue.Geometry);
        }

        public static TissueMat Tissue(MatOpts o, bool holes, float sigma = 0.009f, bool deform = true)
            => new TissueMat(Std(o), holes, deform, sigma, o.side);

        public static Material Unlit(Color c, bool onTop = false)
        {
            var m = new Material(UnlitShader);
            m.SetColor("_Color", c);
            m.SetFloat("_ZTest", (float)(onTop ? CompareFunction.Always : CompareFunction.LessEqual));
            m.SetFloat("_ZWrite", onTop ? 0 : 1);
            m.renderQueue = onTop ? 3990 : 3000;
            return m;
        }
    }
}
