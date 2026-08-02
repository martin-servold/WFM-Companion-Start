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

            GameObject gridObject = new GameObject("CompanionStartLeaderGrid", typeof(RectTransform), typeof(CardContainerGrid));
            gridObject.transform.SetParent(scrollBoundsObject.transform, false);
            gridObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

            CardContainerGrid grid = gridObject.GetComponent<CardContainerGrid>();
            grid.holder = gridObject.GetComponent<RectTransform>();
            grid.onAdd = new UnityEventEntity();
            grid.onRemove = new UnityEventEntity();

            Scroller scroller = gridObject.GetOrAdd<Scroller>();
            scroller.bounds = scrollBoundsRect;

            containerField.SetValue(grid);
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
