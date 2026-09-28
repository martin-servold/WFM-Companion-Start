using System;
using System.Collections;
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
        public override string[] Depends => new string[] { };
        public override string Title => "Companion Start";
        public override string Description => "Adds new clans made up of the game's companions and monsters as selectable leaders.";

        internal const string NamePrefix = "fuko.wildfrost.companionstart.";
        internal const string CompanionLeaderCardTypeName = NamePrefix + "CompanionLeader";
        internal const string ClunkerLeaderCardTypeName = NamePrefix + "ClunkerLeader";
        internal const string ClunkerLeaderMarker = NamePrefix + "ClunkerLeader";
        internal const string TallLeaderMarker = NamePrefix + "TallLeader";
        private const int BossLeaderHealthCap = 12;
        private const string NomAndStompyTitle = "Nom & Stompy";
        private const string CompanionLeaderCrownName = NamePrefix + "CompanionLeaderCrown";
        private const string GameModeName = "GameModeNormal";

        private static readonly string[] SourceClassNames = { "Basic", "Clunk", "Magic" };

        // Naked Gnome isn't drawn from any clan's normal "Units" reward pool - he's only ever
        // obtained by sparing the enemy card "NakedGnome" in the run's first battle - so he needs
        // to be added to the companion pool explicitly rather than falling out of the scrape below.
        private static readonly string[] SituationalCompanionNames = { "NakedGnomeFriendly" };

        // Every non-boss enemy in the game. Unlike companions, enemies aren't tied to any
        // particular clan's own reward pool at all, so all three monster clans below get this
        // exact same roster rather than each drawing from a different natural source.
        private static readonly string[] EnemyLeaderNames =
        {
            "BabySnowbo", "Beeberry", "BerryWitch", "Smakk", "Sheep", "BulbHead", "Burster", "Chungoon", "Conker", "Smash",
            "BerryMonster", "Frostinger", "Gobbler", "Gobling", "Smackgoon", "Gok", "Grink", "Sno", "Grog", "Noodle",
            "Grouchy", "Chunky", "SBelly", "SMime", "Wildling", "JabJoat", "Blockhead", "Kraken", "Icemason", "Lump",
            "Makoko", "Spyke", "Minimoko", "NakedGnome", "Kalamari", "OobaBear", "Stinghorn", "Pecan", "Pengoon", "PepperWitch",
            "Berro", "Popshroom", "Sporkypine", "Prickle", "Puffball", "Pygmy", "Wally", "ShellWitch", "ShroomGobbler", "Shrootles",
            "Confuddler", "SnowGobbler", "Snowbirb", "Snowbo", "Spuncher", "Voido", "Waddlegoons", "Wrecker", "Snoolf", "Burner",
            "SnormWorm", "WoollyDrek"
        };

        // Bosses are kept separate from ordinary enemies so they can be shown as their own
        // group in each of the three monster leader clans.
        internal static readonly HashSet<string> BossLeaderNames = new HashSet<string>(new[]
        {
            "Bamboozle", "BigPeng", "Blot", "Bogberry", "Bolgo", "Bomber", "Bumbo", "CrazyEyes",
            "Frosty", "GukaGuka", "Gunkback", "Infernoko", "MonkeyKing", "Muttonhead", "Numskull",
            "Smosh", "SnowKnight", "Toothless", "Truffle", "Turnip", "VeiledLady",
            "ClunkerBoss", "ClunkerBoss2", "FinalBoss", "FinalBoss2", "FrenzyBoss", "FrenzyBoss2",
            "GuardianGnome", "SplitBoss", "SplitBoss1", "SplitBoss2", "SummonBoss", "SummonBoss2",
            "SummonBoss3", "TrueFinalBoss1", "TrueFinalBoss2", "TrueFinalBoss3", "TrueFinalBoss4",
            "TrueFinalBoss5", "TrueFinalBoss6"
        }, StringComparer.OrdinalIgnoreCase);

        internal static readonly HashSet<string> TallLeaderNames = new HashSet<string>(new[]
        {
            "SplitBoss", "FrenzyBoss", "FrenzyBoss2", "ClunkerBoss", "ClunkerBoss2", "SummonBoss"
        }, StringComparer.OrdinalIgnoreCase);

        // Strongly saturated toward one hue each (rather than the near-neutral greys tried
        // first) so both read clearly against vanilla's blue/teal/orange flags and against
        // each other, even at a glance across a 3x3 grid.
        private static readonly Color CompanionFlagTint = new Color(1f, 0.85f, 0.35f, 1f);
        private static readonly Color MonsterFlagTint = new Color(1f, 0.3f, 0.3f, 1f);

        // Keyed by ClassData.name so DarkModeFlagPatch can tell the two categories of clan apart
        // on the tribe-select screen without needing to know our naming/suffix conventions itself.
        internal static readonly Dictionary<string, Color> FlagTints = new Dictionary<string, Color>();

        // Used for both companion and monster leaders - the CardType/Crown only mark "this card
        // is a player-controlled leader", which is meaningless with respect to where the
        // underlying CardData originally came from.
        private CardType companionLeaderType;
        private CardType clunkerLeaderType;
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

            // "Friendly" is the CardType ordinary, benchable companions use, so it has
            // canReserve = true. Vanilla's own Leader CardType has it false - benching your
            // leader isn't a valid state - but cloning Friendly here carries that true value
            // over, which let CardControllerDeck offer "move to reserve" on companion leaders.
            companionLeaderType.canReserve = false;

            // Same story as canReserve above: "Friendly" allows the in-battle drag-to-discard
            // recall (EntityExt.CanRecall gates it on cardType.canRecall), so without this,
            // companion leaders could be dragged off the board and pulled out of the fight.
            companionLeaderType.canRecall = false;

            AddressableLoader.AddToGroup("CardType", companionLeaderType);

            clunkerLeaderType = Get<CardType>("Clunker").InstantiateKeepName();
            clunkerLeaderType.name = ClunkerLeaderCardTypeName;
            clunkerLeaderType.miniboss = true;
            clunkerLeaderType.canReserve = false;
            clunkerLeaderType.canRecall = false;

            // CardContainerGrid (e.g. the deck viewer) sorts by sortPriority. Clunker's is 2, after
            // Friendly's 1, so the leader was listed after every companion instead of first. Use
            // vanilla Leader's priority so it sorts with the companions and stays at the front.
            clunkerLeaderType.sortPriority = Get<CardType>("Leader").sortPriority;
            AddressableLoader.AddToGroup("CardType", clunkerLeaderType);

            // CardManager builds its per-CardType render-prefab pool once, at scene start, from
            // whatever CardTypes exist in the "CardType" group at that moment - which happens
            // before this mod's Load() runs. A CardType added afterwards never gets a pool entry
            // of its own, so CardManager.Get() throws KeyNotFoundException the first time it's
            // used. Since our clone shares "Friendly"'s prefab exactly, it's safe to just alias
            // its pool keys onto Friendly's already-built pools instead of constructing new ones.
            AliasCardRenderPool(companionLeaderType.name, "Friendly");
            AliasCardRenderPool(clunkerLeaderType.name, "Clunker");

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

            ClassData[] companionClasses = SourceClassNames
                .Select(sourceName => BuildLeaderClass(sourceName, "Companions", CompanionFlagTint, source =>
                    source.rewardPools
                        .Where(pool => pool.type == "Units")
                        .SelectMany(pool => pool.list)
                        .OfType<CardData>()
                        .Concat(source.rewardPools
                            .Where(pool => pool.type == "Items")
                            .SelectMany(pool => pool.list)
                            .OfType<CardData>()
                            .Where(card => card.IsClunker))
                        .Concat(SituationalCompanionNames.Select(TryGetCardData))
                        .Where(card => card != null)
                        .GroupBy(GetRosterIdentity)
                        .Select(SelectPreferredRosterCard)))
                .ToArray();

            ClassData[] monsterClasses = SourceClassNames
                .Select(sourceName => BuildLeaderClass(sourceName, "Monsters", MonsterFlagTint, source =>
                    EnemyLeaderNames.Select(TryGetCardData)
                        .Concat(BossLeaderNames.Select(TryGetCardData).Where(card => !IsFriendOnlyCard(card)))
                        .Where(card => card != null)
                        .GroupBy(GetRosterIdentity)
                        .Select(SelectPreferredRosterCard)))
                .ToArray();

            newClasses = companionClasses.Concat(monsterClasses).ToArray();
            foreach (ClassData newClass in newClasses)
            {
                AddressableLoader.AddToGroup("ClassData", newClass);
            }

            GameMode gameMode = Get<GameMode>(GameModeName);
            originalGameModeClasses = gameMode.classes;
            gameMode.classes = originalGameModeClasses.Concat(newClasses).ToArray();

            Events.OnCampaignInit += EnsureChampionProperties;
            Events.OnSceneChanged += TribeFlagGridPatch.OnSceneChanged;
            Events.OnEntityMove += TallLeaderPatch.OnEntityMove;
            Events.OnCheckAction += TallLeaderPatch.OnCheckAction;
        }

        // OnCampaignInit is a multicast delegate - only the last-invoked subscriber's returned
        // enumerator is actually driven, so this is written as a plain method (no yield) rather
        // than an iterator, guaranteeing it always runs regardless of subscriber order.
        private static IEnumerator EnsureChampionProperties()
        {
            CardData champion = References.PlayerData?.inventory?.deck
                ?.FirstOrDefault(card => card.cardType != null && card.cardType.miniboss);

            if (champion != null && string.Equals(champion.title, "Egg", StringComparison.OrdinalIgnoreCase))
            {
                champion.forceTitle = "Egg, M.D.";
            }
            else if (champion != null && string.Equals(champion.title, "Beeberry", StringComparison.OrdinalIgnoreCase))
            {
                champion.forceTitle = "Peeberry";
            }

            return null;
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

        // Looks up a CardData by name defensively - used for the hand-maintained situational
        // companion and enemy-leader name lists below, where a single typo shouldn't be able to
        // take down the whole mod's Load() with an AddressableLoader exception.
        private CardData TryGetCardData(string name)
        {
            try
            {
                return Get<CardData>(name);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"CompanionStart: couldn't load CardData \"{name}\" - skipping. {ex.Message}");
                return null;
            }
        }

        private static bool IsFriendOnlyCard(CardData card)
        {
            return card != null && string.Equals(card.title, NomAndStompyTitle, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetRosterIdentity(CardData card)
        {
            if (string.Equals(card.title, "Truffle", StringComparison.OrdinalIgnoreCase)
                || card.name.StartsWith("Truffle", StringComparison.OrdinalIgnoreCase))
            {
                return "Truffle";
            }

            return card.name;
        }

        private static CardData SelectPreferredRosterCard(IEnumerable<CardData> cards)
        {
            return cards
                .OrderByDescending(card => TallLeaderNames.Contains(card.name))
                .ThenByDescending(card => card.hp)
                .First();
        }

        private static bool IsBossCard(CardData card)
        {
            return card != null
                && BossLeaderNames.Contains(card.name)
                && !IsFriendOnlyCard(card);
        }

        private static bool IsPhaseChangeStatus(StatusEffectData status)
        {
            if (status == null)
            {
                return false;
            }

            return new[] { status.name, status.type, status.keyword }
                .Any(value => !string.IsNullOrEmpty(value)
                    && (value.IndexOf("nextphase", StringComparison.OrdinalIgnoreCase) >= 0
                        || value.IndexOf("next phase", StringComparison.OrdinalIgnoreCase) >= 0));
        }

        private static void NormalizeBossLeader(CardData card)
        {
            card.hp = Math.Min(card.hp, BossLeaderHealthCap);
            if (card.startWithEffects != null)
            {
                card.startWithEffects = card.startWithEffects
                    .Where(effect => !IsPhaseChangeStatus(effect.data))
                    .ToArray();
            }
        }

        // Builds a brand-new clan mirroring an existing one (same starting deck/reward
        // pools/flag/character prefab) but with its own leader roster, instead of mutating the
        // original clan. Used for both the companion and monster clans - only the roster source,
        // name suffix, and flag tint differ between them.
        private ClassData BuildLeaderClass(string sourceName, string suffix, Color flagTint, Func<ClassData, IEnumerable<CardData>> rosterSelector)
        {
            ClassData source = Get<ClassData>(sourceName);

            ClassData leaderClass = source.InstantiateKeepName();
            leaderClass.name = NamePrefix + sourceName + suffix;
            leaderClass.id = leaderClass.name;
            // Flag stays as the original clan's here - DarkModeFlagPatch tints it on the
            // tribe-select screen instead (see that file for why it isn't done here).

            leaderClass.leaders = rosterSelector(source)
                .Where(companion => companion != null)
                .GroupBy(companion => companion.name)
                .Select(group => group.First())
                .Select(CloneAsLeader)
                .ToArray();

            FlagTints[leaderClass.name] = flagTint;

            return leaderClass;
        }

        private CardData CloneAsLeader(CardData companion)
        {
            bool isClunker = companion.IsClunker;
            bool isBoss = IsBossCard(companion);
            // Matched by name only - the small Truffles (SummonBoss2/3) share Truffle Prime's
            // title, but only Truffle Prime itself (SummonBoss) takes two slots.
            bool isTall = TallLeaderNames.Contains(companion.name);
            CardData clone = companion.Clone(!isBoss);
            clone.cardType = isClunker ? clunkerLeaderType : companionLeaderType;
            companionLeaderCrown.Assign(clone);

            if (isBoss)
            {
                NormalizeBossLeader(clone);
            }

            if (isClunker)
            {
                clone.SetCustomData(ClunkerLeaderMarker, true);
            }

            if (isTall)
            {
                clone.SetCustomData(TallLeaderMarker, true);
            }

            // The base game only persists a cardType override across save/load when
            // CardSaveData spots cardType != original.cardType at save time - but by then
            // this clone's "original" (see CardData.Clone) already carries the same
            // companionLeaderType, so that check never trips and the override is never
            // recorded. Without it, reloading a save resolves the card back to its vanilla
            // CardData (e.g. cardType "Friendly" or "Enemy"), which Character.GetCompanionCount()
            // then miscounts as a companion. Stamping this customData key directly makes the
            // game's own OverrideCardType restore logic (CardSaveData.Load) apply regardless, so
            // the leader keeps its own CardType after a reload.
            clone.SetCustomData("OverrideCardType", clone.cardType.name);

            return clone;
        }

        protected override void Unload()
        {
            Events.OnCampaignInit -= EnsureChampionProperties;
            Events.OnSceneChanged -= TribeFlagGridPatch.OnSceneChanged;
            Events.OnEntityMove -= TallLeaderPatch.OnEntityMove;
            Events.OnCheckAction -= TallLeaderPatch.OnCheckAction;
            FlagTints.Clear();

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

            if (clunkerLeaderType != null)
            {
                AddressableLoader.RemoveFromGroup("CardType", clunkerLeaderType);
                RemoveCardRenderPoolAlias(clunkerLeaderType.name);
            }

            if (companionLeaderCrown != null)
            {
                AddressableLoader.RemoveFromGroup("CardUpgradeData", companionLeaderCrown);
            }

            base.Unload();
        }
    }
}
