using System;
using HarmonyLib;

namespace UnboundPotential
{
    // Presentation-only patch. Never changes the crafting outcome or inventory.
    // The vanilla failure notification is sent before the replacement item is added.
    internal static class RefinementMessages
    {
        [HarmonyPatch(typeof(Player), nameof(Player.Message))]
        private static class FailureMessagePatch
        {
            private static void Prefix(ref string msg, MessageHud.MessageType type)
            {
                if (!RefinementPatches.PreventBreakage.Value || type != MessageHud.MessageType.Center || string.IsNullOrEmpty(msg))
                    return;

                // The supplied game build sometimes returns the unresolved localization
                // token in square brackets. Do not intercept unrelated messages.
                if (msg.IndexOf("msg_upgrader_failed", StringComparison.OrdinalIgnoreCase) < 0)
                    return;

                InventoryGui gui = InventoryGui.instance;
                if (gui == null || gui.m_craftUpgradeItem == null || Player.m_localPlayer == null)
                    return;

                CraftingStation station = Player.m_localPlayer.GetCurrentCraftingStation();
                if (station == null || !station.m_upgrader)
                    return;

                ItemDrop.ItemData item = gui.m_craftUpgradeItem;
                int oldQuality = item.m_quality;
                int newQuality = Math.Max(1, oldQuality - 1);
                string name = item.m_shared.m_name;
                // MessageHud localizes $-prefixed item names when it displays this text.
                msg = newQuality == oldQuality
                    ? $"{name} refinement failed! Quality remains at level {newQuality}."
                    : $"{name} refinement failed! Quality reduced to level {newQuality}.";
            }
        }
    }
}
