using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Engraving.Core.Charges;
using Engraving.Core.Language;
using Engraving.Core.Render;
using UnityEngine;
using UnityEngine.Rendering;

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
        public static void Request(string script, float w, float h, float ppm, Action<Carving> done)
        {
            string key = KeyFor(script, w, h, ppm);
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
            // the map rounded, so faces laid out the same way share a texture
            string R(Vector3 v) => $"{v.x:0.###},{v.y:0.###},{v.z:0.###}";
            return "a|" + c.Key + "|" + (tex != null ? tex.GetInstanceID() : 0) + "|" + sc + of + "|" + R(face.MapU) + "|" + R(face.MapV);
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

        /// <summary>Takes a share of the albedo for this face; <paramref name="ready"/> runs once it exists. Returns the key to release.</summary>
        public static string AcquireAlbedo(Carving c, Material source, CarveFace face, Action<Texture2D> ready)
        {
            string key = AlbedoKey(c, source, face);
            Acquire(key, ready, e =>
            {
                var tex = MainTex(source);
                Vector2 sc = tex != null ? source.mainTextureScale : Vector2.one, of = tex != null ? source.mainTextureOffset : Vector2.zero;
                Vector3 mu = face.MapU, mv = face.MapV;
                void Build()
                {
                    var src = tex != null && SourcePixels.TryGetValue(tex, out var s) ? s : (null, 0, 0);
                    Color32[] px = null;
                    CarvingJobs.Run(() => px = AlbedoPixels(c, src.Item1, src.Item2, src.Item3, mu, mv, sc, of), () =>
                        CarvingJobs.Upload(() => Finish(key, MakeTexture(px, c.Map.Width, c.Map.Height, false, "Engraving_Albedo"))));
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

        static byte ToByte(float n) => (byte)Math.Max(0, Math.Min(255, (int)Math.Round((n * 0.5f + 0.5f) * 255)));

        /// <summary>The wall's texture laid over the face as it was, darkened where the wood is cut away.</summary>
        static Color32[] AlbedoPixels(Carving c, Color32[] src, int sw, int sh, Vector3 mu, Vector3 mv, Vector2 scale, Vector2 offset)
        {
            var m = c.Map;
            int w = m.Width, h = m.Height;
            var plain = new Color32(150, 120, 90, 255);
            var px = new Color32[w * h];
            for (int ty = 0; ty < h; ty++)
            {
                int y = h - 1 - ty;
                float v = (ty + 0.5f) / h;
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w;
                    Color32 col = plain;
                    if (src != null)
                    {
                        float ou = mu.x * u + mu.y * v + mu.z;
                        float ov = mv.x * u + mv.y * v + mv.z;
                        col = Sample(src, sw, sh, ou * scale.x + offset.x, ov * scale.y + offset.y);
                    }
                    // deeper cuts catch less light: 1 at the surface, about 0.65 at the bottom of a groove
                    float shade = 0.55f + 0.45f * m.H[y * w + x];
                    px[ty * w + x] = new Color32((byte)(col.r * shade), (byte)(col.g * shade), (byte)(col.b * shade), col.a);
                }
            }
            return px;
        }

        static Color32 Sample(Color32[] px, int sw, int sh, float u, float v)
        {
            u -= (float)Math.Floor(u); v -= (float)Math.Floor(v);
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
        static void ReadSource(Texture src, Action then)
        {
            if (SourcePixels.ContainsKey(src)) { then(); return; }
            if (SourceWaiting.TryGetValue(src, out var waiting)) { waiting.Add(then); return; }
            SourceWaiting[src] = new List<Action> { then };

            int w = Math.Min(src.width, 1024), h = Math.Min(src.height, 1024);
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
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
            var tmp = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
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
