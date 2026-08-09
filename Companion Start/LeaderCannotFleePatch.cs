using System.Collections;
using HarmonyLib;

namespace CompanionStart
{
    // ActionFlee (used by the "Escape" status effect some monsters carry, e.g. Gobling's passive)
    // moves the fleeing entity to reserve instead of routing it through Entity.Kill() - so it
    // never fires Events.InvokeEntityKilled, which is what Battle's miniboss/leader-death tracking
    // actually depends on. Fine for an ordinary companion, but if the player's own LEADER flees
    // instead of dying, the "leader died -> run over" check never trips, making that leader
    // effectively unkillable through this vector. Not specific to Gobling - any leader with a
    // similar escape effect (present or future, from any clan) would hit the same hole, so this
    // redirects any fleeing player leader into a real death instead, regardless of source.
    [HarmonyPatch(typeof(ActionFlee), nameof(ActionFlee.Run))]
    internal static class LeaderCannotFleePatch
    {
        private static bool Prefix(ActionFlee __instance, ref IEnumerator __result)
        {
            Entity entity = __instance.entity;
            if (entity != null && entity.owner == References.Player
                && entity.data != null && entity.data.cardType != null && entity.data.cardType.miniboss)
            {
                __result = entity.Kill();
                return false;
            }

            return true;
        }
    }
}
