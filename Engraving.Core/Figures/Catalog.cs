using System;
using System.Collections.Generic;
using System.Linq;

namespace Engraving.Core.Figures
{
    /// <summary>What a figure name asks for: which rig draws it, and which body (a humanoid kind, a quadruped species).</summary>
    public sealed class FigureType
    {
        public string Rig;
        /// <summary>The body: a quadruped species, or a humanoid body (viking, troll, ...).</summary>
        public Species Species;
        /// <summary>A trophy or an altar: the creature it belongs to.</summary>
        public FigureType Base;
        public string Id => Species.Name;
        /// <summary>False for things that don't take poses (trees, rocks, trophies, altars).</summary>
        public bool Posable => Rig != "scenery" && Rig != "trophy" && Rig != "altar";
    }

    /// <summary>The figure names a carving can use, and which rig draws each: humanoid, quadruped, flyer, serpent, blob, insect.</summary>
    public static class Catalog
    {
        public static FigureType Find(string name)
        {
            string n = Norm(name);
            if (n.Length == 0) return null;
            var body = Humanoid.BodyFor(n);
            if (body != null) return new FigureType { Rig = "humanoid", Species = body };
            var sp = SpeciesLibrary.Find(n);
            if (sp != null) return new FigureType { Rig = "quadruped", Species = sp };
            if ((sp = Flyer.Bodies.Find(n)) != null) return new FigureType { Rig = "flyer", Species = sp };
            if ((sp = Serpent.Bodies.Find(n)) != null) return new FigureType { Rig = "serpent", Species = sp };
            if ((sp = Blob.Bodies.Find(n)) != null) return new FigureType { Rig = "blob", Species = sp };
            if ((sp = Insect.Bodies.Find(n)) != null) return new FigureType { Rig = "insect", Species = sp };
            if ((sp = Scenery.Find(n)) != null) return new FigureType { Rig = "scenery", Species = sp };
            // "a troll trophy", "the trophy of the elder", "eikthyr's altar", "a sacrificial stone of bonemass"
            foreach (var (word, rig) in Wrappers)
            {
                string rest = null;
                if (n.StartsWith(word) && n.Length > word.Length) rest = n.Substring(word.Length);
                else if (n.EndsWith(word) && n.Length > word.Length) rest = n.Substring(0, n.Length - word.Length);
                if (rest == null) continue;
                if (rest.StartsWith("of")) rest = rest.Substring(2);
                if (rest.StartsWith("the")) rest = rest.Substring(3);
                if (rest.EndsWith("s") && Find(rest) == null) rest = rest.Substring(0, rest.Length - 1);   // "eikthyr's" lost its apostrophe
                var of = Find(rest);
                if (of == null || !of.Posable) continue;
                return new FigureType { Rig = rig, Species = of.Species, Base = of };
            }
            return null;
        }

        static readonly (string word, string rig)[] Wrappers =
        {
            ("trophies", "trophy"), ("trophy", "trophy"), ("head", "trophy"),
            ("sacrificialstone", "altar"), ("offeringstone", "altar"), ("altar", "altar"), ("shrine", "altar"),
        };

        public static bool IsFigure(string name) => Find(name) != null;

        static string Norm(string s) => new string((s ?? "").ToLowerInvariant().Where(char.IsLetter).ToArray());
    }
}
