using System;
using System.Collections.Generic;
using System.Linq;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace Engraving.Carving
{
    /// <summary>
    /// The carving tool. Carving is free; each carving (or change to one) wears the chisel down.
    /// It's the flint knife underneath (equip, swing and durability behave the same), wearing Hippo's
    /// model (Assets/Chisel.fbx → Art/Chisel.hmesh) in place of the knife's, on the ground and in the hand.
    /// </summary>
    internal static class Chisel
    {
        public const string PrefabName = "EngravingChisel";
        public const string Token = "$item_engraving_chisel";
        const string BasePrefab = "KnifeFlint";
        const string ModelResource = "Engraving.Chisel.hmesh";

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
            var item = new CustomItem(PrefabName, BasePrefab, config);
            var shared = item.ItemDrop.m_itemData.m_shared;
            shared.m_maxDurability = EngravingPlugin.ChiselDurability.Value;
            shared.m_durabilityPerLevel = EngravingPlugin.ChiselDurability.Value * 0.5f;
            shared.m_useDurability = true;
            shared.m_canBeReparied = true;
            shared.m_maxQuality = 3;

            try
            {
                Dress(item.ItemPrefab);
                var icon = Icon(item.ItemPrefab);
                if (icon != null) shared.m_icons = new[] { icon };
            }
            catch (Exception e)
            {
                Jotunn.Logger.LogWarning($"[Engraving] Chisel model couldn't be fitted ({e.Message}); it keeps the flint knife's looks.");
            }
            ItemManager.Instance.AddItem(item);

            EngravingPlugin.Localization.AddTranslation("English", new Dictionary<string, string>
            {
                ["item_engraving_chisel"] = "Chisel",
                ["item_engraving_chisel_desc"] = "For carving engravings into walls. Equip it and press E on a wall.",
            });
        }

        // ------------------------------------------------------------------ the model

        /// <summary>Puts the chisel model in place of the knife's, both where it lies on the ground and in the hand.</summary>
        static void Dress(GameObject prefab)
        {
            var parts = HMesh.Load(ModelResource);
            var materials = Materials();
            var attach = prefab.transform.Find("attach");

            // in the hand: the knife's held model lives under "attach", whose origin is the grip
            if (attach != null) Replace(attach, null, parts, materials, true);
            // on the ground: everything else on the item
            Replace(prefab.transform, attach, parts, materials, false);
        }

        /// <summary>
        /// Hides the renderers under <paramref name="slot"/> (skipping <paramref name="skip"/>) and adds the chisel
        /// along their longest axis. Held: the chisel's handle centre sits on the slot's origin (the hand) and the
        /// blade points the way the knife's did. Dropped: it lies where the knife lay.
        /// </summary>
        static void Replace(Transform slot, Transform skip, List<HMesh.Part> parts, Dictionary<string, Material> materials, bool held)
        {
            var renderers = slot.GetComponentsInChildren<Renderer>(true)
                .Where(r => skip == null || !r.transform.IsChildOf(skip))
                .Where(r => r is MeshRenderer || r is SkinnedMeshRenderer)
                .ToList();
            if (renderers.Count == 0) return;

            // the old model's extent in the slot's space
            var toSlot = slot.worldToLocalMatrix;
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            foreach (var r in renderers)
            {
                Mesh mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                var b = mesh.bounds;
                var m = toSlot * r.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(corner);
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                }
            }
            if (float.IsInfinity(min.x)) return;
            Vector3 size = max - min, centre = (min + max) * 0.5f;

            // long axis, pointing towards the blade (further from the grip than the handle is)
            int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
            var dir = Vector3.zero;
            dir[axis] = centre[axis] < 0 ? -1 : 1;

            foreach (var r in renderers)
            {
                var mf = r.GetComponent<MeshFilter>();
                UnityEngine.Object.DestroyImmediate(r);
                if (mf != null) UnityEngine.Object.DestroyImmediate(mf);
            }
            foreach (var lod in slot.GetComponentsInChildren<LODGroup>(true).Where(l => skip == null || !l.transform.IsChildOf(skip)))
                UnityEngine.Object.DestroyImmediate(lod);

            var root = new GameObject("EngravingChisel") { layer = slot.gameObject.layer };
            root.transform.SetParent(slot, false);
            var rot = Quaternion.FromToRotation(Vector3.up, dir);
            root.transform.localRotation = rot;
            // the model's own centre (handle and blade together) is 0.097 m up its length
            var modelCentre = parts.Select(p => p.Mesh.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; }).center;
            root.transform.localPosition = held ? Vector3.zero : centre - rot * modelCentre;
            foreach (var part in parts)
            {
                var go = new GameObject(part.Name) { layer = slot.gameObject.layer };
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = part.Mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial =
                    materials.TryGetValue(part.Name.ToLowerInvariant(), out var mat) ? mat : materials["wood"];
            }
            Jotunn.Logger.LogInfo($"[Engraving] Chisel model fitted to '{slot.name}' ({(held ? "held" : "dropped")}): " +
                                  $"knife was {size.x:0.00}x{size.y:0.00}x{size.z:0.00} m, blade towards {dir}.");
        }

        /// <summary>Vanilla materials by the model's slot names, borrowed as Heraldry Mod does for its table.</summary>
        static Dictionary<string, Material> Materials()
        {
            Material From(string prefabName, string materialName)
            {
                var p = PrefabManager.Instance.GetPrefab(prefabName);
                var all = p != null ? p.GetComponentsInChildren<MeshRenderer>(true).Select(r => r.sharedMaterial).Where(m => m != null).ToList()
                                    : new List<Material>();
                // by name: a piece's first renderer is often its snow layer
                return all.FirstOrDefault(m => string.Equals(m.name, materialName, StringComparison.OrdinalIgnoreCase))
                       ?? all.FirstOrDefault(m => m.name.StartsWith(materialName, StringComparison.OrdinalIgnoreCase))
                       ?? all.FirstOrDefault(m => m.name.IndexOf("snow", StringComparison.OrdinalIgnoreCase) < 0);
            }
            var wood = From("woodwall", "woodwall");
            var metal = From("iron_wall_1x1", "metalwall") ?? wood;
            return new Dictionary<string, Material> { { "wood", wood }, { "metal", metal } };
        }

        static Sprite Icon(GameObject prefab)
        {
            try
            {
                return RenderManager.Instance.Render(new RenderManager.RenderRequest(prefab)
                {
                    Rotation = Quaternion.Euler(20f, 200f, 35f),
                    Width = 128,
                    Height = 128,
                });
            }
            catch (Exception e)
            {
                Jotunn.Logger.LogWarning($"[Engraving] Couldn't render the chisel's icon ({e.Message}); it keeps the knife's.");
                return null;
            }
        }

        // ------------------------------------------------------------------ use

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
