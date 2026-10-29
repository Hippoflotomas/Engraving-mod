using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;

namespace Engraving.Carving
{
    /// <summary>
    /// The carving tool. Carving is free; each carving (or change to one) wears the chisel down.
    /// For now it's the flint knife's model under a new name; Hippo's own model replaces it later.
    /// </summary>
    internal static class Chisel
    {
        public const string PrefabName = "EngravingChisel";
        public const string Token = "$item_engraving_chisel";

        public static void Register()
        {
            PrefabManager.OnVanillaPrefabsAvailable += Add;
        }

        static void Add()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= Add;
            var config = new ItemConfig
            {
                Name = Token,
                Description = Token + "_desc",
                CraftingStation = CraftingStations.Workbench,
                Requirements = new[]
                {
                    new RequirementConfig("Wood", 2, 1, true),
                    new RequirementConfig("Flint", 4, 2, true),
                },
            };
            var item = new CustomItem(PrefabName, "KnifeFlint", config);
            var shared = item.ItemDrop.m_itemData.m_shared;
            shared.m_maxDurability = EngravingPlugin.ChiselDurability.Value;
            shared.m_durabilityPerLevel = EngravingPlugin.ChiselDurability.Value * 0.5f;
            shared.m_useDurability = true;
            shared.m_canBeReparied = true;
            shared.m_maxQuality = 3;
            ItemManager.Instance.AddItem(item);

            EngravingPlugin.Localization.AddTranslation("English", new System.Collections.Generic.Dictionary<string, string>
            {
                ["item_engraving_chisel"] = "Chisel",
                ["item_engraving_chisel_desc"] = "For carving engravings into walls. Equip it and press E on a wall.",
            });
        }

        public static ItemDrop.ItemData Equipped(Player p)
        {
            var item = p.GetRightItem();
            return item != null && item.m_shared.m_name == Token ? item : null;
        }

        public static bool IsEquipped(Player p) => Equipped(p) != null;

        /// <summary>Wears the chisel down for one carving. False (and a message) if it's too blunt.</summary>
        public static bool Wear(Player p)
        {
            var item = Equipped(p);
            if (item == null) return false;
            float cost = EngravingPlugin.ChiselWearPerCarving.Value;
            if (item.m_durability < cost)
            {
                p.Message(MessageHud.MessageType.Center, "Your chisel is too blunt. Repair it at a workbench.");
                return false;
            }
            item.m_durability -= cost;
            return true;
        }
    }
}
