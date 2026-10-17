using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace UnboundPotential
{
    // The vanilla recipe, consumption, item return and break-refund paths remain untouched.
    // Only the two probability reads and the failed-upgrade quality calculation are intercepted.
    internal static class RefinementPatches
    {
        internal static ConfigEntry<bool> OverrideChances = null!;
        internal static ConfigEntry<bool> PreventBreakage = null!;
        internal static ConfigEntry<float> SuccessChance = null!;
        internal static ConfigEntry<float> BreakChance = null!;

        private static float Success(ItemDrop.ItemData.SharedData idol)
        {
            if (!OverrideChances.Value && !PreventBreakage.Value) return idol.m_upgradeChance;
            float success = OverrideChances.Value ? SuccessChance.Value / 100f : idol.m_upgradeChance;
            return Mathf.Clamp01(success);
        }

        private static float Break(ItemDrop.ItemData.SharedData idol)
        {
            if (PreventBreakage.Value) return 0f;
            if (!OverrideChances.Value) return idol.m_breakChance;
            return Mathf.Clamp(BreakChance.Value / 100f, 0f, 1f - Success(idol));
        }

        private static int FailedQuality(int targetQuality, int reduction)
        {
            int result = targetQuality - reduction;
            return PreventBreakage.Value ? Math.Max(1, result) : result;
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.DoCrafting))]
        private static class CraftingPatch
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var code = new List<CodeInstruction>(instructions);
                var successField = AccessTools.Field(typeof(ItemDrop.ItemData.SharedData), nameof(ItemDrop.ItemData.SharedData.m_upgradeChance));
                var breakField = AccessTools.Field(typeof(ItemDrop.ItemData.SharedData), nameof(ItemDrop.ItemData.SharedData.m_breakChance));
                var successMethod = AccessTools.Method(typeof(RefinementPatches), nameof(Success));
                var breakMethod = AccessTools.Method(typeof(RefinementPatches), nameof(Break));
                var qualityMethod = AccessTools.Method(typeof(RefinementPatches), nameof(FailedQuality));
                var addItem = AccessTools.Method(typeof(Inventory), "AddItem", new[] { typeof(string), typeof(int), typeof(int), typeof(int), typeof(long), typeof(string), typeof(Vector2i), typeof(bool), typeof(bool), typeof(bool) });
                int successCount = 0, breakCount = 0, qualityCount = 0;
                for (int i = 0; i < code.Count; i++)
                {
                    if (code[i].LoadsField(successField)) { code[i].opcode = OpCodes.Call; code[i].operand = successMethod; successCount++; }
                    else if (code[i].LoadsField(breakField)) { code[i].opcode = OpCodes.Call; code[i].operand = breakMethod; breakCount++; }
                }
                // Identify the failure-only AddItem call by the quality argument sequence:
                // ldloc targetQuality, ldc.i4.2, sub, ldloc variant, ... AddItem.
                // The exact local load opcode is compiler dependent; inspect only a narrow window
                // preceding each AddItem and require exactly one match.
                if (addItem != null)
                {
                    for (int i = 2; i < code.Count; i++)
                    {
                        if (code[i].opcode != OpCodes.Sub || code[i - 1].opcode != OpCodes.Ldc_I4_2) continue;
                        bool hasAddItem = false;
                        for (int j = i + 1; j < Math.Min(i + 20, code.Count); j++)
                        {
                            if (code[j].Calls(addItem)) { hasAddItem = true; break; }
                            if (code[j].opcode == OpCodes.Ret || code[j].opcode == OpCodes.Br) break;
                        }
                        if (!hasAddItem) continue;
                        code[i].opcode = OpCodes.Call;
                        code[i].operand = qualityMethod;
                        qualityCount++;
                    }
                }
                if (successCount != 1 || breakCount != 1 || qualityCount != 1)
                    throw new InvalidOperationException($"Refinement IL mismatch: success={successCount}, break={breakCount}, failure quality={qualityCount}. Check game version; patch not applied.");
                return code;
            }
        }
    }
}
