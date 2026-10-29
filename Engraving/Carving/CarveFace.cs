using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Engraving.Carving
{
    /// <summary>
    /// The carvable face of one renderer: the triangles on the outermost plane of the piece on one side
    /// (piece-local +Z or -Z), and a copy of the mesh with those triangles moved to their own submesh with
    /// flat UVs (0..1 across the whole face as seen from that side).
    /// </summary>
    internal sealed class CarveFace
    {
        public MeshRenderer Renderer;
        public Mesh Original;
        public Mesh Carved;
        /// <summary>Material slot the face triangles came from (the one most of them used).</summary>
        public int SourceSlot;
        /// <summary>The new submesh index holding the face.</summary>
        public int FaceSlot;
        /// <summary>Least-squares map from face UV (u, v, 1) to the original UV0, so the wall's own texture can be resampled.</summary>
        public Vector3 MapU, MapV;
        public int Triangles;
    }

    /// <summary>All carvable faces of a piece on one side, sharing one face rectangle.</summary>
    internal sealed class CarveModel
    {
        public int Side;
        /// <summary>Face rectangle in piece-local X/Y (metres).</summary>
        public Rect Bounds;
        public readonly List<CarveFace> Faces = new List<CarveFace>();
        public string Note;

        public float Width => Bounds.width;
        public float Height => Bounds.height;

        /// <summary>A face must point within this of the side axis (cos of the angle).</summary>
        const float MinFacing = 0.7f;
        /// <summary>...and lie within this of the outermost point on that side (metres).</summary>
        const float Depth = 0.06f;

        static readonly Dictionary<string, CarveModel> Cache = new Dictionary<string, CarveModel>();

        /// <summary>The renderers that show the piece up close: LOD0 if there is a LOD group, else every mesh renderer.</summary>
        static List<MeshRenderer> CloseRenderers(GameObject root)
        {
            var all = root.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.GetComponent<MeshFilter>()?.sharedMesh != null).ToList();
            var lodGroups = root.GetComponentsInChildren<LODGroup>(true);
            if (lodGroups.Length == 0) return all;
            var far = new HashSet<Renderer>();
            foreach (var g in lodGroups)
            {
                var lods = g.GetLODs();
                for (int i = 1; i < lods.Length; i++)
                    foreach (var r in lods[i].renderers) if (r != null) far.Add(r);
                foreach (var r in lods.Length > 0 ? lods[0].renderers : new Renderer[0]) if (r != null) far.Remove(r);
            }
            return all.Where(r => !far.Contains(r)).ToList();
        }

        /// <summary>Builds (or reuses) the carve model for this piece and side. Null if no flat face was found.</summary>
        public static CarveModel For(GameObject root, string prefabName, int side)
        {
            string key = prefabName + "|" + side;
            if (Cache.TryGetValue(key, out var cached)) return cached?.Bind(root);

            CarveModel model = null;
            try { model = Build(root, side); }
            catch (Exception e) { Jotunn.Logger.LogWarning($"[Engraving] Can't find a carvable face on '{prefabName}': {e.Message}"); }
            if (model != null)
                Jotunn.Logger.LogInfo($"[Engraving] '{prefabName}' side {(side > 0 ? "+Z" : "-Z")}: {model.Note}");
            else
                Jotunn.Logger.LogWarning($"[Engraving] '{prefabName}' has no flat face on side {(side > 0 ? "+Z" : "-Z")}.");
            Cache[key] = model;
            return model?.Bind(root);
        }

        /// <summary>Path of a transform below the root, to find the same renderer on another instance.</summary>
        static string PathOf(Transform t, Transform root)
        {
            var parts = new List<string>();
            for (var x = t; x != null && x != root; x = x.parent) parts.Add(x.name + "#" + x.GetSiblingIndex());
            parts.Reverse();
            return string.Join("/", parts);
        }

        readonly List<string> _paths = new List<string>();

        /// <summary>The cached model with its renderers pointed at this instance's.</summary>
        CarveModel Bind(GameObject root)
        {
            var bound = new CarveModel { Side = Side, Bounds = Bounds, Note = Note };
            var byPath = root.GetComponentsInChildren<MeshRenderer>(true).ToDictionary(r => PathOf(r.transform, root.transform), r => r);
            for (int i = 0; i < Faces.Count; i++)
            {
                if (!byPath.TryGetValue(_paths[i], out var r)) continue;
                var f = Faces[i];
                bound.Faces.Add(new CarveFace
                {
                    Renderer = r, Original = f.Original, Carved = f.Carved, SourceSlot = f.SourceSlot,
                    FaceSlot = f.FaceSlot, MapU = f.MapU, MapV = f.MapV, Triangles = f.Triangles,
                });
            }
            return bound.Faces.Count > 0 ? bound : null;
        }

        static CarveModel Build(GameObject root, int side)
        {
            var rootInv = root.transform.worldToLocalMatrix;
            var renderers = CloseRenderers(root);
            var snaps = new List<(MeshRenderer r, MeshSnapshot s, Matrix4x4 toRoot)>();
            float outer = float.NegativeInfinity;
            foreach (var r in renderers)
            {
                var mesh = r.GetComponent<MeshFilter>().sharedMesh;
                MeshSnapshot snap;
                try { snap = MeshReader.Read(mesh); }
                catch (Exception e) { Jotunn.Logger.LogWarning($"[Engraving] Mesh '{mesh.name}' unreadable: {e.Message}"); continue; }
                var toRoot = rootInv * r.transform.localToWorldMatrix;
                snaps.Add((r, snap, toRoot));
                foreach (var v in snap.Vertices) outer = Mathf.Max(outer, toRoot.MultiplyPoint3x4(v).z * side);
            }
            if (snaps.Count == 0) return null;

            // pick the face triangles on each renderer
            var picked = new List<(MeshRenderer r, MeshSnapshot s, Matrix4x4 toRoot, List<int> tris, int slot)>();
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (var (r, s, toRoot) in snaps)
            {
                var tris = new List<int>();
                var slotCount = new int[s.SubMeshTriangles.Count];
                for (int sm = 0; sm < s.SubMeshTriangles.Count; sm++)
                {
                    var t = s.SubMeshTriangles[sm];
                    for (int i = 0; i + 2 < t.Length; i += 3)
                    {
                        Vector3 a = toRoot.MultiplyPoint3x4(s.Vertices[t[i]]);
                        Vector3 b = toRoot.MultiplyPoint3x4(s.Vertices[t[i + 1]]);
                        Vector3 c = toRoot.MultiplyPoint3x4(s.Vertices[t[i + 2]]);
                        var n = Vector3.Cross(b - a, c - a);
                        if (n.sqrMagnitude < 1e-12f) continue;
                        n.Normalize();
                        // front faces wind clockwise in Unity, for which Cross(b-a, c-a) points outward
                        if (n.z * side < MinFacing) continue;
                        float z = (a.z + b.z + c.z) / 3f * side;
                        if (z < outer - Depth) continue;
                        tris.Add(t[i]); tris.Add(t[i + 1]); tris.Add(t[i + 2]);
                        slotCount[sm]++;
                        foreach (var p in new[] { a, b, c })
                        {
                            x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x);
                            y0 = Mathf.Min(y0, p.y); y1 = Mathf.Max(y1, p.y);
                        }
                    }
                }
                if (tris.Count == 0) continue;
                int slot = Array.IndexOf(slotCount, slotCount.Max());
                picked.Add((r, s, toRoot, tris, slot));
            }
            if (picked.Count == 0 || x1 - x0 < 0.05f || y1 - y0 < 0.05f) return null;

            var model = new CarveModel { Side = side, Bounds = Rect.MinMaxRect(x0, y0, x1, y1) };
            int totalTris = 0;
            foreach (var (r, s, toRoot, tris, slot) in picked)
            {
                var face = MakeFace(model, r, s, toRoot, tris, slot);
                model.Faces.Add(face);
                model._paths.Add(PathOf(r.transform, root.transform));
                totalTris += face.Triangles;
            }
            model.Note = $"{model.Faces.Count} renderer(s), {totalTris} face triangles, face {model.Width:0.00} x {model.Height:0.00} m" +
                         (picked.Any(p => p.s.FromGpu) ? ", mesh read from the GPU" : "");
            return model;
        }

        Vector2 Project(Vector3 p)
        {
            // seen from the +Z side the viewer's right is -X, so flip u there to avoid a mirrored carving
            float u = (p.x - Bounds.xMin) / Bounds.width;
            if (Side > 0) u = 1f - u;
            return new Vector2(u, (p.y - Bounds.yMin) / Bounds.height);
        }

        static CarveFace MakeFace(CarveModel model, MeshRenderer r, MeshSnapshot s, Matrix4x4 toRoot, List<int> faceTris, int sourceSlot)
        {
            var mesh = r.GetComponent<MeshFilter>().sharedMesh;
            bool haveNormals = s.Normals != null && s.Normals.Length == s.Vertices.Length;
            bool haveUvs = s.Uvs != null && s.Uvs.Length == s.Vertices.Length;
            var verts = new List<Vector3>(s.Vertices);
            var norms = haveNormals ? new List<Vector3>(s.Normals) : null;
            var uvs = new List<Vector2>(haveUvs ? s.Uvs : new Vector2[s.Vertices.Length]);

            // remove the face triangles from their submeshes
            var faceSet = new HashSet<long>();
            for (int i = 0; i < faceTris.Count; i += 3) faceSet.Add(TriKey(faceTris[i], faceTris[i + 1], faceTris[i + 2]));
            var subs = new List<int[]>();
            foreach (var t in s.SubMeshTriangles)
            {
                var keep = new List<int>(t.Length);
                for (int i = 0; i + 2 < t.Length; i += 3)
                    if (!faceSet.Contains(TriKey(t[i], t[i + 1], t[i + 2]))) { keep.Add(t[i]); keep.Add(t[i + 1]); keep.Add(t[i + 2]); }
                subs.Add(keep.ToArray());
            }

            // the face on copies of its vertices, with projected UVs; collect data for the UV fit
            var map = new Dictionary<int, int>();
            var faceIdx = new int[faceTris.Count];
            var fit = new List<(Vector2 face, Vector2 orig)>();
            for (int k = 0; k < faceTris.Count; k++)
            {
                int i = faceTris[k];
                if (!map.TryGetValue(i, out int j))
                {
                    j = verts.Count;
                    map[i] = j;
                    verts.Add(s.Vertices[i]);
                    if (haveNormals) norms.Add(s.Normals[i]);
                    var fuv = model.Project(toRoot.MultiplyPoint3x4(s.Vertices[i]));
                    uvs.Add(fuv);
                    if (haveUvs) fit.Add((fuv, s.Uvs[i]));
                }
                faceIdx[k] = j;
            }
            subs.Add(faceIdx);

            var m = new Mesh { indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            m.name = mesh.name + "_carved";
            m.SetVertices(verts);
            if (haveNormals) m.SetNormals(norms);
            m.SetUVs(0, uvs);
            m.subMeshCount = subs.Count;
            for (int i = 0; i < subs.Count; i++) m.SetTriangles(subs[i], i);
            if (!haveNormals) m.RecalculateNormals();
            m.RecalculateBounds();
            m.RecalculateTangents();
            m.UploadMeshData(true);

            var face = new CarveFace
            {
                Renderer = r, Original = mesh, Carved = m, SourceSlot = sourceSlot, FaceSlot = subs.Count - 1,
                Triangles = faceTris.Count / 3,
            };
            FitUvMap(fit, out face.MapU, out face.MapV);
            return face;
        }

        static long TriKey(int a, int b, int c)
        {
            // order-independent key for a triangle
            int lo = Math.Min(a, Math.Min(b, c)), hi = Math.Max(a, Math.Max(b, c)), mid = a + b + c - lo - hi;
            return ((long)lo * 1000003L + mid) * 1000003L + hi;
        }

        /// <summary>Least squares: orig.x ≈ MapU·(u,v,1), orig.y ≈ MapV·(u,v,1).</summary>
        static void FitUvMap(List<(Vector2 face, Vector2 orig)> pts, out Vector3 mu, out Vector3 mv)
        {
            mu = new Vector3(1, 0, 0); mv = new Vector3(0, 1, 0);
            if (pts.Count < 3) return;
            double suu = 0, suv = 0, su = 0, svv = 0, sv = 0, n = pts.Count;
            double sxu = 0, sxv = 0, sx = 0, syu = 0, syv = 0, sy = 0;
            foreach (var (f, o) in pts)
            {
                suu += f.x * f.x; suv += f.x * f.y; su += f.x; svv += f.y * f.y; sv += f.y;
                sxu += o.x * f.x; sxv += o.x * f.y; sx += o.x;
                syu += o.y * f.x; syv += o.y * f.y; sy += o.y;
            }
            var A = new double[3, 3] { { suu, suv, su }, { suv, svv, sv }, { su, sv, n } };
            if (Solve(A, new[] { sxu, sxv, sx }, out var a) && Solve(A, new[] { syu, syv, sy }, out var b))
            {
                mu = new Vector3((float)a[0], (float)a[1], (float)a[2]);
                mv = new Vector3((float)b[0], (float)b[1], (float)b[2]);
            }
        }

        static bool Solve(double[,] m, double[] r, out double[] x)
        {
            x = null;
            double det = m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1]) - m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0]) + m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);
            if (Math.Abs(det) < 1e-12) return false;
            x = new double[3];
            for (int c = 0; c < 3; c++)
            {
                var t = (double[,])m.Clone();
                for (int rr = 0; rr < 3; rr++) t[rr, c] = r[rr];
                x[c] = (t[0, 0] * (t[1, 1] * t[2, 2] - t[1, 2] * t[2, 1]) - t[0, 1] * (t[1, 0] * t[2, 2] - t[1, 2] * t[2, 0]) + t[0, 2] * (t[1, 0] * t[2, 1] - t[1, 1] * t[2, 0])) / det;
            }
            return true;
        }
    }
}
