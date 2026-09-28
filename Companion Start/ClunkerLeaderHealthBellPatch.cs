using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace CompanionStart
{
    // The "+6 leader health" bell runs the "Add Health (6)" CardScriptAddRandomHealth on the leader,
    // and that bails out on cards without hasHealth. Clunker leaders only have Scrap, so the bell
    // silently did nothing for them. Instead, give such a leader +1 Scrap. Matched by asset name so
    // other health scripts (e.g. the random rolls in "Boost Leader Stats") are left alone.
    [HarmonyPatch(typeof(CardScriptAddRandomHealth), nameof(CardScriptAddRandomHealth.Run))]
    internal static class ClunkerLeaderHealthBellPatch
    {
        private const string BellScriptName = "Add Health (6)";
        private const int ScrapToAdd = 1;

        private static bool Prefix(CardScriptAddRandomHealth __instance, CardData target)
        {
            if (!__instance.name.StartsWith(BellScriptName)
                || target == null || target.hasHealth
                || target.cardType?.name != CompanionStart.ClunkerLeaderCardTypeName
                || target.startWithEffects == null || !target.startWithEffects.Any(IsScrap))
            {
                return true;
            }

            // Rebuild the array with fresh stack objects rather than bumping count in place, in case
            // the stacks are shared with the source CardData asset (which would leak into later runs).
            target.startWithEffects = target.startWithEffects
                .Select(stacks => new CardData.StatusEffectStacks(
                    stacks.data, IsScrap(stacks) ? stacks.count + ScrapToAdd : stacks.count))
                .ToArray();
            Debug.Log($"CompanionStart: leader health bell gave {target.name} +{ScrapToAdd} scrap instead of health");
            return false;
        }

        private static bool IsScrap(CardData.StatusEffectStacks stacks) => stacks?.data != null && stacks.data.type == "scrap";
    }
}
