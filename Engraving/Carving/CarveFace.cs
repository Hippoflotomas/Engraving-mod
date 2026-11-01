using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Engraving.Carving
{
    /// <summary>
    /// The carvable face of one renderer on one side (piece-local +Z or -Z): the triangles facing that way in
    /// the front half of the piece, with flat UVs (0..1 across the whole face as seen from that side). Meshes
    /// showing one or both sides carved are built by <see cref="CarvedMeshes"/>.
    /// </summary>
    internal sealed class CarveFace
    {
        public MeshRenderer Renderer;
        public Mesh Original;
        public int Side;
        /// <summary>Material slot the face triangles came from (the one most of them used).</summary>
        public int SourceSlot;
        /// <summary>Least-squares map from face UV (u, v, 1) to the original UV0: the fallback where no triangle covers a texel.</summary>
        public Vector3 MapU, MapV;
        public int Triangles;
        /// <summary>The face's triangles as vertex indices into the original mesh.</summary>
        public int[] FaceTris;
        /// <summary>The original mesh's data (shared by every wall of the prefab).</summary>
        public MeshSnapshot Snap;
        /// <summary>
        /// Per face triangle: its corners in face UV (6 floats), the same corners' original UVs (6 floats) and its
        /// depth towards the viewer (1 float). Lets the wall's texture be resampled exactly, island by island
        /// (2026-10-29: one affine map smeared and striped stone_wall_4x2, whose texture is a patchwork).
        /// </summary>
        public float[] TriFaceUV, TriOrigUV, TriDepth;
        /// <summary>
        /// Per face triangle: how squarely it faces out (1 = straight at the viewer). Joints and bevels between
        /// stones face partly sideways; their texture is a different part of the atlas, so they only supply the
        /// wall texture where nothing squarer covers (2026-10-31: they streaked carvings on stone_wall_4x2).
        /// </summary>
        public float[] TriFacing;
        /// <summary>Unique per face layout (shared by the copies bound to other walls of the prefab): a cache key.</summary>
        public int LayoutId;
        static int _nextLayout;
        public static int NextLayoutId() => System.Threading.Interlocked.Increment(ref _nextLayout);

        public CarveFace BoundTo(MeshRenderer r)
        {
            var f = (CarveFace)MemberwiseClone();
            f.Renderer = r;
            return f;
        }
    }

    /// <summary>
    /// The meshes a carved renderer shows: the original with the carved sides' triangles taken out of their
    /// submeshes and put in one extra submesh each (front first, then back), on their own vertices with face UVs.
    /// Cached per original mesh and set of sides, so every wall of a prefab shares them.
    /// </summary>
    internal static class CarvedMeshes
    {
        public sealed class Variant
        {
            public Mesh Mesh;
            /// <summary>Submesh index of each carved side's face, by side (+1, -1).</summary>
            public readonly Dictionary<int, int> SlotOf = new Dictionary<int, int>();
        }

        static readonly Dictionary<string, Variant> Cache = new Dictionary<string, Variant>();
        /// <summary>The game's mesh each carved mesh was made from.</summary>
        static readonly Dictionary<Mesh, Mesh> Originals = new Dictionary<Mesh, Mesh>();

        /// <summary>
        /// The game's own mesh behind a renderer's mesh: a piece already showing a carving holds one of ours
        /// (2026-10-31: the back of a carved Grausten wall was measured from the front's carved mesh).
        /// </summary>
        public static Mesh OriginalOf(Mesh m) => m != null && Originals.TryGetValue(m, out var o) ? o : m;

        static readonly Dictionary<Mesh, Variant> ByMesh = new Dictionary<Mesh, Variant>();

        /// <summary>The submesh a carved mesh shows a side's carving in.</summary>
        public static bool SlotFor(Mesh carved, int side, out int slot)
        {
            slot = -1;
            return carved != null && ByMesh.TryGetValue(carved, out var v) && v.SlotOf.TryGetValue(side, out slot);
        }

        public static Variant For(IList<CarveFace> faces)
        {
            var ordered = faces.OrderByDescending(f => f.Side).ToList();
            var first = ordered[0];
            string key = first.Original.GetInstanceID() + "|" + string.Join(",", ordered.Select(f => f.Side + ":" +
                f.LayoutId));
            if (Cache.TryGetValue(key, out var v)) return v;

            var s = first.Snap;
            bool haveNormals = s.Normals != null && s.Normals.Length == s.Vertices.Length;
            bool haveUvs = s.Uvs != null && s.Uvs.Length == s.Vertices.Length;
            var verts = new List<Vector3>(s.Vertices);
            var norms = haveNormals ? new List<Vector3>(s.Normals) : null;
            var uvs = new List<Vector2>(haveUvs ? s.Uvs : new Vector2[s.Vertices.Length]);

            var carved = new HashSet<long>();
            foreach (var f in ordered)
                for (int i = 0; i + 2 < f.FaceTris.Length; i += 3) carved.Add(TriKey(f.FaceTris[i], f.FaceTris[i + 1], f.FaceTris[i + 2]));
            var subs = new List<int[]>();
            foreach (var t in s.SubMeshTriangles)
            {
                var keep = new List<int>(t.Length);
                for (int i = 0; i + 2 < t.Length; i += 3)
                    if (!carved.Contains(TriKey(t[i], t[i + 1], t[i + 2]))) { keep.Add(t[i]); keep.Add(t[i + 1]); keep.Add(t[i + 2]); }
                subs.Add(keep.ToArray());
            }

            v = new Variant();
            foreach (var f in ordered)
            {
                // the face on its own copies of the vertices (three per triangle), carrying face UVs
                var idx = new int[f.FaceTris.Length];
                for (int k = 0; k < f.FaceTris.Length; k++)
                {
                    int i = f.FaceTris[k];
                    idx[k] = verts.Count;
                    verts.Add(s.Vertices[i]);
                    if (haveNormals) norms.Add(s.Normals[i]);
                    uvs.Add(new Vector2(f.TriFaceUV[k * 2], f.TriFaceUV[k * 2 + 1]));
                }
                v.SlotOf[f.Side] = subs.Count;
                subs.Add(idx);
            }

            var m = new Mesh { indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            m.name = first.Original.name + "_carved" + string.Join("", ordered.Select(f => f.Side > 0 ? "F" : "B"));
            m.SetVertices(verts);
            if (haveNormals) m.SetNormals(norms);
            m.SetUVs(0, uvs);
            m.subMeshCount = subs.Count;
            for (int i = 0; i < subs.Count; i++) m.SetTriangles(subs[i], i);
            if (!haveNormals) m.RecalculateNormals();
            m.RecalculateBounds();
            m.RecalculateTangents();
            m.UploadMeshData(true);
            v.Mesh = m;
            Originals[m] = first.Original;
            ByMesh[m] = v;
            Cache[key] = v;
            return v;
        }

        static long TriKey(int a, int b, int c)
        {
            // order-independent key for a triangle
            int lo = Math.Min(a, Math.Min(b, c)), hi = Math.Max(a, Math.Max(b, c)), mid = a + b + c - lo - hi;
            return ((long)lo * 1000003L + mid) * 1000003L + hi;
        }
    }

    /// <summary>All carvable faces of a piece on one side, sharing one face rectangle.</summary>
    internal sealed class CarveModel
    {
        public int Side;
        /// <summary>Face rectangle in piece-local X/Y (metres).</summary>
        public Rect Bounds;
        public readonly List<CarveFace> Faces = new List<CarveFace>();
        public string Note;
        /// <summary>Where the carving goes on a decorated door (its flat panel), or null for the whole face.</summary>
        public Engraving.Core.Render.CarveArea Area;

        public float Width => Bounds.width;
        public float Height => Bounds.height;

        /// <summary>A face must point within this of the side axis (cos of the angle; 0.5 = 60°).</summary>
        const float MinFacing = 0.5f;

        static readonly Dictionary<string, CarveModel> Cache = new Dictionary<string, CarveModel>();

        /// <summary>The renderers that show the piece up close: LOD0 if there is a LOD group, else every mesh renderer.</summary>
        static List<MeshRenderer> CloseRenderers(GameObject root)
        {
            var all = root.GetComponentsInChildren<MeshRenderer>(true)
                .Where(r => r.GetComponent<MeshFilter>()?.sharedMesh != null && !IsOverlay(r)).ToList();
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

        /// <summary>Snow and similar layers drawn over the piece: never carved.</summary>
        static bool IsOverlay(MeshRenderer r) =>
            r.sharedMaterials.Any(m => m != null && m.shader != null && m.shader.name.IndexOf("Snow", StringComparison.OrdinalIgnoreCase) >= 0);

        /// <summary>Builds (or reuses) the carve model for this piece and side. Null if no flat face was found.</summary>
        public static CarveModel For(GameObject root, string prefabName, int side)
        {
            string key = prefabName + "|" + side;
            if (Cache.TryGetValue(key, out var cached)) return cached?.Bind(root);

            CarveModel model = null;
            try { model = Build(ShapeSource(root, prefabName), side); }
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
            var bound = new CarveModel { Side = Side, Bounds = Bounds, Note = Note, Area = Area };
            var byPath = root.GetComponentsInChildren<MeshRenderer>(true).ToDictionary(r => PathOf(r.transform, root.transform), r => r);
            for (int i = 0; i < Faces.Count; i++)
                if (byPath.TryGetValue(_paths[i], out var r)) bound.Faces.Add(Faces[i].BoundTo(r));
            return bound.Faces.Count > 0 ? bound : null;
        }

        /// <summary>
        /// Where to measure the face. A door is measured on its prefab, so it's always measured shut (Hippo, 2026-11-01:
        /// a door measured while open carved its frame and leaf as one, split apart when it swung).
        /// </summary>
        static GameObject ShapeSource(GameObject root, string prefabName)
        {
            if (root.GetComponent<Door>() == null) return root;
            var prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefabName) : null;
            return prefab != null ? prefab : root;
        }

        /// <summary>A door's leaf: the renderers under its swinging "door" child (the frame stays put and isn't carved).</summary>
        static List<MeshRenderer> DoorLeaf(GameObject root, List<MeshRenderer> renderers)
        {
            if (root.GetComponent<Door>() == null) return renderers;
            var leaf = renderers.Where(r =>
            {
                for (var t = r.transform; t != null && t != root.transform; t = t.parent)
                    if (t.name.Equals("door", StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            }).ToList();
            return leaf.Count > 0 ? leaf : renderers;
        }

        static CarveModel Build(GameObject root, int side)
        {
            var rootInv = root.transform.worldToLocalMatrix;
            bool door = root.GetComponent<Door>() != null;
            var renderers = DoorLeaf(root, CloseRenderers(root));
            var snaps = new List<(MeshRenderer r, MeshSnapshot s, Matrix4x4 toRoot)>();
            float outer = float.NegativeInfinity, inner = float.PositiveInfinity;
            foreach (var r in renderers)
            {
                var mesh = CarvedMeshes.OriginalOf(r.GetComponent<MeshFilter>().sharedMesh);
                MeshSnapshot snap;
                try { snap = MeshReader.Read(mesh); }
                catch (Exception e) { Jotunn.Logger.LogWarning($"[Engraving] Mesh '{mesh.name}' unreadable: {e.Message}"); continue; }
                var toRoot = rootInv * r.transform.localToWorldMatrix;
                snaps.Add((r, snap, toRoot));
                foreach (var v in snap.Vertices)
                {
                    float z = toRoot.MultiplyPoint3x4(v).z * side;
                    outer = Mathf.Max(outer, z);
                    inner = Mathf.Min(inner, z);
                }
            }
            if (snaps.Count == 0) return null;
            // Everything facing this way in the front half of the piece is carved: planks and stones sit at
            // different depths (2026-10-29: a 6 cm band caught only half of woodwall's planks)
            float middle = (outer + inner) * 0.5f;

            // a door leaf is carved on its main panel only: the depth with the most area, so a rim, straps or a
            // handle standing proud of it keep their own look and the carving is centred on the panel
            float? panel = door ? MainPanelDepth(snaps, side, middle) : (float?)null;
            const float PanelBand = 0.035f;

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
                        if (z < middle) continue;
                        if (panel.HasValue && Mathf.Abs(z - panel.Value) > PanelBand) continue;
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
            if (door) model.Area = model.PanelArea(picked.Select(p => (p.s, p.toRoot, p.tris)).ToList());
            model.Note = $"{model.Faces.Count} renderer(s), {totalTris} face triangles, face {model.Width:0.00} x {model.Height:0.00} m" +
                         (model.Area != null ? $", carved within {model.Area.Box} (no borders)" : "") +
                         (panel.HasValue ? $", door leaf panel at {panel.Value:0.00} m" : "") +
                         (picked.Any(p => p.s.FromGpu) ? ", mesh read from the GPU" : "");
            return model;
        }

        /// <summary>
        /// A decorated door's carving area: the largest upright rectangle on its flat panel. Straps and bars across the
        /// panel are bridged (closing the panel's outline by ~12 cm), a rim round it isn't. Null when the panel is
        /// nearly the whole face (a plain plank door), which then carves like a wall, borders and all.
        /// </summary>
        Engraving.Core.Render.CarveArea PanelArea(List<(MeshSnapshot s, Matrix4x4 toRoot, List<int> tris)> picked)
        {
            const float Cell = 0.025f;
            int gw = Mathf.Clamp(Mathf.CeilToInt(Width / Cell), 4, 400), gh = Mathf.Clamp(Mathf.CeilToInt(Height / Cell), 4, 400);
            float cw = Width / gw, ch = Height / gh;
            var on = new bool[gw * gh];
            foreach (var (s, toRoot, tris) in picked)
                for (int i = 0; i + 2 < tris.Count; i += 3)
                {
                    var a = Project(toRoot.MultiplyPoint3x4(s.Vertices[tris[i]]));
                    var b = Project(toRoot.MultiplyPoint3x4(s.Vertices[tris[i + 1]]));
                    var c = Project(toRoot.MultiplyPoint3x4(s.Vertices[tris[i + 2]]));
                    a = new Vector2(a.x * Width, a.y * Height); b = new Vector2(b.x * Width, b.y * Height); c = new Vector2(c.x * Width, c.y * Height);
                    int ix0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)) / cw)), ix1 = Mathf.Min(gw - 1, Mathf.FloorToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x)) / cw));
                    int iy0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y)) / ch)), iy1 = Mathf.Min(gh - 1, Mathf.FloorToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y)) / ch));
                    float area = (b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y);
                    if (Mathf.Abs(area) < 1e-8f) continue;
                    for (int y = iy0; y <= iy1; y++)
                        for (int x = ix0; x <= ix1; x++)
                        {
                            var p = new Vector2((x + 0.5f) * cw, (y + 0.5f) * ch);
                            float w0 = ((b.x - p.x) * (c.y - p.y) - (c.x - p.x) * (b.y - p.y)) / area;
                            float w1 = ((c.x - p.x) * (a.y - p.y) - (a.x - p.x) * (c.y - p.y)) / area;
                            if (w0 >= -0.01f && w1 >= -0.01f && w0 + w1 <= 1.01f) on[y * gw + x] = true;
                        }
                }
            // bridge straps and bars: dilate then erode
            int r = Mathf.Max(1, Mathf.RoundToInt(0.12f / Cell));
            on = Erode(Dilate(on, gw, gh, r), gw, gh, r);

            // the largest rectangle of panel (row by row, the largest rectangle under a histogram)
            var hist = new int[gw];
            int bestArea = 0, bx0 = 0, by0 = 0, bx1 = 0, by1 = 0;
            for (int y = 0; y < gh; y++)
            {
                for (int x = 0; x < gw; x++) hist[x] = on[y * gw + x] ? hist[x] + 1 : 0;
                var stack = new Stack<int>();
                for (int x = 0; x <= gw; x++)
                {
                    int hgt = x < gw ? hist[x] : 0;
                    while (stack.Count > 0 && hist[stack.Peek()] >= hgt)
                    {
                        int top = stack.Pop();
                        int left = stack.Count > 0 ? stack.Peek() + 1 : 0;
                        int a = hist[top] * (x - left);
                        if (a > bestArea) { bestArea = a; bx0 = left; bx1 = x; by1 = y + 1; by0 = y + 1 - hist[top]; }
                    }
                    stack.Push(x);
                }
            }
            if (bestArea == 0 || bestArea > 0.92f * gw * gh) return null;
            // a little clear of the panel's edges
            float m = 0.04f * Mathf.Min((bx1 - bx0) * cw, (by1 - by0) * ch);
            return new Engraving.Core.Render.CarveArea
            {
                Box = new Engraving.Core.Render.Box(bx0 * cw + m, by0 * ch + m, bx1 * cw - m, by1 * ch - m),
                NoBorders = true,
            };
        }

        static bool[] Dilate(bool[] g, int w, int h, int r) => Morph(g, w, h, r, true);
        static bool[] Erode(bool[] g, int w, int h, int r) => Morph(g, w, h, r, false);

        /// <summary>Square-window dilation or erosion (separable: rows, then columns). Off the grid never adds or removes.</summary>
        static bool[] Morph(bool[] g, int w, int h, int r, bool dilate)
        {
            var rows = new bool[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool v = !dilate;
                    for (int k = -r; k <= r && v == !dilate; k++)
                    {
                        int xx = x + k;
                        bool c = xx >= 0 && xx < w ? g[y * w + xx] : !dilate;
                        if (dilate ? c : !c) v = dilate;
                    }
                    rows[y * w + x] = v;
                }
            var outp = new bool[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool v = !dilate;
                    for (int k = -r; k <= r && v == !dilate; k++)
                    {
                        int yy = y + k;
                        bool c = yy >= 0 && yy < h ? rows[yy * w + x] : !dilate;
                        if (dilate ? c : !c) v = dilate;
                    }
                    outp[y * w + x] = v;
                }
            return outp;
        }

        /// <summary>The depth (2 cm steps) holding the most face area among front-facing triangles in the front half.</summary>
        static float? MainPanelDepth(List<(MeshRenderer r, MeshSnapshot s, Matrix4x4 toRoot)> snaps, int side, float middle)
        {
            var area = new Dictionary<int, float>();
            foreach (var (r, s, toRoot) in snaps)
                foreach (var t in s.SubMeshTriangles)
                    for (int i = 0; i + 2 < t.Length; i += 3)
                    {
                        Vector3 a = toRoot.MultiplyPoint3x4(s.Vertices[t[i]]);
                        Vector3 b = toRoot.MultiplyPoint3x4(s.Vertices[t[i + 1]]);
                        Vector3 c = toRoot.MultiplyPoint3x4(s.Vertices[t[i + 2]]);
                        var n = Vector3.Cross(b - a, c - a);
                        float twice = n.magnitude;
                        if (twice < 1e-9f || n.z / twice * side < MinFacing) continue;
                        float z = (a.z + b.z + c.z) / 3f * side;
                        if (z < middle) continue;
                        int bin = Mathf.RoundToInt(z / 0.02f);
                        // the face-on area: what the eye sees of it
                        area[bin] = (area.TryGetValue(bin, out var v) ? v : 0) + Mathf.Abs(n.z) * 0.5f;
                    }
            if (area.Count == 0) return null;
            int best = area.OrderByDescending(k => k.Value).First().Key;
            // a panel split across two neighbouring bins sits between them
            float w0 = area[best], wm = area.TryGetValue(best - 1, out var m) ? m : 0, wp = area.TryGetValue(best + 1, out var q) ? q : 0;
            return (best + (wp - wm) / (w0 + wm + wp)) * 0.02f;
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
            bool haveUvs = s.Uvs != null && s.Uvs.Length == s.Vertices.Length;
            int nt = faceTris.Count / 3;
            var triFace = new float[nt * 6];
            var triOrig = new float[nt * 6];
            var triDepth = new float[nt];
            var triFacing = new float[nt];
            var fit = new List<(Vector2 face, Vector2 orig)>();
            for (int t = 0; t < nt; t++)
            {
                float z = 0;
                for (int k = 0; k < 3; k++)
                {
                    int i = faceTris[t * 3 + k];
                    var p = toRoot.MultiplyPoint3x4(s.Vertices[i]);
                    var fuv = model.Project(p);
                    triFace[t * 6 + k * 2] = fuv.x; triFace[t * 6 + k * 2 + 1] = fuv.y;
                    var o = haveUvs ? s.Uvs[i] : fuv;
                    triOrig[t * 6 + k * 2] = o.x; triOrig[t * 6 + k * 2 + 1] = o.y;
                    if (haveUvs) fit.Add((fuv, o));
                    z += p.z * model.Side;
                }
                triDepth[t] = z / 3f;
                Vector3 a = toRoot.MultiplyPoint3x4(s.Vertices[faceTris[t * 3]]);
                Vector3 b = toRoot.MultiplyPoint3x4(s.Vertices[faceTris[t * 3 + 1]]);
                Vector3 c = toRoot.MultiplyPoint3x4(s.Vertices[faceTris[t * 3 + 2]]);
                triFacing[t] = Vector3.Cross(b - a, c - a).normalized.z * model.Side;
            }

            var face = new CarveFace
            {
                Renderer = r, Original = CarvedMeshes.OriginalOf(r.GetComponent<MeshFilter>().sharedMesh), Side = model.Side, SourceSlot = sourceSlot,
                Triangles = nt, FaceTris = faceTris.ToArray(), Snap = s,
                TriFaceUV = triFace, TriOrigUV = triOrig, TriDepth = triDepth, TriFacing = triFacing, LayoutId = CarveFace.NextLayoutId(),
            };
            FitUvMap(fit, out face.MapU, out face.MapV);
            return face;
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
