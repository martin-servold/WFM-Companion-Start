using System.Linq;
using HarmonyLib;

namespace CompanionStart
{
    // Naked Gnome is recruited by sparing the enemy card "NakedGnome" in the run's first battle.
    // If the player is already playing as him - either our companion-leader clone of his friendly
    // form, or (since the monster clans let you lead as the raw enemy card directly) the enemy
    // card itself - that same battle would otherwise still spawn "NakedGnome" as an enemy, forcing
    // a fight against (and a choice to spare) a copy of your own leader. Strip him out of any wave
    // that's built while that's the case, regardless of which node the wave belongs to.
    [HarmonyPatch(typeof(BattleWaveManager.Wave), MethodType.Constructor, new[] { typeof(BattleWaveManager.WaveData) })]
    internal static class NakedGnomeSelfEncounterPatch
    {
        private const string NakedGnomeEnemyName = "NakedGnome";
        private const string NakedGnomeCompanionName = "NakedGnomeFriendly";

        private static void Postfix(BattleWaveManager.Wave __instance)
        {
            CardData champion = References.PlayerData?.inventory?.deck
                ?.FirstOrDefault(card => card.cardType != null && card.cardType.miniboss);

            bool playingAsNakedGnome = champion != null &&
                (champion.name == NakedGnomeCompanionName || champion.name == NakedGnomeEnemyName);

            if (playingAsNakedGnome)
            {
                __instance.units.RemoveAll(card => card.name == NakedGnomeEnemyName);
            }
        }
    }
}
