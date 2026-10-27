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

        public static Material Std(MatOpts o)
        {
            var m = new Material(TissueShader);
            m.SetTexture("_BaseMap", o.tex ? o.tex : White);
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
            m.SetVector("_MapScale", new Vector4(1, 1, 0, 0));
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
