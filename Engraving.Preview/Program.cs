using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Engraving.Core.Charges;
using Engraving.Core.Geometry;
using Engraving.Core.Imaging;
using Engraving.Core.Language;
using Engraving.Core.Render;

namespace Engraving.Preview
{
    /// <summary>
    /// Usage: Engraving.Preview [options] script.eng [more.eng ...]
    ///   -o DIR          output folder (default: next to each script)
    ///   -c DIR          extra charge image folder (repeatable; searched in order after built-ins)
    ///   --ppm N         pixels per metre (default 256)
    ///   --size WxH      override panel size in metres, e.g. 2x2
    ///   --poses DIR     extra pose files (*.json; an EngravingPoses folder beside the script is read too)
    ///   --write-poses F write the built-in poses to F as an example, and stop
    ///   --record        print each script as the mod stores it (with "#pose:" lines for non-built-in poses), and stop
    /// Writes NAME.height.png (16-bit heightmap) and NAME.preview.png (lit wood preview).
    /// </summary>
    public static class Program
    {
        /// <summary>Builds every basic-mode combination and checks it parses cleanly and its charge resolves.</summary>
        static int BasicSelfTest(List<string> chargeDirs)
        {
            var lib = new ChargeLibrary();
            foreach (var d in chargeDirs) lib.AddSource(new FolderChargeSource(d));
            var extra = new List<BasicCarving.ChargeChoice> { BasicCarving.Named("My Banner", "My \"Banner\" #2") };
            int n = 0, bad = 0;
            var rendered = new HashSet<string>();
            foreach (var b in BasicCarving.Combinations(extra))
            {
                n++;
                string text = b.Build();
                var parsed = Parser.Parse(text);
                if (parsed.Diagnostics.Count > 0)
                {
                    if (bad++ < 20) Console.WriteLine("PROBLEM:\n" + text + "\n  " + string.Join("\n  ", parsed.Diagnostics));
                    continue;
                }
                // render one of each charge (twice for plurals) and each border style once
                string key = (b.HasCharge ? b.Charge.Label + b.Count : "") + "|" + (b.HasBorder ? b.BorderStyle + b.Width : "");
                if (!rendered.Add(key) || (b.HasCharge && b.Count > 2) || b.Charge.One.StartsWith("\"")) continue;
                var result = EngravingRenderer.Render(parsed.Spec, lib, new RenderOptions { Width = 1, Height = 0.5f, PixelsPerMetre = 48 });
                if (result.Diagnostics.Count > 0)
                {
                    if (bad++ < 20) Console.WriteLine("RENDER:\n" + text + "\n  " + string.Join("\n  ", result.Diagnostics));
                }
            }
            Console.WriteLine($"basic self-test: {n} combinations, {rendered.Count} rendered, {bad} problem(s)");
            return bad == 0 ? 0 : 1;
        }

        /// <summary>"trophies=troll,deer;materials=copper;weapons=sword:30,bow:22;shield": a pretend player, to fill scenes.</summary>
        static Engraving.Core.Language.SceneProgress ReadProgress(string spec)
        {
            var p = new Engraving.Core.Language.SceneProgress();
            foreach (var part in spec.Split(';'))
            {
                var kv = part.Split('=');
                var items = kv.Length > 1 ? kv[1].Split(',').Where(x => x.Length > 0) : Enumerable.Empty<string>();
                switch (kv[0].Trim())
                {
                    case "trophies": foreach (var t in items) p.Trophies.Add(Engraving.Core.Language.Scene.FigureForTrophy(t) ?? t); break;
                    case "materials": foreach (var m in items) p.Materials.Add(Engraving.Core.Language.Scene.MaterialFor(m) ?? m); break;
                    case "weapons":
                        foreach (var w in items) { var a = w.Split(':'); p.Weapons[a[0]] = a.Length > 1 ? float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture) : 1; }
                        break;
                    case "shield": p.Shield = true; break;
                }
            }
            return p;
        }

        public static int Main(string[] args)
        {
            var scripts = new List<string>();
            var chargeDirs = new List<string>();
            var poseDirs = new List<string>();
            bool record = false;
            Engraving.Core.Language.SceneProgress progress = null;
            string outDir = null;
            var opt = new RenderOptions();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-o": outDir = args[++i]; break;
                    case "-c": chargeDirs.Add(args[++i]); break;
                    case "--poses": poseDirs.Add(args[++i]); break;
                    case "--record": record = true; break;
                    case "--progress": progress = ReadProgress(args[++i]); break;
                    case "--write-poses": File.WriteAllText(args[++i], Engraving.Core.Figures.PoseLibrary.BuiltinJson); Console.WriteLine("wrote " + args[i]); return 0;
                    case "--ppm": opt.PixelsPerMetre = float.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); break;
                    case "--size":
                    {
                        var p = args[++i].Split('x', 'X');
                        opt.Width = float.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture);
                        opt.Height = float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture);
                        break;
                    }
                    case "--basic-selftest": return BasicSelfTest(chargeDirs);
                    case "-h": case "--help":
                        Console.WriteLine("Engraving.Preview [-o outdir] [-c chargesdir]... [--ppm N] [--size WxH] script.eng...");
                        return 0;
                    default: scripts.Add(args[i]); break;
                }
            }
            if (scripts.Count == 0)
            {
                Console.Error.WriteLine("no script given (try --help)");
                return 1;
            }

            // poses: the built-in ones, then *.json in each --poses folder and an EngravingPoses folder beside the first script
            if (scripts.Count > 0) poseDirs.Add(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(scripts[0])), "EngravingPoses"));
            var poseFiles = poseDirs.Where(Directory.Exists).SelectMany(d => Directory.GetFiles(d, "*.json").OrderBy(f => f))
                .Select(f => (Path.GetFileName(f), File.ReadAllText(f))).ToList();
            foreach (var problem in Engraving.Core.Figures.PoseLibrary.Load(poseFiles)) Console.WriteLine("poses: " + problem);
            if (poseFiles.Count > 0) Console.WriteLine($"poses: {poseFiles.Count} file(s), {Engraving.Core.Figures.PoseLibrary.All.Count()} pose(s)");

            if (record)
            {
                foreach (var path in scripts) Console.WriteLine(Engraving.Core.Figures.PoseLibrary.Record(File.ReadAllText(path)));
                return 0;
            }

            // default search: an Engravings folder then a Charges folder beside the script, like the mod
            int errors = 0;
            foreach (var path in scripts)
            {
                var lib = new ChargeLibrary();
                foreach (var d in chargeDirs) lib.AddSource(new FolderChargeSource(d));
                string dir = Path.GetDirectoryName(Path.GetFullPath(path));
                lib.AddSource(new FolderChargeSource(Path.Combine(dir, "Engravings")));
                lib.AddSource(new FolderChargeSource(Path.Combine(dir, "Charges")));

                string source = File.ReadAllText(path);
                if (progress != null)
                {
                    var problems = new List<string>();
                    source = Engraving.Core.Language.Scene.Fill(source, progress, problems);
                    Console.WriteLine($"{Path.GetFileName(path)} filled: {source.Trim()}");
                    foreach (var pr in problems) Console.WriteLine("  scene: " + pr);
                }
                var parsed = Parser.Parse(source);
                foreach (var d in parsed.Diagnostics) Console.WriteLine($"{Path.GetFileName(path)}: {d}");
                if (parsed.HasErrors) errors++;

                var sw = Stopwatch.StartNew();
                var result = EngravingRenderer.Render(parsed.Spec, lib, opt);
                sw.Stop();
                foreach (var d in result.Diagnostics) Console.WriteLine($"{Path.GetFileName(path)}: {d}");

                string od = outDir ?? dir;
                Directory.CreateDirectory(od);
                string stem = Path.Combine(od, Path.GetFileNameWithoutExtension(path));
                var map = result.Map;
                File.WriteAllBytes(stem + ".height.png", Png.EncodeGray16(map.H, map.Width, map.Height));
                File.WriteAllBytes(stem + ".preview.png", Png.EncodeRgb8(Shade(map, opt.PixelsPerMetre), map.Width, map.Height));
                Console.WriteLine($"{Path.GetFileName(path)}: {map.Width}x{map.Height} px in {sw.ElapsedMilliseconds} ms -> {stem}.preview.png");
            }
            return errors > 0 ? 2 : 0;
        }

        /// <summary>Simple raking-light render on a wood colour, to judge how a carving will read in game.</summary>
        static float[] Shade(HeightMap m, float ppm)
        {
            const float depthMetres = 0.025f;
            int w = m.Width, h = m.Height;
            var rgb = new float[w * h * 3];
            // light from upper left, fairly low
            float lx = -0.55f, ly = 0.55f, lz = 0.63f;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float hl = m[Math.Max(x - 1, 0), y], hr = m[Math.Min(x + 1, w - 1), y];
                    float hu = m[x, Math.Max(y - 1, 0)], hd = m[x, Math.Min(y + 1, h - 1)];
                    // slopes in metres per metre; image y grows downward, world y up
                    float dx = (hr - hl) * depthMetres * ppm / 2;
                    float dy = (hu - hd) * depthMetres * ppm / 2;
                    float nx = -dx, ny = -dy, nz = 1;
                    float inv = 1 / M.Sqrt(nx * nx + ny * ny + nz * nz);
                    float lambert = M.Max(0, (nx * lx + ny * ly + nz * lz) * inv);
                    float hgt = m[x, y];
                    float occl = 0.55f + 0.45f * hgt;        // deeper cuts are darker
                    float grain = 0.94f + 0.06f * M.Sin(y * 0.09f + 3 * M.Sin(x * 0.013f));
                    float k = (0.25f + 0.85f * lambert) * occl * grain;
                    int o = (y * w + x) * 3;
                    rgb[o] = 0.78f * k; rgb[o + 1] = 0.58f * k; rgb[o + 2] = 0.38f * k;
                }
            return rgb;
        }
    }
}
