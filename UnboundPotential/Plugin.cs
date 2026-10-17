using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using ServerSync;
using UnityEngine;

namespace UnboundPotential
{
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.Jello.UnboundPotential";
        public const string Name = "Unbound Potential";
        public const string Version = "0.3.0";
        private const string PrefabName = "UnboundPotential_Forge";
        private const string OriginalPrefab = "UpgradeStation";

        private static Plugin? _instance;
        private Harmony? _harmony;
        // The server is authoritative for gameplay configuration.
        private readonly ConfigSync _configSync = new ConfigSync(Guid)
        {
            DisplayName = Name,
            CurrentVersion = Version,
            MinimumRequiredVersion = Version,
            ModRequired = true
        };
        private ConfigEntry<bool> _lockConfiguration = null!;
        private ConfigEntry<bool> _enabled = null!;
        private ConfigEntry<string> _requirements = null!;
        private ConfigEntry<string> _requiredStation = null!;
        private ZNetScene? _registeredScene;
        private GameObject? _templateRoot;
        private GameObject? _prefab;
        private PieceTable? _hammerTable;

        private void Awake()
        {
            _instance = this;
            _enabled = Config.Bind("Forge", "Enabled", true, "Register the buildable Forge of Potential.");
            _requirements = Config.Bind("Forge", "Requirements", "Bronze:10:True,FineWood:20:True,SurtlingCore:5:True,Stone:20:True", "ItemPrefab:Amount:Recover, comma separated. Restart the world after changes.");
            _requiredStation = Config.Bind("Forge", "RequiredStation", "forge", "Required crafting station prefab; blank for none. Restart the world after changes.");
            _requirements.SettingChanged += (_, _) => RefreshForgeSettings();
            _requiredStation.SettingChanged += (_, _) => RefreshForgeSettings();
            RefinementPatches.OverrideChances = Config.Bind("Refinement", "OverrideChances", false,
                "False preserves each vanilla idol's own success and break chances. True uses the percentage settings below.");
            RefinementPatches.PreventBreakage = Config.Bind("Refinement", "PreventBreakage", false,
                "Prevent item destruction. Failed refinement reduces quality by one, but never below 1.");
            RefinementPatches.SuccessChance = Config.Bind("Refinement", "SuccessChance", 65f,
                new ConfigDescription("Success percentage when OverrideChances is enabled.", new AcceptableValueRange<float>(0f, 100f)));
            RefinementPatches.BreakChance = Config.Bind("Refinement", "BreakChance", 10f,
                new ConfigDescription("Break percentage when OverrideChances is enabled and PreventBreakage is false. Clamped to remaining probability.", new AcceptableValueRange<float>(0f, 100f)));
            _lockConfiguration = Config.Bind("General", "LockConfiguration", true,
                "Lock synchronized gameplay settings to the server's values.");
            _configSync.AddLockingConfigEntry(_lockConfiguration);
            _configSync.AddConfigEntry(RefinementPatches.OverrideChances).SynchronizedConfig = true;
            _configSync.AddConfigEntry(RefinementPatches.PreventBreakage).SynchronizedConfig = true;
            _configSync.AddConfigEntry(RefinementPatches.SuccessChance).SynchronizedConfig = true;
            _configSync.AddConfigEntry(RefinementPatches.BreakChance).SynchronizedConfig = true;
            _configSync.AddConfigEntry(_enabled).SynchronizedConfig = true;
            _configSync.AddConfigEntry(_requiredStation).SynchronizedConfig = true;
            _configSync.AddConfigEntry(_requirements).SynchronizedConfig = true;
            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            Logger.LogInfo(Name + " " + Version + " loaded");
        }
        
        private void RefreshForgeSettings()
        {
            if (_prefab == null || ObjectDB.instance == null ||
                ZNetScene.instance == null)
                return;

            if (!TryRequirements(ObjectDB.instance, out Piece.Requirement[] resources))
                return;

            CraftingStation? station = null;

            if (!string.IsNullOrWhiteSpace(_requiredStation.Value))
            {
                GameObject? stationPrefab =
                    ZNetScene.instance.GetPrefab(_requiredStation.Value.Trim());

                station = stationPrefab != null
                    ? stationPrefab.GetComponent<CraftingStation>()
                    : null;

                if (station == null)
                {
                    Logger.LogError(
                        "Cannot refresh Forge: unknown station " + _requiredStation.Value);
                    return;
                }
            }

            Piece? piece = _prefab.GetComponent<Piece>();
            if (piece == null) return;

            piece.m_resources = resources;
            piece.m_craftingStation = station;

            Logger.LogInfo("Updated Forge construction settings from configuration.");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            Cleanup();
            if (_instance == this) _instance = null;
        }

        private void Cleanup()
        {
            if (_hammerTable != null && _prefab != null)
                _hammerTable.m_pieces.Remove(_prefab);
            if (_registeredScene != null && _prefab != null)
            {
                _registeredScene.m_prefabs.Remove(_prefab);
                int hash = PrefabName.GetStableHashCode();
                if (_registeredScene.m_namedPrefabs.TryGetValue(hash, out GameObject? registered) && registered == _prefab)
                    _registeredScene.m_namedPrefabs.Remove(hash);
            }
            if (_templateRoot != null) Destroy(_templateRoot);
            _registeredScene = null;
            _templateRoot = null;
            _prefab = null;
            _hammerTable = null;
        }

        private void TryRegister()
        {
            if (!_enabled.Value) return;
            ZNetScene? scene = ZNetScene.instance;
            ObjectDB? db = ObjectDB.instance;
            if (scene == null || db == null) return;

            if (_registeredScene != null && _registeredScene != scene) Cleanup();

            GameObject? hammer = db.GetItemPrefab("Hammer");
            ItemDrop? hammerItem = hammer != null ? hammer.GetComponent<ItemDrop>() : null;
            PieceTable? table = hammerItem != null ? hammerItem.m_itemData.m_shared.m_buildPieces : null;
            if (table == null) return;

            if (_prefab != null)
            {
                AttachHammer(table, db);
                return;
            }

            if (scene.GetPrefab(PrefabName) != null)
            {
                Logger.LogWarning("Another prefab already uses " + PrefabName + "; skipping registration.");
                return;
            }

            GameObject? source = scene.GetPrefab(OriginalPrefab);
            if (source == null) return;

            Piece.Requirement[] resources;
            if (!TryRequirements(db, out resources)) return;

            CraftingStation? station = null;
            if (!string.IsNullOrWhiteSpace(_requiredStation.Value))
            {
                GameObject? stationPrefab = scene.GetPrefab(_requiredStation.Value.Trim());
                station = stationPrefab != null ? stationPrefab.GetComponent<CraftingStation>() : null;
                if (station == null)
                {
                    Logger.LogError("Unknown construction station: " + _requiredStation.Value);
                    return;
                }
            }

            // An inactive parent prevents ZNetView.Awake/Start and Piece.Awake on the template.
            // The child retains activeSelf=true so a normal scene Instantiate creates an active piece.
            GameObject root = new GameObject("UnboundPotential_PrefabStorage");
            root.SetActive(false);
            root.transform.SetParent(scene.transform, false);
            GameObject? clone = null;
            try
            {
                clone = Instantiate(source, root.transform, false);
                clone.name = PrefabName;
                clone.SetActive(true); // Still inactiveInHierarchy because root is inactive.

                foreach (GuidePoint guide in clone.GetComponentsInChildren<GuidePoint>(true))
                {
                    // Do not destroy the entire object: it could contain unrelated vanilla components.
                    guide.enabled = false;
                }

                Piece piece = clone.GetComponent<Piece>();
                if (piece == null) piece = clone.AddComponent<Piece>();
                CraftingStation upgrader = clone.GetComponent<CraftingStation>();
                if (upgrader == null || !upgrader.m_upgrader)
                {
                    Logger.LogError("UpgradeStation has no functional upgrader; registration aborted.");
                    Destroy(root);
                    return;
                }

                piece.m_name = "Forge of Potential";
                piece.m_description = "Refine equipment using idols.";
                piece.m_icon = upgrader.m_icon;
                piece.m_category = Piece.PieceCategory.Crafting;
                piece.m_resources = resources;
                piece.m_craftingStation = station;
                piece.m_canBeRemoved = true;
                piece.m_canRotate = true;
                piece.m_enabled = true;

                int hash = PrefabName.GetStableHashCode();
                if (scene.m_namedPrefabs.ContainsKey(hash))
                {
                    Logger.LogError("Prefab hash collision: " + PrefabName);
                    Destroy(root);
                    return;
                }
                scene.m_prefabs.Add(clone);
                scene.m_namedPrefabs.Add(hash, clone);
                _registeredScene = scene;
                _templateRoot = root;
                _prefab = clone;
                AttachHammer(table, db);
                Logger.LogInfo("Registered " + PrefabName + "; template inactive=" + !clone.activeInHierarchy +
                               ", hammer contains prefab=" + table.m_pieces.Contains(clone) +
                               ", hammer pieces=" + table.m_pieces.Count);
            }
            catch (Exception e)
            {
                Logger.LogError("Forge registration failed: " + e);
                Cleanup();
                if (root != null) Destroy(root);
            }
        }

        private void AttachHammer(PieceTable table, ObjectDB db)
        {
            if (_prefab == null) return;
            if (_hammerTable != null && _hammerTable != table)
                _hammerTable.m_pieces.Remove(_prefab);
            _hammerTable = table;
            if (!table.m_pieces.Contains(_prefab))
            {
                table.m_pieces.Add(_prefab);
                db.m_buildPieces = null;
                Logger.LogInfo("Attached " + PrefabName + " to Hammer; pieces=" + table.m_pieces.Count);
            }
        }

        private bool TryRequirements(ObjectDB db, out Piece.Requirement[] result)
        {
            result = Array.Empty<Piece.Requirement>();
            var parsed = new List<Piece.Requirement>();
            foreach (string raw in _requirements.Value.Split(','))
            {
                string[] parts = raw.Trim().Split(':');
                if (parts.Length != 3 || !int.TryParse(parts[1], out int count) || count <= 0 || !bool.TryParse(parts[2], out bool recover))
                {
                    Logger.LogError("Invalid forge requirement: " + raw);
                    return false;
                }
                GameObject? prefab = db.GetItemPrefab(parts[0].Trim());
                ItemDrop? item = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                if (item == null)
                {
                    Logger.LogError("Unknown item prefab in forge requirements: " + parts[0]);
                    return false;
                }
                parsed.Add(new Piece.Requirement { m_resItem = item, m_amount = count, m_recover = recover });
            }
            if (parsed.Count == 0) return false;
            result = parsed.ToArray();
            return true;
        }

        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Awake))]
        private static class SceneAwakePatch
        {
            private static void Postfix() { _instance?.TryRegister(); }
        }

        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.OnDestroy))]
        private static class SceneDestroyPatch
        {
            private static void Prefix(ZNetScene __instance)
            {
                if (_instance != null && _instance._registeredScene == __instance) _instance.Cleanup();
            }
        }

        [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.UpdateRegisters))]
        private static class DatabaseRegistersPatch
        {
            private static void Postfix() { _instance?.TryRegister(); }
        }
    }
}
