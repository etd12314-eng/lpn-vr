// Prosedürel geometri — Three.js geometri sınıflarının (Sphere, Cylinder, Box, Torus, Plane, tüp) birebir karşılıkları.
// Köşe düzeni, UV'ler ve indeks sırası Three.js ile aynıdır (bkz. Util.cs koordinat kuralı).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace LPNVR
{
    public class MeshData
    {
        public List<Vector3> pos = new List<Vector3>();
        public List<Vector3> nor = new List<Vector3>();
        public List<Vector2> uv = new List<Vector2>();
        public List<int> idx = new List<int>();
        public object userData; // ör. tüpün eğrisi

        public int Count => pos.Count;

        // Three.js computeVertexNormals (indeksli geometri: alan ağırlıklı yüz normalleri toplamı)
        public MeshData ComputeNormals()
        {
            var n = new Vector3[pos.Count];
            for (int i = 0; i < idx.Count; i += 3)
            {
                int a = idx[i], b = idx[i + 1], c = idx[i + 2];
                var cb = Vector3.Cross(pos[c] - pos[b], pos[a] - pos[b]);
                n[a] += cb; n[b] += cb; n[c] += cb;
            }
            nor = new List<Vector3>(pos.Count);
            for (int i = 0; i < n.Length; i++) nor.Add(n[i].N());
            return this;
        }

        public MeshData Translate(Vector3 t) { for (int i = 0; i < pos.Count; i++) pos[i] += t; return this; }
        public MeshData Scale(Vector3 s)
        {
            for (int i = 0; i < pos.Count; i++) pos[i] = Vector3.Scale(pos[i], s);
            if (nor.Count == pos.Count)
            {
                var inv = new Vector3(1 / s.x, 1 / s.y, 1 / s.z);
                for (int i = 0; i < nor.Count; i++) nor[i] = Vector3.Scale(nor[i], inv).N();
            }
            if (s.x * s.y * s.z < 0) FlipWinding();
            return this;
        }
        public MeshData FlipWinding()
        {
            for (int i = 0; i < idx.Count; i += 3) { int t = idx[i + 1]; idx[i + 1] = idx[i + 2]; idx[i + 2] = t; }
            return this;
        }
        public MeshData MapPositions(Func<Vector3, int, Vector3> f)
        {
            for (int i = 0; i < pos.Count; i++) pos[i] = f(pos[i], i);
            return this;
        }
        public MeshData Clone()
        {
            var m = new MeshData();
            m.pos.AddRange(pos); m.nor.AddRange(nor); m.uv.AddRange(uv); m.idx.AddRange(idx); m.userData = userData;
            return m;
        }

        public Mesh ToMesh(string name = "mesh", Mesh reuse = null)
        {
            var m = reuse ? reuse : new Mesh();
            m.name = name;
            if (reuse) m.Clear();
            if (pos.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(pos);
            if (nor.Count == pos.Count) m.SetNormals(nor);
            if (uv.Count == pos.Count) m.SetUVs(0, uv);
            m.SetTriangles(idx, 0, true);
            if (nor.Count != pos.Count) m.RecalculateNormals();
            return m;
        }
    }

    public static class Geo
    {
        public static MeshData Sphere(float r, int ws, int hs, float phiStart = 0, float phiLen = Mathf.PI * 2, float thetaStart = 0, float thetaLen = Mathf.PI)
        {
            var g = new MeshData();
            ws = Mathf.Max(3, ws); hs = Mathf.Max(2, hs);
            float thetaEnd = Mathf.Min(thetaStart + thetaLen, Mathf.PI);
            var grid = new int[hs + 1, ws + 1];
            int index = 0;
            for (int iy = 0; iy <= hs; iy++)
            {
                float v = (float)iy / hs;
                float uOff = 0;
                if (iy == 0 && thetaStart == 0) uOff = 0.5f / ws;
                else if (iy == hs && thetaEnd == Mathf.PI) uOff = -0.5f / ws;
                for (int ix = 0; ix <= ws; ix++)
                {
                    float u = (float)ix / ws;
                    var p = new Vector3(
                        -r * Mathf.Cos(phiStart + u * phiLen) * Mathf.Sin(thetaStart + v * thetaLen),
                        r * Mathf.Cos(thetaStart + v * thetaLen),
                        r * Mathf.Sin(phiStart + u * phiLen) * Mathf.Sin(thetaStart + v * thetaLen));
                    g.pos.Add(p); g.nor.Add(p.N()); g.uv.Add(new Vector2(u + uOff, 1 - v));
                    grid[iy, ix] = index++;
                }
            }
            for (int iy = 0; iy < hs; iy++)
                for (int ix = 0; ix < ws; ix++)
                {
                    int a = grid[iy, ix + 1], b = grid[iy, ix], c = grid[iy + 1, ix], d = grid[iy + 1, ix + 1];
                    if (iy != 0 || thetaStart > 0) { g.idx.Add(a); g.idx.Add(b); g.idx.Add(d); }
                    if (iy != hs - 1 || thetaEnd < Mathf.PI) { g.idx.Add(b); g.idx.Add(c); g.idx.Add(d); }
                }
            return g;
        }

        // Elipsoid (opsiyonel sinüzoidal yüzey gürültüsü) — anatomy.js ellipsoid()
        public static MeshData Ellipsoid(float rx, float ry, float rz, int seg = 40, float noise = 0)
        {
            var g = Sphere(1, seg, Mathf.RoundToInt(seg * 0.75f));
            g.MapPositions((p, i) =>
            {
                float k = 1 + (noise != 0 ? Mathf.Sin(p.x * 9 + p.y * 7) * Mathf.Cos(p.z * 8) * noise : 0);
                return new Vector3(p.x * rx * k, p.y * ry * k, p.z * rz * k);
            });
            return g.ComputeNormals();
        }

        public static MeshData Cylinder(float rTop, float rBottom, float h, int radial = 16, int hSeg = 1, bool open = false, float thetaStart = 0, float thetaLen = Mathf.PI * 2)
        {
            var g = new MeshData();
            float half = h / 2;
            var rows = new List<int[]>();
            float slope = (rBottom - rTop) / h;
            for (int y = 0; y <= hSeg; y++)
            {
                var row = new int[radial + 1];
                float v = (float)y / hSeg;
                float radius = v * (rBottom - rTop) + rTop;
                for (int x = 0; x <= radial; x++)
                {
                    float u = (float)x / radial;
                    float theta = u * thetaLen + thetaStart;
                    float s = Mathf.Sin(theta), c = Mathf.Cos(theta);
                    g.pos.Add(new Vector3(radius * s, -v * h + half, radius * c));
                    g.nor.Add(new Vector3(s, slope, c).N());
                    g.uv.Add(new Vector2(u, 1 - v));
                    row[x] = g.pos.Count - 1;
                }
                rows.Add(row);
            }
            for (int x = 0; x < radial; x++)
                for (int y = 0; y < hSeg; y++)
                {
                    int a = rows[y][x], b = rows[y + 1][x], c = rows[y + 1][x + 1], d = rows[y][x + 1];
                    g.idx.Add(a); g.idx.Add(b); g.idx.Add(d);
                    g.idx.Add(b); g.idx.Add(c); g.idx.Add(d);
                }
            if (!open)
            {
                if (rTop > 0) Cap(g, true, rTop, half, radial, thetaStart, thetaLen);
                if (rBottom > 0) Cap(g, false, rBottom, half, radial, thetaStart, thetaLen);
            }
            return g;
        }

        static void Cap(MeshData g, bool top, float radius, float half, int radial, float thetaStart, float thetaLen)
        {
            float sign = top ? 1 : -1;
            int centerStart = g.pos.Count;
            for (int x = 1; x <= radial; x++)
            {
                g.pos.Add(new Vector3(0, half * sign, 0)); g.nor.Add(new Vector3(0, sign, 0)); g.uv.Add(new Vector2(0.5f, 0.5f));
            }
            int centerEnd = g.pos.Count;
            for (int x = 0; x <= radial; x++)
            {
                float u = (float)x / radial, theta = u * thetaLen + thetaStart;
                float c = Mathf.Cos(theta), s = Mathf.Sin(theta);
                g.pos.Add(new Vector3(radius * s, half * sign, radius * c)); g.nor.Add(new Vector3(0, sign, 0));
                g.uv.Add(new Vector2(c * 0.5f + 0.5f, s * 0.5f * sign + 0.5f));
            }
            for (int x = 0; x < radial; x++)
            {
                int c = centerStart + x, i = centerEnd + x;
                if (top) { g.idx.Add(i); g.idx.Add(i + 1); g.idx.Add(c); }
                else { g.idx.Add(i + 1); g.idx.Add(i); g.idx.Add(c); }
            }
        }

        public static MeshData Cone(float r, float h, int radial = 16, bool open = false) => Cylinder(0, r, h, radial, 1, open);

        public static MeshData Box(float w, float h, float d)
        {
            var g = new MeshData();
            // Three.js BoxGeometry yüz sırası: +x, -x, +y, -y, +z, -z
            Plane(g, 2, 1, 0, -1, -1, d, h, w);
            Plane(g, 2, 1, 0, 1, -1, d, h, -w);
            Plane(g, 0, 2, 1, 1, 1, w, d, h);
            Plane(g, 0, 2, 1, 1, -1, w, d, -h);
            Plane(g, 0, 1, 2, 1, -1, w, h, d);
            Plane(g, 0, 1, 2, -1, -1, w, h, -d);
            return g;
        }

        static void Plane(MeshData g, int u, int v, int w, float udir, float vdir, float width, float height, float depth)
        {
            float wh = width / 2, hh = height / 2, dh = depth / 2;
            int start = g.pos.Count;
            for (int iy = 0; iy <= 1; iy++)
            {
                float y = iy * height - hh;
                for (int ix = 0; ix <= 1; ix++)
                {
                    float x = ix * width - wh;
                    var p = Vector3.zero; p[u] = x * udir; p[v] = y * vdir; p[w] = dh;
                    var n = Vector3.zero; n[w] = depth > 0 ? 1 : -1;
                    g.pos.Add(p); g.nor.Add(n); g.uv.Add(new Vector2(ix, 1 - iy));
                }
            }
            int a = start, b = start + 2, c = start + 3, d = start + 1;
            g.idx.Add(a); g.idx.Add(b); g.idx.Add(d);
            g.idx.Add(b); g.idx.Add(c); g.idx.Add(d);
        }

        // Three.js PlaneGeometry (XY düzleminde, normal +Z)
        public static MeshData PlaneXY(float w, float h, int ws = 1, int hs = 1)
        {
            var g = new MeshData();
            float wh = w / 2, hh = h / 2, sw = w / ws, sh = h / hs;
            for (int iy = 0; iy <= hs; iy++)
                for (int ix = 0; ix <= ws; ix++)
                {
                    g.pos.Add(new Vector3(ix * sw - wh, -(iy * sh - hh), 0));
                    g.nor.Add(new Vector3(0, 0, 1));
                    g.uv.Add(new Vector2((float)ix / ws, 1 - (float)iy / hs));
                }
            for (int iy = 0; iy < hs; iy++)
                for (int ix = 0; ix < ws; ix++)
                {
                    int a = ix + (ws + 1) * iy, b = ix + (ws + 1) * (iy + 1), c = ix + 1 + (ws + 1) * (iy + 1), d = ix + 1 + (ws + 1) * iy;
                    g.idx.Add(a); g.idx.Add(b); g.idx.Add(d);
                    g.idx.Add(b); g.idx.Add(c); g.idx.Add(d);
                }
            return g;
        }

        public static MeshData Torus(float radius, float tube, int radialSeg, int tubularSeg, float arc = Mathf.PI * 2)
        {
            var g = new MeshData();
            for (int j = 0; j <= radialSeg; j++)
                for (int i = 0; i <= tubularSeg; i++)
                {
                    float u = (float)i / tubularSeg * arc, v = (float)j / radialSeg * Mathf.PI * 2;
                    var p = new Vector3((radius + tube * Mathf.Cos(v)) * Mathf.Cos(u), (radius + tube * Mathf.Cos(v)) * Mathf.Sin(u), tube * Mathf.Sin(v));
                    var c = new Vector3(radius * Mathf.Cos(u), radius * Mathf.Sin(u), 0);
                    g.pos.Add(p); g.nor.Add((p - c).N()); g.uv.Add(new Vector2((float)i / tubularSeg, (float)j / radialSeg));
                }
            for (int j = 1; j <= radialSeg; j++)
                for (int i = 1; i <= tubularSeg; i++)
                {
                    int a = (tubularSeg + 1) * j + i - 1, b = (tubularSeg + 1) * (j - 1) + i - 1, c = (tubularSeg + 1) * (j - 1) + i, d = (tubularSeg + 1) * j + i;
                    g.idx.Add(a); g.idx.Add(b); g.idx.Add(d);
                    g.idx.Add(b); g.idx.Add(c); g.idx.Add(d);
                }
            return g;
        }

        // Değişken yarıçaplı tüp (damar, üreter, psoas, kolon) — util.js taperTube
        public static MeshData TaperTube(IList<Vector3> points, Func<float, float> radius, int tubular = 64, int radial = 12)
        {
            var curve = new CatmullRom(points);
            curve.FrenetFrames(tubular, out _, out var N, out var B);
            var g = new MeshData { userData = curve };
            for (int i = 0; i <= tubular; i++)
            {
                float t = (float)i / tubular;
                var P = curve.GetPointAt(t);
                float r = radius(t);
                var n = N[Mathf.Min(i, N.Length - 1)];
                var b = B[Mathf.Min(i, B.Length - 1)];
                for (int j = 0; j <= radial; j++)
                {
                    float a = (float)j / radial * Mathf.PI * 2;
                    var nn = (n * Mathf.Cos(a) + b * Mathf.Sin(a)).N();
                    g.pos.Add(P + nn * r); g.nor.Add(nn); g.uv.Add(new Vector2(t * 4, (float)j / radial));
                }
            }
            for (int i = 0; i < tubular; i++)
                for (int j = 0; j < radial; j++)
                {
                    int a = i * (radial + 1) + j, b = (i + 1) * (radial + 1) + j;
                    g.idx.Add(a); g.idx.Add(a + 1); g.idx.Add(b);
                    g.idx.Add(b); g.idx.Add(a + 1); g.idx.Add(b + 1);
                }
            foreach (int end in new[] { 0, tubular })
            {
                int c = g.pos.Count;
                var P = curve.GetPointAt((float)end / tubular);
                var tg = curve.GetTangentAt((float)end / tubular) * (end == 0 ? -1 : 1);
                g.pos.Add(P); g.nor.Add(tg); g.uv.Add(Vector2.zero);
                for (int j = 0; j < radial; j++)
                {
                    int a = end * (radial + 1) + j;
                    if (end == 0) { g.idx.Add(c); g.idx.Add(a + 1); g.idx.Add(a); }
                    else { g.idx.Add(c); g.idx.Add(a); g.idx.Add(a + 1); }
                }
            }
            return g;
        }
        public static MeshData TaperTube(IList<Vector3> points, float radius, int tubular = 64, int radial = 12)
            => TaperTube(points, _ => radius, tubular, radial);

        public static List<Vector3> SamplePoints(IList<Vector3> points, int n = 40) => new CatmullRom(points).SpacedPoints(n);
    }
}
