using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace CompanionStart
{
    [HarmonyPatch(typeof(Entity), nameof(Entity.AddTo))]
    internal static class TallLeaderPatch
    {
        // Only our own leader clones carry the marker, and only on the player's side of the board
        // - every hook here also sees vanilla enemies (the real SplitBoss, SummonBoss, and friends
        // mid-fight), which the base game already handles and which must be left alone. Matching
        // them by name/title earlier made the small enemy Truffles two slots tall, and the fight
        // no longer ended once they were all killed.
        internal static bool IsTall(Entity entity)
        {
            return entity != null
                && entity.data != null
                && entity.owner != null
                && entity.owner == References.Player
                && entity.data.TryGetCustomData(CompanionStart.TallLeaderMarker, out bool isTall, false)
                && isTall;
        }

        internal static void ApplyHeight(Entity entity)
        {
            if (IsTall(entity))
            {
                entity.height = 2;
            }
        }

        internal static void OnEntityMove(Entity entity)
        {
            if (!IsTall(entity) || Battle.instance == null || entity.owner == null)
            {
                return;
            }

            ApplyHeight(entity);
            List<CardSlot> occupiedSlots = Battle.instance.GetRows(entity.owner)
                .OfType<CardSlotLane>()
                .SelectMany(lane => lane.slots)
                .Where(slot => slot.GetTop() == entity)
                .ToList();
            if (occupiedSlots.Count != 1)
            {
                return;
            }

            CardSlot secondarySlot = GetPartnerSlot(entity, occupiedSlots[0]);
            if (secondarySlot != null && secondarySlot.GetTop() == null)
            {
                secondarySlot.Add(entity);
            }
        }

        // The other slot in the same column as slot, on the entity's own side of the board -
        // i.e. the second half of the space a tall leader needs when its primary half lands in
        // slot. Null when slot isn't a board lane slot at all (hand, discard, etc).
        internal static CardSlot GetPartnerSlot(Entity entity, CardSlot slot)
        {
            CardSlotLane lane = slot?.Group as CardSlotLane;
            if (lane == null || Battle.instance == null || entity.owner == null)
            {
                return null;
            }

            List<CardContainer> rows = Battle.instance.GetRows(entity.owner);
            int rowIndex = rows.IndexOf(lane);
            int slotIndex = lane.slots.IndexOf(slot);
            int adjacentRowIndex = rowIndex + 1 < rows.Count ? rowIndex + 1 : rowIndex - 1;
            if (rowIndex < 0 || slotIndex < 0 || adjacentRowIndex < 0)
            {
                return null;
            }

            CardSlotLane adjacentLane = rows[adjacentRowIndex] as CardSlotLane;
            if (adjacentLane == null || slotIndex >= adjacentLane.slots.Count)
            {
                return null;
            }

            return adjacentLane.slots[slotIndex];
        }

        // Works out whether a tall leader can take both slot and its partner slot. The vanilla
        // drop check only looks at the single slot under the cursor, so without this a tall
        // leader dropped next to a unit already sitting in the partner slot would land in just
        // one slot and silently become single-height. When the partner slot is taken, the
        // occupant gets shoved out of the way (normally just a swap into the column the leader
        // is leaving) using the game's own ShoveSystem rules; shoveData is null if nothing needs
        // to move. Returns false if the partner slot can't be freed at all.
        internal static bool TryClaimPartnerSlot(Entity entity, CardSlot slot, out CardSlot partner, out Dictionary<Entity, List<CardSlot>> shoveData)
        {
            shoveData = null;
            CardSlot partnerSlot = GetPartnerSlot(entity, slot);
            partner = partnerSlot;
            if (partnerSlot == null)
            {
                return false;
            }

            Entity blocker = partnerSlot.GetTop();
            if (blocker == null || blocker == entity)
            {
                return true;
            }

            // CanShove falls back to shoving into the other row when sideways is blocked, and
            // in the same column that's exactly the slot the leader's other half is headed for -
            // so any plan that would park something in either of the leader's two slots is out.
            return ShoveSystem.CanShove(blocker, entity, out shoveData)
                && shoveData != null
                && !shoveData.ContainsKey(entity)
                && !shoveData.Values.Any(slots => slots.Contains(slot) || slots.Contains(partnerSlot));
        }

        // Refuses a tall leader's drop up front (before the turn is spent) when the other half
        // of the destination column is taken by a unit that can't be shoved aside.
        internal static void OnCheckAction(ref PlayAction action, ref bool allow)
        {
            if (!allow
                || !(action is ActionMove move)
                || !IsTall(move.entity)
                || move.toContainers == null
                || move.toContainers.Length != 1
                || !(move.toContainers[0] is CardSlot slot)
                || !(slot.Group is CardSlotLane))
            {
                return;
            }

            ApplyHeight(move.entity);
            if (!TryClaimPartnerSlot(move.entity, slot, out _, out _))
            {
                allow = false;
            }
        }

        private static void Prefix(Entity __instance)
        {
            ApplyHeight(__instance);
        }

        private static void Postfix(Entity __instance)
        {
            ApplyHeight(__instance);
        }
    }

    [HarmonyPatch(typeof(Entity), "Update")]
    internal static class TallLeaderUpdatePatch
    {
        private static void Postfix(Entity __instance)
        {
            TallLeaderPatch.ApplyHeight(__instance);
        }
    }

    [HarmonyPatch(typeof(Entity), "set_data")]
    internal static class TallLeaderDataPatch
    {
        private static void Postfix(Entity __instance)
        {
            TallLeaderPatch.ApplyHeight(__instance);
        }
    }

    [HarmonyPatch(typeof(CardContainer), nameof(CardContainer.GetSecondaryContainers))]
    internal static class TallLeaderContainerPatch
    {
        private static void Prefix(Entity entity)
        {
            TallLeaderPatch.ApplyHeight(entity);
        }
    }

    [HarmonyPatch(typeof(CardSlotLane), nameof(CardSlotLane.Add))]
    internal static class TallLeaderLanePatch
    {
        private static void Prefix(Entity entity)
        {
            TallLeaderPatch.ApplyHeight(entity);
        }

    }

    [HarmonyPatch(typeof(Battle), "CanDeploy")]
    internal static class TallLeaderDeployPatch
    {
        private static void Prefix(Entity entity)
        {
            TallLeaderPatch.ApplyHeight(entity);
        }
    }

    [HarmonyPatch(typeof(Sequences), "CardMove")]
    internal static class TallLeaderCardMovePatch
    {
        private static bool Prefix(Entity entity, ref CardContainer[] toContainers, int insertPos, bool tweenAll, ref IEnumerator __result)
        {
            TallLeaderPatch.ApplyHeight(entity);

            if (!TallLeaderPatch.IsTall(entity)
                || toContainers == null
                || toContainers.Length != 1
                || !(toContainers[0] is CardSlot destinationSlot)
                || !(destinationSlot.Group is CardSlotLane))
            {
                return true;
            }

            if (!TallLeaderPatch.TryClaimPartnerSlot(entity, destinationSlot, out CardSlot partnerSlot, out Dictionary<Entity, List<CardSlot>> shoveData))
            {
                // Rechecked here rather than trusting the drop-time OnCheckAction alone, since
                // effect-driven moves never go through that check. Staying put beats landing
                // at half height.
                entity.TweenToContainer();
                __result = Enumerable.Empty<object>().GetEnumerator();
                return false;
            }

            CardContainer[] bothSlots = { destinationSlot, partnerSlot };
            if (shoveData == null)
            {
                toContainers = bothSlots;
                return true;
            }

            __result = ShoveThenMove(entity, bothSlots, shoveData, insertPos, tweenAll);
            return false;
        }

        // The nested CardMove passes straight through the prefix above, since it's already
        // targeting two containers.
        private static IEnumerator ShoveThenMove(Entity entity, CardContainer[] toContainers, Dictionary<Entity, List<CardSlot>> shoveData, int insertPos, bool tweenAll)
        {
            yield return ShoveSystem.DoShove(shoveData, updatePositions: true);
            yield return Sequences.CardMove(entity, toContainers, insertPos, tweenAll);
        }
    }

    // Vanilla shoves push a whole chain of units along a row, and a tall leader in that chain
    // can only move as a two-slot block - it needs both rows of the next column free. With a
    // full board and the leader in the middle column that never happens, so e.g. dropping a
    // column 1 unit onto a column 3 unit fails outright, and nothing can get "past" the leader.
    // When that happens with one of our tall leaders on the board, fall back to a straight swap:
    // the unit being dropped onto takes the dragged unit's old slot, and the leader stays put.
    [HarmonyPatch(typeof(ShoveSystem), nameof(ShoveSystem.CanShove))]
    internal static class TallLeaderSwapPatch
    {
        private static void Postfix(Entity shovee, Entity shover, ref Dictionary<Entity, List<CardSlot>> shoveData, ref bool __result)
        {
            if (__result
                || shovee == null
                || shover == null
                || shover.owner != References.Player
                || shovee.owner != shover.owner
                || Battle.instance == null
                || TallLeaderPatch.IsTall(shovee)
                || TallLeaderPatch.IsTall(shover)
                || !Events.CheckEntityShove(shovee)
                || !TryGetSingleBoardSlot(shover, out CardSlot shoverSlot)
                || !TryGetSingleBoardSlot(shovee, out _))
            {
                return;
            }

            bool tallLeaderOnBoard = Battle.instance.GetRows(shover.owner)
                .OfType<CardSlotLane>()
                .SelectMany(lane => lane.slots)
                .Any(slot => TallLeaderPatch.IsTall(slot.GetTop()));
            if (!tallLeaderOnBoard)
            {
                return;
            }

            shoveData = new Dictionary<Entity, List<CardSlot>> { { shovee, new List<CardSlot> { shoverSlot } } };
            __result = true;
        }

        // Only units already sitting in exactly one board slot can swap - this keeps hand
        // deploys and summons (whose "shover" isn't on the board yet) on vanilla rules.
        private static bool TryGetSingleBoardSlot(Entity entity, out CardSlot slot)
        {
            slot = null;
            if (entity.actualContainers == null || entity.actualContainers.Count != 1)
            {
                return false;
            }

            slot = entity.actualContainers[0] as CardSlot;
            return slot != null && slot.Group is CardSlotLane;
        }
    }

    [HarmonyPatch(typeof(CardContainer), nameof(CardContainer.Add))]
    internal static class TallLeaderContainerAddPatch
    {
        private static void Prefix(Entity entity)
        {
            TallLeaderPatch.ApplyHeight(entity);
        }

    }
}