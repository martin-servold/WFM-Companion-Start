using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace CompanionStart
{
    // Our companion-leader clones use a distinct CardType (not "Friendly"), so vanilla's own
    // "don't re-offer a companion you already have" dedup in RemoveCardsFromStartingDeck - which
    // only checks cardType.name == "Friendly" - doesn't recognize them, and would let the same
    // companion be offered again later as a normal reward pick during the run.
    [HarmonyPatch(typeof(CharacterRewards), nameof(CharacterRewards.RemoveCardsFromStartingDeck))]
    internal static class RemoveDuplicateLeaderRewardsPatch
    {
        private static void Postfix(CharacterRewards __instance)
        {
            List<DataFile> unitsPool = __instance.GetItemsInPool("Units");
            if (unitsPool == null)
            {
                return;
            }

            HashSet<string> leaderNames = new HashSet<string>(
                References.PlayerData.inventory.deck
                    .Where(card => card.cardType.name == CompanionStart.CompanionLeaderCardTypeName)
                    .Select(card => card.name));

            if (leaderNames.Count > 0)
            {
                unitsPool.RemoveAll(item => leaderNames.Contains(item.name));
            }
        }
    }
}
