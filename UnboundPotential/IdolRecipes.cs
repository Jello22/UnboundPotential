using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using ServerSync;
using UnityEngine;

namespace UnboundPotential
{
    // Recipes only: vanilla idol item prefabs and their upgrader behavior remain untouched.
    internal sealed class IdolRecipes
    {
        private sealed class Entry
        {
            internal string Key = "";
            internal ConfigEntry<bool> Enabled = null!;
            internal ConfigEntry<string> Item = null!;
            internal ConfigEntry<string> Materials = null!;
            internal ConfigEntry<string> Station = null!;
            internal ConfigEntry<int> Level = null!;
            internal Recipe? Recipe;
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private ObjectDB? _db;

        internal IdolRecipes(BaseUnityPlugin plugin, ConfigSync sync)
        {
            // Default item prefab identifiers follow the Valheim 1.0 item catalog.
            // ServerSync synchronizes each recipe setting independently.
            AddTier(plugin, sync, "Wood", 0, "Wood:50,Resin:40", "piece_workbench", 3);
            AddTier(plugin, sync, "Bronze", 1, "Bronze:8,Coal:10", "forge", 2);
            AddTier(plugin, sync, "Iron", 2, "Iron:8,Chain:10", "forge", 4);
            AddTier(plugin, sync, "Silver", 3, "Silver:8,Obsidian:10", "forge", 5);
            AddTier(plugin, sync, "BlackMetal", 4, "BlackMetal:8,LinenThread:10", "forge", 6);
            AddTier(plugin, sync, "BlackMarble", 5, "BlackMarble:10,Sap:15", "blackforge", 1);
            AddTier(plugin, sync, "Flametal", 6, "FlametalNew:8,Ashwood:10", "blackforge", 4);
            AddTier(plugin, sync, "DeepNorth", 7, "Gold:10,Frostwood:10", "blackforge", 6);
        }

        private void AddTier(BaseUnityPlugin plugin, ConfigSync sync, string tier, int index,
            string materials, string station, int level)
        {
            foreach (string type in new[] { "Weapon", "Armor" })
            {
                string section = "Idol - " + tier + " " + type;
                var entry = new Entry { Key = tier + type };
                entry.Enabled = Bind(plugin, sync, section, "Enabled", true, "Enable this crafting recipe.");
                entry.Item = Bind(plugin, sync, section, "ItemPrefab", "Upgrader" + index + type,
                    "Vanilla idol prefab identifier; verify against the installed game assets.");
                entry.Materials = Bind(plugin, sync, section, "Requirements", materials,
                    "ItemPrefab:Amount pairs separated by commas.");
                entry.Station = Bind(plugin, sync, section, "CraftingStation", station,
                    "Vanilla crafting station prefab identifier.");
                entry.Level = Bind(plugin, sync, section, "StationLevel", level,
                    "Minimum crafting station level (1 or higher).");
                entry.Enabled.SettingChanged += (_, _) => Refresh();
                entry.Item.SettingChanged += (_, _) => Refresh();
                entry.Materials.SettingChanged += (_, _) => Refresh();
                entry.Station.SettingChanged += (_, _) => Refresh();
                entry.Level.SettingChanged += (_, _) => Refresh();
                _entries.Add(entry);
            }
        }

        private static ConfigEntry<T> Bind<T>(BaseUnityPlugin plugin, ConfigSync sync,
            string section, string key, T value, string description)
        {
            ConfigEntry<T> config = plugin.Config.Bind(section, key, value, description);
            sync.AddConfigEntry(config).SynchronizedConfig = true;
            return config;
        }

        internal void Refresh()
        {
            ObjectDB? db = ObjectDB.instance;
            ZNetScene? scene = ZNetScene.instance;
            if (db == null || scene == null) return;
            if (_db != null && _db != db) Cleanup();
            _db = db;
            foreach (Entry entry in _entries)
            {
                // Never remove an existing vanilla recipe for the same output.
                if (!TryResolve(db, scene, entry, out ItemDrop? output,
                        out CraftingStation? station, out Piece.Requirement[] requirements))
                {
                    if (entry.Recipe != null) entry.Recipe.m_enabled = false;
                    continue;
                }
                if (entry.Recipe == null)
                {
                    bool vanillaRecipe = false;
                    foreach (Recipe candidate in db.m_recipes)
                    {
                        if (candidate != null && candidate.m_item == output)
                        {
                            vanillaRecipe = true;
                            break;
                        }
                    }
                    if (vanillaRecipe) continue;
                    entry.Recipe = ScriptableObject.CreateInstance<Recipe>();
                    entry.Recipe.name = "UnboundPotential_Recipe_" + entry.Key;
                    db.m_recipes.Add(entry.Recipe);
                }
                Recipe recipe = entry.Recipe;
                recipe.m_item = output;
                recipe.m_amount = 1;
                recipe.m_craftingStation = station;
                recipe.m_minStationLevel = Math.Max(1, entry.Level.Value);
                recipe.m_resources = requirements;
                recipe.m_enabled = entry.Enabled.Value;
            }
        }

        private bool TryResolve(ObjectDB db, ZNetScene scene, Entry entry,
            out ItemDrop? output, out CraftingStation? station, out Piece.Requirement[] resources)
        {
            output = null;
            station = null;
            resources = Array.Empty<Piece.Requirement>();
            if (!entry.Enabled.Value) return false;
            GameObject? item = db.GetItemPrefab(entry.Item.Value.Trim());
            output = item != null ? item.GetComponent<ItemDrop>() : null;
            if (output == null) return false; // Not all tiers may be present in every game build.
            GameObject? stationPrefab = scene.GetPrefab(entry.Station.Value.Trim());
            station = stationPrefab != null ? stationPrefab.GetComponent<CraftingStation>() : null;
            if (station == null) return false;
            var parsed = new List<Piece.Requirement>();
            foreach (string token in entry.Materials.Value.Split(','))
            {
                string[] parts = token.Trim().Split(':');
                if (parts.Length != 2 || !int.TryParse(parts[1], out int count) || count <= 0)
                    return false;
                GameObject? material = db.GetItemPrefab(parts[0].Trim());
                ItemDrop? drop = material != null ? material.GetComponent<ItemDrop>() : null;
                if (drop == null) return false;
                parsed.Add(new Piece.Requirement { m_resItem = drop, m_amount = count, m_amountPerLevel = 0 });
            }
            if (parsed.Count == 0) return false;
            resources = parsed.ToArray();
            return true;
        }

        internal void Cleanup()
        {
            if (_db != null)
                foreach (Entry entry in _entries)
                {
                    if (entry.Recipe == null) continue;
                    _db.m_recipes.Remove(entry.Recipe);
                    UnityEngine.Object.Destroy(entry.Recipe);
                    entry.Recipe = null;
                }
            _db = null;
        }
    }
}
