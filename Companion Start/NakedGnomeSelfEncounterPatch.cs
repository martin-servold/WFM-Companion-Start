using System.Linq;
using HarmonyLib;

namespace CompanionStart
{
    // Naked Gnome is recruited by sparing the enemy card "NakedGnome" in the run's first battle.
    // If the player is already playing as our companion-leader clone of his friendly form, that
    // same battle would otherwise still spawn "NakedGnome" as an enemy - forcing a fight against
    // (and a choice to spare) a copy of your own leader. Strip him out of any wave that's built
    // while that's the case, regardless of which node the wave belongs to.
    [HarmonyPatch(typeof(BattleWaveManager.Wave), MethodType.Constructor, new[] { typeof(BattleWaveManager.WaveData) })]
    internal static class NakedGnomeSelfEncounterPatch
    {
        private const string NakedGnomeEnemyName = "NakedGnome";
        private const string NakedGnomeCompanionName = "NakedGnomeFriendly";

        private static void Postfix(BattleWaveManager.Wave __instance)
        {
            CardData champion = References.PlayerData?.inventory?.deck
                ?.FirstOrDefault(card => card.cardType != null && card.cardType.miniboss);

            if (champion != null && champion.name == NakedGnomeCompanionName)
            {
                __instance.units.RemoveAll(card => card.name == NakedGnomeEnemyName);
            }
        }
    }
}
