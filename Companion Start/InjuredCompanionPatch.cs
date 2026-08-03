using HarmonyLib;

namespace CompanionStart
{
    // After a loss, InjuredCompanionEventSystem can place a map node mid-run offering back one
    // companion from that run's saved deck. Its eligibility check (IsEligible) resolves the card
    // via CardSaveData.Peek(), which - unlike CardSaveData.Load() - looks the card up by name
    // straight from the vanilla CardData asset and never applies the "OverrideCardType" customData
    // correction CompanionStart.cs stamps onto leader clones (see BuildCompanionClass). So a former
    // leader's underlying companion name still reads as cardType.name == "Friendly" here and
    // qualifies as an "injured companion" to offer back. Excluding anything carrying that same
    // OverrideCardType marker closes the gap without touching vanilla's save-loading logic.
    [HarmonyPatch(typeof(InjuredCompanionEventSystem), "IsEligible")]
    internal static class InjuredCompanionPatch
    {
        private static void Postfix(CardSaveData card, ref bool __result)
        {
            if (!__result || card.customData == null)
            {
                return;
            }

            if (card.customData.TryGetValue("OverrideCardType", out object overrideType) &&
                string.Equals(overrideType?.ToString(), CompanionStart.CompanionLeaderCardTypeName))
            {
                __result = false;
            }
        }
    }
}
