using System.Collections.Generic;
using System.Linq;
using Engraving.Core.Language;

namespace Engraving.Carving
{
    /// <summary>
    /// What a scene may show, read from the carver (Hippo, 2026-10-30): every trophy they have ever taken (the
    /// compendium, not what's carried), the materials they know, and the best weapon of each kind in their inventory
    /// (most damage at its quality; the one equipped wins a tie).
    /// </summary>
    public static class SceneProgressReader
    {
        static readonly HashSet<string> Unmatched = new HashSet<string>();

        public static SceneProgress Read(Player player)
        {
            if (player == null) return null;
            var p = new SceneProgress();
            foreach (var t in player.m_trophies)
            {
                var figure = Scene.FigureForTrophy(t);
                if (figure != null) p.Trophies.Add(figure);
                else if (Unmatched.Add(t)) Jotunn.Logger.LogInfo($"[Engraving] No figure for trophy {t}; scenes leave it out");
            }
            foreach (var m in player.m_knownMaterial)
            {
                var material = Scene.MaterialFor(m);
                if (material != null) p.Materials.Add(material);
            }
            var inventory = player.GetInventory();
            if (inventory != null)
                foreach (var item in inventory.GetAllItems())
                {
                    if (item?.m_shared == null) continue;
                    if (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield) { p.Shield = true; continue; }
                    string kind = KindOf(item);
                    if (kind == null) continue;
                    float score = item.GetDamage().GetTotalDamage() + (item.m_equipped ? 0.01f : 0);
                    if (!p.Weapons.TryGetValue(kind, out var best) || score > best) p.Weapons[kind] = score;
                }
            return p;
        }

        /// <summary>The figure prop that draws an item, by the skill it uses.</summary>
        static string KindOf(ItemDrop.ItemData item)
        {
            string name = (item.m_shared.m_name ?? "").ToLowerInvariant();
            switch (item.m_shared.m_skillType)
            {
                case Skills.SkillType.Swords: case Skills.SkillType.Knives: return "sword";
                case Skills.SkillType.Axes: return "axe";
                case Skills.SkillType.Clubs: return name.Contains("club") ? "club" : "hammer";
                case Skills.SkillType.Spears: case Skills.SkillType.Polearms: return "spear";
                case Skills.SkillType.Bows: case Skills.SkillType.Crossbows: return "bow";
                case Skills.SkillType.Pickaxes: return "pickaxe";
                case Skills.SkillType.ElementalMagic: case Skills.SkillType.BloodMagic: return "staff";
            }
            return null;
        }

        /// <summary>The script with its scenes filled in from the local player (unchanged if there's no player or scene).</summary>
        public static string Fill(string script, List<string> problems)
        {
            if (string.IsNullOrEmpty(script) || script.IndexOf("scene", System.StringComparison.OrdinalIgnoreCase) < 0) return script;
            return Scene.Fill(script, Read(Player.m_localPlayer), problems);
        }
    }
}
