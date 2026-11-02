// Prosedürel mesh parçası: GameObject + MeshFilter/MeshRenderer + CPU tarafı köşe verisi (ışın testi, yakınlık).
using System.Collections.Generic;
using UnityEngine;

namespace LPNVR
{
    public class Part : MonoBehaviour
    {
        public Vector3[] verts;
        public int[] tris;
        public Side side = Side.Front;
        public TissueMat tissue;
        public Material material;
        public MeshFilter mf;
        public MeshRenderer mr;
        public object userData;
        Vector3 bsCenter; float bsRadius = -1;

        public bool Visible
        {
            get => gameObject.activeSelf && (mr == null || mr.enabled);
            set => gameObject.SetActive(value);
        }

        public static Part Make(string name, MeshData g, Material mat, Transform parent, Side side = Side.Front)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.layer = parent ? parent.gameObject.layer : 0;
            var p = go.AddComponent<Part>();
            p.mf = go.AddComponent<MeshFilter>();
            p.mr = go.AddComponent<MeshRenderer>();
            p.mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            p.mr.receiveShadows = false;
            p.mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            p.mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            p.SetMaterial(mat);
            p.side = side;
            p.SetMesh(g);
            return p;
        }
        public static Part Make(string name, MeshData g, TissueMat tm, Transform parent)
        {
            var p = Make(name, g, tm.mat, parent, tm.side);
            p.tissue = tm;
            return p;
        }

        public void SetMaterial(Material m) { material = m; if (mr) mr.sharedMaterial = m; }

        public void SetMesh(MeshData g)
        {
            verts = g.pos.ToArray();
            tris = g.idx.ToArray();
            userData = g.userData ?? userData;
            mf.sharedMesh = g.ToMesh(name, mf.sharedMesh);
            mf.sharedMesh.RecalculateBounds();
            // Deforme olan dokular için sınırları genişlet (alet ucu çökmesi kesilmesin)
            var b = mf.sharedMesh.bounds; b.Expand(0.05f); mf.sharedMesh.bounds = b;
            bsRadius = -1;
        }

        public void BoundingSphere(out Vector3 c, out float r)
        {
            if (bsRadius < 0)
            {
                var b = new Bounds(verts.Length > 0 ? verts[0] : Vector3.zero, Vector3.zero);
                foreach (var v in verts) b.Encapsulate(v);
                bsCenter = b.center; bsRadius = 0;
                foreach (var v in verts) bsRadius = Mathf.Max(bsRadius, Vector3.Distance(v, bsCenter));
            }
            c = bsCenter; r = bsRadius;
        }

        // Yerel uzayda bir köşe verilen mesafenin içinde mi (sim.js nearSurface)
        public bool NearSurface(Vector3 lp, float maxD)
        {
            float d2 = maxD * maxD;
            for (int i = 0; i < verts.Length; i += 2) if ((verts[i] - lp).sqrMagnitude < d2) return true;
            return false;
        }

        public struct Hit
        {
            public float t;           // dünya ışın parametresi
            public Vector3 point;     // dünya
            public Vector3 localPoint;
            public Vector3 normalW;   // yüzey (geometrik) normali, dünya
            public Part part;
        }

        // Three.js Mesh.raycast karşılığı (materyal tarafına göre ön/arka yüz). Dünya ışını; t dünya biriminde.
        public void Raycast(Vector3 originW, Vector3 dirW, float near, float far, List<Hit> outHits)
        {
            if (!Visible || verts == null) return;
            var tr = transform;
            var o = tr.InverseTransformPoint(originW);
            var d = tr.InverseTransformVector(dirW); // ölçek korunur: t aynı kalır
            BoundingSphere(out var bc, out var br);
            // ışın–küre ön elemesi
            var oc = o - bc;
            float a = Vector3.Dot(d, d), bq = Vector3.Dot(oc, d), cq = Vector3.Dot(oc, oc) - br * br;
            if (bq * bq - a * cq < 0) return;
            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector3 v0 = verts[tris[i]], v1 = verts[tris[i + 1]], v2 = verts[tris[i + 2]];
                var e1 = v1 - v0; var e2 = v2 - v0;
                var pv = Vector3.Cross(d, e2);
                float det = Vector3.Dot(e1, pv);
                // det = -d·(e1×e2); det < 0 ⇔ ışın arka yüze çarpıyor (Three.js Ray.intersectTriangle kuralı)
                if (Mathf.Abs(det) < 1e-14f) continue;
                bool backface = det < 0;
                if (side == Side.Front && backface) continue;
                if (side == Side.Back && !backface) continue;
                float inv = 1f / det;
                var tv = o - v0;
                float u = Vector3.Dot(tv, pv) * inv; if (u < 0 || u > 1) continue;
                var qv = Vector3.Cross(tv, e1);
                float v = Vector3.Dot(d, qv) * inv; if (v < 0 || u + v > 1) continue;
                float t = Vector3.Dot(e2, qv) * inv;
                if (t < near || t > far) continue;
                var lp = o + d * t;
                var n = Vector3.Cross(e1, e2);
                outHits.Add(new Hit { t = t, point = tr.TransformPoint(lp), localPoint = lp, normalW = tr.TransformVector(n).N(), part = this });
            }
        }

        public static List<Hit> RaycastAll(IEnumerable<Part> parts, Vector3 o, Vector3 dir, float near = 0, float far = float.MaxValue)
        {
            var hits = new List<Hit>();
            foreach (var p in parts) if (p) p.Raycast(o, dir, near, far, hits);
            hits.Sort((x, y) => x.t.CompareTo(y.t));
            return hits;
        }
    }
}
