using System;
using System.Collections.Generic;
using System.Linq;
using Engraving.Core.Charges;
using Engraving.Core.Geometry;
using UnityEngine;
using UnityEngine.Rendering;

namespace Engraving.Carving
{
    /// <summary>
    /// The game's item icons as charges: "a hammer", "two bronze axes", "a wolf trophy". Matched by the item's
    /// name as players see it or its prefab name; plurals fold as for other charges. Icons are the last resort,
    /// after built-in shapes, Heraldry Mod's pictures and the image folders. The silhouette is the icon's alpha.
    /// Icon textures live only on the GPU (often packed in atlases), so each one is copied to a small render
    /// texture and read back asynchronously, once.
    /// </summary>
    internal static class ItemIconCharges
    {
        const int Size = 128;

        static readonly object Gate = new object();
        /// <summary>Icon silhouettes by normalised name, for the background renderer.</summary>
        static readonly Dictionary<string, AlphaMask> Masks = new Dictionary<string, AlphaMask>();
        static readonly HashSet<string> Reading = new HashSet<string>();
        /// <summary>Icons that couldn't be read: not waited for again.</summary>
        static readonly HashSet<string> Failed = new HashSet<string>();
        static Dictionary<string, ItemDrop> _index;

        /// <summary>Set when an icon finished reading back, so carvings waiting for it redraw.</summary>
        public static bool Changed;

        /// <summary>Normalised item names (shown name and prefab name) → item, built once ObjectDB exists.</summary>
        static Dictionary<string, ItemDrop> Index()
        {
            if (_index != null) return _index;
            if (ObjectDB.instance == null || ObjectDB.instance.m_items == null || ObjectDB.instance.m_items.Count == 0) return null;
            var index = new Dictionary<string, ItemDrop>();
            foreach (var go in ObjectDB.instance.m_items)
            {
                var drop = go != null ? go.GetComponent<ItemDrop>() : null;
                if (drop?.m_itemData?.m_shared == null || drop.m_itemData.m_shared.m_icons == null || drop.m_itemData.m_shared.m_icons.Length == 0) continue;
                foreach (var name in new[] { Localization.instance.Localize(drop.m_itemData.m_shared.m_name), go.name })
                {
                    string key = ChargeLibrary.Normalise(name ?? "");
                    if (key.Length > 0 && !index.ContainsKey(key)) index[key] = drop;
                }
            }
            _index = index;
            return _index;
        }

        /// <summary>
        /// Main thread, before drawing: starts reading the icons of any items the script names. Returns the names
        /// whose icons are ready, for the render key; <paramref name="waiting"/> counts icons still reading.
        /// </summary>
        public static string Prepare(string script, out int waiting)
        {
            waiting = 0;
            var index = Index();
            if (index == null) return "";
            var ready = new List<string>();
            foreach (var charge in Core.Language.Parser.Parse(HeraldryCharges.Visible(script)).Spec.Charges)
            {
                foreach (var cand in ChargeLibrary.Candidates(charge.Name))
                {
                    if (BuiltinShapes.TryGet(cand, out _)) break;
                    if (!index.TryGetValue(cand, out var drop)) continue;
                    bool have, failed;
                    lock (Gate) { have = Masks.ContainsKey(cand); failed = Failed.Contains(cand); }
                    if (have) ready.Add(cand);
                    else if (!failed) { waiting++; Read(cand, drop); }
                    break;
                }
            }
            return string.Join(",", ready.Distinct().OrderBy(n => n));
        }

        static void Read(string key, ItemDrop drop)
        {
            if (!Reading.Add(key)) return;
            Sprite sprite;
            try { sprite = drop.m_itemData.GetIcon(); }
            catch { sprite = null; }
            if (sprite == null || sprite.texture == null) { lock (Gate) Failed.Add(key); return; }

            var tex = sprite.texture;
            var r = sprite.textureRect;
            var rt = RenderTexture.GetTemporary(Size, Size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prevActive = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(true, true, Color.clear);
            RenderTexture.active = prevActive;
            // just the icon's rectangle of its (possibly shared) texture
            Graphics.Blit(tex, rt, new Vector2(r.width / tex.width, r.height / tex.height), new Vector2(r.x / tex.width, r.y / tex.height));

            void Done(Color32[] px)
            {
                RenderTexture.ReleaseTemporary(rt);
                if (px == null)
                {
                    lock (Gate) Failed.Add(key);
                    Jotunn.Logger.LogWarning($"[Engraving] Couldn't read the icon of '{drop.name}' back from the GPU.");
                    return;
                }
                // read-back rows run from the bottom; masks run from the top like image files
                var mask = new AlphaMask(Size, Size);
                for (int y = 0; y < Size; y++)
                    for (int x = 0; x < Size; x++)
                        mask.Alpha[y * Size + x] = px[(Size - 1 - y) * Size + x].a / 255f;
                lock (Gate) Masks[key] = mask;
                Changed = true;
            }

            if (SystemInfo.supportsAsyncGPUReadback)
            {
                AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32, req => Done(req.hasError ? null : req.GetData<Color32>().ToArray()));
                return;
            }
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tmp = new Texture2D(Size, Size, TextureFormat.RGBA32, false, false);
            tmp.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            tmp.Apply(false);
            RenderTexture.active = prev;
            var pixels = tmp.GetPixels32();
            UnityEngine.Object.Destroy(tmp);
            Done(pixels);
        }

        /// <summary>The charge source the renderer reads icons from (last in line).</summary>
        public sealed class Source : IChargeSource
        {
            public bool TryGetMask(string normalisedName, out AlphaMask mask)
            {
                lock (Gate) return Masks.TryGetValue(normalisedName, out mask);
            }
        }
    }
}
