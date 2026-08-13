using System.Collections;
using System.Linq;
using HarmonyLib;

namespace CompanionStart
{
    // "Eat something" abilities (e.g. WoollyDrek's "Eat and Absorb a random ally") are built as a
    // self-targeting trigger: the outer effect hits its own owner just to carry effectToApply (a
    // StatusEffectInstantEatSomething) onto them, and THAT effect does the real, separate
    // targeting afterwards (Targets.Get, which already correctly excludes the caster itself and
    // is never touched by anything here). Some of these effects also carry an "Is Not Miniboss"
    // constraint meant only to stop them from eating a miniboss ally - but that same
    // targetConstraints list gets reused, unintentionally, as a gate on whether the
    // self-triggering carrier hit can even be applied to its own owner in the first place
    // (StatusEffectApplyX.CanAffect and StatusEffectSystem.Apply both check it against whichever
    // entity the hit targets, which for the carrier hit is the caster itself).
    //
    // Harmless for a normal enemy, but any of our monster-leader clones IS miniboss-flagged
    // (that's how the leader mechanic works) - so it fails its own gate against itself and the
    // whole ability silently no-ops with no animation and no error. Both patches below bypass
    // that gate only for the exact self-hit case (entity/target being checked is the caster
    // itself); the real "who gets eaten" decision is a completely separate code path neither
    // patch touches, so a normal enemy (miniboss or not) still can't eat an ally miniboss.
    [HarmonyPatch(typeof(StatusEffectApplyX), "CanAffect")]
    internal static class EatSelfTriggerCanAffectPatch
    {
        private static bool Prefix(StatusEffectApplyX __instance, Entity entity, ref bool __result)
        {
            if (entity == __instance.target && __instance.effectToApply is StatusEffectInstantEatSomething)
            {
                __result = true;
                return false;
            }

            return true;
        }
    }

    // StatusEffectSystem.Apply is a coroutine whose "can this even be applied" gate check runs
    // synchronously as the very first thing in its body, before any yield - so it's guaranteed to
    // have already happened by the time the wrapped enumerator's first MoveNext() call returns,
    // regardless of whether that call ran to completion (yield break) or paused at the first real
    // yield point. Clearing the offending constraint only around that single MoveNext() call, then
    // restoring it immediately after, brackets exactly the gate check and nothing past it - the
    // later clone that actually processes "who gets eaten" (StatusEffectData.Instantiate() inside
    // Apply itself) gets its own separate targetConstraints array anyway, so it's structurally
    // unaffected either way.
    [HarmonyPatch(typeof(StatusEffectSystem), nameof(StatusEffectSystem.Apply))]
    internal static class EatSelfTriggerApplyPatch
    {
        private static void Postfix(Entity target, Entity applier, StatusEffectData effectData, ref IEnumerator __result)
        {
            if (target == applier && effectData is StatusEffectInstantEatSomething)
            {
                __result = BypassSelfGate(effectData, __result);
            }
        }

        private static IEnumerator BypassSelfGate(StatusEffectData effectData, IEnumerator original)
        {
            TargetConstraint[] originalConstraints = effectData.targetConstraints;
            bool hasNext;
            object current = null;
            effectData.targetConstraints = originalConstraints.Where(c => c.name != "Is Not Miniboss").ToArray();
            try
            {
                hasNext = original.MoveNext();
                if (hasNext)
                {
                    current = original.Current;
                }
            }
            finally
            {
                effectData.targetConstraints = originalConstraints;
            }

            if (!hasNext)
            {
                yield break;
            }

            yield return current;
            while (original.MoveNext())
            {
                yield return original.Current;
            }
        }
    }
}
