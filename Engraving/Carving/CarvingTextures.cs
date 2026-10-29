using System;
using System.Collections.Generic;
using System.Threading;
using Engraving.Core.Charges;
using Engraving.Core.Language;
using Engraving.Core.Render;
using UnityEngine;

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
    /// Renders carvings on one background thread (the engine is pure C#), so loading a base full of
    /// carved walls doesn't stall the game. Finished renders are handed back on the main thread by
    /// <see cref="Pump"/>. Identical requests share one render.
    /// </summary>
    internal static class CarvingJobs
    {
        sealed class Job
        {
            public string Key, Script;
            public float W, H, Ppm;
            public Carving Result;
            public readonly List<Action<Carving>> Callbacks = new List<Action<Carving>>();
        }

        static readonly object Gate = new object();
        static readonly Queue<Job> Pending = new Queue<Job>();
        static readonly Queue<Job> Done = new Queue<Job>();
        static readonly Dictionary<string, Job> InFlight = new Dictionary<string, Job>();
        static readonly Dictionary<string, Carving> Finished = new Dictionary<string, Carving>();
        static readonly LinkedList<string> FinishedOrder = new LinkedList<string>();
        const int KeepFinished = 48;
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

        /// <summary>Asks for a carving; the callback runs on the main thread (immediately if it's cached).</summary>
        public static void Request(string script, float w, float h, float ppm, Action<Carving> done)
        {
            string key = KeyFor(script, w, h, ppm);
            Carving cached = null;
            lock (Gate)
            {
                if (!Finished.TryGetValue(key, out cached))
                {
                    if (InFlight.TryGetValue(key, out var job)) { job.Callbacks.Add(done); return; }
                    job = new Job { Key = key, Script = script, W = w, H = h, Ppm = ppm };
                    job.Callbacks.Add(done);
                    InFlight[key] = job;
                    Pending.Enqueue(job);
                }
            }
            // a cached carving is handed over outside the lock
            if (cached != null) { done(cached); return; }
            EnsureThread();
            Wake.Set();
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
                Job job = null;
                ChargeLibrary lib;
                lock (Gate)
                {
                    if (Pending.Count > 0) job = Pending.Dequeue();
                    lib = _library;
                }
                if (job == null) { Wake.WaitOne(1000); continue; }

                var carving = new Carving { Key = job.Key, PixelsPerMetre = job.Ppm };
                try
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var parsed = Parser.Parse(job.Script);
                    carving.Diagnostics.AddRange(parsed.Diagnostics);
                    var opt = new RenderOptions { Width = job.W, Height = job.H, PixelsPerMetre = job.Ppm };
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
                lock (Gate)
                {
                    job.Result = carving;
                    Done.Enqueue(job);
                }
            }
        }

        /// <summary>Main thread, every frame: hands finished renders to whoever asked.</summary>
        public static void Pump()
        {
            while (true)
            {
                Job job;
                lock (Gate)
                {
                    if (Done.Count == 0) return;
                    job = Done.Dequeue();
                    InFlight.Remove(job.Key);
                    if (job.Result.Map != null)
                    {
                        Finished[job.Key] = job.Result;
                        FinishedOrder.AddLast(job.Key);
                        while (FinishedOrder.Count > KeepFinished)
                        {
                            Finished.Remove(FinishedOrder.First.Value);
                            FinishedOrder.RemoveFirst();
                        }
                    }
                }
                foreach (var cb in job.Callbacks)
                {
                    try { cb(job.Result); }
                    catch (Exception e) { Jotunn.Logger.LogWarning("[Engraving] " + e); }
                }
            }
        }
    }

    /// <summary>
    /// Builds the textures a carving needs on a particular wall: a normal map from the heightmap
    /// (the maths of braid_gui.py's make_normal), and the wall's own texture resampled onto the face
    /// and darkened in the cuts so the carving reads from a distance too. Shared between identical walls.
    /// </summary>
    internal static class CarvingTextures
    {
        public sealed class Set
        {
            public Texture2D Albedo, Normal;
            public int Users;
        }

        static readonly Dictionary<string, Set> Sets = new Dictionary<string, Set>();
        static readonly Dictionary<Texture, Color32[]> Readable = new Dictionary<Texture, Color32[]>();
        static readonly Dictionary<Texture, Vector2Int> ReadableSize = new Dictionary<Texture, Vector2Int>();

        public static string KeyFor(Carving c, Material source, CarveFace face, float depth) =>
            c.Key + "|" + (source != null ? source.GetInstanceID() : 0) + "|" + face.MapU + face.MapV + "|" + depth;

        public static Set Acquire(Carving c, Material source, CarveFace face, float depthMetres)
        {
            string key = KeyFor(c, source, face, depthMetres);
            if (!Sets.TryGetValue(key, out var set))
            {
                set = new Set { Normal = MakeNormal(c, depthMetres), Albedo = MakeAlbedo(c, source, face) };
                Sets[key] = set;
            }
            set.Users++;
            return set;
        }

        public static void Release(Carving c, Material source, CarveFace face, float depthMetres)
        {
            string key = KeyFor(c, source, face, depthMetres);
            if (!Sets.TryGetValue(key, out var set)) return;
            if (--set.Users > 0) return;
            Sets.Remove(key);
            UnityEngine.Object.Destroy(set.Albedo);
            UnityEngine.Object.Destroy(set.Normal);
        }

        /// <summary>
        /// Unity / OpenGL-style tangent-space normals (Y up). Texture rows run upward while the heightmap's
        /// run downward, so rows are flipped on the way in.
        /// </summary>
        public static Texture2D MakeNormal(Carving c, float depthMetres)
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
                    float inv = 1f / Mathf.Sqrt(nx * nx + ny * ny + nz * nz);
                    px[ty * w + x] = new Color32(
                        (byte)Mathf.RoundToInt((nx * inv * 0.5f + 0.5f) * 255),
                        (byte)Mathf.RoundToInt((ny * inv * 0.5f + 0.5f) * 255),
                        (byte)Mathf.RoundToInt((nz * inv * 0.5f + 0.5f) * 255),
                        255);
                }
            }
            // linear: a normal map holds directions, not colours
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true, true) { name = "Engraving_Normal", wrapMode = TextureWrapMode.Clamp, anisoLevel = 4 };
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>The wall's texture laid over the face as it was, darkened where the wood is cut away.</summary>
        static Texture2D MakeAlbedo(Carving c, Material source, CarveFace face)
        {
            var m = c.Map;
            int w = m.Width, h = m.Height;
            var src = source != null && source.HasProperty("_MainTex") ? source.mainTexture : null;
            Color32[] srcPx = src != null ? ReadPixels(src) : null;
            Vector2Int srcSize = srcPx != null ? ReadableSize[src] : Vector2Int.zero;
            Vector2 scale = source != null && source.HasProperty("_MainTex") ? source.mainTextureScale : Vector2.one;
            Vector2 offset = source != null && source.HasProperty("_MainTex") ? source.mainTextureOffset : Vector2.zero;
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
                    if (srcPx != null)
                    {
                        float ou = face.MapU.x * u + face.MapU.y * v + face.MapU.z;
                        float ov = face.MapV.x * u + face.MapV.y * v + face.MapV.z;
                        col = Sample(srcPx, srcSize, ou * scale.x + offset.x, ov * scale.y + offset.y);
                    }
                    // deeper cuts catch less light: 1 at the surface, about 0.65 at the bottom of a groove
                    float shade = 0.55f + 0.45f * m.H[y * w + x];
                    px[ty * w + x] = new Color32((byte)(col.r * shade), (byte)(col.g * shade), (byte)(col.b * shade), col.a);
                }
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true, false) { name = "Engraving_Albedo", wrapMode = TextureWrapMode.Clamp, anisoLevel = 4 };
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        static Color32 Sample(Color32[] px, Vector2Int size, float u, float v)
        {
            u -= Mathf.Floor(u); v -= Mathf.Floor(v);
            float fx = u * size.x - 0.5f, fy = v * size.y - 0.5f;
            int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy);
            float tx = fx - x0, ty = fy - y0;
            int Wrap(int i, int n) => ((i % n) + n) % n;
            int ax = Wrap(x0, size.x), bx = Wrap(x0 + 1, size.x), ay = Wrap(y0, size.y), by = Wrap(y0 + 1, size.y);
            Color32 a = px[ay * size.x + ax], b = px[ay * size.x + bx], cc = px[by * size.x + ax], d = px[by * size.x + bx];
            return Color32.Lerp(Color32.Lerp(a, b, tx), Color32.Lerp(cc, d, tx), ty);
        }

        /// <summary>A CPU copy of a texture the game keeps only on the GPU (blit, then read back). Cached.</summary>
        static Color32[] ReadPixels(Texture src)
        {
            if (Readable.TryGetValue(src, out var px)) return px;
            int w = Math.Min(src.width, 1024), h = Math.Min(src.height, 1024);
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            try
            {
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                var tmp = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
                tmp.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tmp.Apply(false);
                px = tmp.GetPixels32();
                UnityEngine.Object.Destroy(tmp);
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }
            Readable[src] = px;
            ReadableSize[src] = new Vector2Int(w, h);
            return px;
        }

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
