using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace CompanionStart
{
    [HarmonyPatch(typeof(SelectLeader), "Run", new[] { typeof(List<ClassData>) })]
    internal static class LeaderSelectionPatch
    {
        private static void Postfix(SelectLeader __instance, List<ClassData> tribes)
        {
            // SelectLeader.GenerateLeaders only ever offers `options` (default 3) random
            // leaders, drawn without repeats from the tribes passed to Run(). The real
            // leader-select flow (SelectTribe.StartSelectRoutine) always passes exactly one
            // tribe, so bumping `options` to that tribe's full leader count here - before
            // GenerateLeaders runs - makes it draw every leader in one unique shuffled pass
            // instead of a random subset.
            int totalLeaders = tribes.Sum(tribe => tribe.leaders.Length);
            Traverse.Create(__instance).Field("options").SetValue(totalLeaders);

            ConvertToScrollableGrid(__instance);
        }

        // The vanilla leader-select container lays cards out side by side with no
        // scrolling/wrapping - built for exactly 3 cards. Once "options" isn't capped at 3
        // anymore, cards run off the screen. Swap it for a CardContainerGrid (the same
        // component the deck/journal viewers use for large browsable card lists) with a
        // Scroller attached, so it wraps into columns and scrolls instead of overflowing.
        private static void ConvertToScrollableGrid(SelectLeader selectLeader)
        {
            Traverse instance = Traverse.Create(selectLeader);
            Traverse containerField = instance.Field("leaderCardContainer");
            CardContainer currentContainer = containerField.GetValue<CardContainer>();

            if (currentContainer is CardContainerGrid)
            {
                return;
            }

            Transform parent = currentContainer.transform.parent;
            Vector2 viewportSize = ((RectTransform)currentContainer.transform).rect.size;
            currentContainer.gameObject.Destroy();

            // Scroller.CheckBounds() compares the grid's own anchoredPosition (relative to its
            // parent) against bounds.anchoredPosition as if both are measured from the same
            // origin. That only holds if bounds is a fresh, zero-positioned direct parent of the
            // grid - not the original panel, which sits at whatever arbitrary position the
            // vanilla screen designer placed it, so using it directly as bounds made every frame
            // look like "content already fits, centered" and snapped scrolling back to zero.
            GameObject scrollBoundsObject = new GameObject("CompanionStartLeaderScrollBounds", typeof(RectTransform));
            scrollBoundsObject.transform.SetParent(parent, false);
            scrollBoundsObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            RectTransform scrollBoundsRect = (RectTransform)scrollBoundsObject.transform;
            scrollBoundsRect.sizeDelta = viewportSize;

            GameObject gridObject = new GameObject("CompanionStartLeaderGrid", typeof(RectTransform), typeof(GroupedLeaderGrid));
            gridObject.transform.SetParent(scrollBoundsObject.transform, false);
            gridObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

            CardContainerGrid grid = gridObject.GetComponent<GroupedLeaderGrid>();
            grid.holder = gridObject.GetComponent<RectTransform>();
            grid.onAdd = new UnityEventEntity();
            grid.onRemove = new UnityEventEntity();

            // Default scrollAmount (1) moves one unit of content per wheel notch, which felt like
            // it took far more scrolling than it should to get through a full companion roster.
            Scroller scroller = gridObject.GetOrAdd<Scroller>();
            scroller.bounds = scrollBoundsRect;
            scroller.scrollAmount = 2f;

            containerField.SetValue(grid);
        }
    }

    // FlipUpLeaders' own FlipUpRoutine flips one card at a time with a small random delay between
    // each - barely noticeable for 3 vanilla leaders, but once a clan's roster runs into the
    // dozens (companions ~25, monsters 62) that staggered TL-BR sweep becomes a real multi-second
    // wait. Flipping every card in the same frame instead avoids re-implementing FlipUpRoutine's
    // own private staggering loop just to skip the delay in it.
    [HarmonyPatch(typeof(SelectLeader), nameof(SelectLeader.FlipUpLeaders))]
    internal static class InstantLeaderFlipPatch
    {
        private static bool Prefix(SelectLeader __instance)
        {
            CardContainer leaderCardContainer = Traverse.Create(__instance).Field("leaderCardContainer").GetValue<CardContainer>();
            foreach (Entity entity in leaderCardContainer)
            {
                entity.flipper.FlipUp(force: true);
            }

            return false;
        }
    }

    // GenerateLeaders can now take several frames per card (creating each CardData, then applying
    // status effects/upgrades via Card.UpdateData) instead of finishing near-instantly - for the
    // 62-leader monster clans that's long enough that a player hitting "back" mid-generation is a
    // real scenario, not just a theoretical one. CharacterSelectScreen.Back -> SelectLeader.Cancel
    // destroys every leader entity/CardData created so far, including ones still mid-UpdateData -
    // so those suspended coroutines resume next frame against now-destroyed objects and throw
    // (NullReferenceException in UnityEngine.Object.get_name, UpgradeHolder.Clear, etc.).
    // SelectLeader already tracks exactly this window via its public `generating` flag (Reroll()
    // guards on it for the same reason) - Back() just never checked it.
    [HarmonyPatch(typeof(CharacterSelectScreen), nameof(CharacterSelectScreen.Back))]
    internal static class PreventBackDuringGenerationPatch
    {
        private static bool Prefix(CharacterSelectScreen __instance)
        {
            SelectLeader leaderSelection = Traverse.Create(__instance).Field("leaderSelection").GetValue<SelectLeader>();
            return !leaderSelection || !leaderSelection.generating;
        }
    }

    // SelectLeader.GenerateLeaders draws every leader from a shuffled LeaderPool, so with the
    // full-roster fix above, companions would otherwise show up in random order each visit.
    // SetLeaderPositions runs once after all leaders for this screen are created and right before
    // it positions them via CardContainer.GetChildPosition, which (for the CardContainerGrid we
    // swap in above) derives each card's row/column purely from its index in the container's
    // internal "entities" list - so sorting that list here is enough to get an alphabetical
    // layout, with no need to touch transforms or card creation order.
    [HarmonyPatch(typeof(SelectLeader), "SetLeaderPositions")]
    internal static class LeaderSortPatch
    {
        private static void Prefix(SelectLeader __instance)
        {
            List<SelectLeader.Character> characters = Traverse.Create(__instance).Field("characters").GetValue<List<SelectLeader.Character>>();
            if (characters == null || characters.Count == 0 || !characters[0].data.classData.name.StartsWith(CompanionStart.NamePrefix))
            {
                return;
            }

            CardContainer container = Traverse.Create(__instance).Field("leaderCardContainer").GetValue<CardContainer>();
            Traverse.Create(container).Field("entities").GetValue<List<Entity>>()
                .Sort((a, b) => string.Compare(a.data.title, b.data.title, StringComparison.OrdinalIgnoreCase));
        }
    }
}
