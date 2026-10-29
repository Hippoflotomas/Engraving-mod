using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Engraving.Core.Geometry;
using Engraving.Core.Imaging;

namespace Engraving.Core.Charges
{
    /// <summary>Something that can supply a silhouette for a charge name (image folder, game icons, rigged figures...).</summary>
    public interface IChargeSource
    {
        /// <summary>Called with a normalised name (lower case, no spaces, hyphens or underscores).</summary>
        bool TryGetMask(string normalisedName, out AlphaMask mask);
    }

    /// <summary>
    /// Resolves charge names: built-in geometric shapes first, then each source in order
    /// (in the mod: synced server engravings, the Engravings folder, the heraldry Charges folder, game icons).
    /// Plurals are folded ("wolves" finds "wolf").
    /// </summary>
    public class ChargeLibrary
    {
        readonly List<IChargeSource> _sources = new List<IChargeSource>();
        readonly Dictionary<string, IShape> _cache = new Dictionary<string, IShape>();

        public ChargeLibrary(params IChargeSource[] sources) => _sources.AddRange(sources);

        public void AddSource(IChargeSource s) => _sources.Add(s);

        public static string Normalise(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in name.ToLowerInvariant())
                if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }

        /// <summary>Candidate spellings: as written, then singular forms.</summary>
        public static IEnumerable<string> Candidates(string name)
        {
            string n = Normalise(name);
            yield return n;
            if (n.EndsWith("ves")) { yield return n.Substring(0, n.Length - 3) + "f"; yield return n.Substring(0, n.Length - 3) + "fe"; }
            if (n.EndsWith("ies")) yield return n.Substring(0, n.Length - 3) + "y";
            if (n.EndsWith("es")) yield return n.Substring(0, n.Length - 2);
            if (n.EndsWith("s")) yield return n.Substring(0, n.Length - 1);
            if (n.EndsWith("men")) yield return n.Substring(0, n.Length - 3) + "man";
            if (n == "geese") yield return "goose";
            if (n == "mice") yield return "mouse";
            if (n == "oxen") yield return "ox";
        }

        public IShape Resolve(string name)
        {
            if (_cache.TryGetValue(name, out var cached)) return cached;
            IShape found = null;
            foreach (var cand in Candidates(name))
            {
                if (BuiltinShapes.TryGet(cand, out found)) break;
                foreach (var src in _sources)
                    if (src.TryGetMask(cand, out var mask)) { found = new ImageShape(mask); break; }
                if (found != null) break;
            }
            _cache[name] = found;
            return found;
        }

        public void ClearCache() => _cache.Clear();
    }

    /// <summary>Loads PNGs from a folder, matching file names loosely ("Fire Pit.png" == "firepit").</summary>
    public class FolderChargeSource : IChargeSource
    {
        readonly string _dir;
        Dictionary<string, string> _index;

        public FolderChargeSource(string dir) { _dir = dir; }

        public void Refresh() => _index = null;

        public bool TryGetMask(string normalisedName, out AlphaMask mask)
        {
            mask = null;
            if (_index == null)
            {
                _index = new Dictionary<string, string>();
                if (Directory.Exists(_dir))
                    foreach (var f in Directory.GetFiles(_dir, "*.png", SearchOption.AllDirectories))
                    {
                        var key = ChargeLibrary.Normalise(Path.GetFileNameWithoutExtension(f));
                        if (!_index.ContainsKey(key)) _index[key] = f;
                    }
            }
            if (!_index.TryGetValue(normalisedName, out var path)) return false;
            try
            {
                mask = Png.LoadMask(File.ReadAllBytes(path));
                return true;
            }
            catch (System.Exception)
            {
                return false;
            }
        }
    }
}
