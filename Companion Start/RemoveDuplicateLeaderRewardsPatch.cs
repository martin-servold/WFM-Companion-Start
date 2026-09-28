using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace CompanionStart
{
    // Our leader clones use distinct CardTypes, so vanilla's own "don't re-offer a card you already
    // have" dedup in RemoveCardsFromStartingDeck - which only checks cardType.name against
    // "Friendly" (Units pool) and "Item"/"Clunker" (Items pool) - doesn't recognize them, and would
    // let the same companion or clunker be offered again later as a normal reward pick.
    [HarmonyPatch(typeof(CharacterRewards), nameof(CharacterRewards.RemoveCardsFromStartingDeck))]
    internal static class RemoveDuplicateLeaderRewardsPatch
    {
        private static void Postfix(CharacterRewards __instance)
        {
            RemoveLeadersFromPool(__instance, "Units", CompanionStart.CompanionLeaderCardTypeName);
            RemoveLeadersFromPool(__instance, "Items", CompanionStart.ClunkerLeaderCardTypeName);
        }

        private static void RemoveLeadersFromPool(CharacterRewards rewards, string poolName, string leaderCardTypeName)
        {
            List<DataFile> pool = rewards.GetItemsInPool(poolName);
            if (pool == null)
            {
                return;
            }

            HashSet<string> leaderNames = new HashSet<string>(
                References.PlayerData.inventory.deck
                    .Where(card => card.cardType.name == leaderCardTypeName)
                    .Select(card => card.name));

            if (leaderNames.Count > 0)
            {
                pool.RemoveAll(item => leaderNames.Contains(item.name));
            }
        }
    }
}
