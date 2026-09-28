using HarmonyLib;

namespace CompanionStart
{
    // The deck viewer picks its units grid purely by cardType.tag == "Friendly". Clunker leaders
    // inherit the vanilla Clunker type's "Trap" tag, so they were listed under Items. Retagging the
    // type would also change gameplay that keys off the tag (card-type triggers, effect copying),
    // so only the grid choice is redirected here.
    [HarmonyPatch(typeof(DeckDisplayGroup), nameof(DeckDisplayGroup.GetGrid), typeof(CardData))]
    internal static class ClunkerLeaderDeckGridPatch
    {
        private static void Postfix(DeckDisplayGroup __instance, CardData cardData, ref CardContainerGrid __result)
        {
            if (__instance.grids.Length > 1 && cardData?.cardType?.name == CompanionStart.ClunkerLeaderCardTypeName)
            {
                __result = __instance.grids[1];
            }
        }
    }
}
