// Parçacık sistemi (kan damlaları, koter dumanı) — sim.js emit/stepParticles karşılığı.
// Konumlar hasta-yerel (A.root) uzayındadır; her parçacık billboard dörtgen (LPN/Points shader).
using UnityEngine;

namespace LPNVR
{
    public class ParticleSys
    {
        public readonly int N;
        readonly Vector3[] pos, vel, verts;
        readonly float[] life;
        int i;
        readonly Mesh mesh;
        public readonly GameObject go;

        public ParticleSys(string name, Transform parent, int n, Material mat)
        {
            N = n;
            pos = new Vector3[n]; vel = new Vector3[n]; life = new float[n];
            verts = new Vector3[n * 4];
            var uv = new Vector2[n * 4]; var idx = new int[n * 6];
            for (int k = 0; k < n; k++)
            {
                uv[k * 4] = new Vector2(-1, -1); uv[k * 4 + 1] = new Vector2(-1, 1); uv[k * 4 + 2] = new Vector2(1, 1); uv[k * 4 + 3] = new Vector2(1, -1);
                idx[k * 6] = k * 4; idx[k * 6 + 1] = k * 4 + 1; idx[k * 6 + 2] = k * 4 + 2;
                idx[k * 6 + 3] = k * 4; idx[k * 6 + 4] = k * 4 + 2; idx[k * 6 + 5] = k * 4 + 3;
                pos[k] = new Vector3(0, -10, 0);
            }
            mesh = new Mesh { name = name };
            mesh.MarkDynamic();
            for (int k = 0; k < n; k++) for (int c = 0; c < 4; c++) verts[k * 4 + c] = pos[k];
            mesh.vertices = verts; mesh.uv = uv; mesh.triangles = idx;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100);
            go = U.Group(name, parent);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
        }

        public void Emit(Vector3 p, Vector3 v, float spread)
        {
            i = (i + 1) % N;
            pos[i] = p;
            vel[i] = v + new Vector3((Random.value - .5f) * spread, (Random.value - .5f) * spread, (Random.value - .5f) * spread);
            life[i] = 1;
        }

        public void Step(float dt, Vector3 acc, float decay)
        {
            for (int k = 0; k < N; k++)
            {
                if (life[k] <= 0) pos[k] = new Vector3(0, -10, 0);
                else
                {
                    life[k] -= dt * decay;
                    vel[k] += acc * dt;
                    pos[k] += vel[k] * dt;
                }
                verts[k * 4] = verts[k * 4 + 1] = verts[k * 4 + 2] = verts[k * 4 + 3] = pos[k];
            }
            mesh.vertices = verts;
        }
    }
}
