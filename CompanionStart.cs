using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Deadpan.Enums.Engine.Components.Modding;
using UnityEngine;
using UnityEngine.Pool;

namespace CompanionStart
{
    public class CompanionStart : WildfrostMod
    {
        public CompanionStart(string modDirectory) : base(modDirectory) { }

        public override string GUID => "fuko.wildfrost.companionstart";
        public override string[] Depends => new string[] { "hope.wildfrost.extendedui" };
        public override string Title => "Companion Start";
        public override string Description => "Adds new clans made up entirely of the game's companions as selectable leaders.";

        internal const string NamePrefix = "fuko.wildfrost.companionstart.";
        internal const string CompanionLeaderCardTypeName = NamePrefix + "CompanionLeader";
        private const string CompanionLeaderCrownName = NamePrefix + "CompanionLeaderCrown";
        private const string GameModeName = "GameModeNormal";

        private static readonly string[] SourceClassNames = { "Basic", "Clunk", "Magic" };

        private CardType companionLeaderType;
        private CardUpgradeData companionLeaderCrown;
        private ClassData[] newClasses;
        private ClassData[] originalGameModeClasses;

        protected override void Load()
        {
            base.Load();

            // Companions keep the "Friendly" CardType's own render prefab (so mainSprite still
            // shows) instead of vanilla "Leader"'s round-portrait prefab, which is built for
            // CardScriptLeader's randomized human avatar and has nowhere to display a sprite.
            // Setting miniboss = true is what actually makes References.LeaderData and the drain
            // mechanic recognize this card as the leader - the CardType's name isn't checked there.
            companionLeaderType = Get<CardType>("Friendly").InstantiateKeepName();
            companionLeaderType.name = CompanionLeaderCardTypeName;
            companionLeaderType.miniboss = true;
            AddressableLoader.AddToGroup("CardType", companionLeaderType);

            // CardManager builds its per-CardType render-prefab pool once, at scene start, from
            // whatever CardTypes exist in the "CardType" group at that moment - which happens
            // before this mod's Load() runs. A CardType added afterwards never gets a pool entry
            // of its own, so CardManager.Get() throws KeyNotFoundException the first time it's
            // used. Since our clone shares "Friendly"'s prefab exactly, it's safe to just alias
            // its pool keys onto Friendly's already-built pools instead of constructing new ones.
            AliasCardRenderPool(companionLeaderType.name, "Friendly");

            // Vanilla leaders carry a CardUpgradeData with type == Crown - that's what
            // Battle.DrawChampions pulls into the opening hand and PlayCrownCardsFirstSystem
            // enforces must be played before anything else. It's a marker only (no stat
            // changes), independent of the miniboss flag above, so it needs its own explicit
            // assignment - cloning a companion doesn't carry one over.
            companionLeaderCrown = ScriptableObject.CreateInstance<CardUpgradeData>();
            companionLeaderCrown.name = CompanionLeaderCrownName;
            companionLeaderCrown.type = CardUpgradeData.Type.Crown;
            companionLeaderCrown.attackEffects = new CardData.StatusEffectStacks[0];
            companionLeaderCrown.effects = new CardData.StatusEffectStacks[0];
            companionLeaderCrown.giveTraits = new CardData.TraitStacks[0];
            companionLeaderCrown.scripts = new CardScript[0];

            // Borrow the crown badge sprite from an existing Crown-type upgrade already in the
            // game (shop/boss-reward crowns) rather than shipping our own art. Shop crowns grant
            // a stat buff on top of the badge, so filter for one with no stat changes to land on
            // the plain marker crown rather than a specific flavored buff icon.
            List<CardUpgradeData> crownUpgrades = AddressableLoader.GetGroup<CardUpgradeData>("CardUpgradeData")
                .Where(upgrade => upgrade.type == CardUpgradeData.Type.Crown && upgrade.image != null)
                .ToList();
            CardUpgradeData referenceCrown = crownUpgrades.FirstOrDefault(upgrade =>
                    upgrade.damage == 0 && upgrade.hp == 0 && upgrade.counter == 0 &&
                    upgrade.uses == 0 && upgrade.effectBonus == 0)
                ?? crownUpgrades.FirstOrDefault();
            companionLeaderCrown.image = referenceCrown?.image;

            AddressableLoader.AddToGroup("CardUpgradeData", companionLeaderCrown);

            newClasses = SourceClassNames.Select(BuildCompanionClass).ToArray();
            foreach (ClassData newClass in newClasses)
            {
                AddressableLoader.AddToGroup("ClassData", newClass);
            }

            GameMode gameMode = Get<GameMode>(GameModeName);
            originalGameModeClasses = gameMode.classes;
            gameMode.classes = originalGameModeClasses.Concat(newClasses).ToArray();
        }

        private static void AliasCardRenderPool(string newCardTypeName, string sourceCardTypeName)
        {
            FieldInfo cardPoolsField = typeof(CardManager).GetField("cardPools", BindingFlags.NonPublic | BindingFlags.Static);
            var cardPools = (Dictionary<string, ObjectPool<Card>>)cardPoolsField.GetValue(null);
            for (int frameLevel = 0; frameLevel < 3; frameLevel++)
            {
                if (cardPools.TryGetValue($"{sourceCardTypeName}{frameLevel}", out ObjectPool<Card> pool))
                {
                    cardPools[$"{newCardTypeName}{frameLevel}"] = pool;
                }
            }
        }

        private static void RemoveCardRenderPoolAlias(string cardTypeName)
        {
            FieldInfo cardPoolsField = typeof(CardManager).GetField("cardPools", BindingFlags.NonPublic | BindingFlags.Static);
            var cardPools = (Dictionary<string, ObjectPool<Card>>)cardPoolsField.GetValue(null);
            for (int frameLevel = 0; frameLevel < 3; frameLevel++)
            {
                cardPools.Remove($"{cardTypeName}{frameLevel}");
            }
        }

        // Builds a brand-new companion-only clan mirroring an existing one (same starting
        // deck/reward pools/flag/character prefab), instead of mutating the original clan.
        private ClassData BuildCompanionClass(string sourceName)
        {
            ClassData source = Get<ClassData>(sourceName);

            ClassData companionClass = source.InstantiateKeepName();
            companionClass.name = NamePrefix + sourceName + "Companions";
            companionClass.id = companionClass.name;
            // Flag stays as the original clan's here - DarkModeFlagPatch tints it on the
            // tribe-select screen instead (see that file for why it isn't done here).

            companionClass.leaders = source.rewardPools
                .Where(pool => pool.type == "Units")
                .SelectMany(pool => pool.list)
                .OfType<CardData>()
                .Select(companion =>
                {
                    CardData clone = companion.Clone();
                    clone.cardType = companionLeaderType;
                    companionLeaderCrown.Assign(clone);
                    return clone;
                })
                .ToArray();

            return companionClass;
        }

        protected override void Unload()
        {
            if (originalGameModeClasses != null)
            {
                Get<GameMode>(GameModeName).classes = originalGameModeClasses;
            }

            if (newClasses != null)
            {
                foreach (ClassData newClass in newClasses)
                {
                    AddressableLoader.RemoveFromGroup("ClassData", newClass);
                }
            }

            if (companionLeaderType != null)
            {
                AddressableLoader.RemoveFromGroup("CardType", companionLeaderType);
                RemoveCardRenderPoolAlias(companionLeaderType.name);
            }

            if (companionLeaderCrown != null)
            {
                AddressableLoader.RemoveFromGroup("CardUpgradeData", companionLeaderCrown);
            }

            base.Unload();
        }
    }
}
