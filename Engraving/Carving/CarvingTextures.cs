using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Engraving.Core.Charges;
using Engraving.Core.Language;
using Engraving.Core.Render;
using UnityEngine;
using UnityEngine.Rendering;

using Engraving.Pictures;

namespace Engraving.Carving
{
    /// <summary>A rendered carving: the heightmap (row 0 at the top) and what the script said about itself.</summary>
    internal sealed class Carving
    {
        public string Key;
        public HeightMap Map;
        public float PixelsPerMetre;
        public List<Diagnostic> Diagnostics = new List<Diagnostic>();
        public long Millis;
    }

    /// <summary>
    /// One background thread for everything heavy: drawing carvings (the engine is pure C#) and building
    /// texture pixels. Results come back on the main thread through <see cref="Pump"/>, which also uploads
    /// finished textures a few at a time so no single frame takes the whole cost (2026-10-29: carving a
    /// woodwall stuttered while 32 textures were built in one frame).
    /// </summary>
    internal static class CarvingJobs
    {
        static readonly object Gate = new object();
        static readonly Queue<Action> Pending = new Queue<Action>();
        static readonly Queue<Action> Done = new Queue<Action>();
        static readonly Queue<Action> Uploads = new Queue<Action>();
        static readonly Dictionary<string, List<Action<Carving>>> InFlight = new Dictionary<string, List<Action<Carving>>>();
        static readonly Dictionary<string, Carving> Finished = new Dictionary<string, Carving>();
        static readonly LinkedList<string> FinishedOrder = new LinkedList<string>();
        const int KeepFinished = 48;
        /// <summary>Main-thread time per frame for texture uploads (ms). At least one upload always runs.</summary>
        const double UploadBudgetMs = 3.0;
        static readonly AutoResetEvent Wake = new AutoResetEvent(false);
        static Thread _thread;
        static ChargeLibrary _library;

        /// <summary>Charge library used by the worker. Only touched under the lock or on the worker.</summary>
        public static void SetLibrary(ChargeLibrary lib)
        {
            lock (Gate)
            {
                _library = lib;
                Finished.Clear();
                FinishedOrder.Clear();
            }
        }

        public static string KeyFor(string script, float w, float h, float ppm) =>
            $"{w:0.###}x{h:0.###}@{ppm:0}|{script}";

        /// <summary>Runs <paramref name="work"/> on the background thread, then <paramref name="then"/> on the main thread.</summary>
        public static void Run(Action work, Action then)
        {
            lock (Gate)
            {
                Pending.Enqueue(() =>
                {
                    try { work(); }
                    catch (Exception e) { Jotunn.Logger.LogWarning("[Engraving] background job failed: " + e); }
                    lock (Gate) Done.Enqueue(then);
                });
            }
            EnsureThread();
            Wake.Set();
        }

        /// <summary>Queues main-thread work that touches the GPU (texture uploads); spread over frames.</summary>
        public static void Upload(Action upload)
        {
            lock (Gate) Uploads.Enqueue(upload);
        }

        /// <summary>Asks for a carving; the callback runs on the main thread (immediately if it's cached).</summary>
        public static void Request(string script, float w, float h, float ppm, Action<Carving> done) =>
            Request(script, w, h, ppm, null, done);

        /// <summary>As above; <paramref name="pictures"/> maps picture charge names to fingerprints.</summary>
        public static void Request(string script, float w, float h, float ppm, IDictionary<string, string> pictures, Action<Carving> done)
        {
            string key = KeyFor(script, w, h, ppm) + "|" + EngravingCharges.KeyOf(pictures);
            Carving cached;
            lock (Gate)
            {
                if (!Finished.TryGetValue(key, out cached))
                {
                    if (InFlight.TryGetValue(key, out var waiting)) { waiting.Add(done); return; }
                    InFlight[key] = new List<Action<Carving>> { done };
                }
            }
            if (cached != null) { done(cached); return; }

            var carving = new Carving { Key = key, PixelsPerMetre = ppm };
            Run(() =>
            {
                ChargeLibrary lib;
                lock (Gate) lib = _library;
                try
                {
                    var sw = Stopwatch.StartNew();
                    var parsed = Parser.Parse(script);
                    carving.Diagnostics.AddRange(parsed.Diagnostics);
                    EngravingCharges.Apply(parsed.Spec, pictures);
                    var opt = new RenderOptions { Width = w, Height = h, PixelsPerMetre = ppm };
                    RenderResult result;
                    lock (lib ?? (object)Gate) result = EngravingRenderer.Render(parsed.Spec, lib, opt);
                    carving.Diagnostics.AddRange(result.Diagnostics);
                    carving.Map = result.Map;
                    carving.Millis = sw.ElapsedMilliseconds;
                }
                catch (Exception e)
                {
                    carving.Diagnostics.Add(new Diagnostic(Severity.Error, 0, "the carving couldn't be drawn: " + e.Message));
                }
            }, () =>
            {
                List<Action<Carving>> callbacks;
                lock (Gate)
                {
                    InFlight.TryGetValue(key, out callbacks);
                    InFlight.Remove(key);
                    if (carving.Map != null)
                    {
                        Finished[key] = carving;
                        FinishedOrder.AddLast(key);
                        while (FinishedOrder.Count > KeepFinished)
                        {
                            Finished.Remove(FinishedOrder.First.Value);
                            FinishedOrder.RemoveFirst();
                        }
                    }
                }
                foreach (var cb in callbacks ?? new List<Action<Carving>>())
                {
                    try { cb(carving); }
                    catch (Exception e) { Jotunn.Logger.LogWarning("[Engraving] " + e); }
                }
            });
        }

        static void EnsureThread()
        {
            if (_thread != null && _thread.IsAlive) return;
            _thread = new Thread(Work) { IsBackground = true, Name = "Engraving renderer", Priority = System.Threading.ThreadPriority.BelowNormal };
            _thread.Start();
        }

        static void Work()
        {
            while (true)
            {
                Action job = null;
                lock (Gate) if (Pending.Count > 0) job = Pending.Dequeue();
                if (job == null) { Wake.WaitOne(1000); continue; }
                job();
            }
        }

        /// <summary>Main thread, every frame: finished background work, then texture uploads within the budget.</summary>
        public static void Pump()
        {
            while (true)
            {
                Action then;
                lock (Gate)
                {
                    if (Done.Count == 0) break;
                    then = Done.Dequeue();
                }
                try { then(); }
                catch (Exception e) { Jotunn.Logger.LogWarning("[Engraving] " + e); }
            }

            var sw = Stopwatch.StartNew();
            while (true)
            {
                Action up;
                lock (Gate)
                {
                    if (Uploads.Count == 0) return;
                    up = Uploads.Dequeue();
                }
                try { up(); }
                catch (Exception e) { Jotunn.Logger.LogWarning("[Engraving] texture upload failed: " + e); }
                if (sw.Elapsed.TotalMilliseconds >= UploadBudgetMs) return;
            }
        }
    }

    /// <summary>
    /// The textures a carving needs on a wall. Pixels are computed on the background thread; only the upload
    /// happens on the main thread.
    /// <list type="bullet">
    /// <item>Normal map: one per carving and depth, shared by every renderer and wall showing it
    /// (the maths of braid_gui.py's make_normal).</item>
    /// <item>Albedo: the wall's own texture resampled onto the face and darkened in the cuts, so the carving
    /// reads at a distance. One per carving, source texture and UV layout; identical walls share it.</item>
    /// </list>
    /// </summary>
    internal static class CarvingTextures
    {
        sealed class Entry
        {
            public Texture2D Texture;
            public int Users;
            public readonly List<Action<Texture2D>> Waiting = new List<Action<Texture2D>>();
        }

        static readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>();

        // CPU copies of the game's textures (read back from the GPU once each)
        static readonly Dictionary<Texture, (Color32[] px, int w, int h)> SourcePixels = new Dictionary<Texture, (Color32[], int, int)>();
        static readonly Dictionary<Texture, List<Action>> SourceWaiting = new Dictionary<Texture, List<Action>>();

        public static string NormalKey(Carving c, float depth) => "n|" + c.Key + "|" + depth;

        public static string AlbedoKey(Carving c, Material source, CarveFace face)
        {
            var tex = MainTex(source);
            Vector2 sc = tex != null ? source.mainTextureScale : Vector2.one, of = tex != null ? source.mainTextureOffset : Vector2.zero;
            // the face layout: walls of the same prefab share the same triangle arrays
            int layout = face.LayoutId;
            return "a|" + c.Key + "|" + (tex != null ? tex.GetInstanceID() : 0) + "|" + sc + of + "|" + layout + "|" + EngravingPlugin.AlbedoMaxSize.Value;
        }

        static Texture MainTex(Material m) => m != null && m.HasProperty("_MainTex") ? m.mainTexture : null;

        /// <summary>Takes a share of the carving's normal map; <paramref name="ready"/> runs once it exists. Returns the key to release.</summary>
        public static string AcquireNormal(Carving c, float depth, Action<Texture2D> ready)
        {
            string key = NormalKey(c, depth);
            Acquire(key, ready, e =>
            {
                Color32[] px = null;
                CarvingJobs.Run(() => px = NormalPixels(c, depth), () =>
                    CarvingJobs.Upload(() => Finish(key, MakeTexture(px, c.Map.Width, c.Map.Height, true, "Engraving_Normal"))));
            });
            return key;
        }

        static Texture BumpTex(Material m) => m != null && m.HasProperty("_BumpMap") ? m.GetTexture("_BumpMap") : null;

        /// <summary>
        /// The normal map for this face: the carving's relief, with the wall's own normal map (stone bumps, wood
        /// grain) resampled through the same per-triangle lookup as the colour and blended in, so carved stone stays
        /// rough (2026-10-31: replacing the wall's normal map made carved stone look like smooth plaster). Walls
        /// without a normal map, or with KeepWallBumps off, share the plain per-carving normal map.
        /// </summary>
        public static string AcquireSurfaceNormal(Carving c, float depth, Material source, CarveFace face, Action<Texture2D> ready)
        {
            var bump = EngravingPlugin.KeepWallBumps.Value ? BumpTex(source) : null;
            if (bump == null || face.TriFaceUV == null) return AcquireNormal(c, depth, ready);
            var main = MainTex(source);
            Vector2 sc = source.mainTextureScale, of = source.mainTextureOffset;   // the piece shader samples both with _MainTex's tiling
            float bumpScale = source.HasProperty("_BumpScale") ? source.GetFloat("_BumpScale") : 1f;
            bool point = bump.filterMode == FilterMode.Point;
            string key = "nd|" + AlbedoKey(c, source, face) + "|" + bump.GetInstanceID() + "|" + depth + "|" + bumpScale;
            Acquire(key, ready, e =>
            {
                void Build()
                {
                    var b = SourcePixels.TryGetValue(bump, out var bp) ? bp : (null, 0, 0);
                    var m = main != null && SourcePixels.TryGetValue(main, out var mp) ? mp : (null, 0, 0);
                    int maxSize = EngravingPlugin.AlbedoMaxSize.Value;
                    Color32[] px = null;
                    int aw = 0, ah = 0;
                    CarvingJobs.Run(() =>
                    {
                        // same size as the colour texture, so both follow the wall's texture detail
                        AlbedoSize(c, face, m.Item2 > 0 ? m.Item2 : b.Item2, m.Item3 > 0 ? m.Item3 : b.Item3, sc, maxSize, out aw, out ah);
                        px = b.Item1 != null ? DetailNormalPixels(c, depth, aw, ah, b.Item1, b.Item2, b.Item3, bumpScale, face, sc, of, point)
                                             : ResampledNormal(c, depth, aw, ah);
                    }, () => CarvingJobs.Upload(() => Finish(key, MakeTexture(px, aw, ah, true, "Engraving_Normal"))));
                }
                // the colour texture's size depends on the main texture too
                ReadSource(bump, true, () => { if (main != null) ReadSource(main, Build); else Build(); });
            });
            return key;
        }

        /// <summary>Takes a share of the albedo for this face; <paramref name="ready"/> runs once it exists. Returns the key to release.</summary>
        public static string AcquireAlbedo(Carving c, Material source, CarveFace face, Action<Texture2D> ready)
        {
            string key = AlbedoKey(c, source, face);
            Acquire(key, ready, e =>
            {
                var tex = MainTex(source);
                Vector2 sc = tex != null ? source.mainTextureScale : Vector2.one, of = tex != null ? source.mainTextureOffset : Vector2.zero;
                // Valheim draws most walls with blocky (point-filtered) textures: blending their texels made the
                // carved part look smeared next to the rest of the wall (2026-10-31, Grausten: 128² texture)
                bool point = tex != null && tex.filterMode == FilterMode.Point;
                void Build()
                {
                    var src = tex != null && SourcePixels.TryGetValue(tex, out var sp) ? sp : (null, 0, 0);
                    int maxSize = EngravingPlugin.AlbedoMaxSize.Value;
                    Color32[] px = null;
                    int aw = 0, ah = 0;
                    CarvingJobs.Run(() =>
                    {
                        AlbedoSize(c, face, src.Item2, src.Item3, sc, maxSize, out aw, out ah);
                        px = AlbedoPixels(c, aw, ah, src.Item1, src.Item2, src.Item3, face, sc, of, point);
                    }, () => CarvingJobs.Upload(() => Finish(key, MakeTexture(px, aw, ah, false, "Engraving_Albedo"))));
                }
                if (tex == null) Build();
                else ReadSource(tex, Build);
            });
            return key;
        }

        public static void Release(string key)
        {
            if (key == null || !Entries.TryGetValue(key, out var e)) return;
            if (--e.Users > 0) return;
            Entries.Remove(key);
            if (e.Texture != null) UnityEngine.Object.Destroy(e.Texture);
            // still being built: Finish finds no entry and destroys the texture
        }

        static void Acquire(string key, Action<Texture2D> ready, Action<Entry> start)
        {
            if (Entries.TryGetValue(key, out var e))
            {
                e.Users++;
                if (e.Texture != null) ready(e.Texture);
                else e.Waiting.Add(ready);
                return;
            }
            e = new Entry { Users = 1 };
            e.Waiting.Add(ready);
            Entries[key] = e;
            start(e);
        }

        static void Finish(string key, Texture2D tex)
        {
            if (!Entries.TryGetValue(key, out var e)) { UnityEngine.Object.Destroy(tex); return; } // nobody wants it any more
            e.Texture = tex;
            foreach (var w in e.Waiting)
            {
                try { w(tex); }
                catch (Exception ex) { Jotunn.Logger.LogWarning("[Engraving] " + ex); }
            }
            e.Waiting.Clear();
        }

        static Texture2D MakeTexture(Color32[] px, int w, int h, bool linear, string name)
        {
            // linear for the normal map: it holds directions, not colours
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true, linear) { name = name, wrapMode = TextureWrapMode.Clamp, anisoLevel = 4 };
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        // ------------------------------------------------------------------ pixels (background thread)

        /// <summary>
        /// Unity / OpenGL-style tangent-space normals (Y up). Texture rows run upward while the heightmap's
        /// run downward, so rows are flipped on the way in.
        /// </summary>
        static Color32[] NormalPixels(Carving c, float depthMetres)
        {
            var m = c.Map;
            int w = m.Width, h = m.Height;
            // height units -> metres, and the central difference spans two pixels
            float strength = depthMetres * c.PixelsPerMetre * 0.5f;
            var px = new Color32[w * h];
            for (int ty = 0; ty < h; ty++)
            {
                int y = h - 1 - ty;                          // heightmap row
                int yUp = Math.Max(y - 1, 0), yDown = Math.Min(y + 1, h - 1);
                for (int x = 0; x < w; x++)
                {
                    float dx = (m.H[y * w + Math.Min(x + 1, w - 1)] - m.H[y * w + Math.Max(x - 1, 0)]) * strength;
                    float dy = (m.H[yUp * w + x] - m.H[yDown * w + x]) * strength;   // towards +v (up)
                    float nx = -dx, ny = -dy, nz = 1f;
                    float inv = 1f / (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
                    px[ty * w + x] = new Color32(ToByte(nx * inv), ToByte(ny * inv), ToByte(nz * inv), 255);
                }
            }
            return px;
        }

        /// <summary>The carving's normal at face UV (u, v up), from the heightmap; strength per face UV unit.</summary>
        static void CarvingNormal(HeightMap m, float u, float v, float du, float dv, float su, float sv, out float nx, out float ny, out float nz)
        {
            float dx = (HeightAt(m, u + du, v) - HeightAt(m, u - du, v)) * su;
            float dy = (HeightAt(m, u, v + dv) - HeightAt(m, u, v - dv)) * sv;
            nx = -dx; ny = -dy; nz = 1f;
            float inv = 1f / (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
            nx *= inv; ny *= inv; nz *= inv;
        }

        /// <summary>The plain carving normal map at another size (a wall with a normal map whose pixels didn't read back).</summary>
        static Color32[] ResampledNormal(Carving c, float depth, int aw, int ah)
        {
            var m = c.Map;
            float wm = m.Width / c.PixelsPerMetre, hm = m.Height / c.PixelsPerMetre;
            float du = 1f / aw, dv = 1f / ah;
            // height units -> metres over the two-texel span
            float su = depth / (2 * du * wm), sv = depth / (2 * dv * hm);
            var px = new Color32[aw * ah];
            for (int ty = 0; ty < ah; ty++)
                for (int x = 0; x < aw; x++)
                {
                    CarvingNormal(m, (x + 0.5f) / aw, (ty + 0.5f) / ah, du, dv, su, sv, out float nx, out float ny, out float nz);
                    px[ty * aw + x] = new Color32(ToByte(nx), ToByte(ny), ToByte(nz), 255);
                }
            return px;
        }

        /// <summary>
        /// The carving's normals with the wall's own normal map blended in (whiteout blend). The wall's normals are
        /// in its own UV layout's tangent space, so each is turned into the face's: per triangle, the directions of
        /// increasing original u and v are worked out on the face (in metres) and the normal's x/y re-expressed along
        /// the face's u and v. Texels between stones (no triangle) get the carving alone.
        /// </summary>
        static Color32[] DetailNormalPixels(Carving c, float depth, int aw, int ah, Color32[] bump, int bw, int bh, float bumpScale,
                                            CarveFace face, Vector2 scale, Vector2 offset, bool point)
        {
            var m = c.Map;
            float wm = m.Width / c.PixelsPerMetre, hm = m.Height / c.PixelsPerMetre;
            float du = 1f / aw, dv = 1f / ah;
            float su = depth / (2 * du * wm), sv = depth / (2 * dv * hm);
            var look = Lookup_(face, aw, ah);

            // per triangle: original tangent (+u) and bitangent (+v) directions on the face, in metres
            int nt = face.TriDepth.Length;
            var tan = new float[nt * 4];
            for (int t = 0; t < nt; t++)
            {
                int b = t * 6;
                var f = face.TriFaceUV; var o = face.TriOrigUV;
                // face edges in metres, original UV edges
                float fx1 = (f[b + 2] - f[b]) * wm, fy1 = (f[b + 3] - f[b + 1]) * hm, fx2 = (f[b + 4] - f[b]) * wm, fy2 = (f[b + 5] - f[b + 1]) * hm;
                float ou1 = (o[b + 2] - o[b]) * scale.x, ov1 = (o[b + 3] - o[b + 1]) * scale.y, ou2 = (o[b + 4] - o[b]) * scale.x, ov2 = (o[b + 5] - o[b + 1]) * scale.y;
                // K maps face metres -> original UV: [ou;ov] = K [fx;fy]; the tangents are K's inverse's columns
                float det = fx1 * fy2 - fx2 * fy1;
                if (Math.Abs(det) < 1e-12f) { tan[t * 4] = 1; tan[t * 4 + 3] = 1; continue; }
                // K = Jo * inv(Jf), Jf = [fx1 fx2; fy1 fy2], Jo = [ou1 ou2; ov1 ov2]; inv(K) = Jf * inv(Jo)
                float detO = ou1 * ov2 - ou2 * ov1;
                if (Math.Abs(detO) < 1e-12f) { tan[t * 4] = 1; tan[t * 4 + 3] = 1; continue; }
                float i00 = ov2 / detO, i01 = -ou2 / detO, i10 = -ov1 / detO, i11 = ou1 / detO;   // inv(Jo)
                float tx = fx1 * i00 + fx2 * i10, ty = fy1 * i00 + fy2 * i10;   // column 0 of Jf*inv(Jo): d(face)/d(orig u)
                float bx = fx1 * i01 + fx2 * i11, by = fy1 * i01 + fy2 * i11;   // column 1: d(face)/d(orig v)
                float tl = (float)Math.Sqrt(tx * tx + ty * ty), bl = (float)Math.Sqrt(bx * bx + by * by);
                tan[t * 4] = tl > 0 ? tx / tl : 1; tan[t * 4 + 1] = tl > 0 ? ty / tl : 0;
                tan[t * 4 + 2] = bl > 0 ? bx / bl : 0; tan[t * 4 + 3] = bl > 0 ? by / bl : 1;
            }

            var px = new Color32[aw * ah];
            for (int ty = 0; ty < ah; ty++)
            {
                float v = (ty + 0.5f) / ah;
                for (int x = 0; x < aw; x++)
                {
                    float u = (x + 0.5f) / aw;
                    int i = ty * aw + x;
                    CarvingNormal(m, u, v, du, dv, su, sv, out float cx, out float cy, out float cz);
                    float nx = cx, ny = cy, nz = cz;
                    int t = look.Tri[i];
                    if (t >= 0)
                    {
                        // the wall's normal: DXT5nm/BC5-safe unpack (x = r * a, y = g), then the bump scale
                        var p = Sample(bump, bw, bh, look.U[i] * scale.x + offset.x, look.V[i] * scale.y + offset.y, point);
                        float wx = (p.r / 255f * p.a / 255f * 2 - 1) * bumpScale, wy = (p.g / 255f * 2 - 1) * bumpScale;
                        float wz = (float)Math.Sqrt(Math.Max(0, 1 - Math.Min(1, wx * wx + wy * wy)));
                        // into the face's tangent space
                        float dx = wx * tan[t * 4] + wy * tan[t * 4 + 2], dy = wx * tan[t * 4 + 1] + wy * tan[t * 4 + 3];
                        // whiteout blend: slopes add, the flat parts multiply
                        nx = cx + dx; ny = cy + dy; nz = cz * wz;
                        float inv = 1f / (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
                        nx *= inv; ny *= inv; nz *= inv;
                    }
                    px[i] = new Color32(ToByte(nx), ToByte(ny), ToByte(nz), 255);
                }
            }
            return px;
        }

        static byte ToByte(float n) => (byte)Math.Max(0, Math.Min(255, (int)Math.Round((n * 0.5f + 0.5f) * 255)));

        /// <summary>
        /// The albedo's size: as sharp as the wall's own texture is on this face (so the carving doesn't look
        /// softer than the wall next to it), at least the heightmap's size, at most <paramref name="maxSize"/>.
        /// </summary>
        static void AlbedoSize(Carving c, CarveFace face, int sw, int sh, Vector2 scale, int maxSize, out int aw, out int ah)
        {
            int mw = c.Map.Width, mh = c.Map.Height;
            float k = 1f;
            if (sw > 0 && face.TriFaceUV != null)
            {
                // texels of the source per unit of face UV area, from the triangles' areas in both spaces
                double faceArea = 0, srcArea = 0;
                for (int t = 0; t < face.TriDepth.Length; t++)
                {
                    faceArea += Math.Abs(Area(face.TriFaceUV, t));
                    srcArea += Math.Abs(Area(face.TriOrigUV, t)) * sw * sh * Math.Abs(scale.x * scale.y);
                }
                if (faceArea > 1e-9)
                {
                    // texels per face UV unit, against the heightmap's pixels per face UV unit
                    double density = Math.Sqrt(srcArea / faceArea);
                    k = (float)(density / Math.Sqrt((double)mw * mh));
                }
            }
            k = Mathf.Clamp(k, 1f, 4f);
            float longest = Math.Max(mw, mh) * k;
            if (longest > maxSize) k *= maxSize / longest;
            k = Math.Max(k, 1f);
            aw = Math.Max(1, (int)Math.Round(mw * k));
            ah = Math.Max(1, (int)Math.Round(mh * k));
        }

        static float Area(float[] t, int i)
        {
            int o = i * 6;
            return 0.5f * ((t[o + 2] - t[o]) * (t[o + 5] - t[o + 1]) - (t[o + 4] - t[o]) * (t[o + 3] - t[o + 1]));
        }

        /// <summary>
        /// The wall's texture laid over the face as it was, darkened where the wood is cut away. Each texel looks
        /// up the frontmost face triangle under it and reads the wall's texture at that triangle's own UVs, so
        /// patchwork textures (stone blocks) stay intact; texels no triangle covers use the fitted map.
        /// </summary>
        static Color32[] AlbedoPixels(Carving c, int aw, int ah, Color32[] src, int sw, int sh, CarveFace face, Vector2 scale, Vector2 offset, bool point)
        {
            var m = c.Map;
            int mw = m.Width, mh = m.Height;

            // original UV per texel, from the triangle covering it
            var look = src != null ? Lookup_(face, aw, ah) : null;

            var plain = new Color32(150, 120, 90, 255);
            var px = new Color32[aw * ah];
            for (int ty = 0; ty < ah; ty++)
            {
                float v = (ty + 0.5f) / ah;
                for (int x = 0; x < aw; x++)
                {
                    float u = (x + 0.5f) / aw;
                    int i = ty * aw + x;
                    Color32 col = plain;
                    if (src != null)
                    {
                        float su, sv;
                        if (look.Has(i)) { su = look.U[i]; sv = look.V[i]; }
                        else
                        {
                            su = face.MapU.x * u + face.MapU.y * v + face.MapU.z;
                            sv = face.MapV.x * u + face.MapV.y * v + face.MapV.z;
                        }
                        col = Sample(src, sw, sh, su * scale.x + offset.x, sv * scale.y + offset.y, point);
                    }
                    // deeper cuts catch less light: 1 at the surface, about 0.65 at the bottom of a groove
                    float shade = 0.55f + 0.45f * HeightAt(m, u, v);
                    px[i] = new Color32((byte)(col.r * shade), (byte)(col.g * shade), (byte)(col.b * shade), col.a);
                }
            }
            return px;
        }

        /// <summary>Triangles facing out at least this squarely supply the wall texture first (cos of ~30°).</summary>
        const float SquareFacing = 0.87f;

        /// <summary>
        /// Fills in, per texel, the original UV of the face triangle covering it, and which triangle that is.
        /// Square-on triangles go first (frontmost wins); joints and bevels that face partly sideways only fill
        /// texels nothing squarer covers, since their UVs point into other parts of the texture.
        /// </summary>
        static void Rasterise(CarveFace face, int aw, int ah, float[] ou, float[] ov, float[] depth, int[] tri)
        {
            var f = face.TriFaceUV;
            var o = face.TriOrigUV;
            var covered = new bool[aw * ah];
            const float Edge = -1e-3f;   // a hair of overlap so texels on shared edges aren't missed
            for (int pass = 0; pass < 2; pass++)
            {
                for (int t = 0; t < face.TriDepth.Length; t++)
                {
                    bool square = face.TriFacing == null || face.TriFacing[t] >= SquareFacing;
                    if (square != (pass == 0)) continue;
                    int b = t * 6;
                    float x0 = f[b] * aw, y0 = f[b + 1] * ah, x1 = f[b + 2] * aw, y1 = f[b + 3] * ah, x2 = f[b + 4] * aw, y2 = f[b + 5] * ah;
                    float den = (y1 - y2) * (x0 - x2) + (x2 - x1) * (y0 - y2);
                    if (Math.Abs(den) < 1e-9f) continue;
                    int minX = Math.Max(0, (int)Math.Floor(Math.Min(x0, Math.Min(x1, x2)))), maxX = Math.Min(aw - 1, (int)Math.Ceiling(Math.Max(x0, Math.Max(x1, x2))));
                    int minY = Math.Max(0, (int)Math.Floor(Math.Min(y0, Math.Min(y1, y2)))), maxY = Math.Min(ah - 1, (int)Math.Ceiling(Math.Max(y0, Math.Max(y1, y2))));
                    float d = face.TriDepth[t];
                    for (int y = minY; y <= maxY; y++)
                    {
                        float py = y + 0.5f;
                        for (int x = minX; x <= maxX; x++)
                        {
                            float pxc = x + 0.5f;
                            float w0 = ((y1 - y2) * (pxc - x2) + (x2 - x1) * (py - y2)) / den;
                            float w1 = ((y2 - y0) * (pxc - x2) + (x0 - x2) * (py - y2)) / den;
                            float w2 = 1 - w0 - w1;
                            if (w0 < Edge || w1 < Edge || w2 < Edge) continue;
                            int i = y * aw + x;
                            if (pass == 1 && covered[i]) continue;   // a square-on triangle already has it
                            if (d <= depth[i]) continue;
                            depth[i] = d;
                            tri[i] = t;
                            ou[i] = w0 * o[b] + w1 * o[b + 2] + w2 * o[b + 4];
                            ov[i] = w0 * o[b + 1] + w1 * o[b + 3] + w2 * o[b + 5];
                        }
                    }
                }
                if (pass == 0)
                    for (int i = 0; i < covered.Length; i++) covered[i] = !float.IsNegativeInfinity(depth[i]);
            }
        }

        /// <summary>The texel lookup shared by the colour and normal textures: original UV and triangle per texel.</summary>
        sealed class Lookup
        {
            public float[] U, V, Depth;
            public int[] Tri;
            public bool Has(int i) => !float.IsNegativeInfinity(Depth[i]);
        }

        static Lookup Lookup_(CarveFace face, int aw, int ah)
        {
            var l = new Lookup { U = new float[aw * ah], V = new float[aw * ah], Depth = new float[aw * ah], Tri = new int[aw * ah] };
            for (int i = 0; i < l.Depth.Length; i++) { l.Depth[i] = float.NegativeInfinity; l.Tri[i] = -1; }
            if (face.TriFaceUV != null) Rasterise(face, aw, ah, l.U, l.V, l.Depth, l.Tri);
            return l;
        }

        /// <summary>The heightmap at face UV (u, v up), bilinear; heightmap rows run downward.</summary>
        static float HeightAt(HeightMap m, float u, float v)
        {
            float fx = u * m.Width - 0.5f, fy = (1 - v) * m.Height - 0.5f;
            int x0 = Math.Max(0, Math.Min(m.Width - 1, (int)Math.Floor(fx))), y0 = Math.Max(0, Math.Min(m.Height - 1, (int)Math.Floor(fy)));
            int x1 = Math.Min(m.Width - 1, x0 + 1), y1 = Math.Min(m.Height - 1, y0 + 1);
            float tx = Mathf.Clamp01(fx - x0), ty = Mathf.Clamp01(fy - y0);
            float a = m.H[y0 * m.Width + x0], b = m.H[y0 * m.Width + x1], cc = m.H[y1 * m.Width + x0], d = m.H[y1 * m.Width + x1];
            return (a + (b - a) * tx) + ((cc + (d - cc) * tx) - (a + (b - a) * tx)) * ty;
        }

        /// <summary>The source texture at (u, v), wrapping; <paramref name="point"/>: nearest texel, as the game draws it.</summary>
        static Color32 Sample(Color32[] px, int sw, int sh, float u, float v, bool point)
        {
            u -= (float)Math.Floor(u); v -= (float)Math.Floor(v);
            if (point) return px[Wrap((int)Math.Floor(v * sh), sh) * sw + Wrap((int)Math.Floor(u * sw), sw)];
            float fx = u * sw - 0.5f, fy = v * sh - 0.5f;
            int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy);
            float tx = fx - x0, ty = fy - y0;
            int ax = Wrap(x0, sw), bx = Wrap(x0 + 1, sw), ay = Wrap(y0, sh), by = Wrap(y0 + 1, sh);
            Color32 a = px[ay * sw + ax], b = px[ay * sw + bx], cc = px[by * sw + ax], d = px[by * sw + bx];
            return Lerp(Lerp(a, b, tx), Lerp(cc, d, tx), ty);
        }

        static int Wrap(int i, int n) => ((i % n) + n) % n;

        static Color32 Lerp(Color32 a, Color32 b, float t) => new Color32(
            (byte)(a.r + (b.r - a.r) * t), (byte)(a.g + (b.g - a.g) * t), (byte)(a.b + (b.b - a.b) * t), (byte)(a.a + (b.a - a.a) * t));

        // ------------------------------------------------------------------ the game's textures

        /// <summary>
        /// A CPU copy of a texture the game keeps only on the GPU: blit to a render texture and read it back
        /// asynchronously, so the game doesn't wait for the GPU. Once per texture; then cached.
        /// </summary>
        static void ReadSource(Texture src, Action then) => ReadSource(src, false, then);

        /// <summary><paramref name="linear"/>: read the raw values (normal maps hold directions, not colours).</summary>
        static void ReadSource(Texture src, bool linear, Action then)
        {
            if (SourcePixels.ContainsKey(src)) { then(); return; }
            if (SourceWaiting.TryGetValue(src, out var waiting)) { waiting.Add(then); return; }
            SourceWaiting[src] = new List<Action> { then };

            int w = Math.Min(src.width, 1024), h = Math.Min(src.height, 1024);
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
            Graphics.Blit(src, rt);

            void Done(Color32[] px)
            {
                SourcePixels[src] = (px, w, h);
                var list = SourceWaiting[src];
                SourceWaiting.Remove(src);
                foreach (var a in list)
                {
                    try { a(); }
                    catch (Exception e) { Jotunn.Logger.LogWarning("[Engraving] " + e); }
                }
            }

            if (SystemInfo.supportsAsyncGPUReadback)
            {
                AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32, req =>
                {
                    Color32[] px = null;
                    if (!req.hasError) px = req.GetData<Color32>().ToArray();
                    else Jotunn.Logger.LogWarning($"[Engraving] Couldn't read texture '{src.name}' back from the GPU; carving on plain colour.");
                    RenderTexture.ReleaseTemporary(rt);
                    Done(px);
                });
                return;
            }

            // no async readback on this machine: read it now (a short stall, once per texture)
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tmp = new Texture2D(w, h, TextureFormat.RGBA32, false, linear);
            tmp.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tmp.Apply(false);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var pixels = tmp.GetPixels32();
            UnityEngine.Object.Destroy(tmp);
            Done(pixels);
        }

        // ------------------------------------------------------------------ editor preview

        /// <summary>The lit preview shown in the editor (same look as Engraving.Preview).</summary>
        public static void Preview(Carving c, Texture2D into)
        {
            var m = c.Map;
            int w = m.Width, h = m.Height;
            if (into.width != w || into.height != h) into.Reinitialize(w, h);
            const float depth = 0.025f;
            float k = depth * c.PixelsPerMetre * 0.5f;
            Vector3 light = new Vector3(-0.55f, 0.55f, 0.63f).normalized;
            var px = new Color32[w * h];
            for (int ty = 0; ty < h; ty++)
            {
                int y = h - 1 - ty;
                for (int x = 0; x < w; x++)
                {
                    float dx = (m.H[y * w + Math.Min(x + 1, w - 1)] - m.H[y * w + Math.Max(x - 1, 0)]) * k;
                    float dy = (m.H[Math.Max(y - 1, 0) * w + x] - m.H[Math.Min(y + 1, h - 1) * w + x]) * k;
                    var n = new Vector3(-dx, -dy, 1f).normalized;
                    float lambert = Mathf.Max(0, Vector3.Dot(n, light));
                    float s = (0.25f + 0.85f * lambert) * (0.55f + 0.45f * m.H[y * w + x]);
                    px[ty * w + x] = new Color32((byte)Mathf.Clamp(200 * s, 0, 255), (byte)Mathf.Clamp(150 * s, 0, 255), (byte)Mathf.Clamp(98 * s, 0, 255), 255);
                }
            }
            into.SetPixels32(px);
            into.Apply(false);
        }
    }
}
