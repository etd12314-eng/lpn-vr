// Ortak yardımcılar: vektör kısayolları, deterministik rastgele, poligon mesafesi, CatmullRom eğrisi.
//
// KOORDİNAT KURALI
// Tüm anatomi/simülasyon verisi web sürümüyle (Three.js) birebir aynı sayılarla, sağ-elli "S-uzayı"nda tutulur:
// hasta-yerel +X = hastanın solu, +Y = kraniyal, +Z = anterior. Bu nesnelerin hepsi ölçeği (1, 1, -1) olan
// tek bir FlipRoot altında durur; böylece Unity'nin sol-elli dünyasında anatomi aynalanmadan görünür.
// Unity, negatif ölçekli dönüşümlerde üçgen sarımını kendisi tersine çevirdiği için mesh indeksleri değiştirilmez.
// Kamera, VR kontrolcüleri, aletler ve UI panelleri ise FlipRoot dışında, normal Unity dünyasındadır;
// iki dünya arasında yalnız Transform.TransformPoint / InverseTransformPoint ile geçilir.
using System.Collections.Generic;
using UnityEngine;

namespace LPNVR
{
    public static class VecExt
    {
        // Three.js Vector3.normalize() eşdeğeri. Unity'nin .normalized'ı |v| < 1e-5 için sıfır döndürür;
        // milimetre ölçekli anatomide (tüp teğetleri, küçük üçgen normalleri) bu yanlış sonuç verir.
        public static Vector3 N(this Vector3 v)
        {
            double m = System.Math.Sqrt((double)v.x * v.x + (double)v.y * v.y + (double)v.z * v.z);
            return m > 1e-30 ? new Vector3((float)(v.x / m), (float)(v.y / m), (float)(v.z / m)) : Vector3.zero;
        }
    }

    public static class U
    {
        public static Vector3 V(float x = 0, float y = 0, float z = 0) => new Vector3(x, y, z);
        public static float Clamp(float v, float a, float b) => v < a ? a : v > b ? b : v;
        public const float D2R = Mathf.Deg2Rad;

        // Deterministik rastgele (her açılışta aynı anatomi) — web sürümüyle aynı Park–Miller dizisi
        static double _seed = 1234567;
        public static float Rand()
        {
            _seed = (_seed * 16807) % 2147483647;
            return (float)((_seed - 1) / 2147483646.0);
        }
        public static void ResetSeed(double s = 1234567) { _seed = s; }

        // Three.js Object3D.lookAt (nesneler için): +Z hedefe bakar
        public static Quaternion LookAt(Vector3 from, Vector3 to, Vector3 up)
        {
            var f = (to - from).N();
            if (f == Vector3.zero) return Quaternion.identity;
            up = up.N();
            if (Vector3.Cross(up, f).sqrMagnitude < 1e-8f) up = Mathf.Abs(f.N().y) > 0.9f ? Vector3.forward : Vector3.up;
            return Quaternion.LookRotation(f, up);
        }

        // Three.js Euler sırası 'XZY': R = Rx · Rz · Ry
        public static Quaternion EulerXZY(float x, float y, float z)
            => Quaternion.AngleAxis(x * Mathf.Rad2Deg, Vector3.right) * Quaternion.AngleAxis(z * Mathf.Rad2Deg, Vector3.forward) * Quaternion.AngleAxis(y * Mathf.Rad2Deg, Vector3.up);
        // Three.js varsayılan Euler sırası 'XYZ': R = Rx · Ry · Rz
        public static Quaternion EulerXYZ(float x, float y, float z)
            => Quaternion.AngleAxis(x * Mathf.Rad2Deg, Vector3.right) * Quaternion.AngleAxis(y * Mathf.Rad2Deg, Vector3.up) * Quaternion.AngleAxis(z * Mathf.Rad2Deg, Vector3.forward);

        // Noktanın çoklu doğruya uzaklığı; s = eğri boyunca normalize konum (0..1)
        public static float DistToPolyline(Vector3 p, IList<Vector3> pts, out Vector3 q, out float s)
        {
            float best = float.MaxValue, bi = 0; q = pts[0];
            for (int i = 0; i < pts.Count - 1; i++)
            {
                var ab = pts[i + 1] - pts[i];
                var ap = p - pts[i];
                float t = Clamp(Vector3.Dot(ap, ab) / Mathf.Max(1e-9f, ab.sqrMagnitude), 0, 1);
                var qq = pts[i] + ab * t;
                float d = Vector3.Distance(qq, p);
                if (d < best) { best = d; bi = i + t; q = qq; }
            }
            s = bi / (pts.Count - 1);
            return best;
        }
        public static float DistToPolyline(Vector3 p, IList<Vector3> pts) => DistToPolyline(p, pts, out _, out _);

        public static Color Hex(uint hex, float a = 1)
            => new Color(((hex >> 16) & 255) / 255f, ((hex >> 8) & 255) / 255f, (hex & 255) / 255f, a);

        public static void SetLayer(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform c in go.transform) SetLayer(c.gameObject, layer);
        }

        public static GameObject Group(string name, Transform parent)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.layer = parent ? parent.gameObject.layer : 0;
            return g;
        }

        public static string Fmt(float sec)
        {
            int s = Mathf.Max(0, (int)sec);
            return $"{s / 60:00}:{s % 60:00}";
        }
    }

    // Three.js CatmullRomCurve3 ('centripetal') — getPoint / getPointAt / getSpacedPoints / Frenet çerçeveleri
    public class CatmullRom
    {
        readonly Vector3[] pts;
        float[] lengths;
        const int ArcDivisions = 200;

        public CatmullRom(IList<Vector3> points) { pts = new Vector3[points.Count]; points.CopyTo(pts, 0); }

        public Vector3 GetPoint(float t)
        {
            int l = pts.Length;
            float p = (l - 1) * t;
            int intPoint = Mathf.FloorToInt(p);
            float weight = p - intPoint;
            if (weight == 0 && intPoint == l - 1) { intPoint = l - 2; weight = 1; }
            Vector3 p0 = intPoint > 0 ? pts[(intPoint - 1) % l] : 2 * pts[0] - pts[1];
            Vector3 p1 = pts[intPoint % l], p2 = pts[(intPoint + 1) % l];
            Vector3 p3 = intPoint + 2 < l ? pts[(intPoint + 2) % l] : 2 * pts[l - 1] - pts[l - 2];
            float dt0 = Mathf.Pow((p0 - p1).sqrMagnitude, 0.25f);
            float dt1 = Mathf.Pow((p1 - p2).sqrMagnitude, 0.25f);
            float dt2 = Mathf.Pow((p2 - p3).sqrMagnitude, 0.25f);
            if (dt1 < 1e-4f) dt1 = 1.0f;
            if (dt0 < 1e-4f) dt0 = dt1;
            if (dt2 < 1e-4f) dt2 = dt1;
            return new Vector3(Nonuniform(p0.x, p1.x, p2.x, p3.x, dt0, dt1, dt2, weight),
                               Nonuniform(p0.y, p1.y, p2.y, p3.y, dt0, dt1, dt2, weight),
                               Nonuniform(p0.z, p1.z, p2.z, p3.z, dt0, dt1, dt2, weight));
        }

        static float Nonuniform(float x0, float x1, float x2, float x3, float dt0, float dt1, float dt2, float t)
        {
            float t1 = (x1 - x0) / dt0 - (x2 - x0) / (dt0 + dt1) + (x2 - x1) / dt1;
            float t2 = (x2 - x1) / dt1 - (x3 - x1) / (dt1 + dt2) + (x3 - x2) / dt2;
            t1 *= dt1; t2 *= dt1;
            float c0 = x1, c1 = t1, c2 = -3 * x1 + 3 * x2 - 2 * t1 - t2, c3 = 2 * x1 - 2 * x2 + t1 + t2;
            float tt = t * t;
            return c0 + c1 * t + c2 * tt + c3 * tt * t;
        }

        float[] Lengths()
        {
            if (lengths != null) return lengths;
            lengths = new float[ArcDivisions + 1];
            Vector3 last = GetPoint(0); float sum = 0;
            for (int i = 1; i <= ArcDivisions; i++)
            {
                var c = GetPoint((float)i / ArcDivisions);
                sum += Vector3.Distance(c, last); lengths[i] = sum; last = c;
            }
            return lengths;
        }

        public float UtoT(float u)
        {
            var arc = Lengths();
            int il = arc.Length;
            float target = u * arc[il - 1];
            int low = 0, high = il - 1;
            while (low <= high)
            {
                int i = low + (high - low) / 2;
                float cmp = arc[i] - target;
                if (cmp < 0) low = i + 1; else if (cmp > 0) high = i - 1; else { high = i; break; }
            }
            int k = Mathf.Max(0, high);
            if (arc[k] == target) return (float)k / (il - 1);
            if (k >= il - 1) return 1;
            float before = arc[k], after = arc[k + 1];
            return (k + (target - before) / (after - before)) / (il - 1);
        }

        public Vector3 GetPointAt(float u) => GetPoint(UtoT(u));

        public Vector3 GetTangent(float t)
        {
            const float delta = 0.0001f;
            float t1 = Mathf.Max(0, t - delta), t2 = Mathf.Min(1, t + delta);
            return (GetPoint(t2) - GetPoint(t1)).N();
        }
        public Vector3 GetTangentAt(float u) => GetTangent(UtoT(u));

        public List<Vector3> SpacedPoints(int n)
        {
            var o = new List<Vector3>(n + 1);
            for (int d = 0; d <= n; d++) o.Add(GetPointAt((float)d / n));
            return o;
        }

        // Three.js Curve.computeFrenetFrames (açık eğri)
        public void FrenetFrames(int segments, out Vector3[] T, out Vector3[] N, out Vector3[] B)
        {
            T = new Vector3[segments + 1]; N = new Vector3[segments + 1]; B = new Vector3[segments + 1];
            for (int i = 0; i <= segments; i++) T[i] = GetTangentAt((float)i / segments);
            float min = float.MaxValue;
            float tx = Mathf.Abs(T[0].x), ty = Mathf.Abs(T[0].y), tz = Mathf.Abs(T[0].z);
            var normal = Vector3.zero;
            if (tx <= min) { min = tx; normal = new Vector3(1, 0, 0); }
            if (ty <= min) { min = ty; normal = new Vector3(0, 1, 0); }
            if (tz <= min) { normal = new Vector3(0, 0, 1); }
            var vec = Vector3.Cross(T[0], normal).N();
            N[0] = Vector3.Cross(T[0], vec);
            B[0] = Vector3.Cross(T[0], N[0]);
            for (int i = 1; i <= segments; i++)
            {
                N[i] = N[i - 1];
                B[i] = B[i - 1];
                var v = Vector3.Cross(T[i - 1], T[i]);
                if (v.magnitude > float.Epsilon)
                {
                    v = v.N();
                    float theta = Mathf.Acos(U.Clamp(Vector3.Dot(T[i - 1], T[i]), -1, 1));
                    N[i] = RotateAxis(N[i], v, theta);
                }
                B[i] = Vector3.Cross(T[i], N[i]);
            }
        }

        // Sağ-el kuralı (Three.js makeRotationAxis) ile eksen etrafında döndürme
        public static Vector3 RotateAxis(Vector3 p, Vector3 k, float a)
        {
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            return p * c + Vector3.Cross(k, p) * s + k * Vector3.Dot(k, p) * (1 - c);
        }
    }
}
