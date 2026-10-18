using System;
using HarmonyLib;

namespace UnboundPotential
{

    internal static class RefinementMessages
    {
        [HarmonyPatch(typeof(Player), nameof(Player.Message))]
        private static class FailureMessagePatch
        {
            private static void Prefix(ref string msg, MessageHud.MessageType type)
            {
                if (!RefinementPatches.PreventBreakage.Value || type != MessageHud.MessageType.Center || string.IsNullOrEmpty(msg))
                    return;


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
                
                msg = newQuality == oldQuality
                    ? $"{name} refinement failed! Quality remains at level {newQuality}."
                    : $"{name} refinement failed! Quality reduced to level {newQuality}.";
            }
        }
    }
}
