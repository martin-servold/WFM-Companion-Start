using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CompanionStart
{
    // Vanilla's tribe-select screen lays its flags out in a single non-wrapping row - fine for
    // the game's own 3 clans, but this mod adds 6 more (3 companion + 3 monster) on top. This used
    // to be handled by depending on the separate "Extended UI" mod, whose TribeFlagsGrid converts
    // the panel to a 4-column scrolling GridLayoutGroup (Scroller/ScrollToNavigation/TouchScroller
    // and CoroutineManager are all base-game classes it just calls directly, not anything of its
    // own) - replicating that here instead drops the dependency and lets us use 3 columns instead
    // of 4. GameMode.classes ends up ordered [vanilla][companions][monsters] (see
    // CompanionStart.Load), and GridLayoutGroup lays children out in that same order, so 3 columns
    // means each row lines up with exactly one category instead of splitting across them.
    internal static class TribeFlagGridPatch
    {
        private const string TribeSelectPath = "Canvas/SafeArea/TribeSelect";

        internal static void OnSceneChanged(Scene scene)
        {
            if (scene.name != "CharacterSelect")
            {
                return;
            }

            CoroutineManager.Start(ConvertToGrid());
        }

        private static IEnumerator ConvertToGrid()
        {
            GameObject tribeSelect = GameObject.Find(TribeSelectPath);
            if (tribeSelect == null)
            {
                yield break;
            }

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            if (!tribeSelect.TryGetComponent(out GridLayoutGroup grid))
            {
                tribeSelect.GetComponent<LayoutGroup>()?.Destroy();
                yield return new WaitForFixedUpdate();

                grid = tribeSelect.GetOrAdd<GridLayoutGroup>();
            }

            // Reapplied unconditionally even if a GridLayoutGroup already existed here, as a
            // last line of defense - SuppressExtendedUIGridPatch below is what actually keeps
            // Extended UI's own TribeFlagsGrid from running at all when that mod is present.
            grid.cellSize = new Vector2(3.33f, 4.5f);
            grid.childAlignment = TextAnchor.MiddleCenter;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;
            grid.padding.top = 2;
            grid.padding.bottom = 1;

            // Scroller falls back to its own parent as scroll bounds when none is assigned
            // (Scroller.Awake), and "SafeArea" is already a fresh-enough, properly anchored
            // reference frame for that to work correctly here - no dedicated bounds object needed
            // (contrast LeaderSelectionPatch.ConvertToScrollableGrid, where the vanilla leader
            // panel's own parent was NOT well-behaved enough and one had to be built by hand).
            Scroller scroller = tribeSelect.GetOrAdd<Scroller>();

            // Both fields are private [SerializeField]s with no public setter.
            Traverse.Create(tribeSelect.GetOrAdd<ScrollToNavigation>()).Field("scroller").SetValue(scroller);
            Traverse.Create(tribeSelect.GetOrAdd<TouchScroller>()).Field("scroller").SetValue(scroller);

            yield return null;

            Transform tribeSelectTransform = tribeSelect.transform;
            if (tribeSelectTransform.childCount > 0)
            {
                scroller.Scroll(tribeSelectTransform.GetChild(0).position.y);
            }
        }
    }

    // Both this mod and Extended UI's own TribeFlagsGrid react to the same Events.OnSceneChanged,
    // and C# invokes multicast subscribers in subscription order - which tracks mod *load* order,
    // not anything either mod controls at runtime. Whoever's handler happens to be subscribed
    // last runs last and wins, so ConvertToGrid reapplying its settings "unconditionally" still
    // isn't reliable on its own if Extended UI's Load() happens to run after ours.
    //
    // Patching by type name (rather than referencing ExtendedUI.TribeFlagsGrid directly) keeps
    // this mod fully dependency-free: Prepare() gates the whole patch class on that type actually
    // existing, so on a machine without Extended UI installed this is simply never applied.
    [HarmonyPatch]
    internal static class SuppressExtendedUIGridPatch
    {
        private static bool Prepare()
        {
            return AccessTools.TypeByName("ExtendedUI.TribeFlagsGrid") != null;
        }

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(AccessTools.TypeByName("ExtendedUI.TribeFlagsGrid"), "OnSceneChanged");
        }

        private static bool Prefix()
        {
            return false;
        }
    }
}
